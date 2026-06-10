$log = 'C:\Users\User\source\repos\pulse-brief\collector-task.log'
try {
    $ErrorActionPreference = 'Stop'
    $taskName = 'PulseBrief Collector'
    $collectorPath = 'C:\inetpub\pulse-brief-collector'
    $runnerPath = Join-Path $collectorPath 'run-collector-service.ps1'

    if (-not (Test-Path -LiteralPath $runnerPath)) {
        throw "Collector runner was not found: $runnerPath"
    }

    $action = New-ScheduledTaskAction `
        -Execute 'powershell.exe' `
        -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$runnerPath`"" `
        -WorkingDirectory $collectorPath
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal `
        -UserId 'SYSTEM' `
        -LogonType ServiceAccount `
        -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -RestartCount 999 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -MultipleInstances IgnoreNew

    $task = New-ScheduledTask `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description 'Runs Pulse Brief news collector independently from the IIS web server.'

    Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName

    "DONE: scheduled task registered and started: $taskName" | Out-File -FilePath $log -Encoding utf8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8
    exit 1
}
