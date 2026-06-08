[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,

    [string]$ConnectionString = "mongodb://127.0.0.1:27017",
    [string]$DatabaseName = "pulsebrief",
    [string]$MongorestorePath,
    [switch]$ConfirmRestore
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
        throw "$ToolName was not found. Install MongoDB Database Tools or pass -MongorestorePath."
    }

    return $command.Source
}

function Mask-ConnectionString {
    param([string]$Value)

    return ($Value -replace "(mongodb(?:\+srv)?://)([^:@/]+):([^@/]+)@", '${1}${2}:***@')
}

if (-not $ConfirmRestore) {
    throw "Restore is destructive because it uses --drop. Re-run with -ConfirmRestore after checking the backup path."
}

if (-not (Test-Path -LiteralPath $BackupPath)) {
    throw "Backup path does not exist: $BackupPath"
}

$restoreTool = Resolve-ToolPath -ExplicitPath $MongorestorePath -ToolName "mongorestore"
$resolvedBackupPath = (Resolve-Path -LiteralPath $BackupPath).Path
$candidateDatabasePath = Join-Path $resolvedBackupPath $DatabaseName

if (Test-Path -LiteralPath $candidateDatabasePath) {
    $databaseBackupPath = $candidateDatabasePath
}
else {
    $databaseBackupPath = $resolvedBackupPath
}

Write-Host "MongoDB restore started."
Write-Host "Database: $DatabaseName"
Write-Host "Connection: $(Mask-ConnectionString $ConnectionString)"
Write-Host "Backup: $databaseBackupPath"

& $restoreTool --uri $ConnectionString --drop --db $DatabaseName $databaseBackupPath
if ($LASTEXITCODE -ne 0) {
    throw "mongorestore failed with exit code $LASTEXITCODE."
}

Write-Host "MongoDB restore completed."
