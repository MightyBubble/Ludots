from pathlib import Path

root = Path(r'C:\001_AI\_audit_1485_perf')

def replace(file, old, new):
    path = root / file
    content = path.read_text(encoding='utf-8-sig')
    assert content.count(old) == 1, (file, old[:80], content.count(old))
    path.write_text(content.replace(old, new), encoding='utf-8')

host = 'src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs'
replace(host, '            int screenWidth = config.WindowWidth', '''            bool auditCapture = Environment.GetEnvironmentVariable("LUDOTS_AB_CAPTURE") == "1";
            if (auditCapture)
            {
                config.WindowWidth = 1280;
                config.WindowHeight = 720;
                config.WindowResizable = false;
                config.WindowStartMaximized = false;
            }
            int screenWidth = config.WindowWidth''')
replace(host, '                Rl.InitWindow(screenWidth, screenHeight, title);', '''                if (auditCapture) Rl.SetConfigFlags(0x80);
                Rl.InitWindow(screenWidth, screenHeight, title);''')
replace(host, '                        float dt = Rl.GetFrameTime();', '                        float dt = auditCapture ? 1f / 60f : Rl.GetFrameTime();')
replace(host, '                while (true)\n', '''                var auditRows = new double[AuditColumnNames.Length][];
                for (int column = 0; column < auditRows.Length; column++) auditRows[column] = new double[autoExitFrame + 1];
                while (true)
''')
replace(host, '                        engine.Tick(dt);', '''                        if (auditCapture && MassNavigationIds.TryGetCurrentNavigationRuntime(engine, out var auditBeforeNav))
                            Array.Clear(auditBeforeNav.AuditFrameTotals);
                        engine.Tick(dt);''')

columns = {
 'frame': 'frameIndex + 1',
 'frame_ms': 'presentationTiming!.LastWallFrameMs',
 'simulation': 'presentationTiming.LastSimulationMs',
 'presentation': 'presentationTiming.LastPresentationMs',
 'behavior': 'presentationTiming.LastPresenterBehaviorMs',
 'transform_sync': 'presentationTiming.LastPresenterEntityTransformSyncMs',
 'emit': 'presentationTiming.LastPresenterEmitMs',
 'hud': 'presentationTiming.LastWorldHudProjectionMs',
 'animator': 'presentationTiming.LastPresenterAnimatorMs',
 'animator_updates': 'presentationTiming.PresenterAnimatorUpdatesLastFrame',
 'height_samples': 'presentationTiming.TerrainHeightSamplesLastFrame',
 'terrain_rays': 'hudProjection?.LastTerrainRaycastCount ?? 0',
 'visible': 'presentationTiming.VisibleEntitiesLastFrame',
 'hud_raw': 'presentationTiming.WorldHudItemsLastProjection',
 'hud_projected': 'presentationTiming.WorldHudProjectedLastFrame',
 'behavior_ticks': 'presentationTiming.PresenterTickDrivenCountLastFrame',
 'minimap_collect': 'presentationTiming.LastPresenterMinimapMarkerMs',
 'minimap_project': 'presentationTiming.LastMinimapProjectionMs',
 'culling': 'presentationTiming.LastCameraCullingMs',
 'gpu_instances': 'primitiveRenderer.LastGpuSkinnedInstances',
 'gpu_build': 'primitiveRenderer.LastGpuSkinnedMatrixBuildMs',
 'gpu_draw_cpu': 'primitiveRenderer.LastGpuSkinnedMeshDrawMs',
 'poses': 'primitiveRenderer.LastGpuSkinnedUniquePoses',
 'pose_build': 'primitiveRenderer.LastGpuSkinnedPoseBuildCpuMs',
 'upload_ms': 'primitiveRenderer.LastGpuSkinnedTextureUploadCpuMs',
 'upload_bytes': 'primitiveRenderer.LastGpuSkinnedTextureUploadBytes',
 'main_draws': 'primitiveRenderer.LastGpuSkinnedBatches',
 'shadow_draws': 'primitiveRenderer.LastGpuSkinnedShadowBatches',
 'shadow_submit': 'primitiveRenderer.LastGpuSkinnedShadowSubmitCpuMs',
 'alloc': 'frameAllocatedBytes', 'gen0': 'frameGen0Collections', 'gen1': 'frameGen1Collections',
 'tick': 'presentationTiming.LastTotalTickMs',
 'post': 'presentationTiming.LastHostPostTickMs',
 'mode3d': 'presentationTiming.LastMode3DMs',
 'overlay': 'presentationTiming.LastScreenOverlayDrawMs',
 'overlay_build': 'presentationTiming.LastScreenOverlayBuildMs',
 'end_draw': 'presentationTiming.LastEndDrawingMs',
}
nav_fields = ['nav_target', 'nav_flow', 'nav_prep', 'nav_steering', 'nav_step', 'nav_hard', 'nav_sync', 'nav_steps', 'neighbor_candidates', 'hard_pairs', 'hard_candidates', 'hard_penetrating', 'moved_agents']
for index, field in enumerate(nav_fields):
    columns[field] = f'auditNav?.AuditFrameTotals[{index}] ?? 0'
assignments = '\n'.join(f'                            auditRows[{i}][frameIndex] = {expr};' for i, expr in enumerate(columns.values()))
replace(host, '                        frameIndex++;', '''                        if (auditCapture)
                        {
                            MassNavigationIds.TryGetCurrentNavigationRuntime(engine, out var auditNav);
''' + assignments + '''
                        }
                        frameIndex++;''')
replace(host, '                }\n            }\n            finally', '''                }
                if (auditCapture)
                {
                    string capturePath = Environment.GetEnvironmentVariable("LUDOTS_AB_CAPTURE_PATH")
                        ?? throw new InvalidOperationException("Audit capture path is required.");
                    using var auditWriter = new StreamWriter(capturePath);
                    auditWriter.WriteLine(string.Join(",", AuditColumnNames));
                    for (int row = 0; row < frameIndex; row++)
                    {
                        for (int column = 0; column < auditRows.Length; column++)
                        {
                            if (column > 0) auditWriter.Write(',');
                            auditWriter.Write(auditRows[column][row].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        auditWriter.WriteLine();
                    }
                }
            }
            finally''')
replace(host, '        private static string BuildTimingDiagnostic(', '        private static readonly string[] AuditColumnNames = { ' + ', '.join('"'+k+'"' for k in columns) + ' };\n\n        private static string BuildTimingDiagnostic(')

runtime = 'src/Core/MassNavigation/Runtime/MassNavigationSimulationRuntime.cs'
replace(runtime, '    public MassNavigationTelemetry Telemetry { get; } = new();', '    public double[] AuditFrameTotals { get; } = new double[13];\n    public MassNavigationTelemetry Telemetry { get; } = new();')
for i, name in enumerate(['GroupTargetUpdate','FlowFieldRebuild','StepPrep','LocalSteering','SimStep','HardResolve','EntitySync']):
    replace(runtime, f'    public void Observe{name}(double sampleMs) => Telemetry.Observe{name}(sampleMs);', f'    public void Observe{name}(double sampleMs) {{ AuditFrameTotals[{i}] += sampleMs; Telemetry.Observe{name}(sampleMs); }}')

step = 'src/Core/MassNavigation/Systems/MassNavigationSimulationStepSystem.cs'
replace(step, '    private readonly bool _auditDisabled;', '''    private readonly bool _auditDisabled;
    private readonly bool _auditStatic = Environment.GetEnvironmentVariable("LUDOTS_AB_STATIC") == "1";
    private readonly bool _auditSparse = Environment.GetEnvironmentVariable("LUDOTS_AB_DENSITY") == "sparse";
    private bool _auditLayoutApplied;''')
replace(step, '        int stepsToRun = simulation.CadenceScheduler.BeginFixedTick(dt);', '''        if (!_auditLayoutApplied)
        {
            var flow = simulation.MassNavigationFlow;
            if (_auditSparse)
            {
                int columns = (int)Math.Ceiling(Math.Sqrt(flow.UnitCount));
                float spacing = (flow.FieldWidthCm - 600f) / columns;
                for (int i = 0; i < flow.UnitCount; i++)
                    flow.SetUnitPositionForTests(i, 300f + (i % columns + 0.5f) * spacing, 300f + (i / columns + 0.5f) * spacing);
            }
            if (_auditStatic)
                for (int i = 0; i < flow.UnitCount; i++) flow.HoldUnitAtCurrentPosition(i);
            _auditLayoutApplied = true;
        }
        int stepsToRun = simulation.CadenceScheduler.BeginFixedTick(dt);''')
replace(step, '            simulation.ObserveSimTick();', '            simulation.ObserveSimTick();\n            simulation.AuditFrameTotals[7]++;')
replace(step, '            simulation.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);', '''            simulation.ObserveSimStep((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            simulation.AuditFrameTotals[8] += simulation.MassNavigationFlow.LastAvoidanceNeighborCandidateCheckCount;
            if (step.RunHardResolve)
            {
                simulation.AuditFrameTotals[9] += simulation.LastHardResolvePairCheckCount;
                simulation.AuditFrameTotals[10] += simulation.LastHardResolveCandidateAgentCount;
                simulation.AuditFrameTotals[11] += simulation.LastHardResolvePenetratingPairCount;
            }
            simulation.AuditFrameTotals[12] += simulation.MassNavigationFlow.AuditMovedAgents;''')

flow = 'src/Core/MassNavigation/Runtime/MassNavigationFlowSolverState.cs'
replace(flow, '    public int LastHardResolveCandidateAgentCount { get; private set; }', '    public int AuditMovedAgents { get; private set; }\n    public int LastHardResolveCandidateAgentCount { get; private set; }')
replace(flow, '        long prepStart = System.Diagnostics.Stopwatch.GetTimestamp();', '        AuditMovedAgents = 0;\n        long prepStart = System.Diagnostics.Stopwatch.GetTimestamp();')

runtime_config = 'src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs'
p = root / runtime_config
t = p.read_text(encoding='utf-8')
begin = t.index('        const int sparseFieldSizeCm = 40000;')
end = t.index('        config.Streaming.RadiusCm = 30000;', begin) + len('        config.Streaming.RadiusCm = 30000;')
t = t[:begin] + t[end:]
p.write_text(t, encoding='utf-8')

parser = root / 'tmp/perf-matrix/parse_matrix.py'
t = parser.read_text(encoding='utf-8').replace('value = value.split("/")[0]', 'value = value.split("/")[0].removesuffix("ms")')
parser.write_text(t, encoding='utf-8')
