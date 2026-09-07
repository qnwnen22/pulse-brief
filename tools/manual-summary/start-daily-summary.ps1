[CmdletBinding()]
param(
    [string]$Date,
    [switch]$Interactive,
    [switch]$CheckOnly,
    [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$mutex = $null
$locked = $false
$exitCode = 0

try {
    if ([string]::IsNullOrWhiteSpace($Date)) {
        $Date = [DateTime]::UtcNow.AddHours(9).AddDays(-1).ToString('yyyy-MM-dd')
    }
    $parsed = [DateTime]::ParseExact($Date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    if ($parsed -ge [DateTime]::UtcNow.AddHours(9).Date) { throw '오늘 또는 미래 날짜는 요약할 수 없습니다.' }

    Write-Host "Pulse Brief 전날 뉴스 요약 | 대상: $Date" -ForegroundColor Cyan
    Write-Host '기존 요약을 먼저 확인하고, 새 요약은 완료 즉시 사이트에 자동 반영합니다.'
    $mutex = New-Object Threading.Mutex($false, "Local\PulseBrief.DailySummary.$Date")
    try { $locked = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) {
        Write-Host '같은 날짜의 요약 작업이 이미 실행 중입니다. 새 작업을 시작하지 않습니다.' -ForegroundColor Yellow
    } else {
        $configPath = Join-Path $repoRoot 'data/summary-launcher/config.json'
        if (-not (Test-Path -LiteralPath $configPath)) { throw '설정 파일이 없습니다. 바탕화면 설치 도구를 먼저 실행해 주세요.' }
        $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not (Test-Path -LiteralPath $config.NodePath)) { throw 'Node.js 실행 파일을 찾을 수 없습니다. 설치 도구를 다시 실행해 주세요.' }
        $arguments = @((Join-Path $PSScriptRoot 'run.cjs'), $Date)
        if ($CheckOnly) { $arguments += '--check-only' }
        & $config.NodePath @arguments
        if ($LASTEXITCODE -ne 0) { throw '작업 완료를 확인하지 못했습니다. 위 오류를 확인해 주세요. DB 저장 후 확인만 실패했을 수도 있습니다. 재실행 시 서버 상태와 복구 기록을 먼저 확인합니다.' }

        $resultPath = Join-Path $repoRoot "data/manual-summary-runs/$Date/result.json"
        $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        switch ($result.status) {
            'existing-server' { Write-Host "$Date 요약이 운영 DB에 이미 있습니다. 재생성하거나 덮어쓰지 않습니다." -ForegroundColor Yellow }
            'no-articles' { Write-Host '해당 날짜의 기사가 없습니다. 빈 요약을 생성하지 않습니다.' -ForegroundColor Yellow }
            'published' {
                Write-Host "$Date 요약 배포 완료 | 기사 $($result.articleCount)건" -ForegroundColor Green
                if ($result.website.status -eq 'verified') { Write-Host "사이트 반영 확인: $($result.website.url)" }
                else { Write-Host $result.website.reason -ForegroundColor Yellow }
            }
            'checked' { Write-Host "확인 완료 | 로컬 요약: $($result.localExists) | 서버 요약: $($result.serverExists)" }
            default { throw '실행 결과를 확인할 수 없습니다.' }
        }
    }
} catch {
    $exitCode = 1
    Write-Host $_.Exception.Message -ForegroundColor Red
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
    if ($Interactive) { [void](Read-Host 'Enter를 누르면 창을 닫습니다') }
}
exit $exitCode
