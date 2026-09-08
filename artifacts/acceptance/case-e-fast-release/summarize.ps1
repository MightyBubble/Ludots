$ErrorActionPreference = 'Stop'
$files = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.jsonl' |
    Where-Object { $_.Name -match '^(release|repeat)-.*-(True|False)\.jsonl$' } |
    Sort-Object Name
$rows = foreach ($file in $files) {
    $index = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        [ordered]@{
            eventId = "$($file.BaseName):$index"
            recordedUtc = $file.LastWriteTimeUtc.ToString('O')
            result = $line | ConvertFrom-Json
        } | ConvertTo-Json -Depth 8 -Compress
        $index++
    }
}
$rows | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'trace.jsonl')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$validation = foreach ($name in @('fast-release-before.log', 'fast-release-regression-complete.log')) {
    $name
    Select-String -LiteralPath (Join-Path $repoRoot $name) -Pattern '^  (失败|已通过) (FastBoxRelease|RepeatedFastGestures|ProductionGraphsAndMaps)|^测试总数:|^     (通过数|失败数):|^总时间:' |
        ForEach-Object { $_.Line }
}
$validation | Set-Content -Encoding utf8 (Join-Path $PSScriptRoot 'validation.txt')
