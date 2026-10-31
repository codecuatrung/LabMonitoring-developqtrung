# Lab Monitoring — Hệ thống Giám sát & Quản lý Phòng Thực hành Máy tính

Ba vùng build **độc lập**: `backend/` (.NET 10, Clean Architecture), `agent/` (LabAgent — Windows
Service), `frontend/` (Angular + AdminLTE 4). Xác thực qua Keycloak dùng chung (realm `coreproject`).

Tài liệu: đặc tả `Dac_ta_ky_thuat_SRS_ERD_API_LabMonitoring.docx` · kế hoạch `PLAN.md` ·
Keycloak `KEYCLOAK-INTEGRATION.md` · quyết định kiến trúc `docs/adr/`.

## Bố cục repo

```
backend/    LabMonitoring.Backend.slnx  — Domain · Application · Infrastructure · Contracts · Api  (+ tests)
agent/      LabMonitoring.Agent.slnx    — Worker Service → Windows Service, hoàn toàn độc lập      (+ tests, build scripts)
frontend/   Angular workspace (khởi tạo ở F3)
deploy/     docker-compose.dev.yml (Redis · Seq · MinIO)
docs/adr/   Architecture Decision Records
```

Giao thức lệnh Server↔Agent (SRS §8.5) được nhân bản có chủ đích ở `backend/src/LabMonitoring.Contracts`
và `agent/src/LabMonitoring.Agent/Protocol` — giữ đồng bộ qua `CommandEnvelope.ProtocolVersion`. Xem `docs/adr/ADR-0002`.

Mỗi vùng có `global.json` + `Directory.*.props` riêng — xem `docs/adr/ADR-0002`.

## Yêu cầu môi trường

| Thành phần | Phiên bản | Ghi chú |
|---|---|---|
| .NET SDK | 10.0.30x | pin trong `backend/global.json`, `agent/global.json` |
| SQL Server | 2017+ (khuyến nghị 2022) | native, server `MVT`, `sa` (mật khẩu trong `.env`/User Secrets, xem `.env.example`) |
| Docker | 24+ | Redis / Seq / MinIO cho dev |
| Node.js | 20+ | Angular workspace (F3) |
| Keycloak | — | stack CoreProject, `http://localhost:8082` |

## Chạy nhanh (dev) — Docker

Từ 2026-09-14, backend API + frontend Angular chạy trong Docker luôn (hot-reload qua bind mount),
cùng với Redis/Seq/MinIO đã có sẵn. SQL Server vẫn native trên máy (server `MVT`) — container `api`
kết nối vào qua `host.docker.internal`; Keycloak vẫn là stack CoreProject bên ngoài, cũng qua
`host.docker.internal:8082`.

```bash
docker compose -f deploy/docker-compose.dev.yml up          # tất cả: redis, seq, minio, api, frontend
# hoặc chạy nền: ... up -d
```

- API: `http://localhost:5200` (Scalar: `/scalar/v1`) — sửa file trong `backend/` là `dotnet watch` tự build lại.
- Frontend: `http://localhost:4300` — sửa file trong `frontend/` là Angular dev server tự rebuild.
- Seq (xem log): `http://localhost:5341`. MinIO console: `http://localhost:9001`.
- Chỉ cần hạ tầng phụ trợ (chạy backend/frontend native như trước): `... up redis seq minio`.
- Rebuild image sau khi đổi `Dockerfile.dev`/`package.json`/`*.csproj`: thêm `--build`.

Endpoint kiểm tra: `GET /api/v1/meta`, `GET /health/live`, `GET /health/ready` (health/ready xác nhận
cả SQL Server lẫn Redis đang kết nối được từ container).
Migration DB **vẫn chạy từ máy host** như cũ, nhắm thẳng vào `MVT` (không đổi khi container đang chạy):
`dotnet run --project src/LabMonitoring.Api -- seed` (thêm `--dev-data` để có dữ liệu mẫu), hoặc
`dotnet tool restore && dotnet ef database update --project src/LabMonitoring.Infrastructure --startup-project src/LabMonitoring.Api`.

Chạy backend/frontend native không qua Docker (như trước 2026-09-14) vẫn hoạt động bình thường —
`cd backend && dotnet run --project src/LabMonitoring.Api` / `cd frontend && npm start` — hai cách
không xung đột port miễn không chạy đồng thời.

Agent (`agent/`) không nằm trong Docker (build cho máy trạm Windows, không phải server):
```bash
cd agent && dotnet build LabMonitoring.Agent.slnx && dotnet test LabMonitoring.Agent.slnx
dotnet run --project src/LabMonitoring.Agent                    # console mode
```
Đóng gói Agent để cài lên máy trạm: `agent/README.md`.

## Trạng thái

- [x] **F0** — Scaffold 3 vùng độc lập, Clean Architecture, Serilog, OpenAPI/Scalar, health checks, docker-compose, CI
- [x] **F1** — `LabMonitoringDbContext` + 24 bảng (SRS Mục 7) + migration `InitialCreate` + seed 3 role. Xem `docs/adr/ADR-0003`
- [x] **F2** — Keycloak JWT + provisioning theo `sub` + bootstrap admin + scheme `AgentToken` + policy role/scope + `POST /api/v1/agent/register` + `GET /api/v1/agent/config`. Xem `docs/adr/ADR-0004`
- [x] **F3** — SignalR `/hubs/lab` (group theo phòng) + Redis backplane; Angular 20 workspace trong `frontend/` (Keycloak login, AdminLTE 4 shell, dashboard, SignalR service), dev port 4300. Xem `docs/adr/ADR-0005`
- [x] **F4** — LabAgent: self-register, DPAPI credential store, metric collectors, heartbeat loop, file-backed outbox (500), SignalR client + command verification; backend `POST /api/v1/agent/heartbeat`. Xem `docs/adr/ADR-0006`
- [x] **M1** — FR-1/FR-5: CRUD tòa nhà/phòng/máy, duyệt máy tự đăng ký, offline detector, dashboard live, màn hình giám sát phòng realtime (`docs/adr/ADR-0007`)
- [x] **M2** — FR-6/FR-2: Lớp/SV/Lịch + ghi danh, Session start/allocate/end/report, ghi login/logout SV qua heartbeat (`docs/adr/ADR-0008`)
- [x] **M3** — FR-4: lệnh điều khiển phòng máy ký HMAC-SHA256 (Lock/Restart/Shutdown/Message/OpenApp/OpenUrl), whitelist 2 lớp, xác nhận 2 bước, trạng thái lệnh realtime (`docs/adr/ADR-0009`)
- [x] **M5** — FR-7: cảnh báo CPU/RAM/offline (in-app + email, email tạm chỉ log), báo cáo sử dụng + xuất CSV (`docs/adr/ADR-0010`)
- [x] **M6** — FR-8: quản lý vai trò/phạm vi phòng người dùng, xoay mã đăng ký phòng, tra cứu nhật ký hệ thống (`docs/adr/ADR-0010`)
- [x] **M6+** (2026-09-14) — mô hình phân quyền màn hình/vai trò/user độc lập: vai trò CRUD tuỳ ý, đăng ký "màn hình chức năng", phân quyền theo vai trò + ghi đè riêng từng người, chặn cả API lẫn UI (`docs/adr/ADR-0011`)
- [ ] **M4** — FR-3 giám sát màn hình (tạm hoãn, xem `PLAN.md`)
