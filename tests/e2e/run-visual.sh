#!/usr/bin/env bash
# Visual CRUD test (docs/SKILLS.md §6): demo install + empty install, Playwright on desktop 1366, Pixel 7 and 360px.
# Screenshots: tests/e2e/screenshots/{device}-{nn}-{name}.png — open and LOOK at them after every run.
# Usage (Git Bash on Windows, or Linux/macOS):  bash tests/e2e/run-visual.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
[ -f .env ] || { echo "[e2e] .env not found: python tools/new-env.py"; exit 1; }
# Load .env (values may contain spaces, e.g. "Maximum Pool Size"; inline " # comments" are dropped).
while IFS= read -r line; do
  [[ "$line" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] || continue
  key="${line%%=*}"; value="${line#*=}"; value="${value%% #*}"; value="${value%"${value##*[![:space:]]}"}"
  [ -n "${!key:-}" ] || export "$key=$value"
done < .env
export KARNAMA_SKIP_DOTENV=1 ASPNETCORE_ENVIRONMENT=Development
LOGS="$ROOT/tests/e2e/logs"; mkdir -p "$LOGS"; rm -rf tests/e2e/screenshots; mkdir -p tests/e2e/screenshots

free_port() { (fuser -k "$1/tcp" || npx --yes kill-port "$1") >/dev/null 2>&1 || true; }
for p in 5080 5081 3000 3001 3002; do free_port $p; done

if docker info >/dev/null 2>&1; then docker compose up -d --wait postgres redis; PSQL="docker compose exec -T postgres psql -U karnama -d postgres"
else PSQL="psql postgresql://karnama:${POSTGRES_PASSWORD}@localhost:5432/postgres"; fi

# Both installs start fresh every run (tests change data); your own demo database "karnama" is left alone.
$PSQL -c "DROP DATABASE IF EXISTS karnama_empty WITH (FORCE);" -c "CREATE DATABASE karnama_empty;" >/dev/null
$PSQL -c "DROP DATABASE IF EXISTS karnama_e2e WITH (FORCE);" -c "CREATE DATABASE karnama_e2e;" >/dev/null
EMPTY_CS="${ConnectionStrings__Default/Database=karnama;/Database=karnama_empty;}"
DEMO_CS="${ConnectionStrings__Default/Database=karnama;/Database=karnama_e2e;}"

dotnet build src/Karnama.Api -c Release -v q -nologo
API_DLL="$ROOT/src/Karnama.Api/bin/Release/net10.0/Karnama.Api.dll"

( cd src/Karnama.Api && Seed__Mode=Demo DemoLogin__Enabled=true ConnectionStrings__Default="$DEMO_CS" \
  Cors__AllowedOrigins__0=http://localhost:3000 Cors__AllowedOrigins__1=https://localhost Cors__AllowedOrigins__2=http://localhost:3002 \
  dotnet "$API_DLL" --urls http://localhost:5080 > "$LOGS/api-demo.log" 2>&1 & )
( cd src/Karnama.Api && Seed__Mode=Empty DemoLogin__Enabled=false ConnectionStrings__Default="$EMPTY_CS" \
  Cors__AllowedOrigins__0=http://localhost:3001 dotnet "$API_DLL" --urls http://localhost:5081 > "$LOGS/api-empty.log" 2>&1 & )

cd src/karnama-web
[ -d node_modules ] || npm ci
NEXT_DIST_DIR=.next-demo NEXT_PUBLIC_API_BASE=http://localhost:5080 NEXT_PUBLIC_HUB_BASE=http://localhost:5080 npx next build > "$LOGS/web-demo-build.log" 2>&1
NEXT_DIST_DIR=.next-empty NEXT_PUBLIC_API_BASE=http://localhost:5081 NEXT_PUBLIC_HUB_BASE=http://localhost:5081 npx next build > "$LOGS/web-empty-build.log" 2>&1
# The Android app's web part (static export, CLIENT_KIND=android), served like the app would load it.
rm -rf out && BUILD_TARGET=mobile NEXT_PUBLIC_CLIENT=android NEXT_PUBLIC_API_BASE=http://localhost:5080 NEXT_PUBLIC_HUB_BASE=http://localhost:5080 npx next build > "$LOGS/web-app-build.log" 2>&1
node ../../tools/bundle-size.mjs .next-demo
( NEXT_DIST_DIR=.next-demo npx next start -p 3000 > "$LOGS/web-demo.log" 2>&1 & )
( NEXT_DIST_DIR=.next-empty npx next start -p 3001 > "$LOGS/web-empty.log" 2>&1 & )
( "$(command -v python3 || command -v python)" -m http.server 3002 --bind 127.0.0.1 --directory out > "$LOGS/web-app.log" 2>&1 & )
cd "$ROOT"

wait_for() { for _ in $(seq 1 90); do curl -fs "$1" >/dev/null 2>&1 && return 0; sleep 1; done; echo "[e2e] $1 did not start"; exit 1; }
wait_for http://localhost:5080/health/ready; wait_for http://localhost:5081/health/ready
wait_for http://localhost:3000/login; wait_for http://localhost:3001/login; wait_for http://localhost:3002/login/

cd tests/e2e
[ -d node_modules ] || npm ci
DEMO_URL=http://localhost:3000 EMPTY_URL=http://localhost:3001 APP_URL=http://localhost:3002 \
EMPTY_SETUP_TOKEN="$Setup__Token" EMPTY_API_LOG="$LOGS/api-empty.log" \
npx playwright test "$@"
status=$?
echo "[e2e] screenshots: tests/e2e/screenshots  ·  report: tests/e2e/report/index.html"
for p in 5080 5081 3000 3001 3002; do free_port $p; done
exit $status
