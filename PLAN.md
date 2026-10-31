# Kế hoạch xây dựng Hệ thống Giám sát & Quản lý Phòng Thực hành Máy tính

> Nguồn: `CLAUDE.md` + `Dac_ta_ky_thuat_SRS_ERD_API_LabMonitoring.docx` (SRS + ERD field-level + Đặc tả API).
> Quy mô mục tiêu: 5 phòng máy / ~300 máy tính. Phase 1 (MVP đầy đủ theo từng module).

---

## 0. Quyết định kiến trúc đã chốt

| Hạng mục | Lựa chọn | Ghi chú |
|---|---|---|
| Backend | **ASP.NET Core 10** (`net10.0`), Clean Architecture | Theo yêu cầu mới của bạn (SDK 10.0.301 đã có sẵn). ⚠️ `CLAUDE.md` ghi ".NET 8 LTS" — cần cập nhật lại |
| ORM / DB | EF Core 10 + SQL Server (`MVT`, `sa`, mật khẩu trong `.env`/User Secrets) | Máy đang chạy SQL Server 2017 Express — xác nhận nâng lên 2022 hoặc chấp nhận giới hạn 10GB |
| Realtime | SignalR Hub `/hubs/lab` + Redis backplane | Redis bật sẵn từ MVP (NFR) |
| Xác thực Web | Keycloak OIDC (Authorization Code + PKCE) — **chờ URL/realm/client** | |
| Xác thực Agent | `AgentToken` per-machine (cấp khi register) + `AgentSecret` ký lệnh HMAC-SHA256 | |
| Frontend | **Angular** (SPA) + bộ agent `.claude/agents` | ⚠️ `CLAUDE.md` ghi "LTE4 + bootstrap" — cần cập nhật lại CLAUDE.md |
| LabAgent | .NET 10 Worker Service → Windows Service (Win10/11 x64) | Linux để sau |
| Ảnh screenshot | NAS/Object Storage; DB chỉ lưu metadata | Dev: MinIO qua docker-compose |
| Logging | Serilog (file + console; sink SQL tuỳ chọn) | Mã lỗi chuẩn theo Mục 8.6 |
| Local infra | docker-compose: Redis, MinIO, (Keycloak nếu cần), Seq (xem log) | SQL Server dùng bản đã cài |

---

## 1. Cấu trúc repo — 3 vùng build độc lập (xem `docs/adr/ADR-0002`)

```
backend/                              # .NET 10, build & deploy riêng
 ├─ LabMonitoring.Backend.slnx
 ├─ global.json · Directory.Build.props · Directory.Packages.props · .config/dotnet-tools.json
 ├─ src/
 │   ├─ LabMonitoring.Domain          # Entities, enums, domain events, value objects
 │   ├─ LabMonitoring.Application     # CQRS (MediatR), validators, interfaces, RBAC/scope logic
 │   ├─ LabMonitoring.Infrastructure  # EF Core DbContext + migrations, Redis, storage, email
 │   ├─ LabMonitoring.Contracts       # DTO nội bộ backend + bản gốc giao thức lệnh (SRS §8.5)
 │   └─ LabMonitoring.Api             # REST controllers, SignalR hub, auth handlers, middleware
 └─ tests/  LabMonitoring.UnitTests · LabMonitoring.IntegrationTests (WebApplicationFactory + Testcontainers)

agent/                                # LabAgent — HOÀN TOÀN ĐỘC LẬP, build & đóng gói riêng, copy sang máy trạm
 ├─ LabMonitoring.Agent.slnx
 ├─ global.json · Directory.Build.props · Directory.Packages.props
 ├─ src/LabMonitoring.Agent           # Worker Service → Windows Service: collectors, executors, SignalR client, outbox
 │   └─ Protocol/CommandEnvelope.cs   # bản sao giao thức lệnh, đồng bộ với backend qua ProtocolVersion
 ├─ tests/LabMonitoring.Agent.Tests
 └─ build/  publish.ps1 · install-service.ps1 · uninstall-service.ps1   (thay cho project Installer)

frontend/                             # Angular workspace + AdminLTE 4 (khởi tạo ở F3), build riêng

deploy/   docker-compose.dev.yml (Redis · Seq · MinIO) · keycloak realm export · IIS/systemd configs
docs/     ADR, sơ đồ, hướng dẫn vận hành
```

Agent phụ trách (theo `.claude/agents/README.md`):
- **software-architect** → ADR, cấu trúc solution, trade-off
- **backend-architect** → API, EF Core schema, Keycloak/JWT flow, SignalR
- **database-optimizer** → index bảng time-series `ComputerMetrics`, phân vùng/retention, query báo cáo
- **frontend-developer** → Angular UI, route guard, permission directive theo scope phòng
- **security-architect / security-appsec-engineer / security-penetration-tester** → threat model, review auth/authorization, pentest trước go-live
- **testing-api-tester / testing-test-automation-engineer / testing-reality-checker** → test endpoint + ma trận phân quyền, E2E theo persona, chứng nhận release

---

## 2. Cơ sở dữ liệu (SRS Mục 7)

EF Core **Code-First**, migrations, `SYSUTCDATETIME()` cho mốc thời gian.

Nhóm bảng: A. Users/Roles/UserRoles/UserRoomScopes · B. Buildings/Rooms/Computers · C. ComputerSpecs/ComputerStatus/ComputerMetrics · D. Classes/Students/ClassStudents/Schedules · E. Sessions/SessionComputers · F. Commands/CommandResults · G. Applications/ApplicationPolicies/WebsitePolicies · H. Screenshots · I. AuditLogs/Notifications.

Điểm kỹ thuật:
- `ComputerMetrics` (bigint identity, time-series ~60 rows/s): index `(ComputerId, RecordedAt)`; job dọn dữ liệu cũ; cân nhắc bảng riêng/partition; ghi theo batch.
- `ComputerStatus`, `ComputerSpecs`: quan hệ 1–1 với `Computers`.
- `Commands.Nonce` UNIQUE (chống replay); `Signature` = HMAC-SHA256(payload, AgentSecret).
- `Computers.AgentSecretHash`: chỉ lưu hash; AgentSecret bản rõ chỉ trả 1 lần lúc register.
- Seed: Roles (Administrator/Technician/Teacher), 1 tài khoản Administrator khởi tạo, mã đăng ký phòng.
- Ràng buộc: screenshot không lưu ảnh trong DB (chỉ `FilePath` + metadata + `ExpiresAt`).

---

## 3. Backend API (SRS Mục 8)

### 3.1 Nền tảng
- Clean Architecture + MediatR (CQRS), FluentValidation, AutoMapper.
- Serilog + middleware chuẩn hoá lỗi (400/401/403/409/423/500 theo Mục 8.6).
- Versioned route `/api/v1/*`. Swagger/OpenAPI.
- Health checks, rate limiting (đặc biệt lệnh điều khiển hàng loạt → 423).

### 3.2 Xác thực & phân quyền
- **Web Admin**: JWT Bearer do Keycloak cấp; policy theo role + **scope phòng** (`UserRoomScopes`) cho Technician/Teacher.
- **Agent**: `AuthenticationHandler` cho scheme `AgentToken`; token gắn `MachineId`.
- `ICurrentUser` + `IScopeGuard` (lọc phòng/máy theo phạm vi được gán) dùng xuyên suốt.
- Ghi `AuditLog` mọi thao tác quản trị + lệnh điều khiển (người, thời gian, IP, kết quả).

### 3.3 Nhóm endpoint Agent
`POST /api/v1/agent/register` · `POST /agent/heartbeat` · `POST /agent/command-result` · `POST /agent/screenshot` · `GET /agent/config`.

### 3.4 Nhóm endpoint Web Admin
`/api/v1/rooms` (CRUD) · `/computers?roomId=&status=` · `/computers/{id}/metrics?from=&to=` · `/commands` (POST, ký số phía server, ghi AuditLog) · `/commands/{id}/results` · `/sessions/start` · `/sessions/{id}/end` · `/reports/usage` · `/users` (+ role, scope) · thêm: buildings, classes, students, schedules, applications/website policies, notifications, alerts, audit-logs, dashboard summary.

### 3.5 SignalR Hub `/hubs/lab`
- Server → Web: `ComputerStatusChanged`, `MetricsUpdated`, `NewAlert`, `CommandResultReceived`.
- Server → Agent: `CommandIssued`. Agent → Server: `AckCommand`.
- Group theo `RoomId` (cả Agent lẫn Web client).

### 3.6 Dịch vụ nền (BackgroundService / Hangfire)
- **Offline detector**: đánh dấu Offline khi `LastHeartbeatAt` quá ngưỡng (mặc định 30s, cấu hình được) → `NewAlert`.
- **Alert engine**: CPU/RAM vượt ngưỡng → Notification (in-app + email; Telegram để sau).
- **Command timeout**: lệnh không có kết quả → `Timeout`.
- **Screenshot retention**: xoá file + metadata quá `ExpiresAt`.
- **Metrics retention/rollup**.
- **Session auto-generation** từ `Schedules`.

### 3.7 Giao thức lệnh (Mục 8.5)
Server ký `HMAC-SHA256(payload, AgentSecret)` + `nonce` + `issuedAt`; đẩy qua `CommandIssued`. Xác nhận 2 bước cho Restart/Shutdown cả phòng. Open App/URL kiểm tra whitelist. Trạng thái: Sent → Acknowledged → Executed/Failed/Timeout.

---

## 4. LabAgent (Windows Service)

- .NET 8 Worker Service, cài bằng `sc create` / installer; chạy dưới `LocalSystem`.
- **Provisioning**: lần đầu gọi `/agent/register` (MachineId GUID theo phần cứng, RoomCode, Hostname, specs) → nhận `AgentToken` + `AgentSecret`; lưu **mã hoá DPAPI**.
- **Collectors** (chu kỳ 5–10s): CPU/RAM/Disk/Network (PerformanceCounter / WMI), nhiệt độ CPU best-effort (OpenHardwareMonitor/LibreHardwareMonitor lib — đánh dấu "Không khả dụng" nếu fail), user Windows đang đăng nhập, danh sách tiến trình.
- **Heartbeat** → `POST /agent/heartbeat`.
- **SignalR client**: join group RoomId; nhận `CommandIssued` → verify signature + nonce (TTL 60s) + clock-skew (±5 phút) → `AckCommand` → thực thi → `command-result`.
- **Command executors**: Lock (LogonUI/khoá workstation), Restart, Shutdown, Message (toast/messagebox), OpenApp (whitelist), OpenUrl (whitelist).
- **Screenshot**: chụp theo yêu cầu / theo chu kỳ (chỉ khi Session Active) → đẩy NAS/presigned URL → xác nhận metadata.
- **Độ tin cậy**: hàng đợi cục bộ tối đa 500 bản ghi khi mất mạng; gửi bù; retry exponential backoff.
- **Auto-update**: kiểm tra phiên bản qua `/agent/config` (giai đoạn sau).

---

## 5. Frontend Angular

- Angular workspace, layout kiểu AdminLTE (sidebar theo scope), Bootstrap.
- **Auth**: `angular-oauth2-oidc` với Keycloak (Code + PKCE), interceptor gắn Bearer, refresh token.
- **Guards/Directives**: route guard theo role; directive ẩn/hiện theo scope phòng (mô hình phân quyền 4 lớp trong README agent).
- **SignalR service**: cập nhật realtime trạng thái máy, biểu đồ metrics, alert.
- **Màn hình**: Login → Dashboard tổng quan → Sơ đồ phòng (vị trí máy, màu trạng thái) → Chi tiết máy (metrics history, processes, user) → Bảng điều khiển lệnh (xác nhận 2 bước) → Screenshot viewer → Quản lý Session (bắt đầu/kết thúc, phân bổ SV) → Báo cáo sử dụng → Quản trị danh mục (tòa nhà/phòng/máy/lớp/SV/lịch) → Quản lý người dùng & scope → Whitelist app/URL → Cảnh báo & Audit log.

---

## 6. Hạ tầng & DevOps

- `deploy/docker-compose.dev.yml`: Redis, MinIO, Seq **+ `api`/`frontend` dev (2026-09-14, hot-reload qua bind mount — SQL Server vẫn native, Keycloak vẫn stack CoreProject, cả hai qua `host.docker.internal`)**. Keycloak riêng chỉ cần nếu bạn không dùng CoreProject.
- `keycloak/realm-labmonitoring.json`: realm + client (web public, api confidential) + 3 role + mapper.
- Cấu hình: `appsettings.json` + user-secrets (dev), biến môi trường (prod). Connection string SQL, Keycloak authority, Redis, storage.
- CI (GitHub Actions hoặc script): build + test + `dotnet ef migrations`. Deploy: API trên IIS/Kestrel + reverse proxy HTTPS; Angular build tĩnh; Agent phát hành qua installer.
- Bắt buộc HTTPS/WSS toàn hệ thống.

---

## 7. Lộ trình theo module (làm đầy đủ từng module)

**Giai đoạn nền tảng (bắt buộc trước):**
- **F0** ✅ — `git init`, 3 vùng build độc lập (`backend/`, `agent/` hoàn toàn tách biệt, `frontend/`), mỗi vùng có `global.json`/`Directory.*.props` riêng, `.slnx` (`net10.0`), Clean Architecture skeleton, Serilog + Seq, OpenAPI + Scalar, ProblemDetails (§8.6), health checks, CORS, docker-compose dev (Redis/Seq/MinIO), agent publish/install scripts, ADR-0001 + ADR-0002, CI 2 job.
- **F1** ✅ — `LabMonitoringDbContext` + 24 entity (Mục 7, nhóm A–I) + `IEntityTypeConfiguration` theo nhóm + migration `InitialCreate` (đã apply lên SQL `MVT`, DB `LabMonitoring`) + seed 3 role. Enum lưu string, FK Restrict/SetNull, filtered unique index. Test model + DB-seed (tự skip khi không có DB). Xem `docs/adr/ADR-0003`.
- **F2** ✅ — JWT Bearer (Keycloak realm `coreproject`), `UserProvisioningMiddleware` (upsert theo `sub` + bootstrap admin), `ICurrentUser` / `IRoomScope`, scheme `AgentToken` (`AgentTokenHash` SHA-256 + `IsApproved`, migration `AgentAuthFields`), authorization policies role/scope, `POST /api/v1/agent/register` + `GET /api/v1/agent/config`. Client secret trong user-secrets. Test: 4 agent-endpoint (DB-gated). Xem `docs/adr/ADR-0004`.
- **F3** ✅ — `LabHub` (`/hubs/lab`, group `room:{id}`, `SubscribeToRoom` scope-checked, `AckCommand` sink) + Redis backplane + `IRealtimeNotifier`/`LabHubNotifier`; JWT qua query `access_token`. Angular 20 (`frontend/`, cổng 4300): `angular-oauth2-oidc` (PKCE), `authGuard`, interceptor, `LabHubService`, AdminLTE 4 shell + Dashboard. Test `LabHubTests`. Xem `docs/adr/ADR-0005`.
- **F4** ✅ — LabAgent (`agent/`): `MachineIdentityProvider`, `CredentialStore` (DPAPI), `AgentRegistrationService` (self-register), `SystemMetricsCollector` (CPU/RAM/Disk), `SessionInfoProvider` (WTS), `HeartbeatOutbox` (500, file-backed), `AgentApiClient` + `AgentHubClient` (SignalR, `AgentToken` header) + `CommandReceiver` (verify HMAC/nonce/clock-skew), `AgentWorker` loop. Backend: `POST /api/v1/agent/heartbeat` (status + metric + realtime events). Verified end-to-end (computer #13 registered + heartbeats persisted). 7 agent tests, 12 backend tests. Xem `docs/adr/ADR-0006`.

**Các module chức năng (mỗi module: API + background + Angular UI + test):**
1. **M1 — FR-1/FR-5 Giám sát & Quản lý phòng máy** ✅ — CRUD tòa nhà/phòng/máy (MediatR CQRS + `IAppDbContext`), duyệt/từ chối máy tự đăng ký, `OfflineDetectorService` (FR-1.5), `GET /api/v1/dashboard/summary` + `/computers/{id}/metrics`. Angular: dashboard live, danh sách phòng, **màn hình giám sát phòng** (lưới máy + màu trạng thái + patch realtime qua `ComputerStatusChanged`/`MetricsUpdated`), chi tiết máy, quản lý danh mục. Verify trên trình duyệt: duyệt máy → vào lưới; agent chạy → CPU/RAM cập nhật live không reload. 16 test backend. Xem `docs/adr/ADR-0007`.
2. **M2 — FR-6/FR-2 Ca học & Giám sát sinh viên** ✅ — CRUD Lớp/SV/Lịch học (+ ghi danh), `GET /api/v1/users` (picker giảng viên), Sessions: `start` (policy `teacher-or-admin`, chặn 2 ca Active/phòng, tự tạo `SessionComputers`), `allocate` (auto theo MSSV / thủ công), `end` (tự đăng xuất máy quên logout), `report` (FR-6.4). Heartbeat ghi `LoginAt`/`LogoutAt` theo ca Active (FR-2.1/2.2). Frontend: Lớp học + chi tiết lớp (ghi danh), Ca thực hành (danh sách/chi tiết/báo cáo), panel "Ca thực hành" trong chi tiết phòng. Sửa lỗi Redis single-point-of-failure phát hiện khi test browser (`AbortOnConnectFail=false`). Verify đầy đủ trên trình duyệt (tạo lớp → ghi danh → bắt đầu ca → phân bổ tự động → kết thúc → báo cáo). 17 test backend. Xem `docs/adr/ADR-0008`.
3. **M3 — FR-4 Điều khiển phòng máy** ✅ — `POST /api/v1/commands` (ký HMAC-SHA256 riêng từng máy qua `ICommandSigner`/Data Protection — sửa `AgentSecretHash` từ hash 1 chiều sang mã hoá 2 chiều để server ký được), whitelist app/URL 2 lớp (server `ApplicationPolicies`/`WebsitePolicies` + agent re-check), xác nhận 2 bước lệnh Restart/Shutdown cả phòng, `LabHub` group `computer:{id}` (phát lệnh) + `AckCommand` thật + `CommandTimeoutService`. Agent: `CommandReceiver.VerifyAsync`, `WindowsCommandExecutor` (Lock/Restart/Shutdown/Message/OpenApp/OpenUrl qua `SessionProcessLauncher`/`shutdown.exe`/`WTSSendMessage`), `AgentConfigCache`. Frontend: `CommandPanelComponent` dùng chung Room/Computer Detail, modal xác nhận tuỳ biến, trạng thái lệnh realtime. Live-test qua trình duyệt (đăng nhập Keycloak thật, agent console thật): OpenApp ký+xác thực+ack đúng, thực thi cuối báo lỗi quyền (đúng dự kiến, cần Windows Service thật). 22 test backend, 20 test agent. Xem `docs/adr/ADR-0009`.
4. **M4 — FR-3 Giám sát màn hình** (tạm hoãn, làm sau M6): tích hợp MinIO/NAS, chụp tức thời + theo chu kỳ (chỉ trong Session), viewer, job retention.
5. **M5 — FR-7 Báo cáo & Cảnh báo** ✅ — `IAlertPublisher` (Infrastructure/Notifications) fan-out dùng chung cho mọi cảnh báo: realtime `NewAlert` (giữ nguyên từ M1) + `Notification` per-user (Administrator + Technician/Teacher scoped) + email (`IEmailSender`, `NullEmailSender` mặc định chỉ log — chưa có SMTP thật, `SmtpEmailSender`/MailKit sẵn sàng bật qua config). Cảnh báo CPU/RAM mới (`Alerts:CpuThresholdPercent`/`RamThresholdPercent`, chặn lặp qua `ComputerStatus.CpuAlertRaisedAt`/`RamAlertRaisedAt`) chấm ngay tại `RecordHeartbeatAsync`. `LabHub` group `user:{id}` cho `NotificationCreated`. `GET/POST /api/v1/notifications*`, `GET /api/v1/reports/usage` + `/export` (CSV). Frontend: chuông thông báo trong Shell, trang Báo cáo sử dụng. Live-test: cảnh báo RAM thật xuất hiện live từ agent thật vẫn chạy nền. 2 test mới.
6. **M6 — FR-8 Người dùng & Phân quyền** ✅ — Không có "tạo user" (tài khoản do Keycloak JIT-provision, tạo trước sẽ trùng khi đăng nhập thật — xem ADR-0010); chỉ sửa/gán vai trò/gán phạm vi phòng cho tài khoản đã từng đăng nhập. `LastAdministratorGuard` chặn hạ quyền/vô hiệu hoá Administrator cuối cùng. `POST /rooms/{id}/regenerate-code` xoay `Rooms.Code` (không thêm cột mới). `GET /api/v1/audit-logs` phân trang (`PagedResult<T>`), AuditLog mở rộng cho hành động mới của M6 (chưa truy hồi M1–M3). Frontend: trang Người dùng (danh sách + chi tiết vai trò/phạm vi), trang Nhật ký, nút xoay mã phòng trong Danh mục. 3 test mới. Xem `docs/adr/ADR-0010`.
   - **Mở rộng 2026-09-14 — mô hình phân quyền màn hình/vai trò/user độc lập** (đã dự tính từ đầu ở `.claude/agents`, nay triển khai đúng lớp "màn hình + role + user override"): Vai trò trở thành CRUD tuỳ ý (`RolesController`), `FunctionScreen`/`RoleScreenPermission`/`UserScreenPermission` (bảng mới, migration `AddScreenPermissions`, 10 màn hình seed) là "màn hình chức năng" bạn đăng ký khi thêm tính năng mới. Chặn cả API (`ScreenAuthorizationPolicyProvider` — policy `screen:<code>` động, không cần sửa code khi thêm màn hình) lẫn UI (`PermissionService` + `screenGuard`, menu Angular giờ chính là screen registry, không còn mảng cứng). `GET /api/v1/me/screens` là nguồn sự thật duy nhất cho cả hai phía. 6 test mới (33 tổng). Xem `docs/adr/ADR-0011`.

**Giai đoạn hoàn thiện:**
- **H1** — Agent installer + hướng dẫn triển khai hàng loạt; auto-update.
- **H2** — Security review + pentest luồng auth/authorization (agent security-*), sửa lỗi.
- **H3** — Test E2E theo persona, tải giả lập 300 agent, chứng nhận reality-checker, tài liệu vận hành.

---

## 8. Việc cần bạn xác nhận / cung cấp

1. **Keycloak**: base URL (issuer/authority), realm name, client-id web (public), client-id + secret cho API (confidential), tên role mapping nếu khác `Administrator/Technician/Teacher`.
2. **Cập nhật `CLAUDE.md`**: bạn đã chốt **.NET 10** (thay ".NET 8 LTS") và **Angular** (thay "LTE4 + bootstrap"). Tôi sẽ sửa `CLAUDE.md` cho khớp — xác nhận giúp.
3. **SQL Server**: giữ 2017 Express (giới hạn 10GB) hay bạn sẽ cài bản 2022?
4. **NAS Synology**: giao thức truy cập (SMB share / WebDAV / S3-compatible)? Dev tạm dùng MinIO được không?
5. **Email**: SMTP server để gửi cảnh báo.
6. **Git remote**: có repo remote (GitHub…) để tôi push không, hay chỉ local?
