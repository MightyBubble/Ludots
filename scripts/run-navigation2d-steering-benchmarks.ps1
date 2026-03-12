param(
    [string]$Project = 'src/Tests/Navigation2DTests/Navigation2DTests.csproj',
    [string]$Configuration = 'Release',
    [switch]$Restore
)

$tests = @(
    'Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_ORCA',
    'Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_Sonar',
    'Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_Hybrid',
    'Benchmark_Navigation2DSteering_OscillatingInCell_10kAgents_ORCA',
    'Benchmark_Navigation2DSteering_OscillatingInCell_10kAgents_Sonar',
    'Benchmark_Navigation2DSteering_OscillatingInCell_10kAgents_Hybrid',
    'Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_ORCA',
    'Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_Sonar',
    'Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_Hybrid'
)

$results = [System.Collections.Generic.List[object]]::new()

foreach ($test in $tests)
{
    Write-Host "=== $test ===" -ForegroundColor Cyan

    $arguments = @('test', $Project, '-c', $Configuration, '--filter', $test, '--logger', 'console;verbosity=detailed')
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
    $cellMapMatch = [regex]::Match($joined, 'CellMap Avg UpdatePositions Tick:\s*([0-9.]+)ms')
    $cacheLookupsMatch = [regex]::Match($joined, 'Steering Cache Lookups/Tick:\s*([0-9.]+)')
    $cacheHitsMatch = [regex]::Match($joined, 'Steering Cache Hits/Tick:\s*([0-9.]+)')
    $cacheRateMatch = [regex]::Match($joined, 'Steering Cache Hit Rate:\s*([0-9.]+)\s*%')

    $results.Add([pscustomobject]@{
        Test = $test
        MedianMs = if ($medianMatch.Success) { [double]$medianMatch.Groups[1].Value } else { $null }
        CellMapMs = if ($cellMapMatch.Success) { [double]$cellMapMatch.Groups[1].Value } else { $null }
        CacheLookupsPerTick = if ($cacheLookupsMatch.Success) { [double]$cacheLookupsMatch.Groups[1].Value } else { $null }
        CacheHitsPerTick = if ($cacheHitsMatch.Success) { [double]$cacheHitsMatch.Groups[1].Value } else { $null }
        CacheHitRatePercent = if ($cacheRateMatch.Success) { [double]$cacheRateMatch.Groups[1].Value } else { $null }
    })
}

Write-Host ''
$results | Format-Table -AutoSize
