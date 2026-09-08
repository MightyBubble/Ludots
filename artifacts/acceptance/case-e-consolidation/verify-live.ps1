param([string]$BridgeUrl = 'http://127.0.0.1:47922')
$ErrorActionPreference = 'Stop'
$records = [System.Collections.Generic.List[object]]::new()
function Rpc([string]$method, [hashtable]$parameters) {
    $body = @{ jsonrpc = '2.0'; id = $records.Count + 1; method = $method; params = $parameters } | ConvertTo-Json -Depth 10
    $response = Invoke-RestMethod "$BridgeUrl/rpc" -Method Post -ContentType application/json -Body $body -TimeoutSec 10
    $records.Add(@{ method = $method; params = $parameters; response = $response })
    if ($response.error) { throw ($response.error | ConvertTo-Json -Depth 10) }
    return $response.result
}
function Text($node) {
    if ($node.text) { $node.text }
    foreach ($child in $node.children) { Text $child }
}
try {
    $before = Invoke-RestMethod "$BridgeUrl/health"
    Start-Sleep -Milliseconds 200
    $after = Invoke-RestMethod "$BridgeUrl/health"
    if (!$after.ok -or $after.pumpCount -le $before.pumpCount) { throw 'Game pump is not advancing.' }
    if ($after.instance.mapId -ne 'case_e_selection_10k_field') { throw 'Wrong map.' }
    $records.Add(@{ healthBefore = $before; healthAfter = $after })
    Rpc 'ludots.session.info' @{} | Out-Null
    foreach ($player in @(1, 2, 1)) {
        $clicked = Rpc 'ludots.ui.click' @{ elementId = "case-e-10k-player-$player" }
        if (!$clicked.handled) { throw 'Player button did not handle the click.' }
        Rpc 'ludots.camera.control' @{ action = 'set'; distanceCm = 60000 } | Out-Null
        Start-Sleep -Milliseconds 200
        Rpc 'ludots.camera.control' @{ action = 'get' } | Out-Null
        Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 100; y = 300 } | Out-Null
        Rpc 'ludots.input.raw' @{ op = 'pointerDown'; button = 'left' } | Out-Null
        Start-Sleep -Milliseconds 100
        Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 1450; y = 850 } | Out-Null
        Start-Sleep -Milliseconds 100
        Rpc 'ludots.input.raw' @{ op = 'pointerUp'; button = 'left'; x = 1450; y = 850 } | Out-Null
        Start-Sleep -Milliseconds 200
        $ui = Rpc 'ludots.ui.tree' @{}
        $label = (Text $ui.tree) -join "`n"
        if ($label -notmatch '\u5df2\u9009\uff1a\s*([0-9,]+)') { throw 'Selection count is missing.' }
        $selected = [int]$Matches[1].Replace(',', '')
        if ($selected -le 0) { throw "Player $player selected no units." }
        $records.Add(@{ player = $player; selected = $selected; uiText = $label })
        $shot = Rpc 'ludots.screenshot' @{ name = "consolidation-player-$player" }
        Copy-Item -LiteralPath $shot.path -Destination (Join-Path $PSScriptRoot "player-$player.png")
        "player=$player selected=$selected"
    }
    Rpc 'ludots.logs.tail' @{ minLevel = 'Error'; count = 100 } | ConvertTo-Json -Depth 10
} finally {
    Rpc 'ludots.input.raw' @{ op = 'releaseAll' } | Out-Null
    $records | ForEach-Object { $_ | ConvertTo-Json -Depth 35 -Compress } | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'trace.jsonl')
}
