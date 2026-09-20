param([string]$BridgeUrl = 'http://127.0.0.1:47921')
$ErrorActionPreference = 'Stop'
$records = [System.Collections.Generic.List[object]]::new()
function Rpc([string]$method, [hashtable]$parameters) {
    $body = @{ jsonrpc = '2.0'; id = $records.Count + 1; method = $method; params = $parameters } | ConvertTo-Json -Depth 10
    $response = Invoke-RestMethod "$BridgeUrl/rpc" -Method Post -ContentType application/json -Body $body -TimeoutSec 15
    $records.Add(@{ utc = [DateTime]::UtcNow.ToString('O'); method = $method; params = $parameters; response = $response })
    if ($response.error) { throw ($response.error | ConvertTo-Json -Depth 10) }
    return $response.result
}
function Shot([string]$name) {
    $result = Rpc 'ludots.screenshot' @{ name = "fast-release-$name" }
    Copy-Item -LiteralPath $result.path -Destination (Join-Path $PSScriptRoot "$name.png")
}
function UiText($node) {
    if ($node.text) { $node.text }
    foreach ($child in $node.children) { UiText $child }
}
function CheckSelection([string]$phase) {
    $ui = Rpc 'ludots.ui.tree' @{}
    $text = (UiText $ui.tree) -join "`n"
    if ($text -notmatch '\u5df2\u9009\uff1a\s*([0-9,]+)') { throw 'Selection count is missing from the panel.' }
    $selected = [int]$Matches[1].Replace(',', '')
    $records.Add(@{ phase = $phase; selectedCount = $selected })
    if ($selected -le 0) { throw "$phase did not select any units." }
    $text
}
function Step([int]$steps) {
    $queued = Rpc 'ludots.time.control' @{ action = 'step'; steps = $steps }
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $time = Rpc 'ludots.time.get' @{}
        if ($time.tick -ge $queued.targetTick) { return }
        Start-Sleep -Milliseconds 25
    }
    throw 'Queued simulation steps did not finish.'
}
function Drag {
    Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 150; y = 270 } | Out-Null
    Rpc 'ludots.input.raw' @{ op = 'pointerDown'; button = 'left' } | Out-Null
    Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 1400; y = 820 } | Out-Null
    Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 1450; y = 850 } | Out-Null
    Rpc 'ludots.input.raw' @{ op = 'pointerUp'; button = 'left' } | Out-Null
}
try {
    $first = Invoke-RestMethod "$BridgeUrl/health"
    Start-Sleep -Milliseconds 150
    $second = Invoke-RestMethod "$BridgeUrl/health"
    if (!$second.ok -or $second.pumpCount -le $first.pumpCount) { throw 'Game pump is not advancing.' }
    if ($second.instance.mapId -ne 'case_e_selection_10k_field') { throw 'Expected the 10k showcase map.' }
    $records.Add(@{ healthBefore = $first; healthAfter = $second })
    Rpc 'ludots.ui.click' @{ elementId = 'case-e-10k-player-1' } | Out-Null
    Start-Sleep -Milliseconds 300
    Rpc 'ludots.session.info' @{} | ConvertTo-Json -Depth 8
    Rpc 'ludots.input.raw' @{ op = 'scroll'; x = 800; y = 500; deltaY = -40 } | Out-Null
    Start-Sleep -Milliseconds 300
    Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 150; y = 270 } | Out-Null
    Rpc 'ludots.input.raw' @{ op = 'pointerDown'; button = 'left' } | Out-Null
    Start-Sleep -Milliseconds 150
    Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 1450; y = 850 } | Out-Null
    Start-Sleep -Milliseconds 150
    Shot 'held-preview'
    Rpc 'ludots.input.raw' @{ op = 'pointerUp'; button = 'left'; x = 1450; y = 850 } | Out-Null
    for ($gesture = 0; $gesture -lt 20; $gesture++) { Drag }
    Start-Sleep -Milliseconds 200
    Shot 'after-20-fast-drags'
    CheckSelection 'after-20-fast-drags'
    Rpc 'ludots.input.state' @{} | ConvertTo-Json -Depth 10
    Rpc 'ludots.time.control' @{ action = 'pause' } | Out-Null
    Drag
    Step 8
    Shot 'after-same-tick-release'
    CheckSelection 'after-same-tick-release'
    Rpc 'ludots.time.control' @{ action = 'resume' } | Out-Null
    $switch = Rpc 'ludots.ui.click' @{ elementId = 'case-e-10k-player-2' }
    if (!$switch.handled) { throw 'Player switch was not handled.' }
    Start-Sleep -Milliseconds 300
    Rpc 'ludots.input.raw' @{ op = 'scroll'; x = 800; y = 500; deltaY = -40 } | Out-Null
    Start-Sleep -Milliseconds 300
    for ($gesture = 0; $gesture -lt 10; $gesture++) { Drag }
    Start-Sleep -Milliseconds 200
    Shot 'player-2-after-10-fast-drags'
    CheckSelection 'player-2-after-10-fast-drags'
    Rpc 'ludots.logs.tail' @{ minLevel = 'Error'; count = 100 } | ConvertTo-Json -Depth 10
} finally {
    Rpc 'ludots.input.raw' @{ op = 'releaseAll' } | Out-Null
    Rpc 'ludots.time.control' @{ action = 'resume' } | Out-Null
    $records | ForEach-Object { $_ | ConvertTo-Json -Depth 35 -Compress } | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'live.jsonl')
}
