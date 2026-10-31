# ADR-0008 — M2: Ca thực hành & giám sát sinh viên (FR-6 / FR-2)

- Trạng thái: Accepted
- Ngày: 2026-09-11

## Quyết định

### Backend — tiếp tục pattern M1 (MediatR CQRS + `IAppDbContext` + `IRoomScope`)
- **Classes/Students/Schedules** (`Application/Teaching/`): CRUD giống Buildings/Rooms — viết
  (`Administrator`), đọc (`admin-console`). `Schedule` chặn overlap cùng phòng/thứ/khung giờ.
  Ghi danh sinh viên: `POST/DELETE /api/v1/classes/{id}/students/{studentId}`.
- **`GET /api/v1/users`** (mới, `Application/Users/`): read-only, phục vụ chọn giảng viên khi tạo lớp.
  CRUD/role/scope đầy đủ vẫn là M6 — endpoint này chỉ liệt kê tài khoản đã provision.
- **Sessions** (`Application/Sessions/`):
  - `POST /sessions/start` (policy mới `teacher-or-admin`): kiểm tra người gọi là **giáo viên của lớp
    hoặc Administrator**; chặn 2 ca Active cùng phòng (409); tạo `Session` **và tự động tạo
    `SessionComputers`** cho mọi máy đang active+approved trong phòng — để việc theo dõi đăng
    nhập/đăng xuất (FR-2.2) có chỗ ghi ngay cả trước khi phân bổ.
  - `POST /sessions/{id}/allocate` (FR-6.3): `auto=true` xếp sinh viên đã ghi danh (theo MSSV) vào
    máy trống (theo tên máy) — bỏ qua máy/SV đã gán; `auto=false` nhận danh sách
    `{computerId, studentId}` tường minh (kiểm tra SV có ghi danh đúng lớp).
  - `POST /sessions/{id}/end`: đóng ca, **tự động đăng xuất** (`LogoutAt`) mọi máy còn `LoginAt`
    mà chưa `LogoutAt` (SV quên đăng xuất/máy tắt đột ngột).
  - `GET /sessions/{id}/report` (FR-6.4): thời lượng, số máy đã dùng, số SV đã đăng nhập/tổng SV lớp.

### Liên kết đăng nhập Windows ↔ Session (FR-2.1/2.2)
- Không có bảng map "tên đăng nhập Windows ↔ MSSV" trong ERD — theo đúng thiết kế, việc **gán SV
  vào máy** là ở bước phân bổ (FR-6.3); heartbeat chỉ **ghi thời điểm đăng nhập/đăng xuất Windows
  trên máy đó** trong khi ca đang Active.
- `AgentAccessService.RecordHeartbeatAsync` → `TrackSessionLoginAsync`: tìm `SessionComputers` của
  ca Active tại phòng của máy; heartbeat đầu tiên có `CurrentWindowsUser` khác rỗng → `LoginAt`;
  heartbeat đầu tiên `CurrentWindowsUser` rỗng sau khi đã login → `LogoutAt`. Không xử lý đổi user
  giữa chừng (một lượt login/logout mỗi máy/ca — đủ cho MVP).

### Phân quyền
- Policy mới `teacher-or-admin` (`role:Administrator` **hoặc** `role:Teacher`) cho start/end/allocate.
- Sở hữu ca: handler tự kiểm `session.TeacherId == currentUser.UserId` trừ khi là Administrator
  (không dùng policy vì cần so khớp dữ liệu, không chỉ role).

### Frontend
- `TeachingService`/`SessionsService`/`UsersService` + models. Trang **Lớp học** (CRUD lớp + danh
  mục SV dùng chung), **chi tiết lớp** (ghi danh/bỏ ghi danh), **Ca thực hành** (danh sách + chi
  tiết: bảng máy/SV/đăng nhập/đăng xuất, nút Phân bổ tự động/Kết thúc ca), **Báo cáo ca**.
- **Phòng máy → chi tiết phòng** thêm panel "Ca thực hành": chưa có ca → form chọn lớp + Bắt đầu ca;
  đang có ca → link sang trang quản lý ca.

## Sự cố phát hiện khi test trên trình duyệt (đã sửa)

**Redis backplane là single point of failure**: khi Redis dev container tắt, `IHubContext.SendAsync`
(StackExchange.Redis mặc định `AbortOnConnectFail=true`) ném exception đồng bộ → **mọi** endpoint có
gọi `IRealtimeNotifier` (approve máy, heartbeat, …) trả **500**, dù không liên quan gì tới SignalR.
Đã set `AbortOnConnectFail=false` (`RealtimeExtensions`) để Redis mất kết nối chỉ làm mất đẩy realtime,
không sập cả API. **Vẫn cần Redis container chạy để dev** — `docker compose -f deploy/docker-compose.dev.yml up -d`.

## Hạn chế / để sau

- Đăng nhập sâu vào sau report: đã verify test tự động (heartbeat giả lập user "sv001" → LoginAt/LogoutAt
  đúng); demo trên trình duyệt không thấy vì agent chạy console mode trả `CurrentWindowsUser=null` (WTS,
  hạn chế đã ghi ADR-0006) — cần Windows Service thật để thấy đầy đủ.
- "Sự cố phát sinh" (FR-6.4) — báo cáo hiện chưa có vì cảnh báo (Offline/vượt ngưỡng) chưa được lưu
  thành lịch sử theo ca; sẽ bổ sung khi M5 lưu `Notifications`/`AuditLogs` liên kết Session.
- Điều hướng sau đăng nhập Keycloak luôn về `/dashboard` (không giữ URL đích ban đầu) — chấp nhận được ở MVP.
- Chọn giảng viên khi tạo lớp dùng `GET /api/v1/users` (mọi user active, chưa lọc theo role Teacher) —
  UI đủ dùng, lọc chặt hơn khi M6 làm quản lý user/role đầy đủ.
