export type ComputerStatus = 'Online' | 'Offline' | 'Warning' | 'InUse';

export interface DashboardSummary {
  rooms: number;
  computers: number;
  online: number;
  offline: number;
  warning: number;
  inUse: number;
  pendingApproval: number;
}

export interface Building {
  id: number;
  name: string;
  code: string;
  roomCount: number;
}

export interface Room {
  id: number;
  buildingId: number;
  buildingName: string;
  name: string;
  code: string;
  capacity: number;
  mapImageUrl: string | null;
  computerCount: number;
  /** Live status mix of the room's approved machines (Offline = the rest). */
  onlineCount: number;
  inUseCount: number;
  warningCount: number;
  /** Registered but not yet approved. */
  pendingCount: number;
}

export interface RoomComputer {
  id: number;
  name: string;
  status: ComputerStatus;
  isApproved: boolean;
  isActive: boolean;
  positionX: number | null;
  positionY: number | null;
  currentWindowsUser: string | null;
  cpuUsagePercent: number | null;
  ramUsagePercent: number | null;
  lastHeartbeatAt: string | null;
}

export interface RoomDetail {
  id: number;
  name: string;
  code: string;
  capacity: number;
  mapImageUrl: string | null;
  buildingId: number;
  buildingName: string;
  computers: RoomComputer[];
}

export interface ComputerSpec {
  cpuModel: string | null;
  cpuCores: number | null;
  ramTotalMB: number | null;
  diskTotalGB: number | null;
  osVersion: string | null;
  updatedAt: string;
}

export interface ComputerDetail {
  id: number;
  roomId: number;
  roomName: string;
  name: string;
  machineId: string;
  status: ComputerStatus;
  isApproved: boolean;
  isActive: boolean;
  macAddress: string | null;
  positionX: number | null;
  positionY: number | null;
  agentVersion: string | null;
  createdAt: string;
  currentWindowsUser: string | null;
  lastIpAddress: string | null;
  lastHeartbeatAt: string | null;
  spec: ComputerSpec | null;
}

export interface MetricPoint {
  recordedAt: string;
  cpuUsagePercent: number | null;
  ramUsagePercent: number | null;
  diskUsagePercent: number | null;
  temperatureC: number | null;
}

/** SignalR event payloads (SRS 8.4). */
export interface ComputerStatusChangedEvent {
  computerId: number;
  roomId: number;
  status: ComputerStatus;
  currentWindowsUser: string | null;
  lastHeartbeatAt: string | null;
}

export interface MetricsUpdatedEvent {
  computerId: number;
  roomId: number;
  cpuUsagePercent: number | null;
  ramUsagePercent: number | null;
  diskUsagePercent: number | null;
  temperatureC: number | null;
  recordedAt: string;
}

export interface AlertEvent {
  roomId: number | null;
  computerId: number | null;
  severity: string;
  title: string;
  message: string;
  createdAt: string;
}
