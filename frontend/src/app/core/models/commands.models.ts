export type CommandKind = 'Lock' | 'Restart' | 'Shutdown' | 'Message' | 'OpenApp' | 'OpenUrl' | 'UpdateAgent' | 'StartPresentation' | 'StopPresentation' | 'PushFile' | 'CollectFiles';
export type CommandResultStatus = 'Sent' | 'Acknowledged' | 'Executed' | 'Failed' | 'Timeout';

export interface IssueCommandInput {
  computerId?: number;
  roomId?: number;
  sessionId?: number;
  commandType: CommandKind;
  text?: string;
  executablePath?: string;
  url?: string;
  confirm?: boolean;
}

export interface IssueCommandResult {
  commandId: number;
  targetCount: number;
}

export interface CommandResultDto {
  id: number;
  commandId: number;
  computerId: number;
  computerName: string;
  status: CommandResultStatus;
  executedAt: string | null;
  errorMessage: string | null;
}

/** SignalR event payload for RealtimeEvents.CommandResultReceived (SRS 8.4). */
export interface CommandResultEvent {
  commandId: number;
  computerId: number;
  roomId: number;
  status: CommandResultStatus;
  errorMessage: string | null;
  executedAt: string | null;
}

export interface WakeInput {
  computerId?: number;
  roomId?: number;
}

/** Not a signed Agent command — a raw Wake-on-LAN broadcast, so there's no ack/result lifecycle,
 * just how many targets got a packet vs. were skipped for missing a recorded MAC address. */
export interface WakeResult {
  targetCount: number;
  sentCount: number;
  skippedComputerIds: number[];
}
