$ErrorActionPreference = 'Stop'

$repo = 'C:\001_AI\_audit_1485_perf'
$app = $repo + '\src\Apps\Raylib\Ludots.App.Raylib\bin\Release\net9.0\Ludots.App.Raylib.dll'
$bootstrap = $repo + '\src\Apps\Raylib\Ludots.App.Raylib\bin\Release\net9.0\launcher.runtime.json'
$output = $repo + '\tmp\perf-matrix'

$env:LUDOTS_AUTO_EXIT_FRAME = '350'
$env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES = '5'
$env:LUDOTS_RAYLIB_TIMING_SYSTEM_BREAKDOWN = '1'
$env:LUDOTS_RAYLIB_LIGHTWEIGHT_DIAGNOSTIC_HUD = '0'

$runs = @(
    @{ Name = '1k-moving-crowded'; Agents = '1000'; Speed = '220'; Density = 'crowded' },
    @{ Name = '1k-moving-sparse'; Agents = '1000'; Speed = '220'; Density = 'sparse' },
    @{ Name = '1k-static-crowded'; Agents = '1000'; Speed = '0'; Density = 'crowded' },
    @{ Name = '1k-static-sparse'; Agents = '1000'; Speed = '0'; Density = 'sparse' },
    @{ Name = '5k-moving-crowded'; Agents = '5000'; Speed = '220'; Density = 'crowded' },
    @{ Name = '5k-moving-sparse'; Agents = '5000'; Speed = '220'; Density = 'sparse' },
    @{ Name = '5k-static-crowded'; Agents = '5000'; Speed = '0'; Density = 'crowded' },
    @{ Name = '5k-static-sparse'; Agents = '5000'; Speed = '0'; Density = 'sparse' },
    @{ Name = '10k-moving-sparse'; Agents = '10000'; Speed = '220'; Density = 'sparse' },
    @{ Name = '10k-static-crowded'; Agents = '10000'; Speed = '0'; Density = 'crowded' },
    @{ Name = '10k-static-sparse'; Agents = '10000'; Speed = '0'; Density = 'sparse' },
    @{ Name = '10k-moving-crowded-no-hud'; Agents = '10000'; Speed = '220'; Density = 'crowded'; Hud = '1' },
    @{ Name = '10k-moving-crowded-no-occlusion'; Agents = '10000'; Speed = '220'; Density = 'crowded'; Occlusion = '1' },
    @{ Name = '10k-moving-crowded-no-animator'; Agents = '10000'; Speed = '220'; Density = 'crowded'; Animator = '1' },
    @{ Name = '10k-moving-crowded-no-massnav'; Agents = '10000'; Speed = '220'; Density = 'crowded'; Massnav = '1' },
    @{ Name = '10k-moving-crowded-no-effect'; Agents = '10000'; Speed = '220'; Density = 'crowded'; Effect = '1' },
    @{ Name = '10k-moving-crowded-no-minimap'; Agents = '10000'; Speed = '220'; Density = 'crowded'; Minimap = '1' }
)

foreach ($run in $runs) {
    $env:LUDOTS_AB_TOTAL_AGENTS = $run.Agents
    $env:LUDOTS_AB_AGENT_SPEED_CM_PER_SEC = $run.Speed
    $env:LUDOTS_AB_DENSITY = $run.Density
    $env:LUDOTS_AB_DISABLE_HUD = if ($run.Hud) { $run.Hud } else { '0' }
    $env:LUDOTS_AB_DISABLE_TERRAIN_HUD_OCCLUSION = if ($run.Occlusion) { $run.Occlusion } else { '0' }
    $env:LUDOTS_AB_DISABLE_ANIMATOR = if ($run.Animator) { $run.Animator } else { '0' }
    $env:LUDOTS_AB_DISABLE_MASSNAV = if ($run.Massnav) { $run.Massnav } else { '0' }
    $env:LUDOTS_AB_DISABLE_CONTINUOUS_EFFECT = if ($run.Effect) { $run.Effect } else { '0' }
    $env:LUDOTS_AB_DISABLE_MINIMAP = if ($run.Minimap) { $run.Minimap } else { '0' }
    $env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = $output + '\' + $run.Name + '.log'
    $stdoutPath = $output + '\' + $run.Name + '.stdout.log'

    [Console]::WriteLine("[RUN] " + $run.Name)
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    [void]$startInfo.ArgumentList.Add($app)
    [void]$startInfo.ArgumentList.Add($bootstrap)
    $startInfo.WorkingDirectory = $repo
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [IO.File]::WriteAllText($stdoutPath, $stdoutTask.Result + $stderrTask.Result)
    if ($process.ExitCode -ne 0) {
        throw "Run '$($run.Name)' failed with exit code $($process.ExitCode)."
    }
    [Console]::WriteLine("[DONE] " + $run.Name)
}
