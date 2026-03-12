param(
    [string]$Project = 'src/Tests/Navigation2DTests/Navigation2DTests.csproj',
    [string]$Configuration = 'Release',
    [switch]$Restore
)

$tests = @(
    'Benchmark_Navigation2DFlow_LargeSparseWorldBudgetedPropagation',
    'Benchmark_Navigation2DFlow_CorridorIncrementalActivation'
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
    $medianMatch = [regex]::Match($joined, 'Median Avg Flow Tick:\s*([0-9.]+)ms')
    $frontierMatch = [regex]::Match($joined, 'Frontier Processed/Tick:\s*([0-9.]+)')
    $windowChecksMatch = [regex]::Match($joined, 'Window Tile Checks/Tick:\s*([0-9.]+)')
    $selectedMatch = [regex]::Match($joined, 'Selected Tiles/Tick:\s*([0-9.]+)')
    $incrementalMatch = [regex]::Match($joined, 'Incremental Seeds/Tick:\s*([0-9.]+)')
    $rebuildsMatch = [regex]::Match($joined, 'Full Rebuilds/Tick:\s*([0-9.]+)')

    $results.Add([pscustomobject]@{
        Test = $test
        MedianMs = if ($medianMatch.Success) { [double]$medianMatch.Groups[1].Value } else { $null }
        FrontierProcessedPerTick = if ($frontierMatch.Success) { [double]$frontierMatch.Groups[1].Value } else { $null }
        WindowChecksPerTick = if ($windowChecksMatch.Success) { [double]$windowChecksMatch.Groups[1].Value } else { $null }
        SelectedTilesPerTick = if ($selectedMatch.Success) { [double]$selectedMatch.Groups[1].Value } else { $null }
        IncrementalSeedsPerTick = if ($incrementalMatch.Success) { [double]$incrementalMatch.Groups[1].Value } else { $null }
        FullRebuildsPerTick = if ($rebuildsMatch.Success) { [double]$rebuildsMatch.Groups[1].Value } else { $null }
    })
}

Write-Host ''
$results | Format-Table -AutoSize
