#!/usr/bin/env bash
# Monthly restore test: restores the newest backup into a throw-away database and counts rows. A backup that was never
# restored is not a backup. Cron:  0 4 1 * * /opt/karnama/deploy/restore-test.sh
set -euo pipefail
cd "$(dirname "$0")"
LATEST=$(ls -t backups/db-*.dump | head -1)
C="docker compose -f docker-compose.prod.yml --env-file .env.prod exec -T postgres"
$C psql -U karnama -d postgres -c "DROP DATABASE IF EXISTS restore_test" -c "CREATE DATABASE restore_test"
$C pg_restore -U karnama -d restore_test --no-owner "/backups/$(basename "$LATEST")"
$C psql -U karnama -d restore_test -tAc "SELECT 'users=' || count(*) FROM users UNION ALL SELECT 'orders=' || count(*) FROM orders"
$C psql -U karnama -d postgres -c "DROP DATABASE restore_test"
echo "restore test ok: $LATEST"
