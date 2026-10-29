param(
    [string]$HostExe = '',
    [string]$Baseline = '',
    [string[]]$Packages = @('SUIMONO', 'Nijiiro', 'ABT', 'MiniSimplisticPens', 'AudioHairClip', 'Starlight of Irua'),
    [int]$MaxPrefabs = 3,
    [int]$MaxMaterials = 6,
    [switch]$Update,
    [switch]$SkipBalls
)

# ASCII-only output: PowerShell 5.1 reads BOM-less files as the system ANSI codepage,
# and a UTF-8 CJK trail byte can swallow a following quote and break parsing.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($HostExe)) {
    $HostExe = Join-Path $repo 'src\Akashic.Host\bin\Release\net8.0-windows10.0.26100.0\Akashic.Host.exe'
}
if ([string]::IsNullOrWhiteSpace($Baseline)) {
    $Baseline = Join-Path $repo 'docs\preview-regression-baseline.json'
}
if (-not (Test-Path $HostExe)) { throw "sidecar not found: $HostExe (run scripts/build-app.ps1 first)" }

Write-Output "host: $HostExe"

# stdout goes to a file, not a pipe: the sidecar logs to stdout, and a pipe nobody
# drains fills up and blocks the host mid-request.
$outFile = Join-Path $env:TEMP ('akashic-host-out-' + [guid]::NewGuid().ToString('N') + '.log')
$errFile = Join-Path $env:TEMP ('akashic-host-err-' + [guid]::NewGuid().ToString('N') + '.log')
$process = Start-Process -FilePath $HostExe -ArgumentList '--port 0' -NoNewWindow -PassThru `
    -RedirectStandardOutput $outFile -RedirectStandardError $errFile

$handshake = $null
$deadline = (Get-Date).AddSeconds(90)
while ($null -eq $handshake -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $lines = @(Get-Content $outFile -ErrorAction SilentlyContinue)
    foreach ($line in $lines) {
        if (-not $line.StartsWith('{')) { continue }
        $parsed = $line | ConvertFrom-Json
        if ($parsed.kind -eq 'akashic-host') { $handshake = $parsed; break }
    }
}
if ($null -eq $handshake) {
    try { $process.Kill() } catch { }
    throw 'sidecar printed no handshake line'
}

$base = $handshake.url.TrimEnd('/')
$token = $handshake.token
Write-Output "sidecar: $base"

function Get-Metric($bag, $name) {
    if ($null -eq $bag -or [string]::IsNullOrWhiteSpace([string]$name)) { return 0 }
    if ($bag -is [System.Collections.IDictionary]) {
        if ($bag.Contains($name)) { return [int]$bag[$name] }
        return 0
    }
    $property = $bag.PSObject.Properties[$name]
    if ($null -ne $property) { return [int]$property.Value }
    return 0
}

function Header($response, $name) {
    if ($response.Headers.ContainsKey($name)) { return [string]$response.Headers[$name] }
    return ''
}

function Error-Text($errorRecord) {
    try {
        $response = $errorRecord.Exception.Response
        if ($null -eq $response) { return $errorRecord.Exception.Message }
        $stream = $response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        $body = $reader.ReadToEnd()
        if ($body.Length -gt 160) { $body = $body.Substring(0, 160) }
        return ('{0} {1}' -f [int]$response.StatusCode, $body)
    } catch {
        return $errorRecord.Exception.Message
    }
}

function Invoke-Api($path, $method = 'GET', $body = $null) {
    $headers = @{ 'X-Akashic-Token' = $token }
    if ($null -ne $body) {
        # PS 5.1 encodes a string -Body as ASCII, which turns Chinese resource keys into
        # '?' and makes the sidecar report "resource not in catalog". Send UTF-8 bytes.
        $json = $body | ConvertTo-Json -Depth 8
        return Invoke-WebRequest -Uri "$base$path" -Method $method -ContentType 'application/json; charset=utf-8' `
            -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -UseBasicParsing
    }
    return Invoke-WebRequest -Uri "$base$path" -Method $method -Headers $headers -UseBasicParsing
}

$metrics = [ordered]@{
    packages      = 0
    prefabBuilds  = 0
    buildFail     = 0
    renderable    = 0
    zeroFallback  = 0
    draws         = 0
    fallbackMats  = 0
    reject        = @{}
    notPreviewable = 0
    ballOk        = 0
    ballFail      = 0
    buildMsTotal  = 0
    renderMsTotal = 0
    textureMbMax  = 0
    textureCacheMbMax = 0
}

$rows = @((Invoke-Api '/api/resources').Content | ConvertFrom-Json | ForEach-Object { $_ })
$flat = @()
foreach ($row in $rows) {
    $flat += $row
    foreach ($child in @($row.children)) {
        if ($null -ne $child) { $flat += $child }
    }
}

foreach ($name in $Packages) {
    $picked = @($flat | Where-Object { $_.name -like "*$name*" -and $_.kind -eq 'unitypackage' } |
        Select-Object -First 1)
    if ($picked.Count -eq 0) { Write-Output "skip $name : not in the library"; continue }
    $row = $picked[0]
    $metrics.packages++
    Write-Output "package: $($row.name)"

    $listPath = '/api/preview/list?resourceKey=' + [uri]::EscapeDataString($row.key)
    $assets = @((Invoke-Api $listPath).Content | ConvertFrom-Json | ForEach-Object { $_ })
    $prefabs = @($assets | Where-Object { $_.kind -eq 'prefab' } | Select-Object -First $MaxPrefabs)
    $materials = @($assets | Where-Object { $_.kind -eq 'material' } | Select-Object -First $MaxMaterials)

    foreach ($prefab in $prefabs) {
        $metrics.prefabBuilds++
        $buildWatch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $body = [ordered]@{ resourceKey = $row.key; prefabPath = $prefab.path; entry = $row.archiveEntry }
            $build = (Invoke-Api '/api/preview/build' POST $body).Content | ConvertFrom-Json
        } catch {
            $metrics.buildFail++
            Write-Output ("  build FAIL {0} :: {1}" -f $prefab.path, (Error-Text $_))
            continue
        }
        $buildWatch.Stop()
        $metrics.buildMsTotal += [math]::Round($buildWatch.Elapsed.TotalMilliseconds)

        try {
            $render = Invoke-Api '/api/preview/render' POST ([ordered]@{
                sessionId = $build.sessionId
                width     = 700
                height    = 700
                view      = 'orbit'
                mode      = 'textured'
                raw       = $false
            })
        } catch {
            $detail = Error-Text $_
            if ($detail.StartsWith('422')) {
                $metrics.notPreviewable++
                Write-Output ("  no mesh {0}" -f $prefab.path)
            } else {
                $metrics.buildFail++
                Write-Output ("  render FAIL {0} :: {1}" -f $prefab.path, $detail)
            }
            continue
        }

        $fallbacks = [int](Header $render 'X-Akashic-Fallbacks')
        $draws = [int](Header $render 'X-Akashic-Draws')
        $renderMs = [double](Header $render 'X-Akashic-Render-Ms')
        $textures = Header $render 'X-Akashic-Textures'
        $parts = @($textures.Split('|'))
        if ($parts.Count -eq 3 -and [int]$parts[1] -gt $metrics.textureMbMax) { $metrics.textureMbMax = [int]$parts[1] }
        $cacheMb = [int](Header $render 'X-Akashic-Texture-Cache')
        if ($cacheMb -gt $metrics.textureCacheMbMax) { $metrics.textureCacheMbMax = $cacheMb }
        $process.Refresh()
        $hostMb = [math]::Round($process.PrivateMemorySize64 / 1MB)

        $metrics.draws += $draws
        $metrics.fallbackMats += $fallbacks
        $metrics.renderMsTotal += [math]::Round($renderMs)
        if ($fallbacks -eq 0) { $metrics.zeroFallback++ }
        $metrics.renderable++

        foreach ($key in @($render.Headers.Keys)) {
            if ($key -like 'X-Akashic-Reject-*') {
                $kind = $key.Replace('X-Akashic-Reject-', '')
                $metrics.reject[$kind] = $metrics.reject[$kind] + [int]$render.Headers[$key]
            }
        }

        Write-Output ("  {0,-30} draws={1,-4} fb={2,-3} render={3} ms  textures={4} cache={5} MB host={6} MB" -f $prefab.name, $draws,
            $fallbacks, [math]::Round($renderMs), $textures, $cacheMb, $hostMb)
    }

    if (-not $SkipBalls) {
        foreach ($material in $materials) {
            $query = '/preview/{0}/material?k={1}&p={2}&size=256' -f $token,
                [uri]::EscapeDataString($row.key), [uri]::EscapeDataString($material.path)
            try {
                $ball = Invoke-WebRequest -Uri "$base$query" -UseBasicParsing
                if ($ball.StatusCode -eq 200) { $metrics.ballOk++ } else { $metrics.ballFail++ }
            } catch {
                $metrics.ballFail++
            }
        }
        Write-Output ("  balls ok={0} fail={1}" -f $metrics.ballOk, $metrics.ballFail)
    }
}

Write-Output ($metrics | ConvertTo-Json -Depth 6)

if ($Update -or -not (Test-Path $Baseline)) {
    $json = [ordered]@{
        generated = (Get-Date).ToString('yyyy-MM-dd HH:mm')
        host      = $HostExe
        packages  = $Packages
        metrics   = $metrics
    } | ConvertTo-Json -Depth 8
    Set-Content -Path $Baseline -Value $json -Encoding UTF8
    Write-Output "baseline written: $Baseline"
} else {
    $recorded = Get-Content $Baseline -Raw | ConvertFrom-Json
    $expected = $recorded.metrics
    $sameList = (@($recorded.packages | ForEach-Object { $_ }) -join '|') -eq ($Packages -join '|')

    if (-not $sameList) {
        $covers = @($recorded.packages | ForEach-Object { $_ }) -join ', '
        Write-Output ("baseline covers: " + $covers)
        Write-Output "package list differs - metrics reported, not compared"
    } else {
        $drift = @()
        foreach ($field in @('renderable', 'zeroFallback')) {
            if ($metrics.$field -lt $expected.$field) {
                $drift += ('{0} dropped from {1} to {2}' -f $field, $expected.$field, $metrics.$field)
            }
        }
        foreach ($field in @('buildFail', 'fallbackMats')) {
            if ($metrics.$field -gt $expected.$field) {
                $drift += ('{0} grew from {1} to {2}' -f $field, $expected.$field, $metrics.$field)
            }
        }
        foreach ($kind in @($expected.reject.PSObject.Properties.Name | Where-Object { $_ })) {
            $before = Get-Metric $expected.reject $kind
            $after = Get-Metric $metrics.reject $kind
            if ($after -gt $before) { $drift += ('reject/{0} grew from {1} to {2}' -f $kind, $before, $after) }
        }
        foreach ($kind in @($metrics.reject.Keys | Where-Object { $_ })) {
            if ((Get-Metric $expected.reject $kind) -eq 0) {
                $drift += ('reject/{0} appeared with {1}' -f $kind, $metrics.reject[$kind])
            }
        }
        if ($drift.Count -gt 0) {
            $drift | ForEach-Object { Write-Output "regression: $_" }
            throw 'preview metrics got worse'
        }
        Write-Output "metrics hold against the baseline"
    }
}

try { $process.Kill() } catch { }
Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
Write-Output 'done'
