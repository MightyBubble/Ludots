param([ValidateSet('inspect','drag','switch','return')][string]$Phase = 'inspect')
$ErrorActionPreference = 'Stop'
$evidence = $PSScriptRoot
$records = [System.Collections.Generic.List[object]]::new()
function Rpc([string]$method, [hashtable]$parameters) {
    $body = @{ jsonrpc = '2.0'; id = $records.Count + 1; method = $method; params = $parameters } | ConvertTo-Json -Depth 10
    $response = Invoke-RestMethod http://127.0.0.1:47921/rpc -Method Post -ContentType application/json -Body $body
    $records.Add(@{ utc = [DateTime]::UtcNow.ToString('O'); method = $method; params = $parameters; response = $response })
    if ($response.error) { throw ($response.error | ConvertTo-Json -Depth 10) }
    return $response.result
}
function Shot([string]$name) {
    $result = Rpc 'ludots.screenshot' @{ name = $name }
    Copy-Item -LiteralPath $result.path -Destination (Join-Path $evidence "$name.png")
}
function UiText($node) {
    if ($node.text) { $node.text }
    foreach ($child in $node.children) { UiText $child }
}
try {
    $first = Invoke-RestMethod http://127.0.0.1:47921/health
    Start-Sleep -Milliseconds 150
    $second = Invoke-RestMethod http://127.0.0.1:47921/health
    if (!$second.ok -or $second.pumpCount -le $first.pumpCount) { throw 'Game pump is not advancing.' }
    $records.Add(@{ healthBefore = $first; healthAfter = $second })
    Rpc 'ludots.session.info' @{} | ConvertTo-Json -Depth 8
    if ($Phase -eq 'inspect') {
        Rpc 'ludots.input.raw' @{ op = 'scroll'; x = 800; y = 500; deltaY = -40 } | Out-Null
        Start-Sleep -Milliseconds 300
        Rpc 'ludots.camera.control' @{ action = 'get' } | ConvertTo-Json
    }
    if ($Phase -eq 'drag') {
        Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 150; y = 270 } | Out-Null
        Start-Sleep -Milliseconds 100
        Rpc 'ludots.input.raw' @{ op = 'pointerDown'; button = 'left'; x = 150; y = 270 } | Out-Null
        Start-Sleep -Milliseconds 150
        Rpc 'ludots.input.raw' @{ op = 'pointerMove'; x = 1450; y = 850 } | Out-Null
        Start-Sleep -Milliseconds 300
        Shot 'preview'
        Rpc 'ludots.input.raw' @{ op = 'pointerUp'; button = 'left'; x = 1450; y = 850 } | Out-Null
        Start-Sleep -Milliseconds 300
    }
    if ($Phase -eq 'switch') { Rpc 'ludots.ui.click' @{ elementId = 'case-e-10k-player-2' } | Out-Null }
    if ($Phase -eq 'return') { Rpc 'ludots.ui.click' @{ elementId = 'case-e-10k-player-1' } | Out-Null }
    Start-Sleep -Milliseconds 150
    $ui = Rpc 'ludots.ui.tree' @{}
    UiText $ui.tree
    Shot $Phase
} finally {
    if ($Phase -eq 'drag') { Rpc 'ludots.input.raw' @{ op = 'releaseAll' } | Out-Null }
    $records | ForEach-Object { $_ | ConvertTo-Json -Depth 30 -Compress } | Set-Content -Encoding utf8 (Join-Path $evidence "$Phase.jsonl")
}
