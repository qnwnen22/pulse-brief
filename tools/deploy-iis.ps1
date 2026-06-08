$log = 'C:\Users\User\source\repos\pulse-brief\iis-site-create.log'
try {
    $ErrorActionPreference = 'Stop'
    $projectPath = 'C:\Users\User\source\repos\pulse-brief'
    $sitePath = 'C:\inetpub\pulse-brief'
    $publishPath = Join-Path $projectPath 'publish\iis'
    $offlinePath = Join-Path $sitePath 'app_offline.htm'
    $preservedItems = @('app_offline.htm', '.env', 'appsettings.Production.json')

    New-Item -ItemType Directory -Path $sitePath -Force | Out-Null

    '<html><body>Deploying Pulse Brief...</body></html>' | Out-File -FilePath $offlinePath -Encoding utf8
    Start-Sleep -Seconds 5

    $removeSucceeded = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            Get-ChildItem -LiteralPath $sitePath -Force |
                Where-Object { $preservedItems -notcontains $_.Name } |
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

    if (Test-Path -LiteralPath $offlinePath) { Remove-Item -LiteralPath $offlinePath -Force }

    "DONE: files deployed; existing site .env preserved when present" | Out-File -FilePath $log -Encoding utf8
} catch {
    "ERROR: $($_.Exception.Message)" | Out-File -FilePath $log -Encoding utf8
    exit 1
}
