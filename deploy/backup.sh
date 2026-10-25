#!/usr/bin/env bash
# Daily database + files backup with 14-day retention. Cron (as root):
#   15 3 * * * /opt/karnama/deploy/backup.sh >> /var/log/karnama-backup.log 2>&1
set -euo pipefail
cd "$(dirname "$0")"
STAMP=$(date +%F-%H%M)
mkdir -p backups
docker compose -f docker-compose.prod.yml --env-file .env.prod exec -T postgres pg_dump -U karnama -d karnama -Fc > "backups/db-$STAMP.dump"
docker run --rm -v karnama_storage:/data:ro -v "$PWD/backups:/out" alpine tar -czf "/out/files-$STAMP.tar.gz" -C /data .
find backups -type f -mtime +14 -delete
echo "backup ok: $STAMP"
