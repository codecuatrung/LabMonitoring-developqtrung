import { Injectable, inject, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

export type HubStatus = 'disconnected' | 'connecting' | 'connected';

/**
 * Client for the /hubs/lab SignalR hub (SRS 8.4). Feature modules call `on(...)` to subscribe
 * to server events and `subscribeToRoom(id)` to receive a room's updates.
 */
@Injectable({ providedIn: 'root' })
export class LabHubService {
  private readonly auth = inject(AuthService);
  private connection?: HubConnection;

  readonly status = signal<HubStatus>('disconnected');

  async start(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      return;
    }

    this.connection = new HubConnectionBuilder()
      .withUrl(environment.hubUrl, { accessTokenFactory: () => this.auth.accessToken })
      .withAutomaticReconnect()
      .configureLogging(environment.production ? LogLevel.Warning : LogLevel.Information)
      .build();

    // Matches the server's ClientTimeoutInterval (RealtimeExtensions.cs) and the Agent's own
    // ServerTimeout (AgentHubClient.cs) — the default 30s is tight enough that dev-server jitter
    // (dotnet watch) or ordinary network latency was flapping the "realtime" status needlessly.
    this.connection.serverTimeoutInMilliseconds = 60_000;

    this.connection.onreconnecting(() => this.status.set('connecting'));
    this.connection.onreconnected(() => this.status.set('connected'));
    this.connection.onclose(() => this.status.set('disconnected'));

    this.status.set('connecting');
    try {
      await this.connection.start();
      this.status.set('connected');
    } catch (err) {
      console.error('SignalR hub connection failed', err);
      this.status.set('disconnected');
    }
  }

  async stop(): Promise<void> {
    await this.connection?.stop();
    this.status.set('disconnected');
  }

  on<T = unknown>(event: string, handler: (payload: T) => void): void {
    this.connection?.on(event, handler as (...args: unknown[]) => void);
  }

  /** Like `on`, but for hub events that pass more than one argument (remote control signaling — ADR-0013). */
  onMany(event: string, handler: (...args: unknown[]) => void): void {
    this.connection?.on(event, handler);
  }

  off(event: string): void {
    this.connection?.off(event);
  }

  subscribeToRoom(roomId: number): Promise<void> {
    return this.connection?.invoke('SubscribeToRoom', roomId) ?? Promise.resolve();
  }

  unsubscribeFromRoom(roomId: number): Promise<void> {
    return this.connection?.invoke('UnsubscribeFromRoom', roomId) ?? Promise.resolve();
  }

  /** Generic hub method call (used by remote control — ADR-0013 — for its 5 signaling methods). */
  invoke<T = void>(method: string, ...args: unknown[]): Promise<T> {
    if (!this.connection) return Promise.reject(new Error('Hub is not connected.'));
    return this.connection.invoke<T>(method, ...args);
  }
}
