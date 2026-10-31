import { Component, Input, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CommandsService } from '../../../core/api/commands.service';
import { CommandKind, CommandResultEvent, WakeResult } from '../../../core/models/commands.models';
import { commandKindLabel, commandStatusClass, commandStatusLabel } from '../../../core/models/status-ui';
import { RemoteControlComponent } from '../remote-control/remote-control';
import { PresentationComponent } from '../presentation/presentation';

export type CommandTarget = { computerId: number } | { roomId: number };

/**
 * Issues Lock/Restart/Shutdown/Message/OpenApp/OpenUrl commands to one machine or a whole room
 * (SRS FR-4). Room-wide Restart/Shutdown requires an explicit confirm step (FR-4.3) — a custom
 * Bootstrap modal, not the native browser confirm() which would block the SignalR connection.
 *
 * Recent results are fed by the parent (`[results]`), which already owns the room's hub
 * subscription — this keeps a single SignalR `.on('CommandResultReceived', …)` registration per
 * page instead of each panel instance registering (and later unregistering, via `.off()`) its own,
 * which would clobber a sibling's handler since SignalR's `.off(event)` removes all handlers for
 * that event name.
 */
@Component({
  selector: 'app-command-panel',
  imports: [FormsModule, DatePipe, RemoteControlComponent, PresentationComponent],
  templateUrl: './command-panel.html',
})
export class CommandPanelComponent {
  private readonly commands = inject(CommandsService);

  @Input({ required: true }) target!: CommandTarget;
  @Input() set results(value: CommandResultEvent[]) {
    const scoped = 'computerId' in this.target
      ? value.filter((r) => r.computerId === (this.target as { computerId: number }).computerId)
      : value;
    this.recentResults.set(scoped.slice(0, 20));
  }

  readonly commandKindLabel = commandKindLabel;
  readonly commandStatusClass = commandStatusClass;
  readonly commandStatusLabel = commandStatusLabel;

  readonly isRoomWide = computed(() => !('computerId' in this.target));
  readonly targetComputerId = computed(() => ('computerId' in this.target ? this.target.computerId : undefined));
  readonly recentResults = signal<CommandResultEvent[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly lastIssued = signal<{ commandId: number; targetCount: number } | null>(null);
  readonly lastWake = signal<WakeResult | null>(null);

  messageText = '';
  appPath = '';
  url = '';

  confirmKind: 'Restart' | 'Shutdown' | 'UpdateAgent' | null = null;

  // Plain method, not computed() — confirmKind is a plain field (reassigned directly, not through a
  // signal setter), and computed() only re-evaluates off signal reads, not plain-field mutation.
  confirmVerb(): string {
    switch (this.confirmKind) {
      case 'Restart': return 'khởi động lại';
      case 'Shutdown': return 'tắt';
      case 'UpdateAgent': return 'cập nhật Agent trên';
      default: return '';
    }
  }
  readonly remoteControlOpen = signal(false);
  readonly presentationOpen = signal(false);
  readonly roomId = computed(() => ('roomId' in this.target ? this.target.roomId : undefined));

  wake(): void {
    this.error.set(null);
    this.lastWake.set(null);
    this.busy.set(true);
    this.commands
      .wake({
        computerId: 'computerId' in this.target ? this.target.computerId : undefined,
        roomId: 'roomId' in this.target ? this.target.roomId : undefined,
      })
      .subscribe({
        next: (res) => {
          this.busy.set(false);
          this.lastWake.set(res);
        },
        error: (e) => {
          this.busy.set(false);
          this.error.set(e?.error?.detail ?? e?.message ?? 'Không gửi được tín hiệu bật máy');
        },
      });
  }

  lock(): void {
    this.send({ commandType: 'Lock' });
  }

  restart(): void {
    if (this.isRoomWide()) {
      this.confirmKind = 'Restart';
      return;
    }
    this.send({ commandType: 'Restart' });
  }

  shutdown(): void {
    if (this.isRoomWide()) {
      this.confirmKind = 'Shutdown';
      return;
    }
    this.send({ commandType: 'Shutdown' });
  }

  updateAgent(): void {
    if (this.isRoomWide()) {
      this.confirmKind = 'UpdateAgent';
      return;
    }
    this.send({ commandType: 'UpdateAgent' });
  }

  confirmRoomWide(): void {
    if (!this.confirmKind) return;
    this.send({ commandType: this.confirmKind, confirm: true });
    this.confirmKind = null;
  }

  cancelConfirm(): void {
    this.confirmKind = null;
  }

  sendMessage(): void {
    if (!this.messageText.trim()) return;
    this.send({ commandType: 'Message', text: this.messageText.trim() });
    this.messageText = '';
  }

  openApp(): void {
    if (!this.appPath.trim()) return;
    this.send({ commandType: 'OpenApp', executablePath: this.appPath.trim() });
    this.appPath = '';
  }

  openUrl(): void {
    if (!this.url.trim()) return;
    this.send({ commandType: 'OpenUrl', url: this.url.trim() });
    this.url = '';
  }

  private send(input: { commandType: CommandKind; text?: string; executablePath?: string; url?: string; confirm?: boolean }): void {
    this.error.set(null);
    this.busy.set(true);
    this.commands
      .issue({
        ...input,
        computerId: 'computerId' in this.target ? this.target.computerId : undefined,
        roomId: 'roomId' in this.target ? this.target.roomId : undefined,
      })
      .subscribe({
        next: (res) => {
          this.busy.set(false);
          this.lastIssued.set(res);
        },
        error: (e) => {
          this.busy.set(false);
          this.error.set(e?.error?.detail ?? e?.message ?? 'Không gửi được lệnh');
        },
      });
  }
}
