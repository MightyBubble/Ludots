param([string]$BridgeUrl = 'http://127.0.0.1:47922')
$ErrorActionPreference = 'Stop'
$records = [System.Collections.Generic.List[object]]::new()
Add-Type -AssemblyName System.Drawing
function BluePixels([string]$name) {
    $bitmap = [System.Drawing.Bitmap]::new((Join-Path $PSScriptRoot "$name.png"))
    $count = 0
    try {
        for ($y = 260; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                if ($pixel.B -gt ($pixel.G + 20) -and $pixel.B -gt ($pixel.R + 40) -and $pixel.G -gt 80) { $count++ }
            }
        }
    } finally { $bitmap.Dispose() }
    return $count
}
function Rpc([string]$method, [hashtable]$parameters) {
    $body = @{ jsonrpc = '2.0'; id = $records.Count + 1; method = $method; params = $parameters } | ConvertTo-Json -Depth 10
    $response = Invoke-RestMethod "$BridgeUrl/rpc" -Method Post -ContentType application/json -Body $body -TimeoutSec 10
    $records.Add(@{ method = $method; params = $parameters; response = $response })
    if ($response.error) { throw ($response.error | ConvertTo-Json -Depth 10) }
    return $response.result
}
function Capture([string]$name) {
    Start-Sleep -Milliseconds 300
    Rpc 'ludots.ui.tree' @{} | Out-Null
    $shot = Rpc 'ludots.screenshot' @{ name = $name }
    Copy-Item -LiteralPath $shot.path -Destination (Join-Path $PSScriptRoot "$name.png")
}
function SelectPlayer([int]$player) {
    $result = Rpc 'ludots.ui.click' @{ elementId = "case-e-10k-player-$player" }
    if (!$result.handled) { throw 'Player button did not handle the click.' }
    Rpc 'ludots.camera.control' @{ action = 'set'; targetXCm = 0; targetYCm = 0; yaw = 0; pitch = 58; distanceCm = 60000 } | Out-Null
}
try {
    $before = Invoke-RestMethod "$BridgeUrl/health"
    Start-Sleep -Milliseconds 200
    $after = Invoke-RestMethod "$BridgeUrl/health"
    if (!$after.ok -or $after.pumpCount -le $before.pumpCount) { throw 'Game pump is not advancing.' }
    if ($after.instance.processPath -notlike '*wt-case-e-consolidation*') { throw 'Wrong verification process.' }
    $records.Add(@{ before = $before; after = $after })
    Rpc 'ludots.session.info' @{} | Out-Null
    SelectPlayer 1
    Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 100; y = 300 } | Out-Null
    Rpc 'ludots.input.raw' @{ op = 'pointerDown'; button = 'left' } | Out-Null
    Start-Sleep -Milliseconds 100
    Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 1450; y = 850 } | Out-Null
    Capture 'visibility-preview'
    Rpc 'ludots.input.raw' @{ op = 'pointerUp'; button = 'left'; x = 1450; y = 850 } | Out-Null
    Capture 'visibility-player-1-selected'
    SelectPlayer 2
    Capture 'visibility-player-2-hidden'
    SelectPlayer 1
    Capture 'visibility-player-1-restored'
    $selected = BluePixels 'visibility-player-1-selected'
    $hidden = BluePixels 'visibility-player-2-hidden'
    $restored = BluePixels 'visibility-player-1-restored'
    $pixels = @{ selectedBluePixels = $selected; hiddenBluePixels = $hidden; restoredBluePixels = $restored }
    $records.Add($pixels)
    $pixels | ConvertTo-Json
    if ($selected -le 0 -or $hidden -ne 0 -or $restored -ne $selected) { throw 'Blue ring screenshot verification failed.' }
    Rpc 'ludots.logs.tail' @{ minLevel = 'Error'; count = 100 } | ConvertTo-Json -Depth 10
} finally {
    Rpc 'ludots.input.raw' @{ op = 'releaseAll' } | Out-Null
    $records | ForEach-Object { $_ | ConvertTo-Json -Depth 35 -Compress } | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'visibility-trace.jsonl')
}
