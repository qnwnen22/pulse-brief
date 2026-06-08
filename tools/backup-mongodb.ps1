[CmdletBinding()]
param(
    [string]$ConnectionString = "mongodb://127.0.0.1:27017",
    [string]$DatabaseName = "pulsebrief",
    [string]$OutputRoot = ".\backups\mongodb",
    [string]$MongodumpPath
)

$ErrorActionPreference = "Stop"

function Resolve-ToolPath {
    param(
        [string]$ExplicitPath,
        [string]$ToolName
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path -LiteralPath $ExplicitPath) {
            return (Resolve-Path -LiteralPath $ExplicitPath).Path
        }

        throw "$ToolName path was provided but does not exist: $ExplicitPath"
    }

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "$ToolName was not found. Install MongoDB Database Tools or pass -MongodumpPath."
    }

    return $command.Source
}

function Mask-ConnectionString {
    param([string]$Value)

    return ($Value -replace "(mongodb(?:\+srv)?://)([^:@/]+):([^@/]+)@", '${1}${2}:***@')
}

$dumpTool = Resolve-ToolPath -ExplicitPath $MongodumpPath -ToolName "mongodump"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$destination = Join-Path $OutputRoot "$($DatabaseName)_$timestamp"

New-Item -ItemType Directory -Path $destination -Force | Out-Null

Write-Host "MongoDB backup started."
Write-Host "Database: $DatabaseName"
Write-Host "Connection: $(Mask-ConnectionString $ConnectionString)"
Write-Host "Output: $destination"

& $dumpTool --uri $ConnectionString --db $DatabaseName --out $destination
if ($LASTEXITCODE -ne 0) {
    throw "mongodump failed with exit code $LASTEXITCODE."
}

Write-Host "MongoDB backup completed: $destination"
