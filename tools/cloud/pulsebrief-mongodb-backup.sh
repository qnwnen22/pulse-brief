#!/usr/bin/env bash
set -euo pipefail

backup_root=/var/backups/pulsebrief/mongodb
stamp=$(date -u +%Y%m%d_%H%M%S)
dest="${backup_root}/pulsebrief_${stamp}"

install -d -m 0750 -o pulsebrief -g pulsebrief "${backup_root}"
mongodump --uri mongodb://127.0.0.1:27017 --db pulsebrief --out "${dest}"
chown -R pulsebrief:pulsebrief "${dest}"

find "${backup_root}" \
  -mindepth 1 \
  -maxdepth 1 \
  -type d \
  -name 'pulsebrief_*' \
  -mtime +7 \
  -exec rm -rf {} +
