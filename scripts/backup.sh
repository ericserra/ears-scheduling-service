#!/usr/bin/env bash
# Backup diário do PostgreSQL. Agendar no cron do host:
#   0 3 * * * /opt/ears/scripts/backup.sh >> /var/log/ears-backup.log 2>&1
set -euo pipefail

DESTINO="${BACKUP_DIR:-/opt/ears/backups}"
RETENCAO_DIAS="${BACKUP_RETENTION_DAYS:-14}"
CONTAINER="${POSTGRES_CONTAINER:-ears-postgres}"
BANCO="${POSTGRES_DB:-ears}"
USUARIO="${POSTGRES_USER:-ears}"

mkdir -p "$DESTINO"
ARQUIVO="$DESTINO/ears-$(date +%Y%m%d-%H%M%S).sql.gz"

docker exec "$CONTAINER" pg_dump -U "$USUARIO" -d "$BANCO" | gzip > "$ARQUIVO"
echo "Backup gerado: $ARQUIVO ($(du -h "$ARQUIVO" | cut -f1))"

find "$DESTINO" -name 'ears-*.sql.gz' -mtime +"$RETENCAO_DIAS" -delete
echo "Backups com mais de $RETENCAO_DIAS dias removidos."
