# ADR-0007 — M1: Quản lý & giám sát phòng máy (FR-1 / FR-5)

- Trạng thái: Accepted
- Ngày: 2026-09-11

## Quyết định

### Kiến trúc feature (thiết lập cho M2–M6)
- **MediatR CQRS**: mỗi thao tác = một `IRequest` + handler `internal sealed` (assembly scan).
  Validator FluentValidation + `ValidationBehavior` (pipeline) → lỗi trả 400 kèm `errors` dict.
- **`IAppDbContext`** (Application) là bề mặt persistence — handler không phụ thuộc
  `LabMonitoringDbContext`. `LabMonitoringDbContext : IAppDbContext`, đăng ký
  `AddScoped<IAppDbContext>(sp => sp.GetRequiredService<LabMonitoringDbContext>())`.
- Exception nghiệp vụ: `NotFoundException`→404, `ForbiddenException`→403, `ConflictException`→409
  (map trong `GlobalExceptionHandler`, SRS §8.6).
- **Room scope** (FR-8.2) enforce trong từng handler qua `IRoomScope` (không dùng pipeline behavior
  để mỗi handler kiểm soát rõ ràng phạm vi query của mình).

### Endpoint
- `/api/v1/buildings` CRUD — `Administrator`.
- `/api/v1/rooms` — list/detail (scoped) mọi role admin-console; create/update/delete `Administrator`.
- `/api/v1/computers` — list (`roomId`/`status`/`pendingApproval`, scoped), detail, `GET {id}/metrics?from&to`
  (SRS §8.3), update (tên/vị trí/active), `POST {id}/approve`, `POST {id}/reject` (FR-5.3), delete (`Administrator`).
- `/api/v1/dashboard/summary` — đếm theo scope (UC-02).

### Offline detector (FR-1.5)
- `OfflineDetectorService : BackgroundService`, chu kỳ = `max(5, OfflineThreshold/3)` giây.
  Máy có `LastHeartbeatAt` null hoặc cũ hơn ngưỡng (mặc định 30s) và chưa Offline → set Offline,
  đẩy `ComputerStatusChanged` + `NewAlert` qua `IRealtimeNotifier`. Dùng `ExecuteUpdateAsync` (batch).

### Frontend (Angular)
- `FacilitiesService` + models. Dashboard refetch khi có `ComputerStatusChanged`/`NewAlert` (debounce 800ms).
- **Màn hình giám sát phòng** (`/rooms/:id`): `SubscribeToRoom(id)` khi vào, `unsubscribeFromRoom` khi rời;
  `ComputerStatusChanged`/`MetricsUpdated` **patch tại chỗ** thẻ máy (không refetch), refetch chỉ khi
  xuất hiện máy mới. Lưới máy tô màu theo trạng thái; panel "chờ phê duyệt" với Duyệt/Từ chối.
- Chi tiết máy: thông tin + spec + bảng metrics gần đây (biểu đồ để module báo cáo M5).
- Catalog: form template-driven CRUD tòa nhà/phòng.

### Test
- `AdminApiFactory`: `WebApplicationFactory` **post-configure `JwtBearerOptions`** để chấp nhận token
  ký bằng khóa đối xứng test (`TestJwt`), config `Auth:BootstrapAdmins=[test-admin]` → provisioning cấp
  Administrator. Dùng lại cho M2–M6.
- `FacilitiesEndpointTests` (DB-gated, self-clean): CRUD, register→approve, dashboard, 401.

## Hạn chế / để sau

- Chưa có sơ đồ vị trí máy (kéo-thả PositionX/Y) — mới lưu toạ độ, hiển thị lưới đơn giản.
- Biểu đồ metrics: đang là bảng; chart để M5.
- Ảnh `MapImageUrl` chưa có upload.
- Computer write cho phép mọi role admin-console (scope enforce); tinh chỉnh Technician-only sau nếu cần.
