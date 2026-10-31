import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TeachingService } from '../../../core/api/teaching.service';
import { PageTitleService } from '../../../core/layout/page-title.service';
import { ClassDetail, Student } from '../../../core/models/teaching.models';

@Component({
  selector: 'app-class-detail',
  imports: [FormsModule, RouterLink],
  templateUrl: './class-detail.html',
})
export class ClassDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly teaching = inject(TeachingService);
  private readonly pageTitle = inject(PageTitleService);

  readonly cls = signal<ClassDetail | null>(null);
  readonly candidates = signal<Student[]>([]);
  readonly error = signal<string | null>(null);
  selectedStudentId = 0;

  private classId = 0;

  ngOnInit(): void {
    this.classId = Number(this.route.snapshot.paramMap.get('id'));
    this.reload();
  }

  reload(): void {
    this.teaching.classDetail(this.classId).subscribe({
      next: (c) => {
        this.cls.set(c);
        this.pageTitle.set(`${c.name} (${c.code})`);
        this.teaching.students().subscribe({
          next: (all) => this.candidates.set(all.filter((s) => !c.students.some((cs) => cs.id === s.id))),
        });
      },
      error: (e) => this.fail(e),
    });
  }

  enroll(): void {
    if (!this.selectedStudentId) return;
    this.teaching.enrollStudent(this.classId, this.selectedStudentId).subscribe({
      next: () => {
        this.selectedStudentId = 0;
        this.reload();
      },
      error: (e) => this.fail(e),
    });
  }

  unenroll(studentId: number): void {
    this.teaching.unenrollStudent(this.classId, studentId).subscribe({ next: () => this.reload(), error: (e) => this.fail(e) });
  }

  private fail(e: unknown): void {
    const err = e as { error?: { detail?: string }; message?: string };
    this.error.set(err?.error?.detail ?? err?.message ?? 'Thao tác thất bại');
  }
}
