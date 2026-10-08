[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HostName,

    [string]$UserName = "ubuntu",

    [string]$KeyPath,

    [string]$PackagePath = "publish/cloud/pulsebrief-cloud.zip",

    [string]$MongoBackupPath,

    [switch]$SkipBootstrap,

    [switch]$WebOnly,

    [switch]$StartServices
)

$ErrorActionPreference = "Stop"

if ($WebOnly -and -not $SkipBootstrap) {
    throw "Web-only deployment requires -SkipBootstrap."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $repoRoot

function Resolve-LocalPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return (Resolve-Path -LiteralPath $Path).Path
    }

    return (Resolve-Path -LiteralPath (Join-Path $repoRoot $Path)).Path
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host "==> $Name"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

$target = "$UserName@$HostName"
$sshArgs = @()

if ($KeyPath) {
    $resolvedKeyPath = Resolve-LocalPath $KeyPath
    $sshArgs += @("-i", $resolvedKeyPath)
}

$sshArgs += @("-o", "StrictHostKeyChecking=accept-new")

$packageFullPath = Resolve-LocalPath $PackagePath
$bootstrapFullPath = Resolve-LocalPath "tools/cloud/bootstrap-ubuntu.sh"

Invoke-External -Name "Create remote upload directory" -FilePath "ssh" -Arguments ($sshArgs + @($target, "mkdir -p ~/pulsebrief-upload"))
Invoke-External -Name "Upload bootstrap script" -FilePath "scp" -Arguments ($sshArgs + @($bootstrapFullPath, "${target}:~/pulsebrief-upload/bootstrap-ubuntu.sh"))
Invoke-External -Name "Upload app package" -FilePath "scp" -Arguments ($sshArgs + @($packageFullPath, "${target}:~/pulsebrief-upload/pulsebrief-cloud.zip"))

if ($MongoBackupPath) {
    $backupFullPath = Resolve-LocalPath $MongoBackupPath
    Invoke-External -Name "Upload MongoDB backup" -FilePath "scp" -Arguments ($sshArgs + @("-r", $backupFullPath, "${target}:~/pulsebrief-upload/mongodb-backup"))
}

if (-not $SkipBootstrap) {
    Invoke-External -Name "Bootstrap Ubuntu server" -FilePath "ssh" -Arguments ($sshArgs + @($target, "sudo bash ~/pulsebrief-upload/bootstrap-ubuntu.sh"))
}

$installCommand = @'
bash -lc 'set -euo pipefail
deployment_scope=DEPLOYMENT_SCOPE
rm -rf ~/pulsebrief-cloud
mkdir -p ~/pulsebrief-cloud
set +e
unzip -o ~/pulsebrief-upload/pulsebrief-cloud.zip -d ~/pulsebrief-cloud
unzip_status=$?
set -e
if [ "$unzip_status" -gt 1 ]; then
  exit "$unzip_status"
fi
sudo rsync -a --delete ~/pulsebrief-cloud/web/ /opt/pulsebrief/web/
sudo chown -R pulsebrief:pulsebrief /opt/pulsebrief/web
sudo cp ~/pulsebrief-cloud/systemd/pulsebrief-web.service /etc/systemd/system/
if [ "$deployment_scope" = "all" ]; then
  sudo rsync -a --delete ~/pulsebrief-cloud/collector/ /opt/pulsebrief/collector/
  sudo chown -R pulsebrief:pulsebrief /opt/pulsebrief/collector
  sudo cp ~/pulsebrief-cloud/systemd/pulsebrief-collector.service /etc/systemd/system/
fi
sudo systemctl daemon-reload'
'@
$deploymentScope = if ($WebOnly) { "web" } else { "all" }
$installCommand = $installCommand.Replace("DEPLOYMENT_SCOPE", $deploymentScope)

Invoke-External -Name "Install app files and services" -FilePath "ssh" -Arguments ($sshArgs + @($target, $installCommand))

if ($StartServices) {
    $startCommand = @'
bash -lc 'set -euo pipefail
deployment_scope=DEPLOYMENT_SCOPE
sudo systemctl enable pulsebrief-web
sudo systemctl restart pulsebrief-web
if [ "$deployment_scope" = "all" ]; then
  sudo systemctl enable pulsebrief-collector
  sudo systemctl restart pulsebrief-collector
fi
health_ok=0
for attempt in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:8085/api/health; then
    health_ok=1
    break
  fi
  sleep 1
done
test "$health_ok" -eq 1'
'@
    $startCommand = $startCommand.Replace("DEPLOYMENT_SCOPE", $deploymentScope)
    Invoke-External -Name "Start services and check health" -FilePath "ssh" -Arguments ($sshArgs + @($target, $startCommand))
}

Write-Host ""
Write-Host "Deployment upload complete."
Write-Host "Next server-side steps:"
Write-Host "  1. Edit /etc/pulsebrief/pulsebrief.env"
Write-Host "  2. Restore MongoDB if a backup was uploaded"
if ($WebOnly) {
    Write-Host "  3. Restart the web service only: sudo systemctl restart pulsebrief-web"
} else {
    Write-Host "  3. Start services with: sudo systemctl enable --now pulsebrief-web pulsebrief-collector"
}
