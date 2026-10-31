# ADR-0009 — M3: Điều khiển phòng máy / lệnh có chữ ký (FR-4, SRS §8.5)

- Trạng thái: Accepted
- Ngày: 2026-09-13

## Quyết định

### Sửa lỗi kiến trúc: `AgentSecretHash` phải mã hoá 2 chiều, không phải hash 1 chiều

F2 lưu `Computers.AgentSecretHash` bằng SHA-256 một chiều (`SecretHasher.Hash`) — giống hệt cách
lưu `AgentTokenHash`. Khi thiết kế M3 mới phát hiện đây là lỗi: SRS §8.5 yêu cầu **server ký lệnh
bằng HMAC-SHA256(payload, AgentSecret)** trước khi gửi cho agent — server phải khôi phục được
plaintext secret, một hash một chiều thì không thể. Đổi sang **ASP.NET Core Data Protection**
(`AgentSecretProtector` bọc `IDataProtector`, purpose `"LabMonitoring.AgentSecret.v1"`) — mã hoá
đối xứng có thể giải ngược, khoá lưu tại `%ProgramData%\LabMonitoring\Api\dp-keys`. `AgentTokenHash`
giữ nguyên một chiều (đúng, chỉ dùng để so khớp lúc xác thực, không cần khôi phục).
Đã xoá 2 máy test cũ (Id 5, 13) đăng ký từ trước khi sửa — hash cũ không unprotect được.

Kiểm chứng bằng integration test độc lập với cơ chế: `Single_machine_command_is_sent_and_signed_with_that_machines_secret`
lấy `agentSecret` plaintext trả về lúc đăng ký, tự tính lại `HMACSHA256(agentSecret, payload)` và so
với `Command.Signature` đọc trực tiếp từ DB — chứng minh vòng Protect→lưu DB→Unprotect→ký đúng.

### `CommandEnvelope.CommandId`: `Guid` → `long`, bump `ProtocolVersion` "1.0"→"1.1"

F1 định nghĩa contract với `CommandId: Guid` (đoán trước, chưa có bảng `Commands` dùng tới). M3 nối
với `Commands.Id bigint` thật — đổi thẳng sang `long` thay vì thêm tầng map Guid↔long. Đổi ở cả 2
bản sao contract (`backend/src/LabMonitoring.Contracts` và `agent/src/LabMonitoring.Agent/Protocol`,
theo đúng chủ đích "duplicate on purpose" từ ADR-0002/0006) + bump version.

### Phát lệnh phòng: ký riêng từng máy, gửi theo nhóm SignalR riêng từng máy

Một lệnh "restart cả phòng" phải chạm N máy, **mỗi máy có `AgentSecret` khác nhau** → không thể có
một envelope ký chung để broadcast cả phòng. `IssueCommandHandler` lặp qua từng máy đích, ký riêng
(`ICommandSigner.SignForComputerAsync`) và gọi `IRealtimeNotifier.CommandIssuedAsync(computerId, …)`
riêng cho từng máy. Đổi `LabHub` từ chỉ có `room:{id}` group sang thêm `computer:{id}` group (agent
tự join group máy mình khi connect); `CommandIssued` gửi vào group máy, còn `CommandResultReceived`
vẫn gửi vào group phòng (để trang phòng thấy kết quả mọi máy cùng lúc). `Commands.Signature` (DB)
lưu chữ ký của máy đích đầu tiên — mang tính đại diện/audit, không dùng để verify lại (mỗi
`CommandResult` không lưu signature riêng, việc verify chỉ xảy ra phía agent).

### Xác nhận 2 bước cho lệnh cả phòng (FR-4.3)

`IssueCommandCommand` có cờ `Confirm`. `IssueCommandHandler.ResolveTargetsAsync` ném lỗi (400) nếu
`RoomId` được set, `CommandType` ∈ {Restart, Shutdown} và `Confirm=false`. Chỉ áp dụng cho lệnh cả
phòng — lệnh 1 máy Restart/Shutdown không cần xác nhận. Frontend: `CommandPanelComponent` tự hiện
modal Bootstrap tuỳ biến (không dùng `confirm()` của trình duyệt — sẽ chặn cả kết nối SignalR) khi
bấm Restart/Shutdown ở panel cả phòng; máy đơn thì gửi thẳng.

### Whitelist ứng dụng/URL: chặn 2 lớp (server + agent)

- Server (`IssueCommandHandler.EnsureWhitelistedAsync`): OpenApp/OpenUrl phải có policy `Allow`
  khớp (theo `ApplicationPolicies`/`WebsitePolicies`, `RoomId` null = áp dụng mọi phòng) **và không
  có** policy `Block` khớp — thiếu Allow hoặc có Block đều 403, chặn từ gốc, không gửi lệnh xuống agent.
- Agent (`WindowsCommandExecutor.OpenApp/OpenUrl` gọi `WhitelistChecker`): re-check lại với
  `AgentConfigCache` (đồng bộ mỗi 60s qua `GET /agent/config`) trước khi launch — phòng trường hợp
  policy đổi giữa lúc lệnh được ký và lúc agent thực thi, hoặc agent có bug/bị giả mạo lệnh qua kênh
  khác. Cùng logic (fail-closed nếu chưa có config, Block thắng Allow, URL so khớp theo host/subdomain).

### Vòng đời lệnh phía agent: verify → ack → thực thi → báo kết quả

`CommandReceiver.VerifyAsync` (đổi tên từ `HandleAsync`, đổi kiểu trả `CommandEnvelope?` thay vì chỉ
`Guid?`) kiểm chữ ký/nonce (chống replay, TTL 60s)/lệch giờ (±5 phút) — không đổi logic, chỉ đổi để
trả cả envelope cho bước thực thi dùng tiếp. `AgentHubClient.HandleCommandIssuedAsync` là orchestrator
mới: verify → `InvokeAsync("AckCommand", …)` (lỗi ack không chặn thực thi, chỉ log warning) → 
`ICommandExecutor.ExecuteAsync` → `POST /agent/command-result`. `LabHub.AckCommand` (trước là no-op
từ F3) nay là implementation thật: chuyển `CommandResult.Status` Sent→Acknowledged, đẩy
`CommandResultReceivedAsync`. `CommandTimeoutService` (BackgroundService, quét mỗi 20s) chuyển các
`CommandResult` còn Sent/Acknowledged quá 60s (`AgentDefaultsOptions.CommandTimeoutSeconds`) sang
Timeout — phòng trường hợp máy mất kết nối giữa chừng, không bao giờ báo kết quả.

### Thực thi lệnh trên Windows (agent)

`WindowsCommandExecutor`: Lock/OpenApp/OpenUrl dùng chung `SessionProcessLauncher` — chuỗi Win32
chuẩn để Service (LocalSystem) chạy tiến trình **trong phiên đăng nhập của người dùng đang ngồi máy**
(`WTSGetActiveConsoleSessionId` → `WTSQueryUserToken` → `DuplicateTokenEx` → `CreateEnvironmentBlock`
→ `CreateProcessAsUser`). Restart/Shutdown gọi thẳng `shutdown.exe /r` hoặc `/s` (không cần chạy
trong phiên user, LocalSystem có sẵn quyền, có `/t 30` + thông báo trước khi tắt). Message dùng
`WTSSendMessage` trực tiếp (không cần duplicate token). `UnsupportedPlatformCommandExecutor` (non-
Windows) luôn trả Failed rõ ràng, không âm thầm bỏ qua — Linux là việc của sau (CLAUDE.md).

### Frontend: `CommandPanelComponent` dùng chung cho phòng và máy đơn

Component `features/shared/command-panel` nhận `target: {computerId} | {roomId}` — dùng lại ở cả
Room Detail (điều khiển cả phòng) và Computer Detail (điều khiển 1 máy). Kết quả lệnh gần đây
(`[results]`) do **component cha đẩy vào** (qua `LabHubService.on('CommandResultReceived', …)`)
thay vì để panel tự subscribe hub — lý do: `HubConnection.off(event)` của SignalR gỡ **toàn bộ**
handler của tên event đó, nếu panel con tự `on()`/`off()` độc lập sẽ gỡ luôn handler của trang cha
(hoặc panel khác) dùng chung tên event `CommandResultReceived`. Computer Detail trước đây chưa hề
subscribe hub (chỉ tải tĩnh) — M3 bổ sung `hub.start()` + `subscribeToRoom(c.roomId)` khi tải xong
thông tin máy, dọn ở `ngOnDestroy`, theo đúng pattern Room Detail đã có từ M1.

## Đã live-test qua trình duyệt thật (2026-09-13)

Chạy backend + frontend + agent thật (console mode, `RoomCode=LAB-01`), đăng ký máy mới `MVT`
(#72 — 2 máy test cũ #5/#13 đã xoá vì hash cũ không tương thích), duyệt qua SQL, xác nhận agent
đồng bộ config + kết nối hub. Đăng nhập Keycloak thật qua trình duyệt (session SSO có sẵn của
`mvt`), thao tác trực tiếp trên UI (không có cách nào lấy JWT ra ngoài trình duyệt — đúng chủ đích
bảo mật của môi trường test) — Angular tự gắn Bearer token qua interceptor sẵn có:
- **OpenApp (Chrome, trong whitelist)** từ trang Computer Detail: lệnh phát ra, ký đúng, gửi qua
  SignalR tới đúng agent, agent verify chữ ký/nonce **thành công**, whitelist re-check qua, `AckCommand`
  chuyển trạng thái **Acknowledged** hiển thị realtime trên UI không cần tải lại trang. Bước thực thi
  cuối (`CreateProcessAsUser`) báo lỗi `WTSQueryUserToken failed (Win32 error 1314 — ERROR_PRIVILEGE_NOT_HELD)`
  — **đúng như dự kiến**: `SeTcbPrivilege` chỉ có khi chạy dưới LocalSystem qua Windows Service thật
  (đã ghi nhận từ ADR-0006/F4 "CurrentWindowsUser null ở console mode"), console mode elevated không
  có quyền này. Chứng minh toàn bộ pipeline ký/xác thực/định tuyến/whitelist/ack đúng; riêng bước
  launch tiến trình cần cài Service thật để verify hết.
- **Xác nhận 2 bước cả phòng**: bấm "Khởi động lại" ở Room Detail → modal xác nhận hiện đúng nội
  dung, bấm Hủy để không thực sự restart máy dev đang dùng.
- **Không live-test** Lock/Restart/Shutdown/Message thật lên máy dev (sẽ khoá/tắt/hiện thông báo
  gián đoạn phiên làm việc) — các đường này chỉ được xác minh qua 21 integration test backend +
  20 unit test agent (bao gồm bộ `WhitelistCheckerTests` mới) và code review.

## Hạn chế / để sau

- Chưa cài LabAgent như Windows Service thật trong phiên này để verify nốt `CreateProcessAsUser`
  (cần quyền cài service — hỏi người dùng trước khi làm, chưa được yêu cầu).
- Chưa có UI xem lịch sử toàn bộ lệnh đã phát (`GET /api/v1/commands` list) — Computer/Room Detail
  chỉ hiện kết quả lệnh gần đây theo phiên trình duyệt hiện tại (không tải lịch sử cũ từ DB lúc vào
  trang). Có thể bổ sung ở M5/M6 cùng trang Báo cáo.
- `Commands.Signature` (DB) chỉ lưu chữ ký của máy đích đầu tiên trong lệnh cả phòng — đủ cho audit
  "lệnh này đã được ký", không dùng để verify lại toàn bộ N chữ ký của N máy.
