param(
    [string]$Project = 'src/Tests/Navigation2DTests/Navigation2DTests.csproj',
    [string]$Configuration = 'Release',
    [double]$StaticHybridMaxMs = 6.0,
    [double]$QuarterHybridMaxMs = 5.8,
    [double]$StaticHybridMinCacheHitRatePercent = 80.0,
    [double]$QuarterHybridMinCacheHitRatePercent = 3.0,
    [switch]$Restore
)

$targets = @(
    [pscustomobject]@{
        Test = 'Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_Hybrid'
        MaxMs = $StaticHybridMaxMs
        MinCacheHitRatePercent = $StaticHybridMinCacheHitRatePercent
    },
    [pscustomobject]@{
        Test = 'Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_Hybrid'
        MaxMs = $QuarterHybridMaxMs
        MinCacheHitRatePercent = $QuarterHybridMinCacheHitRatePercent
    }
)

$failures = [System.Collections.Generic.List[string]]::new()
$results = [System.Collections.Generic.List[object]]::new()

foreach ($target in $targets)
{
    Write-Host "=== $($target.Test) ===" -ForegroundColor Cyan

    $arguments = @('test', $Project, '-c', $Configuration, '--filter', $target.Test, '--logger', 'console;verbosity=detailed')
    if (-not $Restore)
    {
        $arguments += '--no-restore'
    }

    $output = & dotnet @arguments 2>&1
    $output | Write-Host
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    $joined = $output -join [Environment]::NewLine
    $medianMatch = [regex]::Match($joined, 'Median Avg Steering Tick:\s*([0-9.]+)ms')
    $cacheRateMatch = [regex]::Match($joined, 'Steering Cache Hit Rate:\s*([0-9.]+)\s*%')
    if (-not $medianMatch.Success)
    {
        $failures.Add("$($target.Test): missing Median Avg Steering Tick")
        continue
    }
    if (-not $cacheRateMatch.Success)
    {
        $failures.Add("$($target.Test): missing Steering Cache Hit Rate")
        continue
    }

    $medianMs = [double]$medianMatch.Groups[1].Value
    $cacheRate = [double]$cacheRateMatch.Groups[1].Value
    $results.Add([pscustomobject]@{
        Test = $target.Test
        MedianMs = $medianMs
        MaxMs = $target.MaxMs
        CacheHitRatePercent = $cacheRate
        MinCacheHitRatePercent = $target.MinCacheHitRatePercent
    })

    if ($medianMs -gt $target.MaxMs)
    {
        $failures.Add("$($target.Test): median ${medianMs}ms > threshold ${($target.MaxMs)}ms")
    }

    if ($cacheRate -lt $target.MinCacheHitRatePercent)
    {
        $failures.Add("$($target.Test): cache hit rate ${cacheRate}% < threshold ${($target.MinCacheHitRatePercent)}%")
    }
}

Write-Host ''
$results | Format-Table -AutoSize

if ($failures.Count -gt 0)
{
    Write-Error ($failures -join [Environment]::NewLine)
    exit 1
}

Write-Host 'Navigation2D performance gate passed.' -ForegroundColor Green
