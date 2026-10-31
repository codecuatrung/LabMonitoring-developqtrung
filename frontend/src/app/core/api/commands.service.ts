import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CommandResultDto, IssueCommandInput, IssueCommandResult, WakeInput, WakeResult } from '../models/commands.models';

/** SRS FR-4: signed room/machine control commands (Lock/Restart/Shutdown/Message/OpenApp/OpenUrl). */
@Injectable({ providedIn: 'root' })
export class CommandsService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  issue(input: IssueCommandInput): Observable<IssueCommandResult> {
    return this.http.post<IssueCommandResult>(`${this.base}/commands`, input);
  }

  results(commandId: number): Observable<CommandResultDto[]> {
    return this.http.get<CommandResultDto[]>(`${this.base}/commands/${commandId}/results`);
  }

  wake(input: WakeInput): Observable<WakeResult> {
    return this.http.post<WakeResult>(`${this.base}/commands/wake`, input);
  }
}
