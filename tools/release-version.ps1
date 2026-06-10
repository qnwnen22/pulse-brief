param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string[]]$Notes = @(),

    [switch]$Tag
)

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$versionPath = Join-Path $projectPath 'VERSION'
$propsPath = Join-Path $projectPath 'Directory.Build.props'
$changelogPath = Join-Path $projectPath 'CHANGELOG.md'

$versionMatch = [regex]::Match($Version, '^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?$')
if (-not $versionMatch.Success) {
    throw "Version must follow SemVer format, such as 0.2.0 or 1.0.0-beta.1."
}

if ($Notes.Count -eq 1 -and $Notes[0] -match ',') {
    $Notes = $Notes[0].Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}

$assemblyVersion = '{0}.{1}.{2}.0' -f $versionMatch.Groups[1].Value, $versionMatch.Groups[2].Value, $versionMatch.Groups[3].Value

Set-Content -LiteralPath $versionPath -Value $Version -Encoding UTF8

$props = Get-Content -LiteralPath $propsPath -Raw -Encoding UTF8
$props = [regex]::Replace($props, '(<PulseBriefVersion>).*?(</PulseBriefVersion>)', "`${1}$Version`${2}")
$props = [regex]::Replace($props, '(<AssemblyVersion>).*?(</AssemblyVersion>)', "`${1}$assemblyVersion`${2}")
$props = [regex]::Replace($props, '(<FileVersion>).*?(</FileVersion>)', "`${1}$assemblyVersion`${2}")
Set-Content -LiteralPath $propsPath -Value $props -Encoding UTF8

$date = Get-Date -Format 'yyyy-MM-dd'
$lines = if ($Notes.Count -gt 0) {
    $Notes | ForEach-Object { "- $_" }
} else {
    @("- 릴리스 내용을 작성하세요.")
}

$entry = "## [$Version] - $date`r`n`r`n$($lines -join "`r`n")`r`n"
$changelog = Get-Content -LiteralPath $changelogPath -Raw -Encoding UTF8
if ($changelog -notmatch [regex]::Escape("## [$Version]")) {
    $changelog = $changelog -replace "# Changelog\r?\n", "# Changelog`r`n`r`n$entry`r`n"
    Set-Content -LiteralPath $changelogPath -Value $changelog -Encoding UTF8
}

if ($Tag) {
    Write-Warning "The -Tag option does not create a tag here because release files must be committed first."
    Write-Warning "After committing and deploying this version, run: powershell -ExecutionPolicy Bypass -File .\tools\tag-release.ps1 -Version $Version"
}

Write-Host "Pulse Brief version updated to $Version"
