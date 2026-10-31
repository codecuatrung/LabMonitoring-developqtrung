# ADR-0013 — Remote Control: xem + điều khiển máy tính từ xa (WebRTC)

- Trạng thái: Accepted
- Ngày: 2026-09-14

## Bối cảnh

Người dùng hỏi có thể xây tính năng remote-điều-khiển-được-máy-chỉ-định không, và chọn hướng "tự xây"
(WebRTC) thay vì tích hợp RDP có sẵn. Phạm vi v1: xem + điều khiển cùng lúc (không làm view-only
trước), có banner "Đang được hỗ trợ từ xa" trên máy đích, 1 máy tại 1 thời điểm, khởi động từ
Room/Computer Detail giống `command-panel` hiện có.

## Quyết định

### Signaling tách khỏi `CommandEnvelope`, không ký, không REST

Một phiên WebRTC là đàm phán 2 chiều, sống trong nhiều phút — khác hẳn lệnh một-chiều, ký HMAC, TTL
60s của `CommandEnvelope` (ADR-0009). SDP offer/answer và ICE candidate là chuỗi cơ hội server chỉ
relay, không parse/ký/chống-replay. Ủy quyền chỉ kiểm 1 lần lúc `RequestRemoteControl` (room-scope,
đúng pattern `SubscribeToRoom`), không có REST controller/Contracts DTO mới — 5 method mới thêm thẳng
vào `LabHub` (`RequestRemoteControl`/`SubmitOffer`/`SubmitAnswer`/`SubmitIceCandidate`/
`EndRemoteControl`) cộng override `OnDisconnectedAsync` để dọn sạch khi 1 trong 2 phía rớt kết nối.

### Session registry in-memory, single-instance

`IRemoteControlSessionRegistry` (`ConcurrentDictionary<Guid, RemoteControlSession>`, singleton) map
`sessionId → (adminConnectionId, computerId, agentConnectionId, ...)` để relay 1-1 thay vì broadcast
cả group. Cùng tradeoff đã chấp nhận ở `IScreenAccessService`'s cache: không đồng bộ nhiều instance —
đủ cho quy mô triển khai hiện tại (1 instance API), chỉ cần xem lại nếu scale-out.

### Chọn SIPSorcery cho phía Agent, cô lập vào 1 project helper riêng

`SIPSorcery` (`RTCPeerConnection`/data channel thuần C#) + `SIPSorceryMedia.Encoders` (mã hoá VP8 qua
`libvpx` native — không phải toàn bộ libwebrtc) chỉ thêm vào project mới
`agent/src/LabMonitoring.Agent.RemoteHelper/`, **không** vào `LabMonitoring.Agent.csproj` chính — giữ
Worker Service luôn chạy nhẹ, cô lập dependency media nặng vào tiến trình phụ trợ chỉ sống khi có
phiên remote-control.

### Capture + inject bắt buộc chạy trong session tương tác của người dùng

LocalSystem/Session 0 không thể BitBlt màn hình hay `SendInput` đúng vào Session 1 của người dùng.
Dùng lại đúng cơ chế `SessionProcessLauncher` (ADR-0009) đã dùng cho Lock/OpenApp/OpenUrl —
`RemoteControlCoordinator` (Worker Service chính) launch `LabMonitoring.Agent.RemoteHelper.exe` vào
session tương tác, giao tiếp qua Named Pipe có ACL rõ ràng (`NamedPipeServerStreamAcl`, vì server
Session 0/LocalSystem còn client Session 1/user token). `SessionProcessLauncher` được thêm 1 overload
trả về `Process` handle (thay vì đóng ngay) để coordinator theo dõi `Exited` và tự dọn session nếu
helper crash.

### Không dùng STUN/TURN cho v1

Server và PC phòng máy chung 1 LAN broadcast domain — đã xác nhận với người dùng ở tính năng
Wake-on-LAN. ICE host candidate là đủ để 2 bên kết nối trực tiếp. `RTCConfiguration.iceServers` để
rỗng; chỉ cân nhắc lại `coturn` nếu triển khai vượt ra ngoài 1 LAN/VLAN.

### Banner bằng WinForms, capture bằng GDI BitBlt (không phải DXGI Desktop Duplication)

Codebase agent hiện không dùng framework UI nào — `UseWindowsForms=true` có sẵn trong SDK, banner chỉ
là 1 `Form` borderless/topmost/`ShowInTaskbar=false`, rẻ hơn nhiều so với thêm WPF. Capture màn hình
dùng GDI `BitBlt` định kỳ cho v1 (đơn giản, đủ dùng); Desktop Duplication API (DXGI) là tối ưu độ trễ
cho sau, không làm ngay.

## Việc không làm ở v1
- Ghi lại/phát lại phiên; nhiều admin cùng điều khiển 1 máy; trang lịch sử phiên qua REST (dữ liệu đã
  nằm trong `AuditLog` action `RemoteControl.Start`/`.End`, chưa có UI xem).
- STUN/TURN server; Desktop Duplication API; dùng lại helper cho screenshot định kỳ (M4).

## Đã xác minh (2026-09-14)

- Backend: build sạch toàn solution, 32 integration test + 1 unit test cũ vẫn xanh (không có test
  riêng cho 5 method mới của `LabHub` — cùng khoảng trống như `SubscribeToRoom`/`AckCommand` trước
  đây, quan sát qua code review).
- Agent: build sạch cả `LabMonitoring.Agent` (coordinator) lẫn `LabMonitoring.Agent.RemoteHelper`
  (SIPSorcery/WinForms/GDI/SendInput) trên .NET SDK thật, đối chiếu API thật của SIPSorcery 10.0.16 /
  SIPSorceryMedia.Encoders 10.0.4 (tải mã nguồn ví dụ chính thức `WebRTCGetStartedLibvpx`/
  `WebRTCWebSocketPeerAspNet` để xác nhận `createOffer`/`setLocalDescription`/`setRemoteDescription`/
  `addIceCandidate`/`ExternalVideoSourceRawSample` thay vì đoán chữ ký). 20 unit test agent cũ vẫn
  xanh. `publish.ps1` chạy thật, xác nhận `RemoteHelper\LabMonitoring.Agent.RemoteHelper.exe` +
  `vpxmd.dll` (native libvpx) nằm đúng chỗ `RemoteControlCoordinator` tìm.
- Frontend: `ng build --configuration production` sạch (strict templates bật) sau khi thêm
  `RemoteControlComponent` + nút "Remote điều khiển" vào `command-panel`.
- **Chưa** live-test đầu-cuối thật trên máy có phiên đăng nhập tương tác (cần cài lại Agent + banner/
  video/input thật) — cùng khoảng trống đã ghi nhận ở ADR-0009 cho Lock/Restart/Shutdown/Message:
  cần môi trường có Windows Service thật, làm ở phiên sau theo đúng kịch bản kiểm chứng trong kế
  hoạch (đăng ký máy → publish → cài service → bấm "Remote điều khiển" từ UI → xác nhận banner/
  video/chuột/phím/dọn dẹp phiên).

## Hạn chế / để sau
- Registry chỉ 1 instance API — không chịu được scale-out nhiều instance.
- Chưa click-through cho banner (`WS_EX_TRANSPARENT`) — dải mỏng, chấp nhận tạm thời.
- Ánh xạ phím trình duyệt → Windows VK code dùng thẳng `KeyboardEvent.keyCode` — đúng với phần lớn
  phím chữ/số/điều hướng trên bàn phím US, sai lệch có thể xảy ra với layout không phải US hoặc phím
  đặc biệt; tinh chỉnh sau nếu cần.
