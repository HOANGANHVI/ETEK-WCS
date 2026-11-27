#!/usr/bin/env bash
# CR-046: builds the images, starts the Compose stack on a THROW-AWAY environment and checks it through Nginx. Test machines and CI only: it removes ITS OWN project and volumes at the end.
# It never connects to a PLC or an RCS (defaults and the simulator), and it prints no password. Usage (repository root):  bash tests/deployment/compose-smoke.sh
set -euo pipefail
cd "$(dirname "$0")/../.."
WORK="$(mktemp -d)"; ENVFILE="$WORK/.env"; JAR="$WORK/cookies"; PORT="${SMOKE_PORT:-8089}"; BASE="http://127.0.0.1:$PORT"
# a project name and volume names of its own, so an installation on the same machine is never touched
PROJECT=wcs-smoke
cat > "$WORK/override.yml" <<'EOF'
volumes:
  wcs_pgdata: { name: wcs_smoke_pgdata }
  wcs_keys: { name: wcs_smoke_keys }
  wcs_config_backups: { name: wcs_smoke_config_backups }
EOF
dc() { docker compose -p "$PROJECT" --env-file "$ENVFILE" -f deployment/docker-compose.yml -f "$WORK/override.yml" "$@"; }
cleanup() { rc=$?; if [ $rc -ne 0 ]; then echo "== backend log (tail)"; dc logs --tail 60 backend migrator || true; fi; dc down -v --remove-orphans >/dev/null 2>&1 || true; rm -rf "$WORK"; exit $rc; }
trap cleanup EXIT

rand() { head -c 18 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 20; }
PGPW="$(rand)"; ADMIN1="Aa1!$(rand)"; ADMIN2="Bb2!$(rand)"
set_env() { local k="$1" v="$2"; grep -v "^$k=" "$ENVFILE" > "$ENVFILE.tmp" || true; mv "$ENVFILE.tmp" "$ENVFILE"; printf '%s=%s\n' "$k" "$v" >> "$ENVFILE"; }
cp deployment/.env.example "$ENVFILE"
set_env POSTGRES_PASSWORD "$PGPW"; set_env WCS_ADMIN_BOOTSTRAP_PASSWORD "$ADMIN1"; set_env WCS_HTTP_PORT "$PORT";
set_env WCS_COOKIE_SECURE SameAsRequest; set_env WCS_VERSION smoke
# the backend reads this folder: a folder of its own that the container user can list ($WORK itself holds the passwords and stays private)
mkdir "$WORK/logs"; chmod 755 "$WORK/logs"; set_env WCS_LOG_DIR "$WORK/logs"
export APP_VERSION="$(tr -d '\r\n' < VERSION)"
export WCS_APP_VERSION="$APP_VERSION" WCS_GIT_COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)" WCS_BUILD_TIME="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
# test machine, step A of deployment/README.md: PLC and RCS simulators
set_env PLC_MODE Simulator; set_env PLC_ALLOW_SIMULATOR_IN_PRODUCTION true; set_env RCS_ALLOW_SIMULATOR_IN_PRODUCTION true

pass() { echo "PASS $*"; }
fail() { echo "FAIL $*"; exit 1; }
check() { local name="$1"; shift; if "$@" >/dev/null 2>&1; then pass "$name"; else fail "$name"; fi; }
api() { local method="$1" path="$2" body="${3:-}"; if [ -n "$body" ]; then curl -fsS -b "$JAR" -c "$JAR" -X "$method" -H 'X-WCS-Client: smoke' -H 'Content-Type: application/json' --data "$body" "$BASE$path"; else curl -fsS -b "$JAR" -c "$JAR" -X "$method" -H 'X-WCS-Client: smoke' "$BASE$path"; fi; }
login() { rm -f "$JAR"; api POST /api/auth/login "{\"username\":\"admin\",\"password\":\"$1\"}"; }
# the answer of a request that is expected to be refused (no -f), and one value from the database of the stack
refused() { curl -sS -b "$JAR" -c "$JAR" -X "$1" -H 'X-WCS-Client: smoke' -H 'Content-Type: application/json' --data "$3" "$BASE$2"; }
sql() { printf '%s\n' "$1" | dc exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -q -t -A -v ON_ERROR_STOP=1' | tr -d '\r'; }
# station/signal=address of every point, from the API
addresses() { api GET /api/plc/points | jq -r '.data[] | "\(.stationId)/\(.signalName)=\(.actualAddress) \(.addressOrigin)"' | sort; }
point_id() { jq -r --arg s "$1" --arg n "$2" '.data[] | select(.stationId == $s and .signalName == $n) | .id' "$WORK/points.json"; }
health() { for _ in $(seq 1 60); do curl -fsS "$BASE/healthz" 2>/dev/null | grep -q '"database":"Up"' && return 0; sleep 2; done; return 1; }

echo "== build"; dc build
docker image ls --format '{{.Repository}}:{{.Tag}} {{.Size}}' | grep '^etek-wcs-' || fail "images"
echo "== up (simulator)"; dc up -d --wait --wait-timeout 240
dc ps --format '{{.Service}} {{.State}} {{.Health}}'
[ "$(docker inspect -f '{{.State.ExitCode}}' "$(dc ps -a -q migrator)")" = "0" ] && pass "migrator exited 0" || fail "migrator"
MIGRATIONS="$(ls src/backend/WCS.Infrastructure/Persistence/Migrations/[0-9]*.cs | grep -vc 'Designer')"
[ "$(sql 'select count(*) from "__EFMigrationsHistory"')" = "$MIGRATIONS" ] && pass "new database: all $MIGRATIONS migrations applied" || fail "migrations"
check "healthz through nginx: application and database up" health
curl -fsS "$BASE/api/version" | grep -q "\"version\":\"$APP_VERSION\"" && pass "version $APP_VERSION" || fail "version"
curl -fsS "$BASE/" | grep -q '<div id="root"' && pass "web page served" || fail "web page"
curl -fsS "$BASE/plc/io-mapping" | grep -q '<div id="root"' && pass "deep link served" || fail "deep link"
login "$ADMIN1" | grep -q '"mustChangePassword":true' && pass "first sign-in demands a new password" || fail "login"
api POST /api/auth/change-password "{\"currentPassword\":\"$ADMIN1\",\"newPassword\":\"$ADMIN2\"}" >/dev/null && pass "password changed" || fail "change password"
api GET /api/plc/points > "$WORK/points.json"
[ "$(jq '.data | length' "$WORK/points.json")" = "96" ] && pass "96 IO points of the IO map" || fail "points"
[ "$(jq -r '[.data[].stationId] | unique | join(",")' "$WORK/points.json")" = "A1,A2,A3,A4,B1,B2,D1,D2,E1,E2,G1,G2,X1,X2,X3,X4" ] && pass "16 stations of the IO map" || fail "stations"
# CR-048: every point of a new installation holds, for its own station and signal, the address of the IO map. The expected list comes from the catalog in the repository, not from the API;
# the catalog is compared with the workbook by the generator (when node is there) and by the unit tests.
CATALOG=src/backend/WCS.Infrastructure/Plc/Catalog/plc-io-catalog.json
if command -v node >/dev/null 2>&1; then node scripts/generate-plc-io-catalog.mjs --check >/dev/null && pass "catalog matches the workbook" || fail "catalog"; else echo "SKIP catalog against workbook: node is not installed"; fi
jq -r '.stations[] | .stationId as $s | .addresses | to_entries[] | "\($s)/\(.key)=\(.value)"' "$CATALOG" | tr -d '\r' | sort > "$WORK/expected.txt"
jq -r '.data[] | "\(.stationId)/\(.signalName)=\(.actualAddress)"' "$WORK/points.json" | tr -d '\r' | sort > "$WORK/actual.txt"
[ "$(wc -l < "$WORK/expected.txt")" = "96" ] && diff "$WORK/expected.txt" "$WORK/actual.txt" && pass "96 default addresses, each on its own station and signal" || fail "addresses"
# two rows written out as the workbook has them
grep -qx 'A1/PLC_MissionRequest=D1500' "$WORK/actual.txt" && grep -qx 'X4/Mission_Feedback=D1631' "$WORK/actual.txt" && pass "sample addresses of the IO map" || fail "sample addresses"
[ "$(jq '[.data[] | select(.actualAddress != .defaultAddress or .addressOrigin != "WorkbookDefault")] | length' "$WORK/points.json")" = "0" ] && pass "every address is marked as the default" || fail "origin"
# an address confirms nothing and permits no write
[ "$(jq '[.data[] | select(.confirmed != false or .writeApproval != "NOT_APPROVED" or .writeAllowed == true)] | length' "$WORK/points.json")" = "0" ] && pass "no signal confirmed, none approved for writing" || fail "write permission"
FEEDBACK="$(point_id A1 Mission_Feedback)"
refused PUT "/api/plc/points/$FEEDBACK/write-allowed" '{"allowed":true}' | grep -q 'WRITE_SIGNAL_NOT_CONFIRMED' && pass "write approval of an unconfirmed signal is refused" || fail "write approval"
api GET /api/plc/io-map | jq -e '.data.sourceSha256 == "405586c9e4d2893f1d3a33922259ecfe1c400ca877710877b3b482e049b34da3"' >/dev/null && pass "IO map hash" || fail "io-map"
api GET /api/plc/overview > "$WORK/ov.json"
jq -e '.data.runtime.effectiveMode == "Simulator" and .data.runtime.writeEnabled == false and (.data.stations | length) == 16 and (.data.plcs | length) == 4' "$WORK/ov.json" >/dev/null && pass "simulator overview" || fail "overview"
api GET /api/plc/monitor/points | jq -e '(.data.points | length) == 96' >/dev/null && pass "monitoring list" || fail "monitor"
api POST /api/plc-simulation/activate '{"confirmed":true}' | jq -e '.data.active == true' >/dev/null && pass "PLC simulation can be activated" || fail "simulation"
api POST '/hubs/wcs/negotiate?negotiateVersion=1' > "$WORK/neg.json" && jq -e '[.availableTransports[].transport] | index("WebSockets") != null' "$WORK/neg.json" >/dev/null && pass "SignalR negotiate through nginx" || fail "negotiate"
TOKEN="$(jq -r .connectionToken "$WORK/neg.json")"
code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 -b "$JAR" -H 'Connection: Upgrade' -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' -H "Origin: $BASE" "$BASE/hubs/wcs?id=$TOKEN" || true)"
[ "$code" = "101" ] && pass "WebSocket upgrade through nginx" || fail "websocket ($code)"

# CR-048: what an Admin enters or removes must survive the restart
EDITED="$(point_id A1 PLC_MissionRequest)"; REMOVED="$(point_id A1 PLC_ReaddyTranfer)"
api PUT "/api/plc/points/$EDITED/address" '{"address":"D1700"}' >/dev/null && api DELETE "/api/plc/points/$REMOVED/address" >/dev/null && pass "an address edited, an address removed" || fail "edit"
addresses > "$WORK/before-restart.txt"

echo "== restart (containers removed, volumes kept)"; dc down; dc up -d --wait --wait-timeout 240
check "healthy after restart" health
login "$ADMIN2" | grep -q '"mustChangePassword":false' && pass "account kept" || fail "account lost"
addresses > "$WORK/after-restart.txt"
[ "$(wc -l < "$WORK/after-restart.txt")" = "96" ] && diff "$WORK/before-restart.txt" "$WORK/after-restart.txt" && pass "96 points kept, nothing duplicated or overwritten" || fail "data"
grep -qx 'A1/PLC_MissionRequest=D1700 Custom' "$WORK/after-restart.txt" && grep -qx 'A1/PLC_ReaddyTranfer=null Custom' "$WORK/after-restart.txt" && pass "edited and removed addresses survive the restart" || fail "admin addresses"
api POST "/api/plc/points/$EDITED/address/restore-default" >/dev/null && api POST "/api/plc/points/$REMOVED/address/restore-default" >/dev/null
api GET /api/plc/points | jq -r '.data[] | "\(.stationId)/\(.signalName)=\(.actualAddress)"' | tr -d '\r' | sort | diff "$WORK/expected.txt" - && pass "restore default brings the IO map addresses back" || fail "restore"
[ "$(sql 'select count(*) from retired_io_points')" = "0" ] && pass "new database: nothing retired" || fail "retired"

echo "== compose defaults (no simulator, no live connection, no write)"
set_env PLC_MODE RealReadWrite; set_env PLC_ALLOW_SIMULATOR_IN_PRODUCTION false; set_env RCS_ALLOW_SIMULATOR_IN_PRODUCTION false; set_env PLC_WRITE_ALLOWLIST 'broken-entry'
dc up -d --wait --wait-timeout 240
check "healthy with the defaults" health
login "$ADMIN2" >/dev/null
api GET /api/plc/overview > "$WORK/ov2.json"
jq -e '.data.runtime.effectiveMode == "RealReadOnly" and .data.runtime.allowLiveConnection == false and .data.runtime.writeEnabled == false' "$WORK/ov2.json" >/dev/null && pass "read only, no live connection, write off" || fail "defaults"
jq -e '[.data.plcs[].state] | unique == ["Disabled"]' "$WORK/ov2.json" >/dev/null && pass "no PLC connection" || fail "plc state"
jq -e '[.data.runtime.notes[] | select(startswith("[WRITE_ALLOWLIST_INVALID]"))] | length == 1' "$WORK/ov2.json" >/dev/null && pass "an unreadable allow list reaches the backend and declares nothing" || fail "allow list"
# a write asked for in the default mode is refused, and nothing was ever authorized or sent to a PLC
refused POST /api/plc/stations/A1/signals/Mission_Feedback/test-write '{"value":1}' > "$WORK/write.json"
jq -e '.data.success != true and .error.code != null' "$WORK/write.json" >/dev/null && pass "a write request is refused ($(jq -r .error.code "$WORK/write.json"))" || fail "write not refused"
[ "$(sql "select count(*) from audit_log where \"Action\" in ('PLC_WRITE_AUTHORIZED','PLC_WRITE_RESULT')")" = "0" ] && pass "no write authorized or executed" || fail "write audit"
[ "$(sql "select count(*) from plc_exchange_log")" = "0" ] && pass "no frame sent to any PLC" || fail "plc traffic"
RUNNING="$(dc ps --status running --format '{{.Service}}' | sort | tr '\n' ' ')"
[ "$RUNNING" = "backend frontend postgres " ] && [ -z "$(dc ps --format '{{.Health}}' | grep -E 'unhealthy|starting' || true)" ] && pass "services running: $RUNNING" || fail "services: $RUNNING"
if dc logs --no-log-prefix backend | grep -q 'LOG_MAINTENANCE_FAILED'; then fail "log maintenance"; else pass "log maintenance runs"; fi
errors="$(dc logs --no-log-prefix backend | grep -cE '"@l":"(Error|Fatal)"|\[(ERR|FTL)\]' || true)"
echo "backend error/fatal log lines: $errors"; [ "$errors" = "0" ] && pass "no error in the backend log" || { dc logs --no-log-prefix backend | grep -E '"@l":"(Error|Fatal)"|\[(ERR|FTL)\]' | head -10; fail "errors in the log"; }
dc ps --format '{{.Service}} {{.State}} {{.Health}}'
echo "== smoke passed"
