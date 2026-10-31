# ADR-0011 — Người dùng / Vai trò / Màn hình chức năng / Phân quyền (SRS FR-8)

- Trạng thái: Accepted
- Ngày: 2026-09-14

## Bối cảnh

Từ đầu dự án, `.claude/agents/README.md` và hầu hết file agent đã mô tả sẵn "mô hình phân quyền 4
lớp (chức năng / màn hình / role / user độc lập)" nhưng chưa từng triển khai — M6 chỉ có 3 vai trò
**cố định trong code** và 6 policy ASP.NET Core **tĩnh, gắn cứng lúc biên dịch**
(`role:Administrator`, `admin-console`, …), gắn thẳng trên từng controller. Không có khái niệm
"màn hình" trong DB; menu Angular là mảng cứng trong `shell.ts`. Phiên này xây đúng lớp **màn hình +
role + user override** — lớp "chức năng" (permission theo từng action/endpoint) nằm ngoài phạm vi,
chỉ kiểm soát truy cập **cả màn hình**.

## Quyết định (đã chốt cùng người dùng trước khi code)

1. **Vai trò CRUD tuỳ ý** — admin tạo được vai trò mới. 3 vai trò gốc (Administrator/Technician/
   Teacher) đánh dấu `Role.IsSystem=true`, không đổi tên/xoá được (chặn ở handler, không phải DB
   constraint) — giữ nguyên toàn bộ hành vi nghiệp vụ gắn cứng theo role hiện có (bootstrap admin,
   `LastAdministratorGuard`, sở hữu lớp/ca học của Teacher, room-scope Technician/Teacher). Vai trò
   mới chỉ có tác dụng qua `RoleScreenPermission`, không có đặc quyền nào khác.
2. **Chặn ở cả API lẫn UI, dùng chung một nguồn dữ liệu** — ẩn menu không phải biên giới bảo mật.

## Schema

```
FunctionScreen            — Id, Code (unique), Name, Route, Icon, DisplayOrder, IsActive
RoleScreenPermission       — PK (RoleId, ScreenId), có dòng = được phép
UserScreenPermission       — PK (UserId, ScreenId), IsGranted:
                              có dòng true  → override CHO PHÉP dù role không có
                              có dòng false → override TỪ CHỐI dù role có
                              không có dòng → theo mặc định của role
Role.IsSystem (mới)
```

10 màn hình seed (migration `AddScreenPermissions`): dashboard, rooms, classes, sessions, catalog,
reports.usage, users, roles, screens, audit-logs. `RoleScreenPermission` seed **khớp chính xác** quyền
cũ để không ai mất quyền khi migrate — soát kỹ theo từng controller, không giả định: Technician/
Teacher có dashboard/rooms/classes/sessions/reports.usage (đúng `AdminConsole` cũ); **catalog KHÔNG
nằm trong đó** — `BuildingsController` vốn `Administrator`-only từ trước (chặt hơn `AdminConsole`),
khác mọi controller "màn hình" khác, nên catalog chỉ Administrator (soát lại kế hoạch ban đầu, đã
sửa trước khi tạo migration thật).

## Backend

**`IScreenAccessService`** (`Infrastructure/Identity/ScreenAccessService.cs`) — hợp
`RoleScreenPermission` của mọi role user có, áp `UserScreenPermission` đè lên; Administrator bypass
hoàn toàn (trả mọi màn hình active, không query permission). Cache `IMemoryCache` theo user, mọi lệnh
ghi (đổi role-permission/user-override/gán lại vai trò) gọi `InvalidateCache()` — clear toàn bộ
(đơn giản/an toàn, số user quản trị ít).

**Policy động — không cần sửa `AddAuthorizationBuilder()` mỗi khi thêm màn hình**:
`ScreenAuthorizationPolicyProvider : IAuthorizationPolicyProvider` bắt tên policy `"screen:<code>"`
(hoặc `"screen:code1,code2"` cho controller dùng chung — xem `RoomsController` bên dưới), tự dựng
`AuthorizationPolicy` với `ScreenPermissionRequirement`; các policy tên cũ vẫn rơi về
`DefaultAuthorizationPolicyProvider` — **các policy hành động chi tiết đang có
(`AuthPolicies.Administrator` trên Create/Update/Delete) giữ nguyên, không đụng tới**, đúng phạm vi
đã chốt (chỉ màn hình, không phải từng nút bấm).

`RoomsController` dùng `"screen:rooms,catalog"` (không phải 1 mã) — controller này thực sự phục vụ
**2 màn hình**: "Phòng máy" (xem/giám sát) và "Danh mục" (CRUD phòng trong trang quản lý danh mục) —
ép 1-1 sẽ sai, `ScreenPermissionRequirement` nhận danh sách mã, thoả 1 trong các mã là đủ.

`GET /api/v1/me/screens` (`MeController`, không gắn role-policy nào — chỉ cần đăng nhập) trả danh
sách màn hình đã resolve cho user hiện tại, dùng chung logic với `ScreenPermissionHandler` — nguồn
sự thật duy nhất cho cả menu Angular lẫn chặn API.

## Frontend

`PermissionService` (`core/auth/`) gọi `/me/screens` 1 lần (memoized qua `Promise`), `screenGuard`
(`CanActivateFn`) await nó trước khi cho qua route có `data.screenCode`. `Shell` bỏ mảng `nav` cứng,
thay bằng `computed` thẳng từ `PermissionService.screens()` — **menu chính là screen registry**: một
màn hình đăng ký + được cấp quyền là tự hiện, không cần sửa code Angular ngoài route thật đã tồn tại.

Trang mới: Vai trò (list + detail: đổi tên/xoá vai trò tuỳ chỉnh, checkbox màn hình), Màn hình chức
năng (CRUD registry), User Detail thêm panel ghi đè màn hình (radio 3 trạng thái: Theo vai trò/Luôn
cho phép/Luôn từ chối).

## Sự cố phát hiện khi build/chạy Docker (đã sửa, không liên quan trực tiếp code phân quyền)

1. **`dotnet ef migrations add` seed sai lần đầu**: giả định "catalog" ngang hàng `AdminConsole`
   như Rooms/Classes/… — sai, `BuildingsController` vốn `Administrator`-only chặt hơn. Phát hiện khi
   soát lại từng controller trước khi tạo migration thật — đã `migrations remove` + revert DB +
   sửa seed + tạo lại migration sạch (chưa commit nên an toàn revert).
2. **`dotnet watch`'s polling file watcher crash** (`ArgumentException: same key added`) khi nhiều
   file migration bị xoá/tạo dồn dập trên host qua bind mount — sập cả container `api`. Không sửa
   được triệt để (bug trong chính `dotnet watch`), chỉ cần biết để `docker compose up -d api` lại.
3. **Container `api` không khởi động lại được sau crash**: `bin`/`obj` của backend cũng bị bind-mount
   từ host — build artifact Windows-native (từ các lệnh `dotnet build`/`dotnet ef` chạy trên host
   suốt phiên) lẫn vào, container Linux đọc không nổi. Sửa giống lỗi `.angular/cache` của frontend ở
   phiên trước: mỗi project (Domain/Contracts/Application/Infrastructure/Api) có volume `bin`/`obj`
   riêng, tách khỏi bind mount nguồn — 10 volume mới trong `docker-compose.dev.yml`.
4. **DataProtection key ring không persist** — phát hiện qua log warning lúc container khởi động lại
   sạch: khoá AgentSecret (ADR-0009) sẽ mất mỗi lần container bị recreate nếu không mount volume
   riêng cho `/var/lib/labmonitoring-api/dp-keys`. Thêm `api-dp-keys` volume.

## Live-test qua trình duyệt thật (2026-09-14)

Đăng nhập `mvt` (Administrator) qua Docker stack thật: menu sidebar hiển thị đúng 10 màn hình theo
thứ tự seed; trang Vai trò hiển thị đúng số màn hình mỗi role (Admin 10, Technician/Teacher 5 mỗi
role); trang chi tiết Technician hiển thị đúng 5 checkbox được tick khớp seed, tên bị khoá sửa (vai
trò hệ thống); trang Màn hình chức năng hiển thị đủ 10 dòng với route/icon/thứ tự đúng; trang chi
tiết User (mvt) hiển thị panel ghi đè 3 trạng thái, mặc định "Theo vai trò" cho mọi màn hình (chưa
có override nào, đúng dữ liệu thật). Không sửa dữ liệu tài khoản `mvt`/`test-admin` thật trong lúc
test (chỉ xem).

## Test

33 test backend (từ 27, +6 — `ScreensAndPermissionsEndpointTests`): role CRUD, chặn sửa/xoá vai trò
hệ thống, screen CRUD, vai trò tuỳ chỉnh chỉ cấp 1 màn hình → 200 màn hình đó/403 màn hình khác, user
override GRANT thắng role thiếu quyền + REVOKE thắng role có quyền (đồng thời, 1 test), `/me/screens`
phản ánh đúng quyền đã resolve. Chạy lại toàn bộ 27 test cũ — xanh, xác nhận đổi 8 controller sang
policy `screen:*` không đổi hành vi cho Administrator (bypass) lẫn dữ liệu demo có sẵn.

## Bổ sung: submenu "Hệ thống" (2026-09-14, cùng phiên)

Theo yêu cầu tiếp theo — "người dùng, vai trò, màn hình chức năng cho vào submenu của hệ thống" —
gom 3 màn hình quản trị (Users/Roles/Screens) vào 1 nhóm submenu thay vì 3 mục ngang hàng trên
sidebar.

**Schema**: `FunctionScreen` thêm `ParentId`/`Parent`/`Children` (self-referencing FK,
`DeleteBehavior.Restrict`), `Route` đổi thành nullable — nhóm submenu (vd "Hệ thống") không có trang
riêng nên không có route. Chỉ cho lồng **2 cấp** (nhóm → màn hình con), không đệ quy sâu hơn — đủ cho
nhu cầu hiện tại, tránh phức tạp hoá UI cây đa cấp. Migration `AddScreenHierarchy`: thêm màn hình
`system` (Id=11, Route=null), reparent `users`/`roles`/`screens` dưới nó, cấp quyền `system` cho
Administrator.

**Backend**: `ScreenHandlers.cs`/`ScreensController.cs` mở rộng `ParentId` vào DTO/command;
`UpdateScreenHandler` chặn tự làm cha của chính mình; `DeleteScreenHandler` chặn xoá màn hình còn con
(`ConflictException`).

**Frontend**: `Shell` render cây 2 cấp — nhóm không route hiện nút toggle (collapse/expand), tự động
mở khi route hiện tại khớp 1 màn hình con (`isGroupExpanded`); màn hình có route hiện như link phẳng
như cũ. Thêm helper dùng chung `flattenScreensForChecklist` (`core/models/screens.models.ts`) để 3
nơi khác cũng hiển thị đúng phân cấp bằng thụt lề: bảng đăng ký Màn hình chức năng, checklist màn
hình trong Vai trò, bảng ghi đè quyền trong User Detail — tránh lặp logic gom-nhóm-theo-cha ở từng
component.

**Sự cố khi live-test**: sau khi thêm màn hình `system` với `Route=null`, gọi `GET /api/v1/me/screens`
trả 500 (`SqlNullValueException` khi EF đọc cột `Route` NULL) dù entity/migration/config đều đã đúng
(nullable từ trước, đã qua review). Nguyên nhân: `dotnet watch`'s hot reload không áp lại đầy đủ
model EF Core khi cấu hình entity đổi giữa chừng phiên chạy — cần restart container
(`docker compose ... restart api`) để build lại sạch, không phải lỗi code. Bài học: sau khi đổi
`IEntityTypeConfiguration`/shape model, luôn restart (không chỉ dựa hot reload) trước khi live-test.

**Live-test qua trình duyệt thật**: đăng nhập `mvt` (Administrator) — sidebar hiện nhóm "Hệ thống" có
chevron, click mở ra đúng 3 mục con (Người dùng/Vai trò/Màn hình chức năng), tự động mở khi vào
`/roles` (route con đang active); trang Vai trò hiện đúng 11 màn hình cho Administrator; trang chi
tiết vai trò Administrator hiện checklist đúng cây (nhóm "Hệ thống" + 3 mục con thụt lề); trang Màn
hình chức năng hiện bảng đúng thứ tự nhóm-rồi-con với cột "Nhóm cha"; trang chi tiết User (mvt) hiện
bảng ghi đè 3 trạng thái cũng đúng thụt lề theo nhóm.

**Test**: build production frontend sạch (chỉ cảnh báo bundle-size, không liên quan); 33/33 test
backend vẫn xanh (logic phân quyền/role/user-override không đổi, chỉ thêm field hiển thị phân cấp).

## Hạn chế / để sau

- Lớp "chức năng" (permission theo action/endpoint chi tiết, vd `rooms:delete` riêng khỏi
  `rooms:view`) chưa làm — ngoài phạm vi đã chốt, các policy Administrator/TeacherOrAdmin chi tiết
  hiện có giữ nguyên.
- Không có trang 403 riêng ở frontend — `screenGuard` điều hướng về `/dashboard` khi bị chặn.
- Chưa test được nhánh "user override" qua trình duyệt thật với 1 tài khoản Keycloak KHÔNG phải
  Administrator (không có tài khoản thật thứ 2 sẵn có) — đã phủ đầy đủ bằng integration test dùng
  `TestJwt` (mint token cho user tuỳ ý, không cần Keycloak thật).
