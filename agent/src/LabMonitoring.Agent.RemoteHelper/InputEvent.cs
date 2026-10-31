namespace LabMonitoring.Agent.RemoteHelper;

/// <summary>
/// Wire shape of one mouse/keyboard event sent by the browser over the WebRTC data channel
/// (ADR-0013). Mouse coordinates are normalized to [0,1] of the video element's rendered size so
/// they're independent of the target machine's actual screen resolution; <see cref="InputInjector"/>
/// scales them back up before calling <c>SendInput</c>.
/// </summary>
internal sealed class RemoteInputEvent
{
    public string Type { get; set; } = "";

    // move
    public double? X { get; set; }
    public double? Y { get; set; }

    // down / up — "left" | "right" | "middle"
    public string? Button { get; set; }

    // wheel
    public double? DeltaY { get; set; }

    // key — a Windows virtual-key code. The browser's KeyboardEvent.keyCode lines up with Win32 VK
    // codes for the common alphanumeric/navigation keys; exotic keys and non-US layouts are a known
    // v1 limitation (ADR-0013).
    public bool? Down { get; set; }
    public int? VkCode { get; set; }
}
