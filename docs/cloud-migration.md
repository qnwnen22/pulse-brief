# Pulse Brief Cloud Migration Runbook

This runbook moves the current Windows/IIS deployment to a small Ubuntu VM such as AWS Lightsail 2 GB.

## Target Shape

```text
Ubuntu VM
|- /opt/pulsebrief/web        PulseBrief web app
|- /opt/pulsebrief/collector  PulseBrief.Collector worker
|- MongoDB                    local pulsebrief database
|- cloudflared                Cloudflare Tunnel to 127.0.0.1:8085
`- systemd                    process restart and boot recovery
```

The VM should keep only SSH open publicly. HTTP traffic should enter through Cloudflare Tunnel.

## 1. Create The Server

Recommended starting point:

- AWS Lightsail
- Ubuntu 24.04 LTS
- 2 GB RAM plan
- Static IP attached

After creation, connect with SSH and confirm:

```bash
lsb_release -a
free -h
df -h
```

## 2. Bootstrap Ubuntu

From this repository, upload `tools/cloud/bootstrap-ubuntu.sh` to the server and run:

```bash
sudo bash bootstrap-ubuntu.sh
```

The script installs:

- .NET 10 SDK and ASP.NET Core runtime
- MongoDB 8.0 and MongoDB Database Tools
- cloudflared
- UFW firewall with SSH allowed
- `/opt/pulsebrief`, `/etc/pulsebrief`, and backup directories

## 3. Publish App Files Locally

On the development PC:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\cloud\publish-cloud.ps1
```

This creates:

```text
publish/cloud/pulsebrief-cloud.zip
```

The archive does not include `.env` or `appsettings.Production.json`.

## 4. Upload And Install App Files

Upload the archive to the server, then run:

```bash
mkdir -p ~/pulsebrief-cloud
unzip -o pulsebrief-cloud.zip -d ~/pulsebrief-cloud

sudo rsync -a --delete ~/pulsebrief-cloud/web/ /opt/pulsebrief/web/
sudo rsync -a --delete ~/pulsebrief-cloud/collector/ /opt/pulsebrief/collector/
sudo chown -R pulsebrief:pulsebrief /opt/pulsebrief

sudo cp ~/pulsebrief-cloud/systemd/pulsebrief-web.service /etc/systemd/system/
sudo cp ~/pulsebrief-cloud/systemd/pulsebrief-collector.service /etc/systemd/system/
sudo systemctl daemon-reload
```

Or use the helper script from the development PC:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\cloud\deploy-to-ubuntu.ps1 `
  -HostName SERVER_IP `
  -KeyPath C:\path\to\lightsail-key.pem
```

## 5. Configure Secrets

Edit the server environment file:

```bash
sudo nano /etc/pulsebrief/pulsebrief.env
```

Minimum production values:

```bash
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:8085
Mongo__ConnectionString=mongodb://127.0.0.1:27017
Mongo__DatabaseName=pulsebrief
Collector__EnableInWebHost=false
Collector__AllowWebManualRefresh=false
OpenAI__ApiKey=REPLACE_ME
Security__AdminToken=REPLACE_ME
```

Do not commit this file.

## 6. Back Up MongoDB On Windows

On the current production PC:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\backup-mongodb.ps1
```

Upload the newest backup directory under `backups/mongodb` to the server.

## 7. Restore MongoDB On Ubuntu

Assuming the uploaded dump folder is `~/pulsebrief-mongodb-backup/pulsebrief`:

```bash
mongorestore --drop --uri "mongodb://127.0.0.1:27017" --db pulsebrief ~/pulsebrief-mongodb-backup/pulsebrief
```

Verify:

```bash
mongosh pulsebrief --eval "db.briefs.countDocuments()"
```

## 8. Start Services

```bash
sudo systemctl enable --now pulsebrief-web
sudo systemctl enable --now pulsebrief-collector

systemctl status pulsebrief-web --no-pager
systemctl status pulsebrief-collector --no-pager
curl -s http://127.0.0.1:8085/api/health | jq
```

Collector logs:

```bash
journalctl -u pulsebrief-collector -n 100 --no-pager
```

## 9. Move Cloudflare Tunnel

Preferred cutover:

1. Install or create the same Cloudflare Tunnel on the Ubuntu server.
2. Point the public hostname `news.pulse-brief.co.kr` to `http://127.0.0.1:8085`.
3. Stop the old Windows tunnel only after the Ubuntu tunnel is healthy.
4. Confirm public traffic reaches the new VM.

Useful checks:

```bash
cloudflared tunnel list
cloudflared tunnel info TUNNEL_NAME_OR_ID
curl -I https://news.pulse-brief.co.kr/
curl -s https://news.pulse-brief.co.kr/api/health
```

## 10. Cutover Verification

Before turning off the PC-hosted server, verify:

- `https://news.pulse-brief.co.kr/` returns 200.
- `/api/health` returns `ok: true`.
- `/api/briefs` has recent data.
- `/api/daily-summary` returns the current summary.
- `pulsebrief-web` and `pulsebrief-collector` are enabled and running.
- MongoDB has a fresh backup plan.

Rollback is simple while the Windows server is still intact: stop the Ubuntu tunnel and restart the Windows tunnel.

## 11. Optional Daily MongoDB Backup

The cloud helper includes a systemd timer that runs a local MongoDB dump once per day and retains about seven days of backups:

```bash
sudo install -m 0750 tools/cloud/pulsebrief-mongodb-backup.sh /usr/local/bin/pulsebrief-mongodb-backup.sh
sudo install -m 0644 tools/cloud/pulsebrief-mongodb-backup.service /etc/systemd/system/pulsebrief-mongodb-backup.service
sudo install -m 0644 tools/cloud/pulsebrief-mongodb-backup.timer /etc/systemd/system/pulsebrief-mongodb-backup.timer
sudo systemctl daemon-reload
sudo systemctl enable --now pulsebrief-mongodb-backup.timer
```

This is not a replacement for provider-level snapshots, but it gives a quick local restore point.

## 12. Connect With MongoDB Compass

The production MongoDB port is intentionally bound to `127.0.0.1` on the Ubuntu server. Do not open `27017` to the internet. Use an SSH tunnel from the development PC:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\cloud\open-mongodb-tunnel.ps1
```

Keep that terminal window open, then connect Compass with:

```text
URI: mongodb://127.0.0.1:27018/pulsebrief
Authentication: None
```

Close the tunnel terminal when you are done.
