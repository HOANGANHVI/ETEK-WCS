# ETEK WCS

New WCS project scaffold.

## Repository layout

- `CLAUDE.md`: mandatory Claude Code project rules.
- `docs/`: approved business specification and change-control records.
- `src/backend/`: .NET solution and projects (created by the setup script).
- `src/frontend/`: React + TypeScript + Vite application (created by the setup script).
- `tests/`: automated tests.
- `deployment/`: deployment assets and environment configuration.
- `scripts/`: local developer setup scripts.

## Current baseline

Business baseline: WCS Specification v1.3. Read `docs/README.md` and all files under `docs/` before implementation.

## Initialize development projects

Open PowerShell in this repository root and run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\initialize-project.ps1
```

The script creates the .NET 10 solution/projects and the React + TypeScript Vite app. It does not implement business logic or connect to live PLC/RCS devices.

## Important

Do not commit credentials, production connection strings, PLC addresses or RCS secrets. Use local environment variables and example configuration files.

## Demo Mode (frontend only, fake data)

Demo Mode lets you explore and try the whole UI **without PostgreSQL, the backend or any device**. All data lives in the browser's memory
and is lost on refresh; no request is sent to the backend (a guard in `main.tsx` also blocks any `/api` or `/hubs` call) and nothing is
written to PostgreSQL.

```powershell
cd src\frontend\wcs-web
npm install            # first time only
npm run dev:demo       # same as: $env:VITE_DEMO_MODE="true"; npm run dev
```

Open http://localhost:5173. A small "Chế độ mô phỏng" chip in the header shows that the data is fake (no large banner). The **simulation dock** at the bottom of the screen (part of the layout, never floating over content) lets you:

- **Bắt đầu / Tạm dừng** automatic running (speed selectable), **Chạy từng bước** for all tasks, **Khởi tạo lại** to restore the initial demo data;
- open **Tình huống và sự cố** to start scenarios for **BP-01, BP-02, BP-03, BP-05, BP-06** (stock is prepared automatically), including a BP-01 case with no free destination and a button that frees a slot;
- inject incidents: RCS interruption, cancel (needs RCS confirmation), AGV connection loss, then reconcile the rack position;
- step one task at a time with *Chạy bước thiết bị tiếp theo (mô phỏng)* in **Quản lý nhiệm vụ**.

The whole UI is in Vietnamese (technical codes such as AGV1, BP-01, PLC, RCS, Rack ID are kept).

`npm run build:demo` produces a demo bundle. With `VITE_DEMO_MODE` unset or `false` (default, `npm run dev` / `npm run build`) the app talks to
the real backend exactly as before. Demo Mode is a UI illustration: the authoritative business rules are those of the backend and the
specification; BP-04, A3/A4 and D1/D2 are not simulated.
