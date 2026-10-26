param(
    [switch]$SkipHost
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'app'
$hostOut = Join-Path $root 'src\Akashic.Host\bin\Release'
$release = Join-Path $app 'build\windows\x64\runner\Release'

$env:PATH = "$(Join-Path $root 'tools');$env:PATH"

if (-not $SkipHost) {
    Write-Host 'building Akashic.Host...'
    dotnet build (Join-Path $root 'src\Akashic.Host\Akashic.Host.csproj') -c Release -v q --nologo | Out-Host

    if ($LASTEXITCODE -ne 0) { throw ('Akashic.Host build failed with exit code ' + $LASTEXITCODE) }
}

Get-Process akashic_ui, Akashic.Host -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Warning ("stopping {0} (pid {1}) - it locks the build output" -f $_.ProcessName, $_.Id)
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 800

Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $app 'build\flutter_assets\fonts\MaterialIcons-Regular.otf')
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $release 'data\flutter_assets\fonts\MaterialIcons-Regular.otf')

Push-Location $app
try {
    flutter build windows --release | Out-Host
    if ($LASTEXITCODE -ne 0) { throw ('flutter build failed with exit code ' + $LASTEXITCODE) }

    if (-not $SkipHost) {
        $staged = Join-Path $release 'host'
        New-Item -ItemType Directory -Force -Path $staged | Out-Null
        $built = Get-ChildItem -Path $hostOut -Recurse -Filter 'Akashic.Host.exe' |
            Select-Object -First 1
        if (-not $built) { throw ("Akashic.Host.exe not found under " + $hostOut) }
        Copy-Item -Path (Join-Path $built.DirectoryName '*') -Destination $staged -Recurse -Force
        Write-Host ("staged sidecar into " + $staged)
    }
}
finally {
    Pop-Location
}

Write-Host ("built: " + (Join-Path $release 'akashic_ui.exe'))
