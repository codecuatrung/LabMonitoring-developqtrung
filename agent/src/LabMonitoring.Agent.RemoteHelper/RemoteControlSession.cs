using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// Orchestrates one remote-control session end to end (ADR-0013): connects back to the Worker
/// Service over a named pipe, builds an <see cref="RTCPeerConnection"/> that captures the screen and
/// injects input, and relays SDP/ICE over the pipe (the main process alone holds the
/// SignalR/AgentToken connection to the server; this process never talks to the server directly).
/// The Agent is always the SDP *offering* side — it owns the media source.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class RemoteControlSession
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const int CaptureFramesPerSecond = 8; // modest for v1 GDI BitBlt + software VP8 encode

    public static async Task RunAsync(Guid sessionId, string pipeName, CancellationToken shutdownCt)
    {
        HelperLog.Write($"[{sessionId}] Showing banner.");
        var banner = new RemoteControlBanner();
        banner.Show();

        HelperLog.Write($"[{sessionId}] Connecting to pipe '{pipeName}'.");
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(15_000, shutdownCt);
        HelperLog.Write($"[{sessionId}] Pipe connected.");

        var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var writeLock = new SemaphoreSlim(1, 1);

        async Task SendAsync(PipeMessage message)
        {
            var json = JsonSerializer.Serialize(message, JsonOpts);
            await writeLock.WaitAsync(shutdownCt);
            try { await writer.WriteLineAsync(json); }
            finally { writeLock.Release(); }
        }

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCt);

        HelperLog.Write($"[{sessionId}] Creating RTCPeerConnection.");
        var pc = new RTCPeerConnection(new RTCConfiguration()); // no STUN/TURN — same-LAN topology (ADR-0013)
        HelperLog.Write($"[{sessionId}] RTCPeerConnection created.");

        var videoEncoder = new VideoEncoderEndPoint();
        // Do NOT use videoEncoder.GetVideoSourceFormats() here — SIPSorceryMedia.Encoders 10.0.4 was
        // compiled against SIPSorceryMedia.Abstractions 8.0.12, whose VideoCodecsEnum had `Unknown`
        // as the LAST member (VP8 = ordinal 7). The Abstractions version actually loaded at runtime
        // (10.0.16, merged into the main sipsorcery repo) moved `Unknown` to ordinal 0, shifting VP8
        // to ordinal 8 and H263 into VP8's old ordinal 7. Encoders.dll's IL still has the literal
        // "7" baked in for what it thinks is VideoCodecsEnum.VP8, so GetVideoSourceFormats() returns
        // a format whose Codec value (7) is read back as H263 by everything else — that's exactly
        // why the SDP offer advertised "a=rtpmap:96 H263/90000" instead of VP8, which every browser
        // rejects (m=video port 0 in the answer), producing VideoIncompatible (ADR-0013). The actual
        // encode/decode path in VpxVideoEncoder/VideoEncoderEndPoint hardcodes VP8 regardless of the
        // Codec enum value passed in, so it's safe to build the format ourselves with the correct,
        // currently-loaded VideoCodecsEnum.VP8 and bypass the stale value entirely.
        var videoFormats = new List<VideoFormat> { new VideoFormat(VideoCodecsEnum.VP8, 96, 90000) };
        var videoTrack = new MediaStreamTrack(videoFormats, MediaStreamStatusEnum.SendRecv);
        pc.addTrack(videoTrack);
        var encodedFrameCount = 0;
        videoEncoder.OnVideoSourceEncodedSample += (dur, buf) =>
        {
            if (encodedFrameCount++ < 5)
                HelperLog.Write($"[{sessionId}] Encoded sample #{encodedFrameCount}: {buf.Length} bytes, durationRtpTs={dur}.");
        };
        videoEncoder.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += formats =>
        {
            HelperLog.Write($"[{sessionId}] OnVideoFormatsNegotiated: {string.Join(", ", formats.Select(f => $"{f.Codec}/{f.ClockRate} id={f.FormatID}"))}");
            videoEncoder.SetVideoSourceFormat(formats.First());
        };
        HelperLog.Write($"[{sessionId}] Video track added.");

        var injector = new InputInjector();
        HelperLog.Write($"[{sessionId}] Creating data channel 'input'.");
        var dataChannel = await pc.createDataChannel("input", null);
        HelperLog.Write($"[{sessionId}] Data channel created. id={dataChannel.id}, label={dataChannel.label}, state={dataChannel.readyState}");
        dataChannel.onopen += () => HelperLog.Write($"[{sessionId}] Data channel OPEN.");
        dataChannel.onclose += () => HelperLog.Write($"[{sessionId}] Data channel CLOSED.");
        dataChannel.onerror += err => HelperLog.Write($"[{sessionId}] Data channel ERROR: {err}");
        var moveMsgCount = 0;
        var otherMsgCount = 0;
        dataChannel.onmessage += (_, _, data) =>
        {
            var text = Encoding.UTF8.GetString(data);
            // "move" floods fast enough to crowd out click/key events under a shared cap — log those
            // unconditionally (small cap) and only sample "move" (ADR-0013 input-injection diagnosis).
            var isMove = text.Contains("\"move\"");
            if (isMove) { if (moveMsgCount++ < 5) HelperLog.Write($"[{sessionId}] Input msg (move) #{moveMsgCount}: {text}"); }
            else if (otherMsgCount++ < 30) HelperLog.Write($"[{sessionId}] Input msg (other) #{otherMsgCount}: {text}");
            injector.HandleMessage(text);
        };

        pc.onicecandidate += candidate =>
        {
            if (!string.IsNullOrEmpty(candidate?.candidate))
                _ = SendAsync(new PipeMessage { Type = "localIce", Candidate = candidate.candidate, SdpMid = candidate.sdpMid, SdpMLineIndex = candidate.sdpMLineIndex });
        };

        Task? captureTask = null;
        pc.onconnectionstatechange += state =>
        {
            HelperLog.Write($"[{sessionId}] connection state: {state}");
            if (state == RTCPeerConnectionState.connected)
            {
                captureTask = RunCaptureLoopAsync(sessionId, videoEncoder, sessionCts.Token);
            }
            else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed or RTCPeerConnectionState.disconnected)
            {
                sessionCts.Cancel();
            }
        };
        pc.oniceconnectionstatechange += state => HelperLog.Write($"[{sessionId}] ICE connection state: {state}");

        HelperLog.Write($"[{sessionId}] Creating SDP offer.");
        var offer = pc.createOffer(null);
        await pc.setLocalDescription(offer);
        HelperLog.Write($"[{sessionId}] Offer created, sending over pipe.");
        HelperLog.Write($"[{sessionId}] OFFER SDP:\n{offer.sdp}");
        await SendAsync(new PipeMessage { Type = "offer", Sdp = offer.sdp });
        HelperLog.Write($"[{sessionId}] Offer sent. Entering pipe read loop.");

        try
        {
            while (!sessionCts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(sessionCts.Token);
                if (line is null)
                {
                    HelperLog.Write($"[{sessionId}] Pipe closed by main process (null read) — exiting loop.");
                    break;
                }

                var message = SafeDeserialize(line);
                if (message is null)
                {
                    HelperLog.Write($"[{sessionId}] Ignoring malformed pipe line: {line}");
                    continue;
                }

                switch (message.Type)
                {
                    case "answer" when message.Sdp is not null:
                        HelperLog.Write($"[{sessionId}] Received answer from pipe.");
                        HelperLog.Write($"[{sessionId}] ANSWER SDP:\n{message.Sdp}");
                        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = message.Sdp });
                        HelperLog.Write($"[{sessionId}] setRemoteDescription result: {result}");
                        break;
                    case "remoteIce" when message.Candidate is not null:
                        pc.addIceCandidate(new RTCIceCandidateInit
                        {
                            candidate = message.Candidate,
                            sdpMid = message.SdpMid,
                            sdpMLineIndex = (ushort)Math.Max(0, message.SdpMLineIndex ?? 0),
                        });
                        break;
                    case "stop":
                        HelperLog.Write($"[{sessionId}] Received stop from pipe.");
                        sessionCts.Cancel();
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            HelperLog.Write($"[{sessionId}] Pipe read loop canceled (expected on shutdown).");
        }
        catch (Exception ex)
        {
            HelperLog.Write($"[{sessionId}] Pipe read loop threw: {ex}");
        }
        finally
        {
            sessionCts.Cancel();
            pc.Close("remote control session ended");
            if (captureTask is not null)
                try { await captureTask; } catch (Exception ex) { HelperLog.Write($"[{sessionId}] Capture task teardown threw: {ex}"); }
            banner.Close();
            HelperLog.Write($"[{sessionId}] Session cleanup complete.");
        }
    }

    private static PipeMessage? SafeDeserialize(string line)
    {
        try { return JsonSerializer.Deserialize<PipeMessage>(line, JsonOpts); }
        catch (JsonException) { return null; }
    }

    private static async Task RunCaptureLoopAsync(Guid sessionId, VideoEncoderEndPoint encoder, CancellationToken ct)
    {
        HelperLog.Write($"[{sessionId}] Capture loop starting.");
        using var capture = new ScreenCapture();
        var frameInterval = TimeSpan.FromSeconds(1.0 / CaptureFramesPerSecond);
        var capturedFrameCount = 0;

        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var (bgra, width, height) = capture.Capture();
                if (capturedFrameCount++ < 5)
                {
                    long sum = 0;
                    for (var i = 0; i < bgra.Length; i += 997) sum += bgra[i];
                    HelperLog.Write($"[{sessionId}] Captured frame #{capturedFrameCount}: {width}x{height}, {bgra.Length} bytes, sampled-avg-byte={(double)sum / (bgra.Length / 997 + 1):F1}, " +
                        $"BitBlt ok={capture.LastBitBltOk}, win32err={capture.LastWin32Error}, hdcScreenValid={capture.LastHdcScreenValid}.");
                }
                encoder.ExternalVideoSourceRawSample((uint)frameInterval.TotalMilliseconds, width, height, bgra, VideoPixelFormatsEnum.Bgra);
            }
            catch (Exception ex)
            {
                HelperLog.Write($"[{sessionId}] capture frame failed: {ex}");
            }

            var remaining = frameInterval - sw.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                try { await Task.Delay(remaining, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        HelperLog.Write($"[{sessionId}] Capture loop ended.");
    }
}
