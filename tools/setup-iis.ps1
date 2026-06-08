$log = 'C:\Users\User\source\repos\pulse-brief\iis-site-create.log'
try {
    $ErrorActionPreference = 'Stop'
    $siteName = 'PulseBrief'
    $appPool = 'PulseBriefPool'
    $sitePath = 'C:\inetpub\pulse-brief'
    $appcmd = 'C:\Windows\System32\inetsrv\appcmd.exe'
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $codexUser = "$env:COMPUTERNAME\CodexSandboxOffline"

    New-Item -ItemType Directory -Path $sitePath -Force | Out-Null

    $existingPool = & $appcmd list apppool /name:$appPool
    if ($LASTEXITCODE -ne 0 -or -not $existingPool) {
        & $appcmd add apppool /name:$appPool /managedRuntimeVersion: /managedPipelineMode:Integrated | Out-Null
    }

    & $appcmd set apppool $appPool /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00 | Out-Null

    $existingSite = & $appcmd list site /name:$siteName
    if ($LASTEXITCODE -eq 0 -and $existingSite) {
        & $appcmd set site $siteName /physicalPath:$sitePath /bindings:http/127.0.0.1:8085: | Out-Null
    } else {
        & $appcmd add site /name:$siteName /physicalPath:$sitePath /bindings:http/127.0.0.1:8085: | Out-Null
    }

    & $appcmd set app $siteName/ /applicationPool:$appPool | Out-Null
    & $appcmd set app $siteName/ /preloadEnabled:true | Out-Null

    icacls $sitePath /grant "${currentUser}:(OI)(CI)M" | Out-Null
    icacls $sitePath /grant "${codexUser}:(OI)(CI)M" 2>$null | Out-Null

    Start-Service W3SVC
    & $appcmd start apppool $appPool 2>$null | Out-Null
    & $appcmd start site $siteName 2>$null | Out-Null

    "DONE: IIS setup completed for $currentUser and $codexUser" | Out-File -FilePath $log -Encoding utf8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8
    exit 1
}
