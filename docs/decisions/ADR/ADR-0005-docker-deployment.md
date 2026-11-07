# ADR-0005 – Docker deployment on Windows 11

Status: accepted (CR-006, 2026-11-07). Implementation executed only statically/with the app outside containers; Docker runtime acceptance pending.

## Context
One Windows 11 machine (8 cores, 16 GB) must run WCS independently of the developer machine, from GitHub `main`, on the same LAN as 4 PLCs. The source uses ASP.NET Core .NET 10, React/Vite, PostgreSQL 16, SignalR; it does not use MQTT. Scheduler, PLC workers and handshake run inside the API process.

## Decisions
1. **Containers:** postgres 16, migrator (one-shot), backend, frontend (Nginx). No MQTT/Redis. Exactly one backend instance.
2. **Networks:** `db_net` is `internal` (postgres, migrator, backend); `edge_net` carries frontend and backend and is the backend's route to the PLC LAN (outbound NAT, no host networking, no privileged mode). Only Nginx publishes a port.
3. **Migrations:** an EF Core migration bundle (self-contained, built in the image) run once before the backend (`service_completed_successfully`); forward only; `deploy.ps1` runs it explicitly after a backup. The backend itself never migrates.
4. **State:** named volumes `etek_wcs_pgdata` and `etek_wcs_keys` (Data Protection key ring, owned by the non-root `app` user). No script removes volumes; no `down -v`.
5. **Configuration:** machine-specific `.env` outside the repository (`C:\ETEK-WCS\config\.env`), passed with `--env-file`; PLC connection data stays in the database. No secrets in images or Git.
6. **Production safety:** Production refuses `Plc:Mode=Simulator` unless `Plc:AllowSimulatorInProduction=true`; compose defaults to `RealReadOnly` + `AllowLiveConnection=false`; simulator and test-write APIs are blocked in Production; RCS stays simulator-only and is disclosed in the UI.
7. **Authentication cookie:** `Auth:CookieSecure` (Always | SameAsRequest), unset = previous behaviour. HTTP over LAN requires SameAsRequest; HTTPS termination is a later option.
8. **Health:** anonymous `GET /healthz` (application / database), used by Docker and `status.ps1`; the detailed `/api/system/health` stays authenticated.
9. **Proxy:** Nginx proxies `/api` and `/hubs` (WebSocket upgrade, long timeouts), falls back to `index.html` for React Router. The backend trusts `X-Forwarded-*` only when `Deployment:TrustForwardedHeaders=true` (compose only; the port is not published).
10. **Git-based deploy:** `deploy.ps1` requires branch `main`, a clean tree and a fast-forward pull, backs up first, builds, migrates, starts, checks health, and logs the commit for rollback. Nothing deploys automatically.

## Consequences / limits
The PLC sees the Windows host IP. Key ring is unencrypted at rest. No HTTPS in this release. Docker behaviour must be accepted with `docs/DEPLOYMENT_TEST_CHECKLIST.md`.
