# ADR-0002 — Tách repo/thư mục: backend · agent · frontend độc lập

- Trạng thái: Accepted
- Ngày: 2026-09-10
- Thay thế một phần: ADR-0001 (mục cấu trúc solution)

## Bối cảnh

ADR-0001 gộp tất cả vào một solution `src/` + `tests/` ở gốc. Yêu cầu chủ dự án: backend, LabAgent,
và frontend phải **build được độc lập**; LabAgent đặc biệt cần đóng gói riêng để copy sang máy trạm.

## Quyết định

Bố cục gốc repo thành các vùng tự chứa:

```
backend/    LabMonitoring.Backend.slnx + global.json + Directory.*.props riêng
            src/{Domain,Application,Infrastructure,Contracts,Api}  tests/{UnitTests,IntegrationTests}
            .config/dotnet-tools.json (dotnet-ef)
agent/      LabMonitoring.Agent.slnx  + global.json + Directory.*.props riêng
            src/LabMonitoring.Agent (kèm Protocol/CommandEnvelope.cs)   tests/LabMonitoring.Agent.Tests
            build/ (publish.ps1, install-service.ps1, uninstall-service.ps1)
frontend/   Angular workspace (F3)
deploy/ docs/ *.md   ở gốc
```

- Mỗi vùng có `global.json` + `Directory.Build.props` + `Directory.Packages.props` riêng → không rò
  rỉ cấu hình/phiên bản package giữa các vùng; `cd backend && dotnet build` và `cd agent && dotnet build`
  hoàn toàn tách biệt.
- **Gốc repo không đặt `Directory.Build.props`/`Directory.Packages.props`** để tránh áp dụng chéo.
- **LabAgent hoàn toàn độc lập**: không có `ProjectReference` nào ra ngoài `agent/`. Giao thức lệnh
  (SRS §8.5) được **nhân bản có chủ đích** ở hai nơi:
  - `backend/src/LabMonitoring.Contracts/Agent/CommandEnvelope.cs`
  - `agent/src/LabMonitoring.Agent/Protocol/CommandEnvelope.cs`
  Hằng số `CommandEnvelope.ProtocolVersion` ("1.0") có ở cả hai; mọi thay đổi phá vỡ shape hoặc đầu
  vào ký HMAC phải bump ở cả hai bên. Không dùng project/NuGet chung để giữ agent tách biệt tuyệt đối.
- CI tách 2 job: `backend` (ubuntu) và `agent` (windows, kèm `dotnet publish` + upload artifact).

## Hệ quả

- LabAgent: `agent/build/publish.ps1` tạo bản self-contained single-file `win-x64`; `install-service.ps1`
  cài Windows Service `LabMonitoringAgent` (sc.exe, start=auto, auto-restart).
- Không còn một lệnh build duy nhất cho toàn hệ thống; tài liệu/README phản ánh 3 vùng.
- Khi cần bản Agent cho Linux (giai đoạn sau): thêm RID `linux-x64` (đã khai trong csproj) + unit systemd.
