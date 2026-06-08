$log = 'C:\Users\User\source\repos\pulse-brief\iis-site-create.log'
try {
    $ErrorActionPreference = 'Stop'
    $siteName = 'PulseBrief'
    $appPool = 'PulseBriefPool'
    $projectPath = 'C:\Users\User\source\repos\pulse-brief'
    $sitePath = 'C:\inetpub\pulse-brief'
    $publishPath = Join-Path $projectPath 'publish\iis'
    $envSource = Join-Path $projectPath '.env'
    $appcmd = 'C:\Windows\System32\inetsrv\appcmd.exe'
    $offlinePath = Join-Path $sitePath 'app_offline.htm'

    New-Item -ItemType Directory -Path $sitePath -Force | Out-Null

    $existingSite = & $appcmd list site /name:$siteName
    if ($LASTEXITCODE -eq 0 -and $existingSite) { & $appcmd stop site $siteName 2>$null | Out-Null }
    $existingPool = & $appcmd list apppool /name:$appPool
    if ($LASTEXITCODE -eq 0 -and $existingPool) { & $appcmd stop apppool $appPool 2>$null | Out-Null }

    '<html><body>Deploying Pulse Brief...</body></html>' | Out-File -FilePath $offlinePath -Encoding utf8
    Start-Sleep -Seconds 2

    Get-ChildItem -LiteralPath $sitePath -Force |
        Where-Object { $_.Name -ne 'app_offline.htm' } |
        Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $publishPath '*') -Destination $sitePath -Recurse -Force
    if (Test-Path -LiteralPath $envSource) {
        Copy-Item -LiteralPath $envSource -Destination (Join-Path $sitePath '.env') -Force
    }

    $existingSite = & $appcmd list site /name:$siteName
    $existingPool = & $appcmd list apppool /name:$appPool

    if ($LASTEXITCODE -ne 0 -or -not $existingPool) {
        & $appcmd add apppool /name:$appPool /managedRuntimeVersion: /managedPipelineMode:Integrated | Out-Null
    }
    & $appcmd set apppool $appPool /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00 | Out-Null
    if ($existingSite) {
        & $appcmd set site $siteName /bindings:http/127.0.0.1:8085: | Out-Null
    } else {
        & $appcmd add site /name:$siteName /physicalPath:$sitePath /bindings:http/127.0.0.1:8085: | Out-Null
    }
    & $appcmd set app $siteName/ /applicationPool:$appPool | Out-Null
    & $appcmd set app $siteName/ /preloadEnabled:true | Out-Null
    if (Test-Path -LiteralPath $offlinePath) { Remove-Item -LiteralPath $offlinePath -Force }
    Start-Service W3SVC
    & $appcmd start apppool $appPool | Out-Null
    & $appcmd start site $siteName | Out-Null

    "DONE" | Out-File -FilePath $log -Encoding utf8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8
    exit 1
}

