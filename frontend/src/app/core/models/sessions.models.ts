export type SessionStatus = 'Scheduled' | 'Active' | 'Ended' | 'Cancelled';

export interface SessionSummary {
  id: number;
  roomId: number;
  roomName: string;
  classId: number;
  className: string;
  teacherId: number;
  teacherName: string;
  startedAt: string;
  endedAt: string | null;
  status: SessionStatus;
  computerCount: number;
  studentCount: number;
}

export interface SessionComputer {
  computerId: number;
  computerName: string;
  studentId: number | null;
  studentCode: string | null;
  studentName: string | null;
  loginAt: string | null;
  logoutAt: string | null;
}

export interface SessionDetail {
  id: number;
  roomId: number;
  roomName: string;
  classId: number;
  className: string;
  teacherId: number;
  teacherName: string;
  startedAt: string;
  endedAt: string | null;
  status: SessionStatus;
  computers: SessionComputer[];
}

export interface SessionReport {
  id: number;
  className: string;
  roomName: string;
  teacherName: string;
  startedAt: string;
  endedAt: string | null;
  durationMinutes: number;
  computersUsed: number;
  studentsLoggedIn: number;
  studentsEnrolled: number;
  computers: SessionComputer[];
}
