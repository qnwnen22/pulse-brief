$log = 'C:\Users\User\source\repos\pulse-brief\iis-site-create.log'
try {
    $ErrorActionPreference = 'Stop'
    $projectPath = 'C:\Users\User\source\repos\pulse-brief'
    $sitePath = 'C:\inetpub\pulse-brief'
    $publishPath = Join-Path $projectPath 'publish\iis'
    $envSource = Join-Path $projectPath '.env'
    $offlinePath = Join-Path $sitePath 'app_offline.htm'

    New-Item -ItemType Directory -Path $sitePath -Force | Out-Null

    '<html><body>Deploying Pulse Brief...</body></html>' | Out-File -FilePath $offlinePath -Encoding utf8
    Start-Sleep -Seconds 5

    $removeSucceeded = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            Get-ChildItem -LiteralPath $sitePath -Force |
                Where-Object { $_.Name -ne 'app_offline.htm' } |
                Remove-Item -Recurse -Force
            $removeSucceeded = $true
            break
        } catch {
            if ($attempt -eq 10) { throw }
            Start-Sleep -Seconds 3
        }
    }

    if (-not $removeSucceeded) { throw 'Failed to clear IIS site directory.' }
    Copy-Item -Path (Join-Path $publishPath '*') -Destination $sitePath -Recurse -Force
    if (Test-Path -LiteralPath $envSource) {
        Copy-Item -LiteralPath $envSource -Destination (Join-Path $sitePath '.env') -Force
    }

    if (Test-Path -LiteralPath $offlinePath) { Remove-Item -LiteralPath $offlinePath -Force }

    "DONE: files deployed" | Out-File -FilePath $log -Encoding utf8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8
    exit 1
}
