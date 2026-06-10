param(
    [switch]$Once
)

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$collectorProject = Join-Path $projectPath 'PulseBrief.Collector\PulseBrief.Collector.csproj'
$dotnetArgs = @('run', '--project', $collectorProject, '--')

if ($Once) {
    $dotnetArgs += '--once'
}

Set-Location -LiteralPath $projectPath
& dotnet @dotnetArgs
