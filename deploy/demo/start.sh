#!/bin/sh
# Demo container start: a private PostgreSQL inside the same container (data in /tmp, so every restart is a fresh demo),
# then the API. Only for the public demo (render.yaml); a real install uses deploy/docker-compose.prod.yml.
set -eu
PGBIN=$(ls -d /usr/lib/postgresql/*/bin | sort -V | tail -1)
PGDATA=/tmp/pgdata
if [ ! -s "$PGDATA/PG_VERSION" ]; then
  "$PGBIN/initdb" -D "$PGDATA" -U karnama --auth=trust -E UTF8 --locale=C.UTF-8 >/dev/null
  # Small memory footprint (free instance: 512 MB for database + API); only reachable from inside the container.
  cat >> "$PGDATA/postgresql.conf" <<CONF
listen_addresses = '127.0.0.1'
unix_socket_directories = '/tmp'
max_connections = 40
shared_buffers = 32MB
work_mem = 2MB
maintenance_work_mem = 16MB
fsync = off
synchronous_commit = off
full_page_writes = off
CONF
fi
"$PGBIN/pg_ctl" -D "$PGDATA" -l /tmp/postgres.log -w start >/dev/null
"$PGBIN/createdb" -h 127.0.0.1 -U karnama karnama 2>/dev/null || true
export ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=karnama;Username=karnama;Maximum Pool Size=25"
exec dotnet Karnama.Api.dll
