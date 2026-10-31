export type NotificationSeverity = 'Info' | 'Warning' | 'Critical';

export interface AppNotification {
  id: number;
  title: string;
  message: string;
  severity: NotificationSeverity;
  isRead: boolean;
  createdAt: string;
}

/** SignalR event payload for RealtimeEvents.NotificationCreated (SRS 8.4/FR-7.2). */
export interface NotificationCreatedEvent {
  id: number;
  title: string;
  message: string;
  severity: NotificationSeverity;
  createdAt: string;
}
