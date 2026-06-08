[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:8085",
    [string]$AdminToken,
    [int]$TimeoutSeconds = 15,
    [switch]$AllowLoopbackAdmin
)

$ErrorActionPreference = "Stop"

function Join-Url {
    param(
        [string]$Root,
        [string]$Path
    )

    return "$($Root.TrimEnd('/'))/$($Path.TrimStart('/'))"
}

function Invoke-Status {
    param(
        [string]$Uri,
        [hashtable]$Headers
    )

    try {
        $response = Invoke-WebRequest -Uri $Uri -Method Get -Headers $Headers -TimeoutSec $TimeoutSeconds -UseBasicParsing
        return [int]$response.StatusCode
    }
    catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            return [int]$_.Exception.Response.StatusCode
        }

        throw
    }
}

function Assert-Status {
    param(
        [string]$Name,
        [int]$Actual,
        [int[]]$Expected
    )

    if ($Expected -notcontains $Actual) {
        throw "$Name failed. Expected status $($Expected -join ', '), got $Actual."
    }

    Write-Host "[OK] $Name ($Actual)"
}

$headers = @{}
$rootStatus = Invoke-Status -Uri (Join-Url $BaseUrl "/") -Headers $headers
Assert-Status -Name "Static site" -Actual $rootStatus -Expected @(200)

$healthUri = Join-Url $BaseUrl "/api/health"
$healthResponse = Invoke-WebRequest -Uri $healthUri -Method Get -TimeoutSec $TimeoutSeconds -UseBasicParsing
Assert-Status -Name "Health API" -Actual ([int]$healthResponse.StatusCode) -Expected @(200)

$health = $healthResponse.Content | ConvertFrom-Json
if (-not $health.ok) {
    throw "Health API returned ok=false."
}

Write-Host "[OK] Database provider: $($health.database)"
Write-Host "[OK] RSS feed count: $($health.rssFeedCount)"

$adminStatus = Invoke-Status -Uri (Join-Url $BaseUrl "/api/articles") -Headers $headers
if ($AllowLoopbackAdmin) {
    Assert-Status -Name "Admin API without token" -Actual $adminStatus -Expected @(200, 401)
}
else {
    Assert-Status -Name "Admin API requires token" -Actual $adminStatus -Expected @(401)
}

if (-not [string]::IsNullOrWhiteSpace($AdminToken)) {
    $adminHeaders = @{ "X-Admin-Token" = $AdminToken }
    $authorizedStatus = Invoke-Status -Uri (Join-Url $BaseUrl "/api/articles") -Headers $adminHeaders
    Assert-Status -Name "Admin API with token" -Actual $authorizedStatus -Expected @(200)
}
else {
    Write-Host "[INFO] Admin token was not provided. Authenticated admin API check was skipped."
}

Write-Host "Deployment check completed."
