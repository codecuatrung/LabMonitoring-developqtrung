# ADR-0001 — Nền tảng kiến trúc (Phase 1 / F0)

- Trạng thái: Accepted
- Ngày: 2026-09-10

## Bối cảnh

Hệ thống Giám sát & Quản lý Phòng Thực hành (SRS đính kèm) cần: API realtime cho ~300 máy/5 phòng,
một Windows Service (LabAgent) trên máy trạm, và một dashboard quản trị. Xác thực dùng Keycloak
đã có (realm `coreproject`, chia sẻ với "CoreProject" — chỉ chia sẻ danh tính người dùng).

## Quyết định

1. **Runtime .NET 10** (`net10.0`) cho backend và Agent. CLAUDE.md ban đầu ghi ".NET 8 LTS"; chủ
   dự án đổi sang .NET 10 (SDK 10.0.301 sẵn có). `global.json` pin SDK, `rollForward: latestFeature`.
2. **Clean Architecture 4 lớp**: `Domain` (thuần, không phụ thuộc) → `Application` (CQRS/MediatR,
   FluentValidation, Mapster) → `Infrastructure` (EF Core + SQL Server, Redis, storage, email) →
   `Api` (controllers + SignalR). `Contracts` chứa DTO dùng chung Server ↔ Agent.
3. **Central Package Management** (`Directory.Packages.props`) + `Directory.Build.props` chung.
4. **Solution format `.slnx`** (định dạng mới, .NET 10 hỗ trợ gốc).
5. **OpenAPI**: `Microsoft.AspNetCore.OpenApi` (built-in) + Scalar UI ở môi trường Development.
6. **Logging**: Serilog, đọc từ config, sink Console + Seq (dev). Mã lỗi HTTP chuẩn hoá qua
   `IExceptionHandler` + ProblemDetails theo SRS Mục 8.6.
7. **Hạ tầng dev**: SQL Server chạy native (`MVT`); Redis + Seq + MinIO qua
   `deploy/docker-compose.dev.yml`. Keycloak lấy từ stack CoreProject (cổng 8082).
8. **Cổng dev**: API `5200/7200`, Angular `4300`, Keycloak `8082`, Seq `5341`, MinIO `9000/9001`.
9. **Xác thực (F2)**: JWT Bearer (Keycloak) cho Web Admin + scheme `AgentToken` cho Agent.
   Phân quyền: role `Administrator/Technician/Teacher` + scope phòng lưu trong DB của hệ thống này,
   không dùng Keycloak roles. User provisioning theo claim `sub`.

## Hệ quả

- SQL Server hiện là bản **2017 Express** (giới hạn 10GB/DB) — bảng `ComputerMetrics` cần job
  retention/rollup; nên nâng lên 2022 trước production.
- Agent chỉ Windows ở Phase 1; cấu trúc tách `Contracts` để phần lõi tái dùng khi làm bản Linux.
- Phụ thuộc Keycloak dùng chung: thay đổi realm `coreproject` ảnh hưởng cả hai hệ thống.
