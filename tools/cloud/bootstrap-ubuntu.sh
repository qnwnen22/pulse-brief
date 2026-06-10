#!/usr/bin/env bash
set -euo pipefail

APP_USER="${APP_USER:-pulsebrief}"
APP_ROOT="${APP_ROOT:-/opt/pulsebrief}"
ENV_DIR="${ENV_DIR:-/etc/pulsebrief}"
LOG_DIR="${LOG_DIR:-/var/log/pulsebrief}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/pulsebrief}"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run this script with sudo: sudo bash tools/cloud/bootstrap-ubuntu.sh" >&2
  exit 1
fi

. /etc/os-release
if [[ "${ID}" != "ubuntu" ]]; then
  echo "This script expects Ubuntu. Detected: ${PRETTY_NAME}" >&2
  exit 1
fi

echo "==> Installing base packages"
apt-get update
apt-get install -y ca-certificates curl gnupg lsb-release unzip jq ufw

echo "==> Installing .NET 10 runtime and SDK"
apt-get update
apt-get install -y dotnet-sdk-10.0 aspnetcore-runtime-10.0

echo "==> Installing MongoDB 8.0 and database tools"
install -d -m 0755 /etc/apt/keyrings
if [[ ! -f /etc/apt/keyrings/mongodb-server-8.0.gpg ]]; then
  curl -fsSL https://www.mongodb.org/static/pgp/server-8.0.asc \
    | gpg --dearmor -o /etc/apt/keyrings/mongodb-server-8.0.gpg
fi

UBUNTU_CODENAME="$(. /etc/os-release && echo "${VERSION_CODENAME}")"
cat >/etc/apt/sources.list.d/mongodb-org-8.0.list <<EOF
deb [ arch=amd64,arm64 signed-by=/etc/apt/keyrings/mongodb-server-8.0.gpg ] https://repo.mongodb.org/apt/ubuntu ${UBUNTU_CODENAME}/mongodb-org/8.0 multiverse
EOF

apt-get update
apt-get install -y mongodb-org mongodb-database-tools
systemctl enable --now mongod

echo "==> Installing cloudflared"
if [[ ! -f /usr/share/keyrings/cloudflare-main.gpg ]]; then
  curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg \
    | gpg --dearmor -o /usr/share/keyrings/cloudflare-main.gpg
fi
cat >/etc/apt/sources.list.d/cloudflared.list <<'EOF'
deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main
EOF
apt-get update
apt-get install -y cloudflared

echo "==> Creating application user and directories"
if ! id "${APP_USER}" >/dev/null 2>&1; then
  useradd --system --create-home --shell /usr/sbin/nologin "${APP_USER}"
fi

install -d -o "${APP_USER}" -g "${APP_USER}" "${APP_ROOT}/web"
install -d -o "${APP_USER}" -g "${APP_USER}" "${APP_ROOT}/collector"
install -d -o "${APP_USER}" -g "${APP_USER}" "${LOG_DIR}"
install -d -o "${APP_USER}" -g "${APP_USER}" "${BACKUP_DIR}"
install -d -m 0750 -o root -g "${APP_USER}" "${ENV_DIR}"

if [[ ! -f "${ENV_DIR}/pulsebrief.env" ]]; then
  cat >"${ENV_DIR}/pulsebrief.env" <<'EOF'
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:8085
Mongo__ConnectionString=mongodb://127.0.0.1:27017
Mongo__DatabaseName=pulsebrief
Collector__EnableInWebHost=false
Collector__AllowWebManualRefresh=false
# OpenAI__ApiKey=
# Security__AdminToken=
EOF
  chown root:"${APP_USER}" "${ENV_DIR}/pulsebrief.env"
  chmod 0640 "${ENV_DIR}/pulsebrief.env"
fi

echo "==> Enabling firewall for SSH-only public access"
ufw allow OpenSSH
ufw --force enable

systemctl daemon-reload

cat <<EOF

Bootstrap complete.

Next steps:
1. Copy published app files into:
   - ${APP_ROOT}/web
   - ${APP_ROOT}/collector
2. Edit ${ENV_DIR}/pulsebrief.env with production secrets.
3. Install systemd service files into /etc/systemd/system.
4. Restore MongoDB data, then start pulsebrief-web and pulsebrief-collector.

EOF
