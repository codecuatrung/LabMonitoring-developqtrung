# Bộ Claude Code Subagent — .NET Core + EF + JWT/Keycloak + Angular

Bộ 10 subagent được tuỳ chỉnh từ [msitarzewski/agency-agents](https://github.com/msitarzewski/agency-agents),
bổ sung riêng cho stack: .NET Core Web API, Entity Framework Core, JWT + Keycloak (xác thực tập trung),
Angular, và mô hình phân quyền 4 lớp (chức năng / màn hình / role / user độc lập).

## Cài đặt cho project cụ thể (không áp dụng toàn máy)

```bash
# Đứng ở thư mục gốc project
mkdir -p .claude/agents
cp claude-agents-dotnet/*.md .claude/agents/
```

Commit thư mục `.claude/agents/` vào git để cả team dùng chung chuẩn.

## Danh sách agent

| File | Vai trò | Khi dùng |
|---|---|---|
| `software-architect.md` | Kiến trúc tổng thể, ADR, trade-off | Trước khi bắt đầu / khi cần quyết định lớn |
| `backend-architect.md` | API .NET, EF Core schema, JWT/Keycloak flow | Thiết kế backend, database, auth |
| `database-optimizer.md` | Tối ưu EF Core query/index | Khi query chậm, thiết kế bảng permission |
| `frontend-developer.md` | Angular UI, route guard, permission directive | Xây dựng giao diện, xử lý phân quyền UI |
| `security-architect.md` | Threat model, trust boundary Keycloak/JWT | Review kiến trúc bảo mật tổng thể |
| `security-appsec-engineer.md` | Review code bảo mật (SAST-style) | Review PR liên quan auth/authorization |
| `security-penetration-tester.md` | Pentest luồng auth/authorization | Trước khi go-live |
| `testing-reality-checker.md` | Chứng nhận production-ready dựa trên bằng chứng | Trước khi release |
| `testing-api-tester.md` | Test từng endpoint, ma trận phân quyền | Trong lúc phát triển + CI |
| `testing-test-automation-engineer.md` | E2E test Angular theo từng persona/role | Kiểm thử UI tự động |

## Đã tuỳ chỉnh gì so với bản gốc

1. **`tools` + `model`**: mỗi agent được giới hạn đúng bộ công cụ cần thiết (không kế thừa toàn bộ tool mặc định),
   và chọn model phù hợp (`opus` cho agent cần suy luận kiến trúc sâu, `sonnet` cho phần còn lại).
2. **Mục "🧩 Bối cảnh dự án"**: chèn ngay sau phần Identity — mô tả cụ thể .NET Core/EF Core/JWT/Keycloak/Angular
   và đặc biệt mô hình phân quyền 4 lớp (chức năng, màn hình, role, user override độc lập) để agent luôn tư duy
   đúng trong bối cảnh dự án của bạn thay vì đưa ví dụ generic (React, Node...).
3. **Mục "🌐 Ngôn ngữ giao tiếp"**: agent luôn trả lời bằng tiếng Việt, giữ nguyên thuật ngữ kỹ thuật/code bằng tiếng Anh.

## Lưu ý

- Đây là các agent tuỳ chỉnh dựa trên nguồn cộng đồng (MIT license), không phải sản phẩm chính thức của Anthropic —
  nên review lại nội dung định kỳ, đặc biệt các agent liên quan bảo mật/phân quyền trước khi tin dùng cho việc quan trọng.
- Nếu Claude Code là lần đầu tạo thư mục `.claude/agents/` cho project này, cần khởi động lại phiên một lần để agent được nhận diện.
