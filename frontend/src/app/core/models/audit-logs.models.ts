export interface AuditLogEntry {
  id: number;
  userId: number | null;
  userName: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  detail: string | null;
  ipAddress: string | null;
  createdAt: string;
}

export interface AuditLogFilter {
  userId?: number;
  action?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}
