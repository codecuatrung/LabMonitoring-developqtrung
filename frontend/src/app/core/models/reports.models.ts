export interface UsageReportRoomRow {
  roomId: number;
  roomName: string;
  sessionCount: number;
  machineHours: number;
  studentCount: number;
}

export interface UsageReportFilter {
  roomId?: number;
  classId?: number;
  from?: string;
  to?: string;
}

export interface UsageReport {
  from: string;
  to: string;
  totalSessions: number;
  totalMachineHours: number;
  totalStudents: number;
  rows: UsageReportRoomRow[];
}
