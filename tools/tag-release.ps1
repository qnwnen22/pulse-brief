param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [switch]$Push
)

$ErrorActionPreference = 'Stop'

$projectPath = Split-Path -Parent $PSScriptRoot
$versionPath = Join-Path $projectPath 'VERSION'

if (-not $Version) {
    $Version = (Get-Content -LiteralPath $versionPath -Raw -Encoding UTF8).Trim()
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version must follow SemVer format, such as 0.2.0 or 1.0.0-beta.1."
}

$head = (& git -C $projectPath rev-parse HEAD).Trim()
$unstaged = & git -C $projectPath diff --name-only
$staged = & git -C $projectPath diff --cached --name-only

if ($unstaged -or $staged) {
    throw "Tracked changes exist. Commit or discard tracked changes before creating a release tag."
}

$tagName = "v$Version"
$existingTag = (& git -C $projectPath tag --list $tagName).Trim()

if ($existingTag) {
    $tagCommit = (& git -C $projectPath rev-list -n 1 $tagName).Trim()
    if ($tagCommit -eq $head) {
        Write-Host "Release tag $tagName already points to HEAD $head"
    } else {
        throw "Release tag $tagName already exists and points to $tagCommit, not HEAD $head."
    }
} else {
    & git -C $projectPath tag -a $tagName -m "Pulse Brief $tagName"
    Write-Host "Created release tag $tagName at $head"
}

if ($Push) {
    & git -C $projectPath push origin $tagName
    Write-Host "Pushed release tag $tagName"
}
