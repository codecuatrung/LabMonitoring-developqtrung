# ADR-0012 — Bảng dữ liệu dùng chung template CoreProject/BSC + phân trang

- Trạng thái: Accepted
- Ngày: 2026-09-14

## Bối cảnh

Người dùng yêu cầu: "các table sử dụng template tại link https://bsc.invatech.com.vn/bsc/huphs, và
cho phép phân trang" — trang đó (BSC HUPH, cùng nền tảng CoreProject dùng chung Keycloak realm
`coreproject` với LabMonitoring) có bảng dữ liệu với header nền xanh nhạt + toolbar phía trên +
chân trang phân trang dạng "Hiển thị [20 ▾] bản ghi/trang" cùng dãy số trang có mũi tên trước/sau.
Trước đó mỗi trang danh sách trong LabMonitoring tự vẽ bảng riêng (không đồng nhất), phần lớn không
phân trang (trả hết toàn bộ bản ghi mỗi lần tải), và Audit Logs là nơi duy nhất có phân trang thật
(chỉ có nút trước/sau, không có ô chọn kích thước trang).

**Quyết định đã chốt cùng người dùng trước khi code:** áp dụng cho **toàn bộ các trang danh sách**
trong app, xử lý phân trang **phía server** (chuẩn hơn, giống hệt cách Audit Logs đã làm).

## Thiết kế

### Backend — `PagedResult<T>` đồng nhất

`Application/Common/Models/PagedResult.cs` đã có sẵn từ M6 (Audit Logs). Áp dụng thêm cho 5 query
list vốn trả `IReadOnlyList<T>` thẳng: `GetBuildingsQuery`, `GetRoomsQuery`, `GetClassesQuery`,
`GetSessionsQuery`, `GetUsersQuery` — mỗi query thêm `Page = 1, PageSize = 20`, handler
`Skip/Take` sau khi `CountAsync`, `PageSize` luôn clamp `[1, 500]`. Controller tương ứng nhận thêm
`[FromQuery] int page = 1, [FromQuery] int pageSize = 20`.

**Roles và Screens KHÔNG phân trang ở API** — đây là ngoại lệ có chủ đích, không phải bỏ sót. Hai
danh mục này nhỏ (do quản trị viên tự tạo, hiện ~3-11 dòng), và được **dùng lại nguyên vẹn** ở nơi
khác: checklist phân quyền màn hình trong Role Detail và User Detail (xem ADR-0011) cần **toàn bộ**
danh sách để dựng checkbox, không phải 1 trang. Nếu bắt `GetRolesQuery`/`GetScreensQuery` trả
`PagedResult` với `PageSize` mặc định nhỏ, hai checklist đó sẽ vỡ (thiếu dòng) — cắt theo trang phía
server còn phá vỡ liên kết cha-con của `Screens` (một "Hệ thống" có thể rơi khác trang với các màn
hình con của nó). Quyết định: 2 endpoint này giữ nguyên trả mảng đầy đủ; phân trang cho *trang danh
sách* Roles/Screens được làm **phía client** (cắt mảng đã tải sẵn) — vẫn cùng giao diện phân trang,
chỉ khác chỗ cắt dữ liệu.

### Frontend

**`core/models/paging.models.ts`** (mới) — định nghĩa `PagedResult<T>` dùng chung (trước đó định
nghĩa trùng lặp bên trong `audit-logs.models.ts`, đã dọn về đây).

**`features/shared/pagination/`** (mới) — `<app-pagination>`, component dùng chung mọi nơi: ô chọn
kích thước trang (10/20/50/100, mặc định 20 khớp mặc định của template BSC) + dãy nút số trang có
dấu `…` khi nhiều trang (giữ trang đầu/cuối + 2 trang quanh trang hiện tại) + nút trước/sau. Input
`page`/`pageSize`/`totalCount`, output `pageChange`/`pageSizeChange` — component cha tự quyết định
gọi lại API (server-side) hay chỉ cập nhật state cắt mảng (client-side).

**`styles.scss`** — thêm style `.table > thead > tr > th` nền xanh nhạt (`#c6efce`) đồng nhất **toàn
bộ** bảng trong app (kể cả những bảng không phân trang như Báo cáo sử dụng) mà không cần sửa từng
template — chỉ cần trang đó đã dùng class Bootstrap `.table` sẵn có.

**Server-side** (Catalog×2 bảng, Classes, Sessions, Users, Audit Logs restyle): mỗi service tương
ứng (`FacilitiesService`, `TeachingService`, `SessionsService`, `UsersService`) đổi chữ ký `list()`
sang nhận `{page?, pageSize?, ...filter}` và trả `Observable<PagedResult<T>>`; component gọi lại API
mỗi khi đổi trang/kích thước trang.

**Client-side** (Roles, Screens): `page`/`pageSize` là `signal` (bắt buộc — dùng field thường thì
`computed()` không re-run vì không đọc tín hiệu, đã tự bắt lỗi này khi live-test: đổi kích thước
trang trên Screens list hiện đúng 2 trang nhưng bảng vẫn hiện đủ 11 dòng, sửa bằng cách chuyển
`page`/`pageSize` thành `signal` thay vì field thường).

**Dropdown/checklist vẫn cần "tất cả"**: mọi nơi dùng `rooms()`/`classes()`/`users()` như nguồn dữ
liệu chọn (không phải bảng duyệt) — dropdown tòa nhà trong form tạo phòng (Catalog), dropdown phòng/
lớp trong Báo cáo sử dụng, dropdown giảng viên khi tạo lớp, checklist phạm vi phòng trong User
Detail, lưới giám sát phòng máy (`/rooms`) — đều gọi lại API với `pageSize: 500` rồi lấy `.items`,
thay vì dùng trang mặc định 20 (sẽ cắt mất dữ liệu tham chiếu).

**Catalog**: 2 danh sách (Tòa nhà, Phòng máy) trước đây là `<ul class="list-group">`, đổi sang
`<table>` để đồng nhất giao diện + nhận style header xanh + có chỗ gắn `<app-pagination>` — mỗi bảng
có state trang riêng.

## Sự cố phát hiện khi live-test (đã sửa)

**`GetMyScreensQuery`/pagination không liên quan trực tiếp phiên này**, nhưng khi restart container
`api` (theo bài học từ ADR-0011 — luôn restart sau khi đổi handler/controller, không tin hot reload)
phát hiện lại lỗi quen thuộc `dotnet watch ❌ Failed to read staticwebassets.development.json` (bin/
obj lẫn build Windows-native) — không chặn khởi động (đã có volume riêng từ trước), chỉ log cảnh báo.

**Bug thật phát hiện khi test Screens list đổi kích thước trang**: đổi "Hiển thị 20" → "10" hiện đúng
"1 2" ở chân trang nhưng bảng vẫn hiện đủ 11 dòng thay vì cắt còn 10 — do `pagedScreens`/`roles` là
`computed()` nhưng đọc `this.page`/`this.pageSize` là field thường (không phải signal), nên Angular
không biết phải tính lại khi 2 field đó đổi giá trị qua phép gán thường. Sửa bằng cách chuyển cả hai
thành `signal(...)`, gọi `.set()` thay vì gán trực tiếp. Đã live-test lại: đổi kích thước trang cắt
đúng số dòng, chuyển trang 2 hiện đúng dòng còn lại.

## Live-test qua trình duyệt thật (2026-09-14)

Đăng nhập `mvt` (Administrator) qua Docker stack thật: Catalog (2 bảng Tòa nhà/Phòng máy) hiện đúng
style xanh + chân trang phân trang; Lớp học, Ca thực hành, Người dùng hiện đúng dữ liệu + phân trang
qua API thật; Vai trò, Màn hình chức năng phân trang phía client đúng (test đổi kích thước trang +
chuyển trang trên Màn hình chức năng, xác nhận bug trên đã được sửa); Nhật ký hệ thống (Audit Logs)
hiện 288 bản ghi / 15 trang với dấu `…` đúng như mẫu BSC; trang chi tiết User (mvt) vẫn hiện đủ
checklist vai trò/màn hình/phòng (không bị cắt do phân trang phía dropdown).

## Test

Build backend sạch, 33/33 test xanh (chỉ 1 test integration cần sửa để đọc `PagedResult<RoomDto>`
thay vì `List<RoomDto>` — `FacilitiesEndpointTests.cs`). Build frontend production sạch (chỉ cảnh
báo bundle-size sẵn có, không liên quan).

## Hạn chế / để sau

- Roles/Screens phân trang phía client — nếu 2 danh mục này lớn hơn nhiều trong tương lai (không dự
  kiến, vốn là catalog do admin tự quản), sẽ cần thiết kế lại (vd: endpoint "all" riêng cho
  checklist, tách khỏi endpoint phân trang cho bảng).
- Chưa có ô tìm kiếm/lọc theo tên trên hầu hết các trang danh sách mới phân trang (Buildings/Rooms/
  Classes/Sessions/Users) — chỉ Audit Logs có ô lọc theo hành động từ trước. Ngoài phạm vi yêu cầu
  lần này (chỉ "dùng template + phân trang").
