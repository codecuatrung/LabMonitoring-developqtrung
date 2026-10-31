import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ClassDetail, ClassSummary, Student } from '../models/teaching.models';
import { PagedResult } from '../models/paging.models';

export interface ClassInput {
  code: string;
  name: string;
  subject: string;
  teacherId: number;
  semester: string | null;
}
export interface StudentInput {
  studentCode: string;
  fullName: string;
  email: string | null;
}

@Injectable({ providedIn: 'root' })
export class TeachingService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  // Classes
  classes(params: { teacherId?: number; page?: number; pageSize?: number } = {}): Observable<PagedResult<ClassSummary>> {
    const q = new URLSearchParams();
    if (params.teacherId) q.set('teacherId', String(params.teacherId));
    if (params.page) q.set('page', String(params.page));
    if (params.pageSize) q.set('pageSize', String(params.pageSize));
    const qs = q.toString();
    return this.http.get<PagedResult<ClassSummary>>(`${this.base}/classes${qs ? '?' + qs : ''}`);
  }
  classDetail(id: number): Observable<ClassDetail> {
    return this.http.get<ClassDetail>(`${this.base}/classes/${id}`);
  }
  createClass(input: ClassInput): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.base}/classes`, input);
  }
  updateClass(id: number, input: ClassInput): Observable<void> {
    return this.http.put<void>(`${this.base}/classes/${id}`, input);
  }
  deleteClass(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/classes/${id}`);
  }
  enrollStudent(classId: number, studentId: number): Observable<void> {
    return this.http.post<void>(`${this.base}/classes/${classId}/students/${studentId}`, {});
  }
  unenrollStudent(classId: number, studentId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/classes/${classId}/students/${studentId}`);
  }

  // Students
  students(search?: string): Observable<Student[]> {
    const q = search ? `?search=${encodeURIComponent(search)}` : '';
    return this.http.get<Student[]>(`${this.base}/students${q}`);
  }
  createStudent(input: StudentInput): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(`${this.base}/students`, input);
  }
  deleteStudent(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/students/${id}`);
  }
}
