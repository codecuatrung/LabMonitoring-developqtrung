export type Entity = { id: string; [key: string]: string | number | boolean | undefined }
export type Collection = 'teachers' | 'classes' | 'rooms' | 'equipment' | 'subjects' | 'timeSlots' | 'schedules' | 'sessions' | 'shifts' | 'requests' | 'users' | 'permissions' | 'dutyTemplates' | 'academicTerms' | 'notifications' | 'auditLogs'

export const seed: Record<Collection, Entity[]> = {
  teachers: [
    { id: 'gv1', name: 'Nguyễn Minh Anh', email: 'minhanh@lab.edu.vn', department: 'Điện - Điện tử', phone: '0901 234 567', status: 'Đang hoạt động', role: 'Giáo viên' },
    { id: 'gv2', name: 'Trần Quốc Bảo', email: 'quocbao@lab.edu.vn', department: 'Công nghệ thông tin', phone: '0902 345 678', status: 'Đang hoạt động', role: 'Giáo viên' },
    { id: 'gv3', name: 'Lê Thu Hà', email: 'thuha@lab.edu.vn', department: 'Cơ khí', phone: '0903 456 789', status: 'Đang hoạt động', role: 'Giáo viên' },
    { id: 'gv4', name: 'Phạm Đức Long', email: 'duclong@lab.edu.vn', department: 'Điện - Điện tử', phone: '0904 567 890', status: 'Đang hoạt động', role: 'Điều phối' },
    { id: 'gv5', name: 'Vũ Khánh Linh', email: 'khanhlinh@lab.edu.vn', department: 'Công nghệ thông tin', phone: '0905 678 901', status: 'Đang hoạt động', role: 'Kỹ thuật viên' },
  ],
  classes: [
    { id: 'cl1', name: 'Điện công nghiệp K24', code: 'DCD-K24', teacher: 'Nguyễn Minh Anh', students: 28, semester: 'Học kỳ I · 2026–2027' },
    { id: 'cl2', name: 'Lập trình Web K25', code: 'LTW-K25', teacher: 'Trần Quốc Bảo', students: 32, semester: 'Học kỳ I · 2026–2027' },
    { id: 'cl3', name: 'Cơ khí chính xác K24', code: 'CKCX-K24', teacher: 'Lê Thu Hà', students: 24, semester: 'Học kỳ I · 2026–2027' },
  ],
  rooms: [
    { id: 'r1', name: 'Phòng thực hành A.203', building: 'Nhà A', capacity: 32, status: 'Sẵn sàng' },
    { id: 'r2', name: 'Phòng máy B.105', building: 'Nhà B', capacity: 36, status: 'Sẵn sàng' },
    { id: 'r3', name: 'Xưởng thực hành C.01', building: 'Nhà C', capacity: 28, status: 'Bảo trì' },
  ],
  equipment: [
    { id: 'eq1', name: 'Máy tính trạm A203-01', room: 'Phòng thực hành A.203', status: 'Sẵn sàng', lastChecked: '2026-11-02' },
    { id: 'eq2', name: 'Bộ PLC S7-1200', room: 'Phòng thực hành A.203', status: 'Sẵn sàng', lastChecked: '2026-11-02' },
    { id: 'eq3', name: 'Máy CNC mini', room: 'Xưởng thực hành C.01', status: 'Cần kiểm tra', lastChecked: '2026-10-28' },
  ],
  subjects: [
    { id: 's1', name: 'Thực hành PLC', code: 'EET204', hours: 45 },
    { id: 's2', name: 'Phát triển giao diện web', code: 'WEB301', hours: 60 },
    { id: 's3', name: 'Gia công CNC', code: 'MEC215', hours: 45 },
  ],
  timeSlots: [
    { id: 'slot-am', name: 'Buổi sáng', start: '07:30', end: '11:30', kind: 'Giảng dạy' },
    { id: 'slot-pm', name: 'Buổi chiều', start: '13:00', end: '17:00', kind: 'Giảng dạy' },
    { id: 'slot-duty-evening', name: 'Ca trực tối', start: '17:30', end: '21:00', kind: 'Ca trực' },
  ],
  schedules: [
    { id: 'sch1', day: 'Thứ 2', date: '2026-11-02', start: '08:00', end: '10:00', teacher: 'Nguyễn Minh Anh', className: 'Điện công nghiệp K24', subject: 'Thực hành PLC', room: 'Phòng thực hành A.203', type: 'Giảng dạy', status: 'Đã xác nhận', semester: 'Học kỳ I · 2026–2027' },
    { id: 'sch2', day: 'Thứ 2', date: '2026-11-02', start: '10:30', end: '12:00', teacher: 'Trần Quốc Bảo', className: 'Lập trình Web K25', subject: 'Phát triển giao diện web', room: 'Phòng máy B.105', type: 'Giảng dạy', status: 'Đã xác nhận', semester: 'Học kỳ I · 2026–2027' },
    { id: 'sch3', day: 'Thứ 3', date: '2026-11-03', start: '13:00', end: '16:00', teacher: 'Lê Thu Hà', className: 'Cơ khí chính xác K24', subject: 'Gia công CNC', room: 'Xưởng thực hành C.01', type: 'Giảng dạy', status: 'Chờ xác nhận', semester: 'Học kỳ I · 2026–2027' },
    { id: 'sch4', day: 'Thứ 4', date: '2026-11-04', start: '08:00', end: '10:00', teacher: 'Nguyễn Minh Anh', className: 'Điện công nghiệp K24', subject: 'Thực hành PLC', room: 'Phòng thực hành A.203', type: 'Giảng dạy', status: 'Đã xác nhận', semester: 'Học kỳ I · 2026–2027' },
    { id: 'sch5', day: 'Thứ 5', date: '2026-11-05', start: '13:30', end: '16:00', teacher: 'Trần Quốc Bảo', className: 'Lập trình Web K25', subject: 'Phát triển giao diện web', room: 'Phòng máy B.105', type: 'Giảng dạy', status: 'Đã xác nhận', semester: 'Học kỳ I · 2026–2027' },
    { id: 'sch6', day: 'Thứ 6', date: '2026-11-06', start: '08:00', end: '11:00', teacher: 'Lê Thu Hà', className: 'Cơ khí chính xác K24', subject: 'Gia công CNC', room: 'Xưởng thực hành C.01', type: 'Giảng dạy', status: 'Đã xác nhận', semester: 'Học kỳ I · 2026–2027' },
    { id: 'sch7', day: 'Thứ 6', date: '2026-11-06', start: '14:00', end: '16:00', teacher: 'Nguyễn Minh Anh', className: 'Điện công nghiệp K24', subject: 'Thực hành PLC', room: 'Phòng thực hành A.203', type: 'Giảng dạy', status: 'Đã xác nhận', semester: 'Học kỳ I · 2026–2027' },
  ],
  sessions: [
    { id: 'session-sch1', scheduleId: 'sch1', date: '2026-11-02', startedAt: '2026-11-02 08:02', endedAt: '2026-11-02 10:01', status: 'Hoàn thành', note: 'Đủ thiết bị thực hành' },
  ],
  shifts: [
    { id: 'd1', date: '2026-11-03', start: '07:30', end: '11:30', room: 'Phòng thực hành A.203', assignee: 'Nguyễn Minh Anh', required: 1, status: 'Đã phân công' },
    { id: 'd2', date: '2026-11-03', start: '13:00', end: '17:00', room: 'Phòng máy B.105', assignee: 'Vũ Khánh Linh', required: 1, status: 'Đã phân công' },
    { id: 'd3', date: '2026-11-04', start: '07:30', end: '11:30', room: 'Xưởng thực hành C.01', assignee: '', required: 1, status: 'Chưa phân công' },
    { id: 'd4', date: '2026-11-05', start: '13:00', end: '17:00', room: 'Phòng thực hành A.203', assignee: 'Trần Quốc Bảo', required: 1, status: 'Đang diễn ra' },
  ],
  dutyTemplates: [
    { id: 'template-am', name: 'Ca trực buổi sáng', start: '07:30', end: '11:30', required: 1, active: true },
    { id: 'template-pm', name: 'Ca trực buổi chiều', start: '13:00', end: '17:00', required: 1, active: true },
    { id: 'template-evening', name: 'Ca trực buổi tối', start: '17:30', end: '21:00', required: 1, active: false },
  ],
  academicTerms: [
    { id: 'term-2026-1', name: 'Học kỳ I · 2026–2027', startDate: '2026-09-01', endDate: '2027-01-15', status: 'Đang diễn ra' },
    { id: 'term-2026-2', name: 'Học kỳ II · 2026–2027', startDate: '2027-01-18', endDate: '2027-05-30', status: 'Sắp diễn ra' },
  ],
  notifications: [],
  auditLogs: [],
  requests: [{ id: 'rq1', date: '2026-11-06', shift: 'Ca chiều · Phòng A.203', from: 'Nguyễn Minh Anh', to: 'Lê Thu Hà', reason: 'Có lịch công tác đột xuất', status: 'Chờ duyệt' }],
  users: [
    { id: 'u1', name: 'Admin Demo', email: 'admin@lab.edu.vn', role: 'Quản trị hệ thống', scope: 'Toàn đơn vị', status: 'Đang hoạt động' },
    { id: 'u2', name: 'Phạm Đức Long', email: 'duclong@lab.edu.vn', role: 'Điều phối/giáo vụ', scope: 'Điện - Điện tử', status: 'Đang hoạt động' },
    { id: 'u3', name: 'Nguyễn Minh Anh', email: 'minhanh@lab.edu.vn', role: 'Giáo viên', scope: 'Cá nhân', status: 'Đang hoạt động' },
    { id: 'u4', name: 'Trần Quốc Bảo', email: 'quocbao@lab.edu.vn', role: 'Giáo viên', scope: 'Cá nhân', status: 'Đang hoạt động' },
    { id: 'u5', name: 'Lê Thu Hà', email: 'thuha@lab.edu.vn', role: 'Giáo viên', scope: 'Cá nhân', status: 'Đang hoạt động' },
    { id: 'u6', name: 'Vũ Khánh Linh', email: 'khanhlinh@lab.edu.vn', role: 'Kỹ thuật viên', scope: 'Nhà A · Nhà B', status: 'Đang hoạt động' },
    { id: 'u7', name: 'Hoàng Ngọc Mai', email: 'ngocmai@lab.edu.vn', role: 'Điều phối/giáo vụ', scope: 'Cơ khí', status: 'Đang hoạt động' },
    { id: 'u8', name: 'Đỗ Thanh Tùng', email: 'thanhtung@lab.edu.vn', role: 'Giáo viên', scope: 'Cá nhân', status: 'Tạm khóa' },
    { id: 'u9', name: 'Nguyễn Hà Linh', email: 'halinh@lab.edu.vn', role: 'Quản lý đơn vị', scope: 'Toàn đơn vị', status: 'Đang hoạt động' },
    { id: 'u10', name: 'Lê Quang Vinh', email: 'quangvinh@lab.edu.vn', role: 'Giáo viên', scope: 'Cá nhân', status: 'Ngừng hoạt động' },
  ],
  permissions: [
    ...['teachers.read', 'teachers.write', 'classes.read', 'classes.write', 'subjects.read', 'subjects.write', 'rooms.read', 'rooms.write', 'schedules.read', 'schedules.write', 'duties.read', 'duties.write', 'duties.assign', 'duties.approve', 'reports.read', 'users.write'].map((permission) => ({ id: `Quản trị hệ thống::${permission}`, role: 'Quản trị hệ thống', permission, enabled: true })),
    ...['teachers.read', 'teachers.write', 'classes.read', 'classes.write', 'subjects.read', 'subjects.write', 'rooms.read', 'rooms.write', 'schedules.read', 'schedules.write', 'duties.read', 'duties.write', 'duties.assign', 'duties.approve', 'reports.read'].map((permission) => ({ id: `Điều phối/giáo vụ::${permission}`, role: 'Điều phối/giáo vụ', permission, enabled: true })),
    ...['teachers.read', 'classes.read', 'subjects.read', 'rooms.read', 'schedules.read', 'duties.read', 'duties.swapRequest', 'reports.read'].map((permission) => ({ id: `Giáo viên::${permission}`, role: 'Giáo viên', permission, enabled: true })),
    ...['rooms.read', 'duties.read'].map((permission) => ({ id: `Kỹ thuật viên::${permission}`, role: 'Kỹ thuật viên', permission, enabled: true })),
    ...['teachers.read', 'classes.read', 'subjects.read', 'rooms.read', 'schedules.read', 'duties.read', 'reports.read'].map((permission) => ({ id: `Quản lý đơn vị::${permission}`, role: 'Quản lý đơn vị', permission, enabled: true })),
  ],
}
