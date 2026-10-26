#!/usr/bin/env bash
# One isolated QA API (no website): bash tests/qa/qa-api.sh <port> <database>   e.g. 5091 karnama_qa_a
# Fresh demo database + QA crowd; QA mode on; 127.0.0.1 trusted as a proxy so scenarios can fake the client IP with X-Forwarded-For.
set -euo pipefail
PORT="${1:?port}"; DB="${2:?database}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; cd "$ROOT"
while IFS= read -r line; do
  [[ "$line" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] || continue
  key="${line%%=*}"; value="${line#*=}"; value="${value%% #*}"; value="${value%"${value##*[![:space:]]}"}"
  [ -n "${!key:-}" ] || export "$key=$value"
done < .env
export KARNAMA_SKIP_DOTENV=1 ASPNETCORE_ENVIRONMENT=Development PATH="$HOME/.dotnet:$PATH"
LOGS="$ROOT/tests/qa/logs"; mkdir -p "$LOGS"
(fuser -k "$PORT/tcp" || true) >/dev/null 2>&1
psql "postgresql://karnama:${POSTGRES_PASSWORD}@localhost:5432/postgres" -c "DROP DATABASE IF EXISTS $DB WITH (FORCE);" -c "CREATE DATABASE $DB;" >/dev/null
CS="${ConnectionStrings__Default/Database=karnama;/Database=$DB;}"
[ -f src/Karnama.Api/bin/Release/net10.0/Karnama.Api.dll ] || dotnet build src/Karnama.Api -c Release -v q -nologo >/dev/null
( cd src/Karnama.Api && Seed__Mode=Demo DemoLogin__Enabled=true Qa__Enabled=true ConnectionStrings__Default="$CS" ReverseProxy__KnownProxies__0=127.0.0.1 \
  RateLimits__AuthPerIpPerMinute=100000 RateLimits__FormsPerIpPer10Minutes=100000 RateLimits__OtpPerIpPer10Minutes=100000 RateLimits__RefreshPerIpPerMinute=100000 \
  RateLimits__UserBurst=100000 RateLimits__HeavyPerUserPerMinute=100000 RateLimits__DevicePerIpPerMinute=100000 \
  setsid nohup dotnet bin/Release/net10.0/Karnama.Api.dll --urls "http://localhost:$PORT" > "$LOGS/api-$PORT.log" 2>&1 & )
for _ in $(seq 1 150); do curl -fs "http://localhost:$PORT/health/ready" >/dev/null 2>&1 && break; sleep 1; done
curl -fs -X POST "http://localhost:$PORT/api/v1/qa/seed-crowd" -o /dev/null && echo "[qa] api :$PORT on $DB ready (crowd seeded)"
