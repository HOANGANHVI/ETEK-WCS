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
