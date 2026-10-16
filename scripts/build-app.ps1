# Builds the Flutter shell together with the sidecar it spawns, and stages one into the other.
#
# The font step is not cosmetic: Flutter keeps its tree-shaken icon font in build\flutter_assets
# and does not notice when the set of Icons.* used in the app changes, so an incremental build
# ships the previous glyph list and buttons come out with blank icons. Deleting it forces a
# regeneration without paying for a full "flutter clean".
#
# Keep this file ASCII only: Windows PowerShell reads a BOM-less .ps1 as ANSI, so any non-ASCII
# character in a string breaks the parse.

param(
    [switch]$SkipHost
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'app'
$hostOut = Join-Path $root 'src\Akashic.Host\bin\Release'
$release = Join-Path $app 'build\windows\x64\runner\Release'

# The WebView2 plugin restores its NuGet package at build time, and the repo carries the
# client so a clean machine needs no global install.
$env:PATH = "$(Join-Path $root 'tools');$env:PATH"

if (-not $SkipHost) {
    Write-Host 'building Akashic.Host...'
    dotnet build (Join-Path $root 'src\Akashic.Host\Akashic.Host.csproj') -c Release -v q --nologo | Out-Host

    # A native command's exit code does not raise, so without this check a failed C# build
    # falls through to flutter and stages the PREVIOUS sidecar: the app then runs old code
    # while looking like it was just rebuilt.
    if ($LASTEXITCODE -ne 0) { throw ('Akashic.Host build failed with exit code ' + $LASTEXITCODE) }
}

# A running exe locks its own output, and the build then fails with MSB3027 rather than a
# clear message, so stop anything that is holding the directory.
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
        Copy-Item -Path (Join-Path $built.DirectoryName '*') -Destination $staged -Force
        Write-Host ("staged sidecar into " + $staged)
    }
}
finally {
    Pop-Location
}

Write-Host ("built: " + (Join-Path $release 'akashic_ui.exe'))
