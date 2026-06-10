[CmdletBinding()]
param(
    [string]$HostName = "13.124.97.153",
    [string]$UserName = "ubuntu",
    [string]$KeyPath = "$env:USERPROFILE\.ssh\pulse-brief-lightsail.pem",
    [int]$LocalPort = 27018,
    [int]$RemotePort = 27017
)

$ErrorActionPreference = "Stop"

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
