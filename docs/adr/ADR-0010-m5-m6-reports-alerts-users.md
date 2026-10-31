# ADR-0010 — M5 + M6: Báo cáo/Cảnh báo & Người dùng/Phân quyền (FR-7, FR-8)

- Trạng thái: Accepted
- Ngày: 2026-09-13

Làm gộp M5 và M6 theo yêu cầu (bỏ qua M4 — giám sát màn hình — để sau).

## M5 — FR-7 Báo cáo & Cảnh báo

### Alert engine dùng chung cho mọi loại cảnh báo, không chỉ offline

`IAlertPublisher`/`AlertPublisher` (Infrastructure/Notifications) là điểm fan-out duy nhất cho mọi
cảnh báo: đẩy realtime `NewAlert` (giữ nguyên hành vi cũ), **ghi `Notification`** cho từng user liên
quan (mọi Administrator đang hoạt động + Technician/Teacher có `UserRoomScope` đúng phòng), và gửi
email cho user có `Email`. `OfflineDetectorService` (M1) đổi từ gọi thẳng `IRealtimeNotifier.AlertRaisedAsync`
sang gọi `IAlertPublisher.RaiseAsync` — cùng một hành vi realtime, cộng thêm lưu trữ + email miễn phí.

### Cảnh báo CPU/RAM (mới, chưa có từ trước) — chặn tại điểm ghi heartbeat, không phải sweep riêng

Không thêm một `BackgroundService` quét định kỳ mới: `AgentAccessService.RecordHeartbeatAsync` đã có
sẵn giá trị CPU/RAM mới nhất trong tay ngay tại điểm ghi, nên so ngưỡng (`Alerts:CpuThresholdPercent`/
`RamThresholdPercent`, mặc định 90%) ngay tại đó là rẻ nhất. Chống báo lặp: `ComputerStatus` có thêm
`CpuAlertRaisedAt`/`RamAlertRaisedAt` (nullable) — set khi vượt ngưỡng lần đầu, **giữ nguyên** trong
suốt thời gian còn vượt (không báo lại mỗi heartbeat), **xoá** khi chỉ số về dưới ngưỡng để lần vượt
tiếp theo được báo lại. Migration `AddResourceAlertTracking`.

### Email: mặc định chỉ ghi log, không chặn tiến độ

Bạn xác nhận chưa có SMTP thật, dùng chế độ ghi log tạm thời (giống NAS bị hoãn ở M4). `IEmailSender`
có 2 cài đặt: `NullEmailSender` (mặc định, `Email:Enabled=false`) chỉ log "sẽ gửi tới X: tiêu đề",
`SmtpEmailSender` (MailKit) — bật thật bằng `Email:Enabled=true` + `Email:Host` trong config, không
cần đổi code. Cả hai implement `IEmailSender` nên `AlertPublisher` không biết/không cần biết đang
dùng cái nào. Gửi email luôn best-effort (try/catch nuốt lỗi, chỉ log warning) — một alert không được
để hỏng vì email lỗi, đúng tinh thần bài học `AbortOnConnectFail=false` ở M2.

### Notification realtime: nhóm SignalR theo user, không phải theo phòng

`LabHub` có thêm `user:{id}` group — Web Admin client tự động vào nhóm này khi connect (đọc claim
`lab:uid` do `UserProvisioningMiddleware` gắn, chạy trước `MapHub` trong pipeline nên hoạt động đúng
ngay từ lần connect đầu). `NotificationCreatedAsync` gửi trực tiếp vào group này — không cần client
gọi `Subscribe` gì thêm, khác với `SubscribeToRoom` (chủ động) của các event khác.

### Báo cáo sử dụng (FR-7.4)

`GET /api/v1/reports/usage?roomId=&classId=&from=&to=` — lọc `Sessions` theo khoảng thời gian/phòng/lớp
(có áp `IRoomScope` như mọi list khác), gom nhóm theo phòng: số ca, **giờ máy** (tổng
`(LogoutAt ?? now) - LoginAt` của mọi `SessionComputers` có đăng nhập), số SV duy nhất. Query kéo dữ
liệu Sessions+SessionComputers về rồi tính tổng hợp ở tầng ứng dụng (không cố ép LINQ dịch phép trừ
`DateTime` lồng `??` thành SQL — dễ vỡ, quy mô ~300 máy/vài trăm ca đủ nhỏ để làm trong bộ nhớ).
Xuất CSV (`/reports/usage/export`) dùng `StringBuilder` thủ công, không thêm thư viện CSV mới.

## M6 — FR-8 Người dùng & Phân quyền

### Cố ý KHÔNG có "tạo user mới"

`UserProvisioner` (F2) khớp tài khoản với `Users.KeycloakUserId` (= `sub`) — **chỉ tra cứu, không
bao giờ tra theo email/username**. Nếu M6 cho tạo trước một dòng User với `KeycloakUserId=null`, lần
đăng nhập Keycloak đầu tiên của người đó sẽ **không khớp được dòng có sẵn** (tra theo `sub` ra null)
→ provisioner tạo dòng MỚI → trùng 2 tài khoản cho cùng 1 người. Vì vậy M6 chỉ **quản lý** tài khoản
đã từng đăng nhập ít nhất 1 lần (đã có `sub`): đổi họ tên/điện thoại/trạng thái hoạt động, gán vai
trò, gán phạm vi phòng. Username/Email hiển thị nhưng không cho sửa tay — đồng bộ lại từ Keycloak mỗi
lần đăng nhập, sửa tay sẽ bị ghi đè ngay lần đăng nhập kế tiếp.

### Chặn tự khoá hệ thống: không được hạ quyền/vô hiệu hoá Administrator cuối cùng

`LastAdministratorGuard.GuardAsync` (dùng chung bởi `UpdateUserHandler` khi `IsActive: true→false` và
`SetUserRolesHandler` khi gỡ role Administrator): đếm Administrator đang hoạt động khác user hiện tại,
0 thì 409. Lưu ý: `UserProvisioner` (F2) đã có sẵn cơ chế "tự cấp lại Administrator cho bootstrap admin
nếu họ về 0 role" (`Auth:BootstrapAdmins`) — 2 cơ chế độc lập nhưng bổ trợ nhau (guard chặn thao tác
qua UI/API; tự-cấp-lại là lưới an toàn cuối nếu ai đó chỉnh DB tay).

**Hạn chế khi test**: DB dev dùng chung đã có sẵn ≥2 Administrator thật (`mvt`, `test-admin`) từ các
phiên trước, nên **không thể integration-test nhánh "bị chặn"** một cách an toàn (sẽ phải tự demote
rồi tự khôi phục 1 trong 2 tài khoản thật đang được các test khác dùng chung — rủi ro race với test
khác chạy song song). Test tự động chỉ phủ nhánh "cho phép khi có ≥2 admin", dùng một tài khoản phụ
tạo riêng cho test. Nhánh chặn đã review logic (đếm đơn giản) nhưng cần DB sạch/cô lập để test tự động
đầy đủ — để sau nếu cần.

### Mã đăng ký phòng: xoay vòng `Rooms.Code` sẵn có, không thêm cột mới

`Rooms.Code` đã đóng vai trò kép từ F0 (mã hiển thị + bí mật để agent tự đăng ký — xem field đã có
comment "also used by the Agent as the room registration code"). Thay vì tách thêm
`RegistrationCode` riêng (đổi schema, cần migrate dữ liệu, đổi endpoint `/agent/register`), M6 chỉ
thêm `POST /rooms/{id}/regenerate-code` sinh mã 8 ký tự ngẫu nhiên (bỏ 0/O/1/I/L dễ nhầm), ghi đè
`Code`. Máy đã đăng ký không bị ảnh hưởng (xác thực bằng `AgentToken` riêng, không dùng lại Code).
Frontend: nút xoay vòng 2 bước xác nhận ngay trong dòng (không dùng modal riêng, giống UX Duyệt/Từ
chối máy có sẵn) thay vì `confirm()` của trình duyệt.

### AuditLog: chỉ mở rộng cho hành động MỚI của M6, không truy hồi M1–M3

Trước M6, chỉ `IssueCommandHandler` (M3) ghi `AuditLog`. Việc bổ sung ghi log cho *toàn bộ* thao tác
quản trị đã có từ M1–M3 (CRUD tòa nhà/phòng/máy, duyệt máy, CRUD lớp/SV/lịch, vòng đời ca học...) là
khối lượng việc riêng biệt, rủi ro thấp nhưng chạm rất nhiều file — cố ý để ngoài phạm vi M5/M6 lần
này. M6 chỉ ghi `AuditLog` cho hành động **mới** nó tạo ra (`User.Update`, `User.SetRoles`,
`User.SetRoomScopes`, `Room.RegenerateCode`). `IpAddress` vẫn để trống ở mọi nơi (kể cả M3) — cần
`ICurrentUser` biết `HttpContext` (qua `IHttpContextAccessor`) mới điền được, chưa làm.

`GetAuditLogsQuery`/`AuditLogsController`: phân trang qua `PagedResult<T>` (kiểu chung mới,
`Application/Common/Models`) — endpoint đầu tiên cần phân trang thật (audit log có thể rất nhiều dòng).

## Sự cố phát hiện khi chạy test lặp lại (đã sửa)

Chạy full test suite lặp lại nhiều lần phát hiện `NotificationsAndReportsEndpointTests` thỉnh thoảng
500 — không phải flaky ngẫu nhiên mà là **race thật giữa 2 file test của chính phiên này**:
`UserManagementAndAuditEndpointTests` tạm thời thăng cấp một user phụ lên Administrator rồi xoá hẳn
user đó lúc dọn dẹp; nếu đúng lúc đó `AlertPublisher.RaiseAsync` (test M5) đang SELECT "mọi
Administrator đang hoạt động" để gửi Notification, user vừa bị xoá vẫn nằm trong danh sách nhận —
INSERT `Notifications` vỡ FK `FK_Notifications_Users_UserId` → 500 nguyên cả request heartbeat.

Sửa 2 lớp:
1. **Cách ly test**: `UserManagementAndAuditEndpointTests` và `NotificationsAndReportsEndpointTests`
   dùng chung `[Collection("UserManagementSensitive", DisableParallelization = true)]` — xUnit mặc
   định chạy song song các class test khác nhau, 2 file này đụng thẳng vào bảng `Users`/`UserRoles`
   dùng chung toàn hệ thống nên phải chạy tuần tự với nhau (không ảnh hưởng các file test khác).
2. **`AlertPublisher` phải thực sự "best-effort" như doc comment đã hứa**: trước đó chỉ bọc
   try/catch phần đẩy realtime/email, còn `db.SaveChangesAsync()` ghi `Notifications` thì không —
   lỗi ở đây rơi thẳng ra ngoài, và (nặng hơn) EF Core vẫn giữ các dòng `Notification` chưa lưu được
   ở trạng thái `Added` trong **cùng DbContext** mà caller (`RecordHeartbeatAsync`) đang dùng, nên
   lần `SaveChangesAsync` cuối của heartbeat lại thử lưu lại đúng những dòng đó và vỡ lại lần nữa.
   Sửa: bọc try/catch quanh `SaveChangesAsync`, và khi lỗi thì `RemoveRange` các `Notification` vừa
   `Add` để không bị cố lưu lại lần sau. Đúng tinh thần "một kênh cảnh báo lỗi không được làm hỏng
   luồng chính" đã áp dụng cho Redis ở M2.

Đã thêm `EnableRetryOnFailure` cho SqlServer (tự thử lại lỗi tạm thời như deadlock/mất kết nối) —
giữ lại vì là cải thiện độ bền chung hợp lý, dù không phải nguyên nhân chính của lỗi trên (đây là
FK violation, lỗi vĩnh viễn, retry đúng là không tự sửa được — cần 2 fix ở trên mới hết).

## Live-test qua trình duyệt thật (2026-09-13)

Chạy backend + frontend thật, đăng nhập Keycloak thật (`mvt`, Administrator). Phát hiện thú vị: agent
console-mode từ phiên M3 trước đó **vẫn đang chạy nền** (sống sót qua `TaskStop`, đúng vấn đề đã ghi ở
`project-state`/`agent-howto`) và tiếp tục gửi heartbeat thật lên backend mới khởi động lại — RAM máy
dev thật vượt 90% → **3 thông báo "RAM vượt ngưỡng" xuất hiện live trên chuông thông báo** không cần
thao tác gì thêm, chứng minh trọn vẹn pipeline cảnh báo tài nguyên hoạt động đúng bằng dữ liệu thật
100%, không phải dữ liệu dàn dựng. Đã dọn tiến trình agent thừa này sau khi test xong.

Đã xác minh trực tiếp trên UI: trang **Người dùng** (danh sách + chi tiết, vai trò/phạm vi phòng),
chuông thông báo (đọc 1/đọc tất cả, cập nhật realtime), trang **Báo cáo sử dụng** (số liệu đúng với
dữ liệu ca học cũ), trang **Nhật ký** (hiển thị đúng audit log thật từ các lần chạy integration test
trước đó, lọc theo hành động hoạt động đúng, phân trang), và nút **xoay mã đăng ký phòng** ở Danh mục
(xác nhận 2 bước, mã đổi thành công, LAB-02 không có máy nào bị ảnh hưởng).

## Test

27 test backend (từ 22): +5 (`NotificationsAndReportsEndpointTests` 2 bài — cảnh báo CPU tạo
Notification + không báo lặp khi còn vượt ngưỡng, báo cáo sử dụng + xuất CSV; `UserManagementAndAuditEndpointTests`
3 bài — cập nhật vai trò/phạm vi phòng, guard admin cuối cùng (nhánh cho phép), xoay mã phòng + audit
log). Không thêm test agent (M5/M6 không đụng tới `agent/`).

## Hạn chế / để sau

- Nhánh "chặn" của `LastAdministratorGuard` chưa có integration test (lý do: DB dev dùng chung đã có
  sẵn nhiều Administrator thật — xem mục M6 phía trên). Logic đơn giản, đã review kỹ bằng tay.
- AuditLog chưa phủ các thao tác quản trị từ M1–M3 (chỉ M3's Commands + M6's User/Room actions).
- `AuditLog.IpAddress` luôn null — cần `IHttpContextAccessor` trong `ICurrentUser`.
- Chưa có UI xem lịch sử toàn bộ lệnh đã phát (`GET /api/v1/commands`, đã ghi nhận từ ADR-0009) —
  không thuộc phạm vi M5/M6.
- Report "theo lớp" chỉ dùng làm bộ lọc (`classId`), bảng kết quả vẫn gom theo phòng — đủ dùng cho
  MVP, có thể thêm bảng gom theo lớp riêng nếu cần.
