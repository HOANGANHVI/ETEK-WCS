# syntax=docker/dockerfile:1
# ETEK WCS web UI: Vite production build served by Nginx. Build context = repository root.
FROM node:22-alpine AS build
WORKDIR /web
COPY src/frontend/wcs-web/package.json src/frontend/wcs-web/package-lock.json ./
RUN npm ci
COPY src/frontend/wcs-web/ ./
# Same-origin API (VITE_API_BASE_URL stays empty): Nginx proxies /api and /hubs. Demo Mode and engineering tools are off in this build.
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY deployment/docker/frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1/nginx-health || exit 1
