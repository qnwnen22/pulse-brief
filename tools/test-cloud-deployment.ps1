param([Parameter(Mandatory = $true)][string]$PackagePath)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$deployment = Join-Path $PSScriptRoot "cloud/deploy-to-ubuntu.ps1"
$global:PulseBriefDeploymentCommands = [System.Collections.Generic.List[string]]::new()
function global:ssh { $global:PulseBriefDeploymentCommands.Add([string]$args[-1]); $global:LASTEXITCODE = 0 }
function global:scp { $global:LASTEXITCODE = 0 }

try {
    foreach ($webOnly in @($false, $true)) {
        $global:PulseBriefDeploymentCommands.Clear()
        & $deployment -HostName "fixture.invalid" -PackagePath $PackagePath -SkipBootstrap -WebOnly:$webOnly -StartServices
        $install = @($global:PulseBriefDeploymentCommands | Where-Object { $_.Contains("rsync") })
        $start = @($global:PulseBriefDeploymentCommands | Where-Object { $_.Contains("health_ok=0") })
        $scope = if ($webOnly) { "web" } else { "all" }
        if ($install.Count -ne 1 -or $start.Count -ne 1 -or -not $install[0].Contains("deployment_scope=$scope") -or -not $start[0].Contains("deployment_scope=$scope")) {
            throw "Deployment scope was not forwarded to remote installation and restart."
        }
        if ($install[0] -notmatch '(?s)if \[ "\$deployment_scope" = "all" \]; then\s+sudo rsync.*?/collector/.*?fi' -or
            $start[0] -notmatch '(?s)if \[ "\$deployment_scope" = "all" \]; then\s+sudo systemctl enable pulsebrief-collector\s+sudo systemctl restart pulsebrief-collector\s+fi') {
            throw "Collector changes must be guarded by full deployment scope."
        }
        if ($global:PulseBriefDeploymentCommands.Count -ne 3) { throw "Unexpected provisioning or extra SSH command." }
    }
    $global:PulseBriefDeploymentCommands.Clear()
    $rejected = $false
    try { & $deployment -HostName "fixture.invalid" -PackagePath $PackagePath -WebOnly }
    catch { $rejected = $_.Exception.Message.Contains("requires -SkipBootstrap") }
    if (-not $rejected -or $global:PulseBriefDeploymentCommands.Count -ne 0) { throw "Web-only bootstrap must be rejected before SSH." }
    Write-Host "PASS: full/web-only deployment scopes, collector guards and bootstrap rejection; SSH/SCP mocked"
} finally {
    Remove-Item Function:ssh, Function:scp
    Remove-Variable PulseBriefDeploymentCommands -Scope Global
    Set-Location $root
}
