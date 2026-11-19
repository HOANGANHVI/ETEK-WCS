# ETEK WCS – Configuration package format 1.0 (CR-026)

A configuration package is a ZIP produced by *System → Configuration transfer → Export* and read by *Import*. It carries configuration only.

## Files

| File | Content |
|---|---|
| `manifest.json` | `formatVersion` ("1.0"; this version reads any `1.x`), `exportedAt`, `exportedBy`, `appVersion`, `schemaMigration` (newest applied migration of the source), `sourceSite`, `groups[]` (group, file, record count, SHA-256), `excluded[]` (what is never exported and why), `warnings[]`, `unresolvedReferences[]` (groups a selected group depends on that were not exported), `fileConfig` (SHA-256 of the appsettings sections `ProcessFlows`, `RouteEngine`, `Scheduler`; for comparison only). |
| `locations.json`, `agvs.json`, `plc-stations.json`, `business-processes.json` | `{ "group": "...", "records": [ { "key": "<natural key>", "fields": { ... } } ] }` |
| `checksums.sha256` | `<sha256>  <file>` for every other file. Verified on upload. |
| `README.txt` | Human-readable note. |

Records are identified by their **natural key** (Location code, AGV code, PLC station id, Process code). A package never contains a Guid, a row version or any database id, so it moves between systems whose ids differ.

## Groups (Phase 2)

| Group | Key | Fields | Import behaviour |
|---|---|---|---|
| `Location` | code | name, type, rackCapacity, allowedRackKind, enabled, requiresPlcPermission | Create or update (the type of an existing location is not changed). Operational fields are never read or written. |
| `Agv` | code | role, rcsRobotCode, enabled | **Update only** an existing AGV; never creates one; the role must match. |
| `PlcStation` | station id | plcId, order, locationCode, enabled | **Update only** the link and the enabled flag of an existing station; never creates a station, PLC connection or IO point; never touches addresses or approvals. |
| `BusinessProcess` | code | name, description, requestType, rackKind, triggerLocationCodes, enabled\*, reviewBlocked\*, reviewReason\*, definitionSource\*, definition | New code: process + Draft version. Existing code: header (name, description, trigger locations) after the Admin confirms, plus a new Draft version when the definition differs. Never Publishes; a Published or Disabled version is never edited; `reviewBlocked` is never changed (a process blocked in the package is not created). \* informational, not applied. `definition` is the Published version of the source (else its latest). |

Dependency order is derived from the groups' declared dependencies: Location → AGV → PlcStation → BusinessProcess.

## Never in a package

Passwords, tokens, private keys, RCS secrets; `Approved`, `WriteAllowed`, `WriteApproval` and approver data; IO points and addresses; PLC connections; user accounts; racks, reservations, inventory transactions; tasks, orders, legs, station transactions and requests; logs, audit, PLC communication history; appsettings.

## Import rules

Upload → Validate (format, size, entry names, checksums, schema of every record, duplicate keys) → Preview (no write; Create / Update / Unchanged / Skip / Conflict with field changes) → Apply. Apply needs Maintenance ON, no active Task / Order / Station Transaction, a usable persistent backup folder, and a Preview token bound to the package **and** the current database state. Writes go through the existing services inside one transaction together with their audit entries; a backup of the records to be changed is written and verified before the first write. Nothing is deleted. No PLC runtime reload, no PLC command, no Publish. Maintenance stays ON afterwards.

Limits: package ≤ `ConfigTransfer:MaxPackageMb` (20), unpacked ≤ `MaxUncompressedMb` (50), ≤ 16 entries, only the file names above.
