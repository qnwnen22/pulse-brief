$projectPath = Resolve-Path (Join-Path $PSScriptRoot '..')
$log = Join-Path $projectPath 'collector-deploy.log'
try {
    $ErrorActionPreference = 'Stop'
    $webSitePath = 'C:\inetpub\pulse-brief'
    $collectorPath = 'C:\inetpub\pulse-brief-collector'
    $publishPath = Join-Path $projectPath 'publish\collector'
    $preservedItems = @('.env', 'appsettings.Production.json', 'logs')
    $runnerPath = Join-Path $collectorPath 'run-collector-service.ps1'
    $taskName = 'PulseBrief Collector'
    $restartTask = $false

    if (-not (Test-Path -LiteralPath $publishPath)) {
        throw "Collector publish path does not exist: $publishPath"
    }

    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task -and $task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $taskName
        $restartTask = $true
        Start-Sleep -Seconds 3
    }

    New-Item -ItemType Directory -Path $collectorPath -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $collectorPath 'logs') -Force | Out-Null

    foreach ($item in @('.env', 'appsettings.Production.json')) {
        $targetItem = Join-Path $collectorPath $item
        $sourceItem = Join-Path $webSitePath $item
        if (-not (Test-Path -LiteralPath $targetItem) -and (Test-Path -LiteralPath $sourceItem)) {
            Copy-Item -LiteralPath $sourceItem -Destination $targetItem -Force
        }
    }

    Get-ChildItem -LiteralPath $collectorPath -Force |
        Where-Object { $preservedItems -notcontains $_.Name } |
        Remove-Item -Recurse -Force

    Copy-Item -Path (Join-Path $publishPath '*') -Destination $collectorPath -Recurse -Force

    @'
$ErrorActionPreference = 'Continue'
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DOTNET_ENVIRONMENT = 'Production'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'PulseBrief.Collector.exe'
$logDir = Join-Path $root 'logs'
$logPath = Join-Path $logDir 'collector-service.log'

New-Item -ItemType Directory -Path $logDir -Force | Out-Null
Set-Location -LiteralPath $root

while ($true) {
    $startedAt = Get-Date -Format o
    "[$startedAt] PulseBrief.Collector starting" | Out-File -LiteralPath $logPath -Append -Encoding utf8

    if (-not (Test-Path -LiteralPath $exe)) {
        "[$(Get-Date -Format o)] Collector executable was not found: $exe" | Out-File -LiteralPath $logPath -Append -Encoding utf8
        Start-Sleep -Seconds 60
        continue
    }

    & $exe *>> $logPath
    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    "[$(Get-Date -Format o)] PulseBrief.Collector exited with code $exitCode. Restarting in 30 seconds." | Out-File -LiteralPath $logPath -Append -Encoding utf8
    Start-Sleep -Seconds 30
}
'@ | Out-File -FilePath $runnerPath -Encoding utf8 -Force

    if ($restartTask) {
        Start-ScheduledTask -TaskName $taskName
    }

    "DONE: collector deployed to $collectorPath" | Out-File -FilePath $log -Encoding utf8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8
    exit 1
}
