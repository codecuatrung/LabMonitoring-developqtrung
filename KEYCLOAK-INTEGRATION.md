# Đấu project mới vào Keycloak dùng chung (realm `coreproject`)

Hướng dẫn cho **một project mới** (ví dụ "Lab Monitoring / Hệ thống giám sát phòng máy")
dùng lại Keycloak + toàn bộ user sẵn có của hệ thống này, mà **không** phải migrate user,
**không** đụng tới database của CoreProject.

> Keycloak ở đây là 1 instance, 1 realm `coreproject`, hiện đang chạy trong
> `docker-compose.yml` của repo này. Chưa tách thành repo riêng — project mới chỉ cần
> địa chỉ HTTP của nó là đủ, không cần source.

---

## 1. Mô hình dùng chung

```
                    ┌──────────────────────────────────────────┐
                    │  Keycloak — realm "coreproject"          │
                    │  (user, mật khẩu, Google IdP dùng chung)  │
                    └──────────────────────────────────────────┘
                       ▲                         ▲
          OIDC login   │                         │  OIDC login
                       │                         │
   ┌───────────────────┴────────┐   ┌────────────┴───────────────────┐
   │ CoreProject (app hiện có)   │   │ Project MỚI (Lab Monitoring)    │
   │  - clients: coreproject-*   │   │  - clients: labmonitoring-*     │
   │  - DB riêng: CoreProjectDb  │   │  - DB riêng của nó              │
   │  - phân quyền riêng         │   │  - phân quyền riêng             │
   └────────────────────────────┘   └────────────────────────────────┘
```

**Điểm mấu chốt:** hai app **chỉ chia sẻ danh tính người dùng**, không chia sẻ gì khác.

- Khoá định danh chung giữa 2 app = claim **`sub`** trong access token
  (bên CoreProject nó được lưu ở cột `dbo.Users.KeycloakId`).
- Cùng một người → cùng `sub` ở cả 2 app (vì cùng realm).
- Mỗi app tự có **bảng user riêng, mô hình phân quyền riêng** trong DB riêng của nó.
- Lần đầu một người đăng nhập vào app mới → app mới đọc `sub`/`email`/`name` từ token
  rồi **tự tạo bản ghi user trong DB của chính nó** (xem mục 5).

---

## 2. Cần bao nhiêu client?

Mô hình chuẩn (giống CoreProject): **frontend SPA + API .NET riêng** → tạo **2 client**.

| Client | Type | Vai trò |
|---|---|---|
| `labmonitoring-spa` | **Public** — Standard flow ON, PKCE `S256`, Direct access grants OFF | Angular/React chạy trên browser, tự thực hiện luồng login |
| `labmonitoring-api` | **Confidential** — tắt hết Authentication flow, tắt Authorization | Chỉ là đích `aud` để backend .NET kiểm tra token; không tham gia luồng login |

> Nếu project mới là web app **có backend tự xử lý OAuth** (ASP.NET MVC/Razor, Blazor Server,
> Next.js server…): chỉ cần **1 client** confidential `labmonitoring-api` với Standard flow ON —
> xem Phụ lục B.

### Trạng thái hiện tại (cập nhật 2026-09-10)

✅ **Trên Keycloak local dev (`http://localhost:8082`) đã cấu hình xong, đúng chuẩn, dùng ngay được:**

| Client | Type | Flow | Redirect / Origin |
|---|---|---|---|
| `labmonitoring-spa` | Public | Standard flow ON, Direct access grants OFF, PKCE `S256` | redirect `http://localhost:4300/*`, post-logout `http://localhost:4300/*`, web origins `+` |
| `labmonitoring-api` | Confidential (client auth ON) | tất cả flow OFF, Authorization OFF | – (không tham gia luồng browser) |

Audience mapper `labmonitoring-api-audience` đã thêm vào `labmonitoring-spa` → access token có
`aud: labmonitoring-api`. Đã test: authorize với redirect hợp lệ trả `200`, redirect sai trả `400`.

Việc còn lại của bạn: **build project mới trỏ vào config ở mục 6**, và khi deploy thì **tạo lại
2 client này trên Keycloak production** (mục 3 + 8) — client trên local **không tự đồng bộ** sang prod.

> Client cũ `labmonitoring-api` (confidential + Authorization ON + Direct access grants ON +
> redirect `/*`, thiếu audience mapper) đã bị xoá và tạo lại cho đúng.

---

## 3. Tạo lại client trên Keycloak **production**

Local dev đã xong (mục 2). Phần này chỉ cần làm **một lần trên Keycloak production**
(`https://sso.invatech.com.vn`) khi deploy. Admin Console → chọn realm
`coreproject` (góc trên bên trái) → **Clients**. Giá trị y hệt local, chỉ đổi URL sang domain thật.

### 3.1. Client frontend — `labmonitoring-spa` (Create client)

**General settings**
| Field | Giá trị |
|---|---|
| Client type | OpenID Connect |
| Client ID | `labmonitoring-spa` |
| Name | `Lab Monitoring SPA` |

**Capability config**
| Field | Giá trị |
|---|---|
| Client authentication | **Off** (đây là public client — browser không giữ được secret) |
| Authorization | Off |
| Standard flow | **✓ On** |
| Direct access grants | **Off** |
| Implicit flow | Off |
| Service accounts roles | Off |

**Login settings / Access settings**
| Field | Dev | Production |
|---|---|---|
| Root URL | `http://localhost:4300` | `https://<domain-app-moi>` |
| Valid redirect URIs | `http://localhost:4300/*` | `https://<domain-app-moi>/*` |
| Valid post logout redirect URIs | `http://localhost:4300/*` | `https://<domain-app-moi>/*` |
| Web origins | `+` | `+` |

- Đổi `4300` thành port thật của app mới (đừng trùng `4200` của CoreProject khi chạy local song song).
- `Web origins = +` nghĩa là "lấy đúng các origin từ Valid redirect URIs" — không dùng `*`.
- **Không** dùng `/*` trơ trọi như client cũ: đó là redirect tương đối, quá rộng.

**Advanced → Proof Key for Code Exchange Code Challenge Method** = `S256`.

### 3.2. Client API — `labmonitoring-api` (Create client)

**Capability config**
| Field | Giá trị |
|---|---|
| Client authentication | **On** |
| Authorization | **Off** |
| Standard flow | **Off** |
| Direct access grants | **Off** |
| Service accounts roles | Off (chỉ bật nếu backend cần gọi Keycloak Admin API — xem mục 6.4) |

Redirect URIs / Web origins: để trống. Client này không tham gia luồng browser.
Lấy client secret ở tab **Credentials** sau khi tạo.

---

## 4. Audience mapper (bắt buộc — nếu thiếu, backend .NET luôn trả 401)

> Trên **local dev đã thêm sẵn** (mục 2). Phần này để làm lại trên **production**.

Backend .NET validate `aud` của token. Mặc định token cấp cho `labmonitoring-spa`
**không** chứa `labmonitoring-api` trong `aud` → phải thêm mapper.

Clients → **`labmonitoring-spa`** → tab **Client scopes** → click
`labmonitoring-spa-dedicated` → nút **Configure a new mapper** → **Audience**:

| Field | Giá trị |
|---|---|
| Name | `labmonitoring-api-audience` |
| Included Client Audience | `labmonitoring-api` |
| Add to ID token | Off |
| Add to access token | **On** |

(Đây đúng là cách CoreProject làm — xem mapper `coreproject-api-audience` trong
`keycloak/import/coreproject-realm.json`.)

Kiểm tra nhanh sau khi cấu hình: đăng nhập thử, decode access token trên jwt.io, xác nhận
`"aud"` có `"labmonitoring-api"` và `"azp": "labmonitoring-spa"`.

---

## 5. Provisioning user lần đầu (trong DB của project mới)

Access token sau khi login chứa: `sub`, `email`, `preferred_username`, `name`,
`given_name`, `family_name`.

Project mới **tự** dựng bảng user riêng. Luồng chuẩn (copy từ CoreProject):

- `backend/src/CoreProject.Infrastructure/Permissions/UserProvisioningService.cs`
  — tra user theo `KeycloakId` (= `sub`); nếu chưa có thì INSERT bản ghi mới.
- `backend/src/CoreProject.Api/Middleware/UserProvisioningMiddleware.cs`
  — gọi provisioning trên **mọi** request đã xác thực.
- `backend/src/CoreProject.Api/Services/CurrentUserService.cs` — đọc claim ra `KeycloakId`.

Nguyên tắc:
- **`sub` là khoá duy nhất** để nhận diện người dùng. Không join theo email (email có thể đổi).
- Không đọc / ghi bảng nào của CoreProject. App mới có schema riêng hoàn toàn.
- Phân quyền: tự thiết kế trong DB app mới (bảng role/permission riêng), khoá theo `sub`.

---

## 6. Cấu hình code

### 6.1. Frontend — Angular (giống CoreProject)

`environment.ts`:
```ts
export const environment = {
  apiBaseUrl: 'http://localhost:5200/api',   // API của app mới
  keycloak: {
    url: 'http://localhost:8082',            // prod: https://sso.invatech.com.vn
    realm: 'coreproject',
    clientId: 'labmonitoring-spa',
  },
};
```

Copy gần như nguyên các file sau từ repo này sang project mới:
- `frontend/src/app/app.config.ts` — **giữ `checkLoginIframe: false`** (xem comment trong file:
  bsc./sso. khác subdomain → iframe session-poll bị chặn 3rd-party-cookie).
- `frontend/src/app/core/guards/auth.guard.ts`
- `frontend/src/app/core/services/token-refresh.service.ts` — refresh token bằng interval
  (access token chỉ sống 300s).
- `frontend/src/app/core/interceptors/*` — bearer token, xử lý lỗi.
- `frontend/src/silent-check-sso.html` — copy vào `public/` hoặc `src/`.

### 6.2. Backend — .NET (copy từ `backend/src/CoreProject.Api/Program.cs`, dòng ~46–108)

`appsettings.json`:
```jsonc
"Keycloak": {
  // Issuer công khai — phải khớp CHÍNH XÁC "iss" trong token (không có dấu / ở cuối).
  "Authority": "http://localhost:8082/realms/coreproject",
  // Cách API container gọi tới Keycloak để lấy metadata/JWKS. Nếu API chạy cùng
  // docker network với Keycloak, dùng service name nội bộ. Ngoài Docker: để giống Authority.
  "InternalAuthority": "http://keycloak:8080/realms/coreproject",
  "Audience": "labmonitoring-api"
}
```

```csharp
var authority         = builder.Configuration["Keycloak:Authority"]!;
var internalAuthority  = builder.Configuration["Keycloak:InternalAuthority"] ?? authority;
var audience           = builder.Configuration["Keycloak:Audience"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MetadataAddress = $"{internalAuthority}/.well-known/openid-configuration";
        options.Audience = audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,          // public issuer, KHÔNG phải internalAuthority
            ValidateAudience = true,
            ValidateLifetime = true,
            NameClaimType = "preferred_username",
        };
    });
```

CORS: cho phép đúng origin của `labmonitoring-spa`:
```jsonc
"Cors": { "AllowedOrigins": [ "http://localhost:4300" ] }
```

### 6.3. Nếu chạy chung `docker-compose`

Nếu project mới có `docker-compose.yml` riêng và muốn API của nó gọi Keycloak qua tên
service nội bộ (`http://keycloak:8080`), hai compose stack phải chung một Docker network:

```yaml
# trong compose của project mới
networks:
  default:
    external: true
    name: coreproject_default   # tên network của compose stack CoreProject
```
Không làm vậy thì để `InternalAuthority` = URL công khai — vẫn chạy được
(Keycloak đã bật `--hostname-backchannel-dynamic=true`), chỉ là mỗi lần validate token
phải đi vòng ra ngoài.

### 6.4. Nếu backend app mới cần gọi Keycloak Admin API

(ví dụ: tự tạo user, reset mật khẩu từ trong app). Tạo thêm 1 client service-account
riêng — **đừng** dùng chung `coreproject-admin-cli`:

- Client ID `labmonitoring-admin-cli`, Client authentication On, Service accounts roles On,
  các flow khác Off.
- Tab **Service accounts roles** → Assign role → chọn từ `realm-management` đúng những
  role cần (`view-users`, `manage-users`, …). Cấp tối thiểu.
- Backend lấy token bằng `grant_type=client_credentials` với client id + secret này.

---

## 7. Google login ("Đăng nhập Email HUPH")

**Không cần cấu hình gì thêm** cho project mới:
- Google IdP nằm ở tầng realm, dùng chung cho mọi client trong realm.
- Redirect URI phía Google Cloud Console vẫn là
  `https://sso.invatech.com.vn/realms/coreproject/broker/google/endpoint` — **không đổi**.
- Nút "Đăng nhập với Google" xuất hiện tự động trên trang login của Keycloak cho
  `labmonitoring-spa`.

Muốn nút Google luôn hiện màn hình chọn tài khoản (không tự dùng tài khoản Gmail đang
đăng nhập sẵn trên browser): đó là setting `prompt=select_account` ở tầng IdP, đã xử lý
riêng cho realm — không phải việc của project mới.

---

## 8. Checklist khi deploy production

- [ ] Tạo lại `labmonitoring-spa` + `labmonitoring-api` trên `https://sso.invatech.com.vn`
      (Admin Console), redirect URIs trỏ domain thật.
- [ ] Thêm lại audience mapper trên `labmonitoring-spa` production (mục 4).
- [ ] Lấy **client secret production** của `labmonitoring-api` ở tab **Credentials**
      (khác secret dev) → đưa vào `.env`/secret store của app mới, **không commit**.
- [ ] Frontend build với `keycloak.url = https://sso.invatech.com.vn`,
      `clientId = labmonitoring-spa` (Angular bake vào bundle lúc build — đổi phải rebuild).
- [ ] Backend `.env`: `Keycloak__Authority`, `Keycloak__Audience`, `Cors__AllowedOrigins`.
- [ ] Sau khi reverse proxy (NPM) route xong subdomain mới: test login end-to-end bằng
      **1 tài khoản `@huph.edu.vn` thật** (cả login mật khẩu lẫn Google).

---

## 9. Thông tin tham chiếu

| | Dev (local) | Production |
|---|---|---|
| Keycloak base URL | `http://localhost:8082` | `https://sso.invatech.com.vn` |
| Realm | `coreproject` | `coreproject` |
| Issuer (`iss` trong token) | `http://localhost:8082/realms/coreproject` | `https://sso.invatech.com.vn/realms/coreproject` |
| Discovery document | `<issuer>/.well-known/openid-configuration` | |
| JWKS (verify chữ ký) | `<issuer>/protocol/openid-connect/certs` | |
| Authorization endpoint | `<issuer>/protocol/openid-connect/auth` | |
| Token endpoint | `<issuer>/protocol/openid-connect/token` | |
| UserInfo endpoint | `<issuer>/protocol/openid-connect/userinfo` | |
| Logout (end session) | `<issuer>/protocol/openid-connect/logout` | |
| Frontend clientId | `labmonitoring-spa` | `labmonitoring-spa` |
| API audience (`aud`) | `labmonitoring-api` | `labmonitoring-api` |
| Thuật toán ký token | RS256 | |
| Access token lifespan | 300 giây | |
| Claim có sẵn | `sub`, `email`, `preferred_username`, `name`, `given_name`, `family_name` | |

---

## 10. Lỗi thường gặp (đúc kết từ khi deploy CoreProject)

| Triệu chứng | Nguyên nhân thật | Cách xử lý |
|---|---|---|
| Backend luôn 401 dù token hợp lệ | Token không có `aud: labmonitoring-api` | Thêm audience mapper (mục 4) |
| Backend 401, log `IDX10205: Issuer validation failed` | `Authority` không khớp `iss` (thừa/thiếu `/`, http vs https, còn là `localhost`) | Set `ValidIssuer` = đúng chuỗi trong token |
| Login xong quay lại `redirect_uri` báo lỗi "Invalid parameter: redirect_uri" | Redirect URI trên client không khớp URL app | Thêm đúng `https://<domain>/*` vào client |
| Browser báo lỗi CORS mơ hồ / "Failed to fetch" khi gọi API | API thực chất đang **502/500** — response lỗi không kèm header CORS nên browser che mất status thật | `curl` thẳng vào API endpoint để xem status thật; sửa API trước |
| Trang login reload liên tục, không vào được app | keycloak-js `checkLoginIframe` fail do 3rd-party-cookie giữa các subdomain | Đặt `checkLoginIframe: false` trong `initOptions` (đã có sẵn ở `app.config.ts`) |
| Sau khi submit form login Keycloak → 502 | Header/cookie của Keycloak vượt buffer mặc định của nginx | Tăng `proxy_buffer_size` / `large_client_header_buffers` ở reverse proxy |
| Google báo "redirect_uri_mismatch" | Client ID/Secret của Google IdP trong Keycloak không khớp cái đã khai redirect URI ở Google Console | Sửa ở tầng realm IdP — không phải việc của project mới |

---

## Phụ lục A — Test client bằng curl (không cần browser)

Kiểm tra một client + redirect URI đã đúng chưa, không cần đăng nhập thật:

```bash
curl -sI "http://localhost:8082/realms/coreproject/protocol/openid-connect/auth\
?client_id=labmonitoring-spa\
&response_type=code\
&scope=openid\
&redirect_uri=http://localhost:4300/\
&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM\
&code_challenge_method=S256"
```
- `200` + trả về HTML trang login → client + redirect URI hợp lệ.
- `Client not found` → sai `client_id`.
- `Invalid parameter: redirect_uri` → redirect URI chưa được khai trên client.

Lấy discovery document (public, không cần auth):
```bash
curl -s http://localhost:8082/realms/coreproject/.well-known/openid-configuration | jq .
```

---

## Phụ lục B — Nếu project mới là web app có backend tự xử lý OAuth

(ASP.NET MVC/Razor Pages, Blazor Server, Next.js server, …) — không phải SPA.

Chỉ cần **1 client** `labmonitoring-api`:
- Client authentication **On** (confidential)
- Standard flow **On**
- Authorization **Off**, Direct access grants **Off**
- Valid redirect URIs = `https://<domain>/signin-oidc` (ASP.NET) hoặc callback tương ứng
- Valid post logout redirect URIs = `https://<domain>/signout-callback-oidc`
- Client secret: lấy ở tab **Credentials**

Backend dùng `AddOpenIdConnect` (không phải `AddJwtBearer`):
```csharp
builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(o =>
{
    o.Authority = "https://sso.invatech.com.vn/realms/coreproject";
    o.ClientId = "labmonitoring-api";
    o.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
    o.ResponseType = "code";
    o.UsePkce = true;
    o.Scope.Add("openid");
    o.Scope.Add("email");
    o.GetClaimsFromUserInfoEndpoint = true;
    o.TokenValidationParameters.NameClaimType = "preferred_username";
});
```
Không cần audience mapper trong trường hợp này (ID token của chính client đó).
