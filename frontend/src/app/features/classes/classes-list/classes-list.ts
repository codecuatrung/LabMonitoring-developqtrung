import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ClassInput, TeachingService } from '../../../core/api/teaching.service';
import { UserSummary, UsersService } from '../../../core/api/users.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { ClassSummary, Student } from '../../../core/models/teaching.models';
import { PaginationComponent } from '../../shared/pagination/pagination';

@Component({
  selector: 'app-classes-list',
  imports: [FormsModule, RouterLink, PaginationComponent],
  templateUrl: './classes-list.html',
})
export class ClassesList implements OnInit {
  private readonly teaching = inject(TeachingService);
  private readonly usersApi = inject(UsersService);
  private readonly pageTitle = inject(PageTitleService);

  readonly classes = signal<ClassSummary[]>([]);
  readonly totalCount = signal(0);
  readonly students = signal<Student[]>([]);
  readonly teachers = signal<UserSummary[]>([]);
  readonly error = signal<string | null>(null);

  page = 1;
  pageSize = 20;

  newClass: ClassInput = { code: '', name: '', subject: '', teacherId: 0, semester: null };
  newStudent = { studentCode: '', fullName: '', email: '' };

  ngOnInit(): void {
    this.pageTitle.set('Lớp học & Sinh viên');
    this.load();
    // Teacher picker for the "new class" form needs the full active-user list, not a page of it.
    this.usersApi.list({ pageSize: 500 }).subscribe({ next: (r) => this.teachers.set(r.items) });
    this.teaching.students().subscribe({ next: (s) => this.students.set(s) });
  }

  load(): void {
    this.teaching.classes({ page: this.page, pageSize: this.pageSize }).subscribe({
      next: (r) => {
        this.classes.set(r.items);
        this.totalCount.set(r.totalCount);
      },
      error: (e) => this.fail(e),
    });
  }

  onPageChange(page: number): void {
    this.page = page;
    this.load();
  }

  onPageSizeChange(pageSize: number): void {
    this.pageSize = pageSize;
    this.page = 1;
    this.load();
  }

  addClass(): void {
    this.teaching.createClass(this.newClass).subscribe({
      next: () => {
        this.newClass = { code: '', name: '', subject: '', teacherId: this.newClass.teacherId, semester: null };
        this.load();
      },
      error: (e) => this.fail(e),
    });
  }

  deleteClass(c: ClassSummary): void {
    this.teaching.deleteClass(c.id).subscribe({ next: () => this.load(), error: (e) => this.fail(e) });
  }

  addStudent(): void {
    this.teaching
      .createStudent({
        studentCode: this.newStudent.studentCode,
        fullName: this.newStudent.fullName,
        email: this.newStudent.email || null,
      })
      .subscribe({
        next: () => {
          this.newStudent = { studentCode: '', fullName: '', email: '' };
          this.teaching.students().subscribe({ next: (s) => this.students.set(s) });
        },
        error: (e) => this.fail(e),
      });
  }

  private fail(e: unknown): void {
    const err = e as { error?: { detail?: string }; message?: string };
    this.error.set(err?.error?.detail ?? err?.message ?? 'Thao tác thất bại');
  }
}
