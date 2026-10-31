# ADR-0004 — Xác thực & phân quyền (F2)

- Trạng thái: Accepted
- Ngày: 2026-09-10

## Bối cảnh

SRS 8.1: Web Admin xác thực bằng JWT Keycloak; Agent xác thực bằng AgentToken cấp lúc đăng ký.
Keycloak dùng chung realm `coreproject` — chỉ chia sẻ danh tính (`sub`); role/scope là của hệ thống này
(xem `KEYCLOAK-INTEGRATION.md`).

## Quyết định

### Web Admin — JWT Bearer (scheme `Bearer`)
- `AddJwtBearer` với `MetadataAddress` = `{InternalAuthority}/.well-known/openid-configuration`,
  `ValidIssuer` = public `Authority` (khớp `iss` chính xác), `ValidAudience` = `labmonitoring-api`,
  `MapInboundClaims = false` (giữ tên claim gốc: `sub`, `preferred_username`, `email`, `name`).
- **Không** dùng role của Keycloak. Role lấy từ bảng `UserRoles` của hệ thống này.

### Provisioning theo `sub` (`UserProvisioningMiddleware`)
- Chạy sau `UseAuthentication`, trước `UseAuthorization`, chỉ cho request JWT.
- Upsert `Users` theo `KeycloakUserId == sub` (không bao giờ join theo email), đồng bộ
  `Username/FullName/Email` từ token.
- Thêm claim `lab:uid` (= `Users.Id`) và `lab:role` (mỗi role trong DB) vào principal.
- **Bootstrap admin**: user chưa có role nào mà `preferred_username`/`email` nằm trong
  `Auth:BootstrapAdmins` → tự cấp role Administrator (giải bài toán "admin đầu tiên").

### Agent — scheme `AgentToken`
- Header `Authorization: AgentToken <token>`. `AgentTokenAuthenticationHandler` tra
  `Computers.AgentTokenHash` (SHA-256, so sánh fixed-time), gắn claim
  `lab:computer_id` / `lab:machine_id` / `lab:room_id` / `lab:agent_approved`.
- Token & AgentSecret là **chuỗi ngẫu nhiên ~256 bit, chỉ lưu HASH** (`SecretHasher`).
  Plaintext trả đúng 1 lần ở `/agent/register`. Thu hồi 1 máy = xoay `AgentTokenHash`.
- Thêm cột `Computers.AgentTokenHash` + `Computers.IsApproved` (migration `AgentAuthFields`).

### Authorization policies
`role:Administrator|Technician|Teacher`, `admin-console` (một trong 3 role), `approved-agent`
(scheme AgentToken + `lab:agent_approved=true`). Mỗi policy khoá đúng authentication scheme.

### Room scope (`IRoomScope`)
Administrator → không giới hạn (`null`); còn lại → tập `UserRoomScopes`. Kết cấu sẵn; enforce
từng endpoint sẽ làm ở M1.

### Endpoint Agent (SRS 8.2)
- `POST /api/v1/agent/register` — `[AllowAnonymous]`, gate bằng `Rooms.Code`. Máy mới → `IsApproved=false`.
  MachineId đã tồn tại → 409; room code sai → 400.
- `GET /api/v1/agent/config` — scheme `AgentToken`. Trả chu kỳ heartbeat/offline/screenshot
  (`AgentDefaults`) + whitelist app/URL theo `ApplicationPolicies`/`WebsitePolicies` của phòng (+ toàn hệ thống).

## Hệ quả / ghi chú

- `Keycloak:ClientSecret` **không** cần cho việc validate JWT; chỉ lưu sẵn (user-secrets, không commit)
  để sau này gọi Keycloak Admin API. Đã set trên máy dev.
- Convention: controller quản trị đặt `[Authorize(Policy = ...)]` (chưa có fallback policy toàn cục để
  không vướng Scalar/health).
- Chưa có endpoint `/api/v1/users` (CRUD user/role/scope) — thuộc M6.
