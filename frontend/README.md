# frontend — Lab Monitoring Web

Angular 20 SPA (standalone) + AdminLTE 4 + Bootstrap 5. Đăng nhập qua Keycloak
(client `labmonitoring-spa`, realm `coreproject` — xem `../KEYCLOAK-INTEGRATION.md`),
tiêu thụ REST API + SignalR `/hubs/lab` của backend.

## Chạy dev

```bash
cd frontend
npm install
npm start            # ng serve trên http://localhost:4300  (proxy /api -> http://localhost:5200)
```

Cần backend chạy (`cd ../backend && dotnet run --project src/LabMonitoring.Api`) và Keycloak dev
(`http://localhost:8082`). Đăng nhập lần đầu tạo bản ghi user; để có quyền Administrator, đặt
`Auth:BootstrapAdmins` ở backend bằng `preferred_username`/`email` của bạn.

## Cấu trúc

```
src/
  environments/            environment.ts (dev) · environment.production.ts
  app/
    core/
      auth/                auth.config · auth.service (angular-oauth2-oidc) · auth.guard · auth.interceptor
      api/                 meta.service (và các API service khác)
      realtime/            lab-hub.service (@microsoft/signalr)
    layout/shell/          khung AdminLTE: sidebar + topbar + <router-outlet>
    features/dashboard/    trang chủ (xác nhận kết nối API + SignalR)
  styles.scss              global; Bootstrap/AdminLTE/icons nạp qua angular.json
proxy.conf.json            /api -> backend (dev)
```

## Build

```bash
npm run build            # dist/lab-monitoring-web  (config production đổi sang sso.invatech.com.vn)
```
