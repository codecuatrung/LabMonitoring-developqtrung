export interface ClassSummary {
  id: number;
  code: string;
  name: string;
  subject: string;
  teacherId: number;
  teacherName: string;
  semester: string | null;
  studentCount: number;
}

export interface Student {
  id: number;
  studentCode: string;
  fullName: string;
  email: string | null;
}

export interface ClassDetail {
  id: number;
  code: string;
  name: string;
  subject: string;
  teacherId: number;
  teacherName: string;
  semester: string | null;
  students: Student[];
}
