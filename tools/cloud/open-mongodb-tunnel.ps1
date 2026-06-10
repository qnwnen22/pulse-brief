[CmdletBinding()]
param(
    [string]$HostName = $env:PULSEBRIEF_SSH_HOST,
    [string]$UserName = "ubuntu",
    [string]$KeyPath = $env:PULSEBRIEF_SSH_KEY_PATH,
    [int]$LocalPort = 27018,
    [int]$RemotePort = 27017
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($HostName)) {
    throw "HostName was not provided. Pass -HostName SERVER_IP or set PULSEBRIEF_SSH_HOST."
}

if ([string]::IsNullOrWhiteSpace($KeyPath)) {
    throw "KeyPath was not provided. Pass -KeyPath C:\path\to\lightsail-key.pem or set PULSEBRIEF_SSH_KEY_PATH."
}

if (-not (Test-Path -LiteralPath $KeyPath)) {
    throw "SSH key was not found: $KeyPath"
}

Write-Host "Opening SSH tunnel for MongoDB..."
Write-Host "Compass URI: mongodb://127.0.0.1:$LocalPort/pulsebrief"
Write-Host "Keep this window open while using Compass."
Write-Host ""

ssh `
    -i $KeyPath `
    -o ExitOnForwardFailure=yes `
    -o ServerAliveInterval=30 `
    -L "${LocalPort}:127.0.0.1:${RemotePort}" `
    "${UserName}@${HostName}"
