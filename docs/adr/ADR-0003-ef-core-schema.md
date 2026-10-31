# ADR-0003 — EF Core schema (F1)

- Trạng thái: Accepted
- Ngày: 2026-09-10

## Bối cảnh

Hiện thực hoá ERD field-level (SRS Mục 7, nhóm A–I) bằng EF Core 10 Code-First trên SQL Server,
tạo migration đầu và seed.

## Quyết định

- **Code-First**, `LabMonitoringDbContext` trong `LabMonitoring.Infrastructure/Persistence`, cấu hình
  qua `IEntityTypeConfiguration<T>` (gom theo nhóm chức năng), `ApplyConfigurationsFromAssembly`.
- Migration + snapshot: `Persistence/Migrations/` (assembly `LabMonitoring.Infrastructure`).
- **24 bảng** đúng theo ERD. `dotnet ef` chạy với `--startup-project src/LabMonitoring.Api`
  (lấy connection string từ `appsettings.json`).
- **Enum lưu dạng string** (`HasConversion<string>()` + `HasMaxLength`) khớp kiểu `nvarchar(...)` của SRS:
  `ComputerState`, `SessionStatus`, `CommandKind`, `CommandResultStatus`, `PolicyType`,
  `StorageProvider`, `NotificationSeverity`.
- `CreatedAt` / `UpdatedAt`: `HasDefaultValueSql("SYSUTCDATETIME()")` (đúng SRS).
- **Delete behavior**: phần lớn FK để `Restrict` / `SetNull` để tránh lỗi multiple cascade paths của
  SQL Server; chỉ cascade ở quan hệ sở hữu thực sự (join tables, ComputerSpec/Status/Metric → Computer,
  CommandResult → Command, Notification → User).
- **Seed**: 3 role cố định Id (`Administrator`=1, `Technician`=2, `Teacher`=3) qua `HasData`
  (hằng số trong `Domain.Common.SeededRoles`). Chưa seed tài khoản Administrator — sẽ tạo qua
  provisioning theo `sub` ở F2 (không seed user rỗng để tránh lệch với Keycloak).
- **Index thêm** ngoài SRS (phục vụ truy vấn thường gặp): `ComputerStatus.LastHeartbeatAt`,
  `ComputerMetrics(ComputerId, RecordedAt)` + `RecordedAt`, `Screenshots.CapturedAt` + `ExpiresAt`,
  `Commands.Nonce` UNIQUE + `Commands.IssuedAt`, `CommandResults(CommandId, ComputerId)` UNIQUE,
  `Sessions(RoomId, Status)` + `StartedAt`, `Schedules(RoomId, DayOfWeek)`,
  `Notifications(UserId, IsRead)`, filtered unique cho `Users.Email` / `Users.KeycloakUserId`.

## Sai lệch có chủ đích so với SRS

| SRS | Thực hiện | Lý do |
|---|---|---|
| Bảng/entity `Applications` | class `ManagedApplication`, **table vẫn là `Applications`** | tránh trùng tên assembly `LabMonitoring.Application` |
| `Students.FullName / Email nvarchar(150) NOT NULL` | `FullName` NOT NULL, **`Email` NULL** | nhiều SV không có email; hiểu dòng SRS là ghi gộp |
| `Notifications.Title / Message nvarchar` (không nêu size) | `Title` 200, `Message` 1000 | cần độ dài cụ thể cho cột nvarchar |

## Hệ quả

- `ComputerMetrics` tăng nhanh (~60 rows/s ở 300 máy) — cần job retention/rollup (M5); Express 2017
  giới hạn 10GB (xem ADR-0001).
- Chạy `dotnet ef database update` khi clone mới; test `DatabaseSeedTests` tự bỏ qua khi không có DB.
