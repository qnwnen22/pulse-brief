[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "PowerShell 구문 오류: $($file.Name)" }
}
$mutex = New-Object Threading.Mutex($false, 'Local\PulseBrief.SummaryWorkflow')
$locked = $false
try {
    $locked = $mutex.WaitOne(0)
    if (-not $locked) { throw '동일 날짜 작업이 실행 중입니다. 검증을 중단합니다.' }
    foreach ($mode in @('daily', 'weekly', 'all')) {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'start-daily-summary.ps1') -Mode $mode -NoOpen
        if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch '이미 실행 중') { throw '동시 실행 차단 검증 실패' }
    }
    Write-Host 'PASS: PowerShell 구문 및 전날/주간/통합 실행 간 중복 차단. DB/Codex 호출 없음.'
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
