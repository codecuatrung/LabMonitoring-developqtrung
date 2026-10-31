import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';
import { environment } from '../../../environments/environment';

export type TransferDirection = 'Push' | 'Collect';

export interface TransferFolder {
  id: number;
  roomId: number | null;
  roomName: string | null;
  path: string;
  direction: TransferDirection;
  label: string;
  isActive: boolean;
}

export interface SaveTransferFolderInput {
  roomId: number | null;
  path: string;
  direction: TransferDirection;
  label: string;
  isActive: boolean;
}

export interface FileTransferSummary {
  id: number;
  direction: TransferDirection;
  roomId: number | null;
  roomName: string | null;
  computerId: number | null;
  computerName: string | null;
  folderPath: string;
  fileName: string | null;
  sizeBytes: number;
  filter: string | null;
  overwrite: boolean;
  commandId: number | null;
  createdByName: string | null;
  createdAt: string;
}

export interface TransferTargetResult {
  computerId: number;
  computerName: string;
  status: 'Sent' | 'Acknowledged' | 'Executed' | 'Failed' | 'Timeout';
  executedAt: string | null;
  errorMessage: string | null;
  collectedBytes: number | null;
  collectedFileCount: number | null;
  collectedTruncated: boolean | null;
}

export interface FileTransferDetail {
  transfer: FileTransferSummary;
  targets: TransferTargetResult[];
}

export interface IssuedTransfer {
  transferId: number;
  commandId: number;
  targetCount: number;
}

/** Progress of an upload: `percent` while sending, `result` once the server answered. */
export type PushProgress = { percent: number; result?: undefined } | { percent: 100; result: IssuedTransfer };

/** Copy files to client machines / collect folders back (file transfer). Allowed folders are managed separately. */
@Injectable({ providedIn: 'root' })
export class FileTransfersService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  // ---- allowed folders ----
  folders(params: { roomId?: number; direction?: TransferDirection; includeInactive?: boolean } = {}): Observable<TransferFolder[]> {
    const q = new URLSearchParams();
    if (params.roomId != null) q.set('roomId', String(params.roomId));
    if (params.direction) q.set('direction', params.direction);
    if (params.includeInactive) q.set('includeInactive', 'true');
    const qs = q.toString();
    return this.http.get<TransferFolder[]>(`${this.base}/transfer-folders${qs ? '?' + qs : ''}`);
  }

  createFolder(input: SaveTransferFolderInput): Observable<number> {
    return this.http.post<number>(`${this.base}/transfer-folders`, input);
  }

  updateFolder(id: number, input: SaveTransferFolderInput): Observable<number> {
    return this.http.put<number>(`${this.base}/transfer-folders/${id}`, input);
  }

  deleteFolder(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/transfer-folders/${id}`);
  }

  // ---- transfers ----
  list(params: { roomId?: number; direction?: TransferDirection } = {}): Observable<FileTransferSummary[]> {
    const q = new URLSearchParams();
    if (params.roomId != null) q.set('roomId', String(params.roomId));
    if (params.direction) q.set('direction', params.direction);
    const qs = q.toString();
    return this.http.get<FileTransferSummary[]>(`${this.base}/file-transfers${qs ? '?' + qs : ''}`);
  }

  get(id: number): Observable<FileTransferDetail> {
    return this.http.get<FileTransferDetail>(`${this.base}/file-transfers/${id}`);
  }

  push(input: {
    file: File;
    folderId: number;
    overwrite: boolean;
    confirm: boolean;
    roomId?: number;
    computerId?: number;
  }): Observable<PushProgress> {
    const form = new FormData();
    form.append('FolderId', String(input.folderId));
    form.append('Overwrite', String(input.overwrite));
    form.append('Confirm', String(input.confirm));
    if (input.roomId != null) form.append('RoomId', String(input.roomId));
    if (input.computerId != null) form.append('ComputerId', String(input.computerId));
    form.append('File', input.file, input.file.name);

    return this.http
      .post<IssuedTransfer>(`${this.base}/file-transfers/push`, form, { reportProgress: true, observe: 'events' })
      .pipe(
        filter((e: HttpEvent<IssuedTransfer>) => e.type === HttpEventType.UploadProgress || e.type === HttpEventType.Response),
        map((e): PushProgress =>
          e.type === HttpEventType.Response
            ? { percent: 100, result: e.body as IssuedTransfer }
            : { percent: e.total ? Math.round((100 * e.loaded) / e.total) : 0 },
        ),
      );
  }

  collect(input: { folderId: number; filter: string | null; confirm: boolean; roomId?: number; computerId?: number }): Observable<IssuedTransfer> {
    return this.http.post<IssuedTransfer>(`${this.base}/file-transfers/collect`, {
      folderId: input.folderId,
      filter: input.filter,
      confirm: input.confirm,
      roomId: input.roomId ?? null,
      computerId: input.computerId ?? null,
    });
  }

  /** Downloads one machine's collected zip through HttpClient (so the bearer token is attached) and saves it. */
  downloadCollected(transferId: number, computerId: number): Observable<Blob> {
    return this.http.get(`${this.base}/file-transfers/${transferId}/collected/${computerId}`, { responseType: 'blob' });
  }
}
