[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "PowerShell 구문 오류: $($file.Name)" }
}
$date = '2000-01-01'
$mutex = New-Object Threading.Mutex($false, "Local\PulseBrief.DailySummary.$date")
$locked = $false
try {
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { throw '동일 날짜 작업이 실행 중입니다. 검증을 중단합니다.' }
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'start-daily-summary.ps1') -Date $date -NoOpen
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch '이미 실행 중') { throw '동시 실행 차단 검증 실패' }
    Write-Host 'PASS: PowerShell 구문 및 날짜별 동시 실행 차단. DB/Codex 호출 없음.'
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
