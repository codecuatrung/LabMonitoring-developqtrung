# Hướng dẫn triển khai production

Mô hình: **máy ứng dụng `192.168.1.28`** chạy Docker Compose; **SQL Server nằm trên máy riêng `192.168.1.168`**
(cài trực tiếp, không dùng Docker).

| Thành phần | Chạy ở đâu | Cổng |
|---|---|---|
| `web` (nginx): giao diện Angular + proxy `/api`, `/hubs` | Docker @ 192.168.1.28 | **4300** (HTTPS) |
| `api` (.NET 10): backend + SignalR, agent kết nối trực tiếp | Docker @ 192.168.1.28 | **5200** (HTTP) |
| `redis`: backplane SignalR | Docker @ 192.168.1.28 | nội bộ |
| `seq`: xem log (tuỳ chọn) | Docker @ 192.168.1.28 | chỉ `127.0.0.1:5341` |
| `migrate`: chạy một lần, áp dụng migration lên database có sẵn rồi thoát | Docker @ 192.168.1.28 | — |
| **SQL Server** | **192.168.1.168** (không Docker) | **1433** |

> **Vì sao bắt buộc HTTPS cho web admin?** Trình duyệt chỉ cho `crypto.subtle` (đăng nhập SSO dùng PKCE)
> và `getDisplayMedia` (trình chiếu màn hình) trên origin an toàn. `http://192.168.1.28` **không** đăng nhập
> được; phải là `https://192.168.1.28:4300`. Agent dùng HTTP thường qua cổng 5200 (mạng LAN nội bộ).

## 1. Chuẩn bị máy ứng dụng (192.168.1.28)

Linux (khuyến nghị Ubuntu 22.04/24.04), ≥ 2 GB RAM, 10 GB ổ đĩa.

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker
docker compose version

# Firewall (nếu bật ufw)
sudo ufw allow 4300/tcp && sudo ufw allow 5200/tcp
```

Máy cần ra internet (cổng 443) để API gọi `https://sso2.huph.edu.vn` (xác thực token) và kéo image lần đầu.
Nên đặt IP tĩnh/DHCP reservation cho `192.168.1.28`.

## 2. Kết nối tới SQL Server có sẵn (192.168.1.168)

Database đã có sẵn nên chỉ cần bảo đảm máy `192.168.1.28` kết nối được và tài khoản đủ quyền. Kiểm tra lần lượt:

**a) Mạng và giao thức** — từ máy ứng dụng:

```bash
nc -zv 192.168.1.168 1433          # phải báo "succeeded"
```

Nếu không thông: trên 192.168.1.168 bật TCP/IP + cổng tĩnh 1433 (SQL Server Configuration Manager → restart dịch vụ)
và mở firewall cho 192.168.1.28:

```powershell
New-NetFirewallRule -DisplayName "SQL Server 1433 (LabMonitoring app)" -Direction Inbound `
  -Protocol TCP -LocalPort 1433 -RemoteAddress 192.168.1.28 -Action Allow
```

Nếu SQL Server dùng named instance/cổng động thì đặt `DB_HOST=192.168.1.168` và `DB_PORT=<cổng thực tế>`.

**b) Tài khoản** — dùng SQL login (Windows Authentication không dùng được từ container Linux). Cần chế độ
Mixed Mode và login có quyền **`db_owner`** trên database (migration cần tạo/sửa bảng khi có phiên bản mới):

```sql
-- chỉ chạy nếu chưa có login riêng; nếu dùng login sẵn có thì bỏ qua 2 dòng đầu
CREATE LOGIN labmonitoring WITH PASSWORD = N'<mat-khau-manh>', CHECK_POLICY = ON;
USE <TenDatabase>;  CREATE USER labmonitoring FOR LOGIN labmonitoring;
ALTER ROLE db_owner ADD MEMBER labmonitoring;
```

**c) Thử đăng nhập bằng đúng thông tin sẽ điền vào `.env`** (từ máy ứng dụng):

```bash
docker run --rm mcr.microsoft.com/mssql-tools18 /opt/mssql-tools18/bin/sqlcmd -C \
  -S 192.168.1.168,1433 -U <DB_USER> -P '<DB_PASSWORD>' -d <DB_NAME> -Q "SELECT DB_NAME(), COUNT(*) FROM sys.tables"
```

(Nếu image `mssql-tools18` không kéo được, bỏ qua bước này; bước `migrate` ở mục 6 cũng cho biết kết nối có được không.)

**d) Tình trạng schema của database có sẵn** — `migrate` (chạy mỗi lần `up`) áp dụng các migration còn thiếu:
- Database **rỗng** hoặc đã tạo bằng chính các migration của dự án (có bảng `__EFMigrationsHistory`): chạy bình thường.
- Database đã có bảng **nhưng không có** `__EFMigrationsHistory` (ví dụ tạo bằng script tay): migration sẽ báo lỗi
  "object already exists". Khi đó báo lại để xử lý (đánh dấu baseline), đừng xoá dữ liệu.
- Database restore từ máy dev: hợp lệ, chỉ cập nhật phần thiếu. Lưu ý `KeycloakUserId` của user phải khớp `sub`
  hiện tại trên SSO, và các agent đăng ký từ dev phải đăng ký lại (secret mã hoá bằng key Data Protection của dev).

## 3. Lấy mã nguồn và cấu hình

```bash
git clone https://github.com/mvthuy278/LabMonitoring.git
cd LabMonitoring        # thư mục chứa deploy/, backend/, frontend/

cp deploy/.env.prod.example deploy/.env
nano deploy/.env
```

Trong `deploy/.env` (không commit): `SERVER_HOST=192.168.1.28`, `DB_HOST=192.168.1.168`, `DB_USER=labmonitoring`,
`DB_PASSWORD=<mật khẩu ở bước 2b>`, `DB_NAME=<tên database có sẵn>`, `BOOTSTRAP_ADMIN=mvt` (username SSO sẽ thành Administrator ở lần đăng nhập đầu
của database mới). Kết nối tới SQL Server dùng `Encrypt=True;TrustServerCertificate=True` nên không cần cài chứng chỉ
cho SQL Server.

## 4. Tạo chứng chỉ HTTPS (tự ký)

```bash
chmod +x deploy/gen-cert.sh
./deploy/gen-cert.sh            # tạo deploy/certs/server.crt + server.key cho 192.168.1.28
```

Trên **mỗi máy admin** mở web: nhập `server.crt` vào *Trusted Root Certification Authorities*
(Windows: chuột phải file → Install Certificate → Local Machine → Trusted Root) để trình duyệt không cảnh báo.
Có tên miền nội bộ + chứng chỉ do CA cấp thì chỉ cần thay 2 file `server.crt`/`server.key`.

## 5. Khai báo trên SSO (một lần)

Trong Keycloak realm `coreproject` (`https://sso2.huph.edu.vn`), client **`labmonitoring-spa`** → thêm:

- **Valid redirect URIs**: `https://192.168.1.28:4300/*`
- **Valid post logout redirect URIs**: `https://192.168.1.28:4300/*`
- **Web origins**: `https://192.168.1.28:4300`

(Client `labmonitoring-spa`/`labmonitoring-api`, scope `basic` và audience mapper đã thiết lập khi đấu SSO; file mẫu ở
`deploy/keycloak/`.)

## 6. Khởi động

```bash
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env ps
```

Lần đầu build mất vài phút. Thứ tự tự động: `migrate` (áp dụng migration lên database có sẵn trên 192.168.1.168) → `api`
(healthy) → `web`. Kiểm tra:

```bash
docker compose -f deploy/docker-compose.prod.yml logs migrate       # "Applying 6 migration(s)..."
curl -fsS http://192.168.1.28:5200/health/ready                      # sql-server + redis: Healthy
curl -kI https://192.168.1.28:4300/                                   # HTTP/2 200
```

Mở `https://192.168.1.28:4300/` → đăng nhập SSO bằng tài khoản `BOOTSTRAP_ADMIN` → tự có quyền Administrator.
Người dùng khác: đăng nhập SSO lần đầu rồi gán vai trò trong **Hệ thống → Người dùng**.

> Nếu database trên 192.168.1.168 đã có dữ liệu từ trước (ví dụ restore từ bản dev), migration chỉ cập nhật thêm schema.
> Lưu ý `KeycloakUserId` của user phải khớp `sub` hiện tại trên SSO.
>
> **Lỗi `The key {…} was not found in the key ring`**: secret của các agent đã đăng ký ở dev được mã hoá bằng key
> Data Protection của máy dev. Muốn giữ agent cũ, chép key ring dev sang production (mục 6b); nếu không thì đăng ký
> lại từng agent.

### 6b. Chép key Data Protection từ máy dev (chỉ khi dùng database có agent đăng ký từ dev)

Trên **máy dev** (PowerShell, Docker Desktop đang chạy):

```powershell
docker volume ls --format '{{.Name}}' | Select-String dp-keys      # thường là deploy_api-dp-keys
docker run --rm -v deploy_api-dp-keys:/k -v "${PWD}:/b" alpine tar czf /b/dp-keys-dev.tgz -C /k .
scp dp-keys-dev.tgz <user>@192.168.1.28:~/
```

Trên **máy chủ 192.168.1.28** (key là dữ liệu nhạy cảm — xoá file `.tgz` sau khi xong):

```bash
docker run --rm -v labmonitoring_api-dp-keys:/k -v "$HOME":/b alpine \
  sh -c "tar xzf /b/dp-keys-dev.tgz -C /k && chown -R 1654:1654 /k"
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env restart api
shred -u ~/dp-keys-dev.tgz
```

Key ring của production được giữ nguyên và hợp nhất với key dev (key mới nhất dùng để mã hoá mới, key cũ vẫn giải mã được).
Ứng dụng dùng `SetApplicationName("LabMonitoring")` cố định nên key dev dùng được ở production.

## 7. Cài Agent lên máy phòng thực hành

1. Trên máy dev: `agent/build/publish.ps1` → thư mục `agent/artifacts/agent` (hoặc `agent-full-<phiên bản>.zip`).
2. Copy sang máy trạm, sửa `install-config.txt`:
   ```
   ServerBaseUrl=http://192.168.1.28:5200
   RoomCode=LAB-01
   ```
3. Chạy `install-service.ps1` bằng PowerShell **Administrator**.
4. Trên web: duyệt máy mới (Phòng máy → Máy chờ duyệt).

Các bản sau có thể cập nhật từ xa: **Hệ thống → Phiên bản Agent** upload zip → nút "Cập nhật Agent".

## 8. Vận hành

```bash
# Cập nhật phiên bản mới của hệ thống (migrate tự chạy lại)
git pull
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d --build

docker compose -f deploy/docker-compose.prod.yml logs -f --tail=200 api
docker compose -f deploy/docker-compose.prod.yml restart api
docker compose -f deploy/docker-compose.prod.yml down          # giữ dữ liệu (volume)
```

Log tập trung: `http://127.0.0.1:5341` trên máy 192.168.1.28 (Seq); xem từ máy khác qua SSH tunnel
`ssh -L 5341:127.0.0.1:5341 user@192.168.1.28`.

### Sao lưu (quan trọng)

**Database (trên 192.168.1.168)** — dùng cơ chế sao lưu của SQL Server (Maintenance Plan / SQL Agent job), ví dụ:

```sql
BACKUP DATABASE LabMonitoring TO DISK = N'D:\Backup\LabMonitoring.bak' WITH INIT, COMPRESSION, CHECKSUM;
```

**Trên 192.168.1.28** — key mã hoá secret của agent và gói agent đã upload (mất key = mọi agent phải đăng ký lại):

```bash
mkdir -p ~/backup && cd ~/backup
docker run --rm -v labmonitoring_api-dp-keys:/k -v "$PWD":/b alpine tar czf /b/dp-keys.tgz -C /k .
docker run --rm -v labmonitoring_api-agent-packages:/p -v "$PWD":/b alpine tar czf /b/agent-packages.tgz -C /p .
```

Nên đặt vào cron hằng đêm và copy ra ổ khác/NAS.

### Truyền file (Gửi/Thu file)

- Dữ liệu lưu trên volume `labmonitoring_api-file-transfers` (file đã gửi + zip thu về); job dọn dẹp xoá bản ghi và file
  cũ hơn `FileTransfer:RetentionDays` (mặc định 14 ngày) và cảnh báo trong log khi ổ đĩa còn dưới 5 GB.
- Cấu hình (biến môi trường của service `api`, mặc định trong ngoặc): `FileTransfer__MaxPushBytes` (500 MB),
  `FileTransfer__MaxCollectBytes` (200 MB), `FileTransfer__MaxConcurrentDownloads` (8 máy tải cùng lúc, máy khác chờ),
  `FileTransfer__RetentionDays` (14). nginx cho phép upload tới 600 MB (`client_max_body_size`).
- Trước khi dùng: vào **Truyền file → Thư mục cho phép** (Administrator) thêm thư mục đích/nguồn trên máy khách, ví dụ
  `D:\BaiTap` (gửi) và `D:\BaiNop` (thu). Cần agent ≥ 1.0.8 trên các máy.
- Sao lưu: volume này chỉ chứa dữ liệu tạm thời/trung gian nên không bắt buộc, nhưng zip bài nộp đã thu về nên tải xuống
  lưu riêng nếu cần giữ lâu hơn thời hạn lưu.

## 9. Xử lý sự cố nhanh

| Triệu chứng | Kiểm tra |
|---|---|
| `migrate` lỗi "A network-related or instance-specific error" | Không tới được `192.168.1.168:1433`: TCP/IP chưa bật, firewall chưa mở cho 192.168.1.28, hoặc SQL đang dùng cổng động |
| `migrate` lỗi "Login failed for user 'labmonitoring'" | Sai `DB_PASSWORD`, chưa bật Mixed mode, hoặc login chưa được tạo user trong database (mục 2b) |
| `migrate` lỗi "permission denied" / "CREATE TABLE permission denied" | Login thiếu `db_owner` trên database — cấp theo mục 2b |
| `/health/ready` báo `sql-server` Unhealthy | Cùng nhóm nguyên nhân kết nối SQL ở trên; xem `docker compose logs api` |
| Web đăng nhập xong báo lỗi / vòng lặp | Redirect URI `https://192.168.1.28:4300/*` đã có trên client `labmonitoring-spa`? Có mở bằng **https**? |
| Đăng nhập được nhưng 403 mọi trang | Token thiếu `sub` (scope `basic`) hoặc user chưa có vai trò → gán trong Người dùng |
| 401 mọi request | API không gọi được SSO (`docker compose logs api`), hoặc token thiếu audience `labmonitoring-api` |
| Agent không kết nối | `curl http://192.168.1.28:5200/health/live` từ máy trạm; firewall cổng 5200 |
| Trình chiếu/khoá máy không hoạt động | Cần agent ≥ 1.0.7 và mở web bằng HTTPS (`getDisplayMedia`) |
| Trình duyệt cảnh báo chứng chỉ | Import `deploy/certs/server.crt` vào Trusted Root của máy đó |
