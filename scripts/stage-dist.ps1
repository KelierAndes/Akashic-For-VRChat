# Replaces dist/Akashic with the Flutter build output and re-zips it with the
# same top-level folder the old archive used.
#
# The *.WebView2 directory is the runtime profile (login state, cache), not an
# artifact, so it must not ship.
#
# Keep this file ASCII only: Windows PowerShell reads a BOM-less .ps1 as ANSI.

$ErrorActionPreference = 'Stop'

$root  = 'E:\Akashic\VPMManager'
$src   = Join-Path $root 'app\build\windows\x64\runner\Release'
$dst   = Join-Path $root 'dist\Akashic'
$zip   = Join-Path $root 'dist\Akashic.zip'

if (-not (Test-Path (Join-Path $src 'akashic_ui.exe'))) {
    throw ("release output not found at " + $src + " - run scripts\build-app.ps1 first")
}

Get-Process akashic_ui, Akashic.Host -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Warning ("stopping {0} (pid {1})" -f $_.ProcessName, $_.Id)
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 800

if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dst | Out-Null

Get-ChildItem $src -Force | Where-Object { $_.Name -notlike '*.WebView2' } | ForEach-Object {
    Copy-Item $_.FullName -Destination $dst -Recurse -Force
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $dst -DestinationPath $zip -CompressionLevel Optimal

$files = Get-ChildItem $dst -Recurse -File
Write-Output ("dist\Akashic : {0} files, {1:N1} MB" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
Write-Output ("dist\Akashic.zip : {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))
Write-Output ("exe: " + (Get-ChildItem $dst -Filter '*.exe' | Select-Object -ExpandProperty Name) + " + host\" + (Get-ChildItem (Join-Path $dst 'host') -Filter '*.exe' | Select-Object -ExpandProperty Name))
