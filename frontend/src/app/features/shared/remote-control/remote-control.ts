import { Component, ElementRef, EventEmitter, Input, OnDestroy, OnInit, Output, ViewChild, inject, signal } from '@angular/core';
import { LabHubService } from '../../../core/realtime/lab-hub.service';

type SessionPhase = 'connecting' | 'live' | 'ended' | 'error';

interface IceCandidatePayload {
  candidate: string;
  sdpMid: string | null;
  sdpMLineIndex: number | null;
}

/**
 * Live screen view + mouse/keyboard control of one machine over WebRTC (ADR-0013). The Agent's
 * RemoteHelper process is always the SDP *offering* side (it owns the media source) — this
 * component waits for `RemoteControlOffer`, answers it, and exchanges ICE candidates, all relayed
 * through the existing /hubs/lab SignalR hub (`LabHubService`), never a REST endpoint. No STUN/TURN
 * configured — server and lab-room PCs share one LAN broadcast domain (same assumption as
 * Wake-on-LAN), so host ICE candidates alone are enough to connect.
 */
@Component({
  selector: 'app-remote-control',
  templateUrl: './remote-control.html',
})
export class RemoteControlComponent implements OnInit, OnDestroy {
  private readonly hub = inject(LabHubService);

  @Input({ required: true }) computerId!: number;
  @Output() closed = new EventEmitter<void>();

  @ViewChild('remoteVideo') videoRef?: ElementRef<HTMLVideoElement>;

  readonly phase = signal<SessionPhase>('connecting');
  readonly error = signal<string | null>(null);

  private sessionId: string | null = null;
  private pc: RTCPeerConnection | null = null;
  private dataChannel: RTCDataChannel | null = null;

  async ngOnInit(): Promise<void> {
    this.hub.onMany('RemoteControlOffer', (...args) => this.onOffer(args[0] as string, args[1] as string));
    this.hub.onMany('RemoteControlIceCandidate', (...args) => this.onRemoteIce(args[0] as string, args[1] as IceCandidatePayload));
    this.hub.on<string>('RemoteControlEnded', (sessionId) => this.onEnded(sessionId));

    try {
      await this.hub.start();
      this.sessionId = await this.hub.invoke<string>('RequestRemoteControl', this.computerId);
    } catch (e: any) {
      this.phase.set('error');
      this.error.set(e?.message ?? 'Không bắt đầu được phiên điều khiển từ xa');
    }
  }

  ngOnDestroy(): void {
    this.hub.off('RemoteControlOffer');
    this.hub.off('RemoteControlIceCandidate');
    this.hub.off('RemoteControlEnded');
    if (this.sessionId && this.phase() !== 'ended') {
      this.hub.invoke('EndRemoteControl', this.sessionId).catch(() => {});
    }
    this.teardownPeerConnection();
  }

  close(): void {
    this.closed.emit();
  }

  private async onOffer(sessionId: string, sdp: string): Promise<void> {
    if (sessionId !== this.sessionId) return;

    const pc = new RTCPeerConnection({ iceServers: [] });
    this.pc = pc;

    pc.ontrack = (ev) => {
      const video = this.videoRef?.nativeElement;
      if (video && ev.streams[0]) video.srcObject = ev.streams[0];
    };
    pc.onicecandidate = (ev) => {
      if (ev.candidate) {
        this.hub
          .invoke('SubmitIceCandidate', sessionId, ev.candidate.candidate, ev.candidate.sdpMid, ev.candidate.sdpMLineIndex)
          .catch(() => {});
      }
    };
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'connected') this.phase.set('live');
      else if (pc.connectionState === 'failed' || pc.connectionState === 'closed') this.phase.set('ended');
    };
    pc.ondatachannel = (ev) => {
      this.dataChannel = ev.channel;
      this.wireInputEvents();
    };

    await pc.setRemoteDescription({ type: 'offer', sdp });
    const answer = await pc.createAnswer();
    await pc.setLocalDescription(answer);
    await this.hub.invoke('SubmitAnswer', sessionId, answer.sdp);
  }

  private async onRemoteIce(sessionId: string, payload: IceCandidatePayload): Promise<void> {
    if (sessionId !== this.sessionId || !this.pc || !payload.candidate) return;
    try {
      await this.pc.addIceCandidate({
        candidate: payload.candidate,
        sdpMid: payload.sdpMid ?? undefined,
        sdpMLineIndex: payload.sdpMLineIndex ?? undefined,
      });
    } catch {
      // a candidate can legitimately arrive after the connection has already moved on — ignore
    }
  }

  private onEnded(sessionId: string): void {
    if (sessionId !== this.sessionId) return;
    this.phase.set('ended');
    this.teardownPeerConnection();
  }

  private teardownPeerConnection(): void {
    this.dataChannel?.close();
    this.dataChannel = null;
    this.pc?.close();
    this.pc = null;
  }

  private wireInputEvents(): void {
    const video = this.videoRef?.nativeElement;
    const dc = this.dataChannel;
    if (!video || !dc) return;

    const send = (payload: unknown) => {
      if (dc.readyState === 'open') dc.send(JSON.stringify(payload));
    };
    const normalized = (ev: MouseEvent) => {
      const rect = video.getBoundingClientRect();
      return { x: (ev.clientX - rect.left) / rect.width, y: (ev.clientY - rect.top) / rect.height };
    };
    const button = (ev: MouseEvent) => (ev.button === 2 ? 'right' : ev.button === 1 ? 'middle' : 'left');

    video.addEventListener('mousemove', (ev) => send({ type: 'move', ...normalized(ev) }));
    video.addEventListener('mousedown', (ev) => {
      ev.preventDefault();
      send({ type: 'down', button: button(ev) });
    });
    video.addEventListener('mouseup', (ev) => {
      ev.preventDefault();
      send({ type: 'up', button: button(ev) });
    });
    video.addEventListener('contextmenu', (ev) => ev.preventDefault());
    video.addEventListener(
      'wheel',
      (ev) => {
        ev.preventDefault();
        send({ type: 'wheel', deltaY: ev.deltaY });
      },
      { passive: false },
    );
    video.setAttribute('tabindex', '0');
    video.addEventListener('keydown', (ev) => {
      ev.preventDefault();
      send({ type: 'key', down: true, vkCode: ev.keyCode });
    });
    video.addEventListener('keyup', (ev) => {
      ev.preventDefault();
      send({ type: 'key', down: false, vkCode: ev.keyCode });
    });
  }
}
