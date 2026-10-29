param(
    [switch]$SkipDotnet,
    [switch]$SkipFlutter
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $SkipDotnet) {
    Write-Host 'dotnet test...'
    dotnet test (Join-Path $root 'tests\Akashic.Tests\Akashic.Tests.csproj') --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw ('dotnet test failed with exit code ' + $LASTEXITCODE) }
}

if (-not $SkipFlutter) {
    Write-Host 'flutter test...'
    Push-Location (Join-Path $root 'app')
    try {
        flutter test | Out-Host
        if ($LASTEXITCODE -ne 0) { throw ('flutter test failed with exit code ' + $LASTEXITCODE) }
    }
    finally {
        Pop-Location
    }
}

Write-Host 'all tests passed'
