# ADR-0005 — Realtime (SignalR) & Angular shell (F3)

- Trạng thái: Accepted
- Ngày: 2026-09-10

## Realtime — SignalR `/hubs/lab` (SRS 8.4)

- Một hub `LabHub`, group theo phòng: `room:{roomId}`.
  - Web Admin client gọi `SubscribeToRoom(roomId)` — kiểm tra `IRoomScope.CanAccessRoomAsync`
    (SRS FR-8.2) trước khi add vào group.
  - Agent tự vào group phòng của mình khi kết nối (đọc claim `lab:room_id`).
  - `AckCommand(commandId)` — sink rỗng, M3 sẽ ghi trạng thái Acknowledged.
- `[Authorize]` với **cả hai scheme** `Bearer` + `AgentToken` (Agent dùng .NET SignalR client,
  set được header `Authorization: AgentToken ...`).
- Web client (browser) gửi JWT qua **query string** `access_token` — `JwtBearerEvents.OnMessageReceived`
  đọc token này khi path bắt đầu bằng `/hubs/lab` (WebSocket không set được header).
- **Redis backplane** bật ngay từ MVP: `AddStackExchangeRedis` khi có `ConnectionStrings:Redis`
  (channel prefix `labmonitoring`). NFR "Khả năng mở rộng" — scale-out chỉ là đổi cấu hình.
- `IRealtimeNotifier` (Application) + `LabHubNotifier` (Api) — module đẩy sự kiện
  (`ComputerStatusChanged`/`MetricsUpdated`/`NewAlert`/`CommandResultReceived`/`CommandIssued`)
  mà không phụ thuộc trực tiếp SignalR.

## Frontend — Angular 20 (standalone) trong `frontend/`

- Workspace tạo bằng Angular CLI 20.3, `frontend/` là gốc (build/deploy độc lập — ADR-0002).
- **Auth**: `angular-oauth2-oidc` (Authorization Code + PKCE), client `labmonitoring-spa`,
  `sessionChecksEnabled: false` (bsc./sso. khác subdomain → iframe bị chặn 3rd-party cookie —
  theo `KEYCLOAK-INTEGRATION.md`). `provideAppInitializer` gọi `AuthService.init()` (load discovery +
  tryLogin) trước khi render. `authGuard` redirect sang Keycloak khi chưa có token.
  HTTP interceptor gắn `Authorization: Bearer` cho request tới `/api`.
- **Realtime**: `LabHubService` (`@microsoft/signalr`) — `accessTokenFactory` lấy token từ `AuthService`,
  `withAutomaticReconnect`, expose `status` signal.
- **UI**: AdminLTE 4 + Bootstrap 5 + bootstrap-icons (nạp qua `angular.json` styles/scripts, không
  import trong SCSS để tránh xung đột `@use`/`@import`). Layout `Shell` (sidebar + topbar +
  `<router-outlet>`); trang `Dashboard` hiện xác nhận kết nối API (`/api/v1/meta`) + trạng thái hub.
- **Cổng dev 4300**, `proxy.conf.json` chuyển `/api` → `http://localhost:5200`; `hubUrl` để tuyệt đối
  (WebSocket không qua proxy). Prod: `fileReplacements` → `environment.production.ts` (sso.invatech.com.vn).

## Hệ quả

- Các trang Phòng máy / Ca thực hành / Báo cáo / Người dùng mới là menu (disabled) — điền ở M1–M6.
- `styles.css` ~750 kB (Bootstrap + AdminLTE + icons) — chấp nhận ở dev; tối ưu/purge khi hoàn thiện.
- `.NET` SignalR client cho Agent sẽ thêm ở F4.
