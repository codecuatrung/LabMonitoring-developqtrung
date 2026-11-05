# LabMonitoring Demo

Demo quản lý lịch giảng dạy, ca trực, danh mục, báo cáo và quyền động bằng React, TypeScript, Ant Design và SQLite (`sql.js`).

## Chạy local

```sh
npm install
npm run dev
```

Vite phục vụ giao diện và mount API nội bộ `/api/state`; SQLite chạy trong Node.js, trong RAM của tiến trình API. Không cần cấu hình database, API key hay biến môi trường.

## Deploy Vercel

Đưa repository lên Git rồi import trên Vercel. Vercel nhận diện Vite và các function trong `api/`; dùng `npm run build`, output `dist`. Không cần dịch vụ hoặc database bên ngoài.

## Giới hạn database RAM

Database không ghi ra đĩa. Local sẽ mất dữ liệu khi tiến trình dev dừng. Trên Vercel, mỗi serverless instance có bộ nhớ riêng và có thể bị khởi động lại; vì vậy dữ liệu có thể mất hoặc các instance có snapshot khác nhau. Mô hình này chỉ dành cho demo thử luồng server-side, không bảo đảm chia sẻ hay lưu bền vững.

API hiện không có đăng nhập/xác thực; ma trận quyền điều khiển trải nghiệm demo, không phải ranh giới bảo mật production.
