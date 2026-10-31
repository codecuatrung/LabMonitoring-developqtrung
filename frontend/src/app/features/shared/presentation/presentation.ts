import { Component, EventEmitter, Input, OnDestroy, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LabHubService } from '../../../core/realtime/lab-hub.service';

const MAX_WIDTH = 1280;
const CAPTURE_INTERVAL_MS = 250;
const KEEPALIVE_MS = 2000;
const JPEG_QUALITY = 0.6;
// Change detection runs on a tiny thumbnail: a frame is only re-sent when enough pixels moved,
// so a static slide costs one keepalive frame every couple of seconds instead of a video stream.
const THUMB_W = 192;
const THUMB_H = 108;
const PIXEL_DIFF = 40;
const CHANGED_PIXELS = 3;

/**
 * Presents this browser's screen (getDisplayMedia) to every machine in a room: a JPEG of the shared
 * screen is pushed through the hub whenever it changes (plus a keepalive that doubles as the server's
 * and Agents' dead-man signal). The room's machines show it full-screen and — when "lock" is on —
 * ignore mouse/keyboard until the presentation ends. Closing the tab or navigating away ends it.
 */
@Component({
  selector: 'app-presentation',
  imports: [FormsModule],
  template: `
    <div class="modal d-block" tabindex="-1" style="background: rgba(0,0,0,0.5)">
      <div class="modal-dialog modal-dialog-centered">
        <div class="modal-content">
          <div class="modal-header">
            <h5 class="modal-title"><i class="bi bi-easel"></i> Trình chiếu tới phòng máy</h5>
          </div>
          <div class="modal-body">
            @if (error(); as e) {
              <div class="alert alert-danger py-2 small">{{ e }}</div>
            }
            @if (!running()) {
              <p class="mb-2">
                Chia sẻ một màn hình hoặc cửa sổ (ví dụ bài PowerPoint đang trình chiếu). Toàn bộ máy đã duyệt
                trong phòng sẽ hiển thị toàn màn hình nội dung đó.
              </p>
              <div class="form-check">
                <input class="form-check-input" type="checkbox" id="lockInput" [(ngModel)]="lockInput" />
                <label class="form-check-label" for="lockInput">Khóa chuột và bàn phím của máy khách trong lúc trình chiếu</label>
              </div>
            } @else {
              <div class="alert alert-success py-2 mb-2">
                <i class="bi bi-broadcast"></i> Đang trình chiếu{{ lockInput ? ' (đã khóa thao tác máy khách)' : '' }}.
              </div>
              <div class="small text-muted">Đã gửi {{ framesSent() }} khung hình · {{ kilobytesSent() }} KB</div>
              <div class="small text-muted mt-1">Đóng tab hoặc bấm "Dừng chia sẻ" của trình duyệt cũng sẽ kết thúc trình chiếu.</div>
            }
          </div>
          <div class="modal-footer">
            @if (!running()) {
              <button class="btn btn-secondary" type="button" [disabled]="busy()" (click)="closed.emit()">Đóng</button>
              <button class="btn btn-primary" type="button" [disabled]="busy()" (click)="start()">
                <i class="bi bi-display"></i> Chọn màn hình & bắt đầu
              </button>
            } @else {
              <button class="btn btn-danger" type="button" [disabled]="busy()" (click)="stop()">
                <i class="bi bi-stop-circle"></i> Kết thúc trình chiếu
              </button>
            }
          </div>
        </div>
      </div>
    </div>
  `,
})
export class PresentationComponent implements OnDestroy {
  private readonly hub = inject(LabHubService);

  @Input({ required: true }) roomId!: number;
  @Output() closed = new EventEmitter<void>();

  lockInput = true;
  readonly running = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly framesSent = signal(0);
  readonly kilobytesSent = signal(0);

  private sessionId: string | null = null;
  private stream: MediaStream | null = null;
  private timer: ReturnType<typeof setInterval> | null = null;
  private video: HTMLVideoElement | null = null;
  private readonly canvas = document.createElement('canvas');
  private readonly thumb = document.createElement('canvas');
  private lastThumb: Uint8ClampedArray | null = null;
  private lastSentAt = 0;
  private sending = false;

  async start(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      this.stream = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: 5 }, audio: false });
    } catch {
      this.busy.set(false);
      this.error.set('Bạn chưa chọn màn hình để chia sẻ.');
      return;
    }

    try {
      await this.hub.start();
      this.sessionId = await this.hub.invoke<string>('StartPresentation', this.roomId, this.lockInput);
    } catch (e) {
      this.releaseStream();
      this.busy.set(false);
      this.error.set(this.messageOf(e, 'Không bắt đầu được trình chiếu.'));
      return;
    }

    this.video = document.createElement('video');
    this.video.muted = true;
    this.video.srcObject = this.stream;
    await this.video.play();
    this.stream.getVideoTracks()[0].addEventListener('ended', () => void this.stop());
    this.hub.onMany('PresentationEnded', (id) => {
      if (id === this.sessionId) this.teardownLocal();
    });

    this.running.set(true);
    this.busy.set(false);
    this.timer = setInterval(() => void this.tick(), CAPTURE_INTERVAL_MS);
  }

  async stop(): Promise<void> {
    const id = this.sessionId;
    this.teardownLocal();
    if (id) {
      try { await this.hub.invoke('StopPresentation', id); } catch { /* the server ends idle sessions itself */ }
    }
    this.closed.emit();
  }

  ngOnDestroy(): void {
    const id = this.sessionId;
    this.teardownLocal();
    if (id) void this.hub.invoke('StopPresentation', id).catch(() => undefined);
    this.hub.off('PresentationEnded');
  }

  private teardownLocal(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.releaseStream();
    this.sessionId = null;
    this.running.set(false);
  }

  private releaseStream(): void {
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
    this.video = null;
  }

  private async tick(): Promise<void> {
    const video = this.video;
    if (!video || !this.sessionId || this.sending || video.videoWidth === 0) return;

    const scale = Math.min(1, MAX_WIDTH / video.videoWidth);
    const w = Math.round(video.videoWidth * scale);
    const h = Math.round(video.videoHeight * scale);
    this.canvas.width = w;
    this.canvas.height = h;
    const ctx = this.canvas.getContext('2d');
    if (!ctx) return;
    ctx.drawImage(video, 0, 0, w, h);

    const changed = this.hasChanged();
    const now = Date.now();
    if (!changed && now - this.lastSentAt < KEEPALIVE_MS) return;

    this.sending = true;
    try {
      const blob = await new Promise<Blob | null>((res) => this.canvas.toBlob(res, 'image/jpeg', JPEG_QUALITY));
      if (!blob || !this.sessionId) return;
      const base64 = await this.toBase64(blob);
      await this.hub.invoke('SendPresentationFrame', this.sessionId, base64);
      this.lastSentAt = now;
      this.framesSent.update((n) => n + 1);
      this.kilobytesSent.update((k) => k + Math.round(blob.size / 1024));
    } catch (e) {
      this.error.set(this.messageOf(e, 'Mất kết nối khi gửi hình.'));
    } finally {
      this.sending = false;
    }
  }

  private hasChanged(): boolean {
    this.thumb.width = THUMB_W;
    this.thumb.height = THUMB_H;
    const tctx = this.thumb.getContext('2d', { willReadFrequently: true });
    if (!tctx) return true;
    tctx.drawImage(this.canvas, 0, 0, THUMB_W, THUMB_H);
    const data = tctx.getImageData(0, 0, THUMB_W, THUMB_H).data;

    const prev = this.lastThumb;
    this.lastThumb = data;
    if (!prev) return true;

    let changed = 0;
    for (let i = 0; i < data.length; i += 4) {
      const d = Math.abs(data[i] - prev[i]) + Math.abs(data[i + 1] - prev[i + 1]) + Math.abs(data[i + 2] - prev[i + 2]);
      if (d > PIXEL_DIFF && ++changed >= CHANGED_PIXELS) return true;
    }
    return false;
  }

  private toBase64(blob: Blob): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '');
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(blob);
    });
  }

  private messageOf(e: unknown, fallback: string): string {
    const m = (e as { message?: string })?.message ?? '';
    // SignalR wraps hub exceptions as "An unexpected error occurred invoking 'X' on the server. HubException: <msg>".
    const hubMessage = m.split('HubException:')[1]?.trim();
    return hubMessage || fallback;
  }
}
