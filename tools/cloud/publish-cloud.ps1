[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "publish/cloud"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$outputPath = Join-Path $repoRoot $OutputRoot
$webOutput = Join-Path $outputPath "web"
$collectorOutput = Join-Path $outputPath "collector"
$systemdOutput = Join-Path $outputPath "systemd"
$archivePath = Join-Path $outputPath "pulsebrief-cloud.zip"

Set-Location $repoRoot

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Path $webOutput, $collectorOutput, $systemdOutput | Out-Null

Invoke-Checked -Name "Web publish" -Command {
    dotnet publish (Join-Path $repoRoot "PulseBrief.csproj") `
        --configuration $Configuration `
        --output $webOutput `
        /p:UseAppHost=false
}

Invoke-Checked -Name "Collector publish" -Command {
    dotnet publish (Join-Path $repoRoot "PulseBrief.Collector/PulseBrief.Collector.csproj") `
        --configuration $Configuration `
        --output $collectorOutput `
        /p:UseAppHost=false
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "pulsebrief-web.service") -Destination $systemdOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "pulsebrief-collector.service") -Destination $systemdOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bootstrap-ubuntu.sh") -Destination $outputPath

$archiveTemp = Join-Path ([System.IO.Path]::GetTempPath()) ("pulsebrief-cloud-" + [System.Guid]::NewGuid().ToString("N") + ".zip")
Compress-Archive -Path (Join-Path $outputPath "*") -DestinationPath $archiveTemp -Force
Move-Item -LiteralPath $archiveTemp -Destination $archivePath -Force

Write-Host ""
Write-Host "Cloud publish package created:"
Write-Host "  $archivePath"
Write-Host ""
Write-Host "This package intentionally does not include .env or appsettings.Production.json."
Write-Host "Create /etc/pulsebrief/pulsebrief.env on the server for production secrets."
