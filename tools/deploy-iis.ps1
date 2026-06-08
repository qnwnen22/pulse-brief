$log = 'C:\Users\User\source\repos\pulse-brief\iis-site-create.log'
try {
    $ErrorActionPreference = 'Stop'
    $siteName = 'PulseBrief'
    $appPool = 'PulseBriefPool'
    $projectPath = 'C:\Users\User\source\repos\pulse-brief'
    $sitePath = 'C:\inetpub\pulse-brief'
    $publishPath = Join-Path $projectPath 'publish\iis'
    $dbSource = Join-Path $projectPath 'data\pulsebrief.db'
    $dataPath = Join-Path $sitePath 'data'
    $appcmd = 'C:\Windows\System32\inetsrv\appcmd.exe'

    New-Item -ItemType Directory -Path $sitePath -Force | Out-Null
    Copy-Item -Path (Join-Path $publishPath '*') -Destination $sitePath -Recurse -Force
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
    Copy-Item -LiteralPath $dbSource -Destination (Join-Path $dataPath 'pulsebrief.db') -Force

    $existingSite = & $appcmd list site /name:$siteName
    if ($LASTEXITCODE -eq 0 -and $existingSite) { & $appcmd delete site $siteName | Out-Null }
    $existingPool = & $appcmd list apppool /name:$appPool
    if ($LASTEXITCODE -eq 0 -and $existingPool) { & $appcmd delete apppool $appPool | Out-Null }

    & $appcmd add apppool /name:$appPool /managedRuntimeVersion: /managedPipelineMode:Integrated | Out-Null
    & $appcmd set apppool $appPool /startMode:AlwaysRunning /processModel.idleTimeout:00:00:00 | Out-Null
    & $appcmd add site /name:$siteName /physicalPath:$sitePath /bindings:http/127.0.0.1:8085: | Out-Null
    & $appcmd set app $siteName/ /applicationPool:$appPool | Out-Null
    & $appcmd set app $siteName/ /preloadEnabled:true | Out-Null
    icacls $dataPath /grant "IIS AppPool\${appPool}:(OI)(CI)M" /T | Out-Null
    Start-Service W3SVC
    & $appcmd start apppool $appPool | Out-Null
    & $appcmd start site $siteName | Out-Null

    "DONE" | Out-File -FilePath $log -Encoding utf8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8
    exit 1
}

