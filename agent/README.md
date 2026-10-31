# LabMonitoring.Agent

Dịch vụ Windows chạy trên từng máy trạm: thu thập trạng thái/metrics, nhận & thực thi lệnh điều khiển
(SRS Mục 8.5). Solution **hoàn toàn độc lập** — không tham chiếu project nào ngoài `agent/`,
build và đóng gói riêng, copy tới máy client.

## Cấu trúc

```
agent/
  LabMonitoring.Agent.slnx
  global.json · Directory.Build.props · Directory.Packages.props
  src/LabMonitoring.Agent/
    AgentWorker.cs                  vòng lặp: đăng ký → hub → heartbeat + outbox drain
    AgentPaths.cs                   %ProgramData%\LabMonitoring\Agent (creds, outbox, logs)
    Configuration/AgentOptions.cs   ServerBaseUrl · RoomCode · HeartbeatSeconds · OutboxCapacity
    Protocol/                       CommandEnvelope + bản sao contract register/heartbeat/config
    Identity/                       MachineIdentityProvider · CredentialStore (DPAPI)
    Monitoring/                     SystemMetricsCollector (CPU/RAM/Disk) · SessionInfoProvider (WTS)
    Outbox/HeartbeatOutbox.cs       hàng đợi 500, file-backed, resend on reconnect
    Client/                         AgentApiClient · AgentHubClient · AgentRegistrationService
    Commands/CommandReceiver.cs     verify chữ ký/nonce/clock-skew (thực thi = M3)
  tests/LabMonitoring.Agent.Tests/
  build/                            publish + install scripts
```

## Chạy thử với backend local

```powershell
# backend đã chạy ở http://localhost:5200, phòng LAB-01 đã seed (dotnet run -- seed --dev-data)
cd agent
$env:Agent__RoomCode = 'LAB-01'
$env:Agent__ServerBaseUrl = 'http://localhost:5200'
dotnet run --project src/LabMonitoring.Agent
```
Agent tự đăng ký (trạng thái *pending approval*), lưu token vào `credentials.dat` (mã hoá DPAPI),
gửi heartbeat mỗi 5s. Xoá `%ProgramData%\LabMonitoring\Agent` để đăng ký lại từ đầu.

## Build & test (máy dev)

```powershell
cd agent
dotnet build LabMonitoring.Agent.slnx
dotnet test  LabMonitoring.Agent.slnx
dotnet run   --project src/LabMonitoring.Agent      # chạy console để debug
```

## Đóng gói để cài lên máy trạm

```powershell
cd agent
./build/publish.ps1                                 # → agent/artifacts/agent (self-contained, single-file)
```

Copy thư mục `artifacts/agent` sang máy client rồi chạy **PowerShell (Administrator)**:

```powershell
./install-service.ps1 -ServerBaseUrl https://labmonitoring.truong.local -RoomCode LAB-01
```

Script sẽ: copy binary vào `C:\Program Files\LabMonitoring\Agent`, đặt biến môi trường máy
(`Agent__ServerBaseUrl`, `Agent__RoomCode`), tạo service `LabMonitoringAgent` (start=auto, tự
restart khi lỗi), và start service.

Gỡ: `./uninstall-service.ps1`.

## Cấu hình

`appsettings.json` mục `Agent` (ghi đè bằng biến môi trường `Agent__*` khi cài service).
AgentToken/AgentSecret nhận khi đăng ký lần đầu sẽ được lưu **mã hoá DPAPI** (F4).
