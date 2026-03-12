## Summary
- add explicit `Navigation2D.Spatial` config wired through the shared `game.json -> ConfigPipeline -> GameConfig.Navigation2D -> Navigation2DRuntime` path
- switch `Nav2DCellMap` steady-state churn handling to an adaptive rebuild strategy, while keeping the incremental path as an explicit mode
- keep the existing steering SoA / ORCA / Sonar / Hybrid runtime intact and remove the cell-migration hitch source instead of papering over it
- wire `Navigation2DPlaygroundMod` to a config-driven `5000/team` acceptance crowd and expose spatial rebuild counters on the HUD
- extend Navigation2D tests for spatial config parsing and rebuild-mode correctness, and keep 10k benchmark coverage

## Validation
- `dotnet build src/Tests/Navigation2DTests/Navigation2DTests.csproj -c Debug /nodeReuse:false`
- `dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj --no-build --filter "FullyQualifiedName!~Navigation2DBenchmarkTests" --logger "console;verbosity=minimal"`
- `dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj --no-build --filter "Name=Benchmark_Navigation2DSteering_StaticCrowd_10kAgents_ORCA" --logger "console;verbosity=normal"`
- `dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj --no-build --filter "Name=Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_ORCA" --logger "console;verbosity=normal"`
- `dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj --no-build --filter "Name=Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_Hybrid" --logger "console;verbosity=normal"`
- `dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj --no-build --filter "Name=Benchmark_Navigation2DSteering_QuarterCrossCellMigration_10kAgents_Sonar" --logger "console;verbosity=normal"`
- `dotnet build src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release /nodeReuse:false`

## Key Results
- ORCA `QuarterCrossCellMigration` / `10k` agents: `434.9739ms` investigation baseline -> `11.7765ms`
- Hybrid `QuarterCrossCellMigration` / `10k` agents: `10.7081ms`
- Sonar `QuarterCrossCellMigration` / `10k` agents: `11.2656ms`
- ORCA `StaticCrowd` / `10k` agents: `9.5508ms`
- `Nav2DCellMap.UpdatePositions` under churn: `0.2212ms - 0.2523ms`
- measured-thread allocations for the validated runs: `0B`

## Notes
- this PR stays scoped to Navigation2D-related files and the dedicated playable playground entry
- the large hitch was in the spatial index probe path after repeated deletions, not in ORCA math itself
- acceptance artifacts live under `artifacts/acceptance/navigation2d-spatial-adaptive-rebuild/`
