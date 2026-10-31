# Production image for the Angular admin SPA: static build served by nginx, which also reverse-
# proxies /api and /hubs to the API container so the browser only ever talks to one origin.
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# Uses the "production" configuration -> environment.production.ts (SSO issuer, relative API/hub URLs).
RUN npx ng build

FROM nginx:1.27-alpine
RUN apk add --no-cache tzdata
ENV TZ=Asia/Ho_Chi_Minh
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/lab-monitoring-web/browser /usr/share/nginx/html
EXPOSE 443
