param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [string]$KeyPath = 'E:\Akashic\backups\Akashic-UpdateSigning\update-signing-key.pem'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Zip)) { throw ("zip not found: " + $Zip) }
if (-not (Test-Path $KeyPath)) { throw ("signing key not found: " + $KeyPath) }

$openssl = 'C:\Program Files\Git\usr\bin\openssl.exe'
if (-not (Test-Path $openssl)) {
    $cmd = Get-Command openssl -ErrorAction SilentlyContinue
    if ($cmd) { $openssl = $cmd.Source } else { throw 'openssl not found (Git for Windows bundles it)' }
}

$zipFull = (Resolve-Path $Zip).Path
$sigPath = $zipFull + '.sig'
$hashPath = $zipFull + '.sha256'

$derPath = $zipFull + '.sig.der'
& $openssl dgst -sha256 -sign $KeyPath -out $derPath $zipFull
if ($LASTEXITCODE -ne 0) { throw ('openssl sign failed: ' + $LASTEXITCODE) }
Set-Content -Path $sigPath -Value ([Convert]::ToBase64String([IO.File]::ReadAllBytes($derPath))) -Encoding ascii -NoNewline
Remove-Item $derPath -Force

$hash = (Get-FileHash -Path $zipFull -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $hashPath -Value $hash -Encoding ascii -NoNewline

Write-Output ('sha256 ' + $hash)
Write-Output ('sig    ' + $sigPath)
Write-Output 'upload all three files to the GitHub release'
