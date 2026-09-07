[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$HostName,
    [Parameter(Mandatory = $true)][string]$KeyPath,
    [string]$UserName = 'ubuntu',
    [string]$DatabaseName = 'pulsebrief'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$key = (Resolve-Path -LiteralPath $KeyPath).Path
if ($HostName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]*$') { throw '올바르지 않은 서버 주소입니다.' }
if ($UserName -notmatch '^[a-z_][a-z0-9_-]*$' -or $DatabaseName -notmatch '^[A-Za-z0-9_-]+$') { throw '사용자 또는 DB 이름이 올바르지 않습니다.' }
$node = (Get-Command node -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$ssh = (Get-Command ssh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$codex = (Get-Command codex -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$configDirectory = Join-Path $repoRoot 'data/summary-launcher'
[void](New-Item -ItemType Directory -Path $configDirectory -Force)
$configPath = Join-Path $configDirectory 'config.json'
$config = [ordered]@{}
if (Test-Path -LiteralPath $configPath) {
    $old = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in $old.PSObject.Properties) { $config[$property.Name] = $property.Value }
}
$config.HostName = $HostName
$config.UserName = $UserName
$config.DatabaseName = $DatabaseName
$config.KeyPath = $key
$config.NodePath = $node
$config.SshPath = $ssh
$config.CodexPath = $codex
if (-not $config.Contains('MaxArticles')) { $config.MaxArticles = 10000 }
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))

$desktop = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($desktop)) { throw '바탕화면 경로를 찾지 못했습니다.' }
$shortcutPath = Join-Path $desktop '전날 뉴스 요약.lnk'
$scriptPath = Join-Path $PSScriptRoot 'start-daily-summary.ps1'
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.Arguments -notlike "*$scriptPath*") {
    throw '같은 이름의 다른 바로가기가 있습니다. 기존 파일은 덮어쓰지 않습니다.'
}
$shortcut.TargetPath = $powershell
$shortcut.Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Interactive"
$shortcut.WorkingDirectory = $repoRoot
$shortcut.IconLocation = "$powershell,0"
$shortcut.Description = '전날 뉴스 요약 생성 후 사이트에 자동 반영. 기존 날짜 재생성 및 덮어쓰기 방지.'
$shortcut.WindowStyle = 1
$shortcut.Save()
Write-Host "바탕화면 바로가기 생성: $shortcutPath"
Write-Host '서버 주소와 키 경로는 Git에서 제외되는 로컬 설정에만 저장했습니다.'
