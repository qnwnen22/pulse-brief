[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$HostName,

    [string]$UserName = "ubuntu",

    [string]$KeyPath,

    [Parameter(Mandatory = $true)]
    [string]$SummaryPath,

    [string]$DatabaseName = "pulsebrief"
)

$ErrorActionPreference = "Stop"

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

$summaryFullPath = Resolve-LocalPath $SummaryPath
$importerFullPath = Resolve-LocalPath "tools/cloud/import-daily-summary.js"
$remoteSummaryPath = "/tmp/pulsebrief-manual-summary.json"
$remoteImporterPath = "/tmp/pulsebrief-import-daily-summary.js"

Invoke-External -Name "Upload manual summary" -FilePath "scp" -Arguments ($sshArgs + @($summaryFullPath, "${target}:$remoteSummaryPath"))
Invoke-External -Name "Upload summary importer" -FilePath "scp" -Arguments ($sshArgs + @($importerFullPath, "${target}:$remoteImporterPath"))
Invoke-External -Name "Import manual summary" -FilePath "ssh" -Arguments ($sshArgs + @($target, "mongosh $DatabaseName --quiet $remoteImporterPath"))
Invoke-External -Name "Remove temporary files" -FilePath "ssh" -Arguments ($sshArgs + @($target, "rm -f $remoteSummaryPath $remoteImporterPath"))

Write-Host ""
Write-Host "Manual daily summary imported."
