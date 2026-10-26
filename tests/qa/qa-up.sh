#!/usr/bin/env bash
# QA environment (docs/PROMPT3.md Q0): a fresh demo database "karnama_qa", QA mode on (movable clock, /api/v1/qa/*),
# API on :5090, website (production build) on :3090, Android web build served on :3091.
# Usage: bash tests/qa/qa-up.sh [--no-build]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; cd "$ROOT"
while IFS= read -r line; do
  [[ "$line" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] || continue
  key="${line%%=*}"; value="${line#*=}"; value="${value%% #*}"; value="${value%"${value##*[![:space:]]}"}"
  [ -n "${!key:-}" ] || export "$key=$value"
done < .env
export KARNAMA_SKIP_DOTENV=1 ASPNETCORE_ENVIRONMENT=Development
LOGS="$ROOT/tests/qa/logs"; mkdir -p "$LOGS"
for p in 5090 3090 3091; do (fuser -k $p/tcp || true) >/dev/null 2>&1; done
if docker info >/dev/null 2>&1; then PSQL="docker compose exec -T postgres psql -U karnama -d postgres"; else PSQL="psql postgresql://karnama:${POSTGRES_PASSWORD}@localhost:5432/postgres"; fi
$PSQL -c "DROP DATABASE IF EXISTS karnama_qa WITH (FORCE);" -c "CREATE DATABASE karnama_qa;" >/dev/null
CS="${ConnectionStrings__Default/Database=karnama;/Database=karnama_qa;}"
dotnet build src/Karnama.Api -c Release -v q -nologo >/dev/null
( cd src/Karnama.Api && Seed__Mode=Demo DemoLogin__Enabled=true Qa__Enabled=true ConnectionStrings__Default="$CS" \
  ReverseProxy__KnownProxies__0=127.0.0.1 RateLimits__AuthPerIpPerMinute=100000 RateLimits__FormsPerIpPer10Minutes=100000 RateLimits__OtpPerIpPer10Minutes=100000 RateLimits__RefreshPerIpPerMinute=100000 RateLimits__UserBurst=100000 RateLimits__HeavyPerUserPerMinute=100000 RateLimits__DevicePerIpPerMinute=100000 Cors__AllowedOrigins__0=http://localhost:3090 Cors__AllowedOrigins__1=http://localhost:3091 \
  setsid nohup dotnet bin/Release/net10.0/Karnama.Api.dll --urls http://localhost:5090 > "$LOGS/api.log" 2>&1 & )
for _ in $(seq 1 120); do curl -fs http://localhost:5090/health/ready >/dev/null 2>&1 && break; sleep 1; done
curl -fs -X POST http://localhost:5090/api/v1/qa/seed-crowd -o "$LOGS/crowd.json"
echo "[qa] crowd: $(grep -o '"phone"' "$LOGS/crowd.json" | wc -l) people"
cd src/karnama-web
if [ "${1:-}" != "--no-build" ]; then
  NEXT_DIST_DIR=.next-qa NEXT_PUBLIC_API_BASE=http://localhost:5090 NEXT_PUBLIC_HUB_BASE=http://localhost:5090 API_INTERNAL_BASE=http://localhost:5090 npx next build > "$LOGS/web-build.log" 2>&1
  rm -rf out-qa && BUILD_TARGET=mobile NEXT_PUBLIC_CLIENT=android NEXT_PUBLIC_API_BASE=http://localhost:5090 NEXT_PUBLIC_HUB_BASE=http://localhost:5090 npx next build > "$LOGS/app-build.log" 2>&1 && mv out out-qa
fi
( NEXT_DIST_DIR=.next-qa API_INTERNAL_BASE=http://localhost:5090 setsid nohup npx next start -p 3090 > "$LOGS/web.log" 2>&1 & )
( setsid nohup python3 -m http.server 3091 --bind 127.0.0.1 --directory out-qa > "$LOGS/app.log" 2>&1 & )
for _ in $(seq 1 90); do curl -fs http://localhost:3090/login >/dev/null 2>&1 && break; sleep 1; done
echo "[qa] up: api :5090 · web :3090 · app :3091"
