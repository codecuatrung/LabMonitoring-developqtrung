Hãy đọc file Dac_ta_ky_thuat_SRS_ERD_API_LabMonitoring.docx để hiểu phân tích thiết kế của hệ thống, có một số điểm lưu ý:
- Đọc thêm PLAN.md (kế hoạch triển khai theo module) và KEYCLOAK-INTEGRATION.md (đấu nối Keycloak dùng chung).
- Dùng .claude/agents để xây dựng hệ thống.
- Cơ sở dữ liệu dùng SQL Server, server `MVT`, tài khoản `sa` (mật khẩu lưu trong `.env`/User Secrets cục bộ, không commit) để xây dựng cơ sở dữ liệu.
- Agent được xây dựng dạng service chạy được trên máy client sử dụng Windows 10 hoặc cao hơn, ngoài ra cần xây dựng để chạy trên Linux (việc này tôi sẽ yêu cầu thực hiện sau).
- Backend sử dụng .NET 10 (ASP.NET Core), kiến trúc chuẩn (Clean Architecture), dùng Keycloak/SSO đã có (realm `coreproject`, `https://sso2.huph.edu.vn`) để xác thực người dùng.
- Frontend sử dụng Angular + AdminLTE 4 + Bootstrap.
- Quy ước cổng dev: Angular `4300`, API `5200` (Keycloak nay là SSO ngoài `sso2.huph.edu.vn`).
