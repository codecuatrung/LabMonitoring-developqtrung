# ADR-0006 — LabAgent bản khung (F4)

- Trạng thái: Accepted
- Ngày: 2026-09-10

## Bối cảnh

Xây LabAgent (`agent/`, độc lập) đủ để: tự đăng ký, lưu bí mật an toàn, gửi heartbeat định kỳ,
kết nối SignalR, và chịu được mất mạng. Thực thi lệnh điều khiển là M3.

## Quyết định

### Backend — endpoint heartbeat (F4 mở rộng SRS 8.2)
- `POST /api/v1/agent/heartbeat` (scheme `AgentToken`): upsert `ComputerStatus`
  (CurrentStatus/LastHeartbeatAt/CurrentWindowsUser/LastIpAddress), thêm 1 `ComputerMetric`,
  cập nhật `Computers.AgentVersion` khi đổi, đẩy `ComputerStatusChanged` + `MetricsUpdated` qua
  `IRealtimeNotifier`. Trả `HeartbeatSeconds` để server điều chỉnh chu kỳ.

### Agent
- **Nhận diện máy** (`MachineIdentityProvider`): GUID ưu tiên registry `MachineGuid` (Windows) /
  `/etc/machine-id` (Linux) / GUID sinh & lưu `machine-id`. Hostname + MAC card đang Up.
- **Lưu bí mật** (`CredentialStore`): `{ComputerId, AgentToken, AgentSecret}` — Windows mã hoá
  **DPAPI** `LocalMachine` scope (`credentials.dat`); non-Windows ghi plaintext 0600 + cảnh báo
  (keystore Linux để sau). Xoay được (`Clear`).
- **Đăng ký** (`AgentRegistrationService`): nếu chưa có creds → `POST /agent/register` với
  MachineId + inventory (`SystemMetricsCollector.Inventory`). 409 (máy đã đăng ký, mất creds) →
  log rõ, cần admin reset token (M1).
- **Thu thập** (`SystemMetricsCollector`): CPU% (delta tổng `TotalProcessorTime` mọi tiến trình,
  cross-platform), RAM% (`GlobalMemoryStatusEx` / `/proc/meminfo`), Disk% ổ hệ thống (`DriveInfo`),
  nhiệt độ = null (LibreHardwareMonitor sau). `SessionInfoProvider`: user console qua WTS API (Windows).
- **Hàng đợi** (`HeartbeatOutbox`): bounded 500, file-backed (`outbox.jsonl`), drop cũ nhất khi đầy,
  `DrainAsync` dừng ở lần gửi lỗi đầu tiên, khôi phục từ disk khi khởi động.
- **SignalR** (`AgentHubClient`): `HubConnectionBuilder().WithUrl(..., header Authorization: AgentToken)`
  `.WithAutomaticReconnect()`. Nhận `CommandIssued` → `CommandReceiver` (verify HMAC-SHA256 +
  nonce TTL 60s + clock-skew ±5 phút — SRS 8.5) → `AckCommand`. Thực thi lệnh = M3.
- **`AgentWorker`**: ensure-registered (backoff 5s→300s) → refresh config → connect hub →
  vòng lặp `HeartbeatSeconds`: collect → POST; thành công thì drain outbox + đảm bảo hub connected;
  thất bại thì enqueue.
- `AgentPaths` là class inject được (test chuyển hướng sang thư mục tạm).
- csproj: `AllowUnsafeBlocks` (yêu cầu của `LibraryImport` source-gen).

## Đã kiểm chứng end-to-end (2026-09-10)

Agent chạy với `Agent__RoomCode=LAB-01` → đăng ký thành computer #13 (MVT, pending approval),
lưu `credentials.dat` (blob DPAPI, không phải JSON), lấy config (heartbeat 5s + whitelist LAB-01),
kết nối hub, gửi heartbeat: `ComputerStatuses` = Online + `ComputerMetrics` (CPU/RAM/Disk thật).

## Hạn chế đã biết

- `CurrentWindowsUser` trả null khi chạy console mode (WTS active-console-session) — cần xác nhận
  lại khi chạy dạng Service (SYSTEM); tinh chỉnh ở M1.
- Chưa có: danh sách tiến trình (FR-1.4), nhiệt độ CPU, cơ chế thu hồi token phía admin.
