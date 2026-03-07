using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Navigation2DPlaygroundMod;
using Navigation2DPlaygroundMod.Input;
using Navigation2DPlaygroundMod.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public sealed class Navigation2DPlaygroundPlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void Navigation2DPlayground_PlayableFlowAndScenarioSwitch_WritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-playground-playable");
            Directory.CreateDirectory(artifactDir);

            var snapshots = new List<PlayableSnapshot>();
            var frameTimesMs = new List<double>();
            var engine = new GameEngine();
            try
            {
                engine.InitializeWithConfigPipeline(
                    new List<string>
                    {
                        Path.Combine(modsRoot, "LudotsCoreMod"),
                        Path.Combine(modsRoot, "Navigation2DPlaygroundMod")
                    },
                    assetsRoot);

                PlayerInputHandler input = InstallDummyInput(engine);
                engine.Start();
                engine.LoadMap(engine.MergedConfig.StartupMapId);

                TickEngine(engine, 4, frameTimesMs);
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

                var navRuntime = engine.GetService(CoreServiceKeys.Navigation2DRuntime);
                var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
                var debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
                Assert.That(navRuntime, Is.Not.Null);
                Assert.That(overlay, Is.Not.Null);
                Assert.That(debugDraw, Is.Not.Null);

                CaptureSnapshot(engine, navRuntime, overlay, frameTimesMs, snapshots, "warmup");

                int initialScenarioIndex = engine.GetService(Navigation2DPlaygroundKeys.ScenarioIndex);
                string initialScenarioName = engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown";
                int initialAgentsPerTeam = engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam);
                int scenarioCount = engine.GetService(Navigation2DPlaygroundKeys.ScenarioCount);
                Assert.That(scenarioCount, Is.GreaterThanOrEqualTo(6));
                AssertOverlayLines(overlay, navRuntime.FlowEnabled);

                input.InjectButtonPress(Navigation2DPlaygroundInputActions.NextScenario);
                TickEngine(engine, 1, frameTimesMs);
                int nextScenarioIndex = engine.GetService(Navigation2DPlaygroundKeys.ScenarioIndex);
                string nextScenarioName = engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown";
                Assert.That(nextScenarioIndex, Is.EqualTo((initialScenarioIndex + 1) % scenarioCount));
                Assert.That(nextScenarioName, Is.Not.EqualTo(initialScenarioName));
                CaptureSnapshot(engine, navRuntime, overlay, frameTimesMs, snapshots, "next_scenario");

                input.InjectButtonPress(Navigation2DPlaygroundInputActions.IncreaseAgentsPerTeam);
                TickEngine(engine, 1, frameTimesMs);
                int increasedAgentsPerTeam = engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam);
                int teamCount = engine.GetService(Navigation2DPlaygroundKeys.ScenarioTeamCount);
                int liveAgents = engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal);
                Assert.That(increasedAgentsPerTeam, Is.GreaterThan(initialAgentsPerTeam));
                Assert.That(liveAgents, Is.EqualTo(increasedAgentsPerTeam * teamCount));
                CaptureSnapshot(engine, navRuntime, overlay, frameTimesMs, snapshots, "increase_agents");

                input.InjectButtonPress(Navigation2DPlaygroundInputActions.ToggleFlowDebug);
                TickEngine(engine, 2, frameTimesMs);
                Assert.That(navRuntime.FlowDebugEnabled, Is.True);
                int flowDebugLines = engine.GetService(Navigation2DPlaygroundKeys.FlowDebugLines);
                Assert.That(flowDebugLines, Is.GreaterThanOrEqualTo(0));
                Assert.That(ExtractOverlayText(overlay).Exists(l => l.Contains("FlowEnabled=") && l.Contains("FlowDebug=True")), Is.True);
                CaptureSnapshot(engine, navRuntime, overlay, frameTimesMs, snapshots, "toggle_flow_debug");

                int flowModeBefore = navRuntime.FlowDebugMode;
                input.InjectButtonPress(Navigation2DPlaygroundInputActions.CycleFlowDebugMode);
                TickEngine(engine, 1, frameTimesMs);
                Assert.That(navRuntime.FlowDebugMode, Is.EqualTo((flowModeBefore + 1) % 3));
                CaptureSnapshot(engine, navRuntime, overlay, frameTimesMs, snapshots, "cycle_flow_mode");

                bool flowEnabledBefore = navRuntime.FlowEnabled;
                input.InjectButtonPress(Navigation2DPlaygroundInputActions.ToggleFlowEnabled);
                TickEngine(engine, 1, frameTimesMs);
                Assert.That(navRuntime.FlowEnabled, Is.EqualTo(!flowEnabledBefore));
                CaptureSnapshot(engine, navRuntime, overlay, frameTimesMs, snapshots, "toggle_flow_enabled");

                AssertOverlayLines(overlay, navRuntime.FlowEnabled);
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

                string traceJsonl = BuildTraceJsonl(snapshots);
                string battleReport = BuildBattleReport(snapshots, frameTimesMs);
                string pathMmd = BuildPathMermaid();

                File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), battleReport);
                File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), traceJsonl);
                File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), pathMmd);
            }
            finally
            {
                engine.Dispose();
                }
        }

        private static void CaptureSnapshot(
            GameEngine engine,
            Navigation2DRuntime navRuntime,
            ScreenOverlayBuffer overlay,
            IReadOnlyList<double> frameTimesMs,
            List<PlayableSnapshot> snapshots,
            string step)
        {
            var lines = ExtractOverlayText(overlay);
            snapshots.Add(new PlayableSnapshot(
                Step: step,
                ScenarioIndex: engine.GetService(Navigation2DPlaygroundKeys.ScenarioIndex),
                ScenarioName: engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown",
                AgentsPerTeam: engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam),
                LiveAgents: engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal),
                Blockers: engine.GetService(Navigation2DPlaygroundKeys.BlockerCount),
                FlowEnabled: navRuntime.FlowEnabled,
                FlowDebugEnabled: navRuntime.FlowDebugEnabled,
                FlowDebugMode: navRuntime.FlowDebugMode,
                FlowDebugLines: engine.GetService(Navigation2DPlaygroundKeys.FlowDebugLines),
                SteeringCacheLookups: navRuntime.AgentSoA.SteeringCacheLookupsFrame,
                SteeringCacheHits: navRuntime.AgentSoA.SteeringCacheHitsFrame,
                SteeringCacheStores: navRuntime.AgentSoA.SteeringCacheStoresFrame,
                OverlayLines: lines,
                TickMs: frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d));
        }

        private static void TickEngine(GameEngine engine, int count, List<double> frameTimesMs)
        {
            for (int i = 0; i < count; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.Tick(DeltaTime);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static PlayerInputHandler InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            return inputHandler;
        }

        private static List<string> ExtractOverlayText(ScreenOverlayBuffer overlay)
        {
            var lines = new List<string>();
            foreach (var item in overlay.GetSpan())
            {
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }

            return lines;
        }

        private static void AssertOverlayLines(ScreenOverlayBuffer overlay, bool expectedFlowEnabled)
        {
            var lines = ExtractOverlayText(overlay);
            string dump = string.Join(" || ", lines);
            Assert.That(dump.Contains("Navigation2D Playground", StringComparison.Ordinal), Is.True, dump);
            Assert.That(dump.Contains("Steering=", StringComparison.Ordinal) && dump.Contains("CacheCfg=", StringComparison.Ordinal), Is.True, dump);
            Assert.That(dump.Contains("CacheLookups=", StringComparison.Ordinal) && dump.Contains("HitRate=", StringComparison.Ordinal), Is.True, dump);
            Assert.That(dump.Contains("FlowEnabled=", StringComparison.Ordinal) && dump.Contains("FlowDebug=", StringComparison.Ordinal), Is.True, dump);
            Assert.That(dump.Contains("Spatial=", StringComparison.Ordinal) && dump.Contains("CellMigrations=", StringComparison.Ordinal), Is.True, dump);
        }

        private static string BuildTraceJsonl(IReadOnlyList<PlayableSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                double hitRate = snapshot.SteeringCacheLookups > 0 ? (double)snapshot.SteeringCacheHits / snapshot.SteeringCacheLookups : 0d;
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"nav2d-playable-{i + 1:000}",
                    step = snapshot.Step,
                    scenario_index = snapshot.ScenarioIndex,
                    scenario_name = snapshot.ScenarioName,
                    agents_per_team = snapshot.AgentsPerTeam,
                    live_agents = snapshot.LiveAgents,
                    blockers = snapshot.Blockers,
                    flow_enabled = snapshot.FlowEnabled,
                    flow_debug_enabled = snapshot.FlowDebugEnabled,
                    flow_debug_mode = snapshot.FlowDebugMode,
                    flow_debug_lines = snapshot.FlowDebugLines,
                    steering_cache_lookups = snapshot.SteeringCacheLookups,
                    steering_cache_hits = snapshot.SteeringCacheHits,
                    steering_cache_stores = snapshot.SteeringCacheStores,
                    steering_cache_hit_rate = Math.Round(hitRate, 4),
                    tick_ms = Math.Round(snapshot.TickMs, 4),
                    overlay_head = snapshot.OverlayLines.Take(4).ToArray(),
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(IReadOnlyList<PlayableSnapshot> snapshots, IReadOnlyList<double> frameTimesMs)
        {
            var timeline = new StringBuilder();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                double hitRate = snapshot.SteeringCacheLookups > 0 ? (double)snapshot.SteeringCacheHits / snapshot.SteeringCacheLookups : 0d;
                timeline.AppendLine($"- [T+{i + 1:000}] {snapshot.Step} | Scenario={snapshot.ScenarioIndex + 1}:{snapshot.ScenarioName} | Agents/team={snapshot.AgentsPerTeam} | Live={snapshot.LiveAgents} | Blockers={snapshot.Blockers} | Flow={snapshot.FlowEnabled}/{snapshot.FlowDebugEnabled}/Mode{snapshot.FlowDebugMode} | FlowDbgLines={snapshot.FlowDebugLines} | CacheHitRate={hitRate:P1} | Tick={snapshot.TickMs:F3}ms");
            }

            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            var finalSnapshot = snapshots[^1];
            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: navigation2d-playground-playable");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: launch the Navigation2D playground, switch scenarios, and inspect steering/flow/debug overlays without custom test-only runtime paths.");
            sb.AppendLine("- Gameplay domain: Navigation2D playground mod, input pipeline, HUD overlay, and debug draw integration.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`");
            sb.AppendLine("- Mods: `LudotsCoreMod`, `Navigation2DPlaygroundMod`");
            sb.AppendLine("- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.");
            sb.AppendLine("- Input source: `InputConfigPipelineLoader` + `PlayerInputHandler.InjectButtonPress()`.");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Boot the real `GameEngine` with the playground mod through the config pipeline.");
            sb.AppendLine("2. Warm the entry scene and validate HUD/debug services.");
            sb.AppendLine("3. Inject `NextScenario`, `IncreaseAgentsPerTeam`, `ToggleFlowDebug`, `CycleFlowDebugMode`, and `ToggleFlowEnabled`.");
            sb.AppendLine("4. Record scenario services, overlay text, cache counters, flow debug lines, and wall-clock tick cost.");
            sb.AppendLine();
            sb.AppendLine("## Expected Outcomes");
            sb.AppendLine("- Primary success condition: the playground stays interactive through the normal mod/input/UI pipeline.");
            sb.AppendLine("- Failure branch condition: scenario switching, overlay rendering, or flow debug toggles do not update runtime state.");
            sb.AppendLine("- Key metrics: scenario index/name, agents per team, flow debug lines, steering cache counters, median tick wall time.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-playable/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-playable/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/navigation2d-playground-playable/path.mmd`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.Append(timeline.ToString());
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: the playable Navigation2D mod is wired through the unified config, input, HUD, and debug draw pipeline.");
            sb.AppendLine($"- reason: final state reached scenario `{finalSnapshot.ScenarioName}` with flow enabled=`{finalSnapshot.FlowEnabled}`, flow debug lines=`{finalSnapshot.FlowDebugLines}`, and median headless tick cost `{medianTickMs:F3}ms`.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            sb.AppendLine($"- final agents per team: `{finalSnapshot.AgentsPerTeam}`");
            sb.AppendLine($"- final live agents: `{finalSnapshot.LiveAgents}`");
            sb.AppendLine($"- final flow debug lines: `{finalSnapshot.FlowDebugLines}`");
            sb.AppendLine("- reusable wiring: `ConfigPipeline`, `PlayerInputHandler`, `ScreenOverlayBuffer`, `DebugDrawCommandBuffer`, `Navigation2DRuntime`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Boot GameEngine with Navigation2DPlaygroundMod] --> B[Warm entry map]",
                "    B --> C[Validate overlay and runtime services]",
                "    C --> D[Inject NextScenario]",
                "    D --> E[Inject IncreaseAgentsPerTeam]",
                "    E --> F[Inject ToggleFlowDebug + CycleMode]",
                "    F --> G[Inject ToggleFlowEnabled]",
                "    G --> H[Write battle-report + trace + path]"
            }) + Environment.NewLine;
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var sorted = values.OrderBy(v => v).ToArray();
            int mid = sorted.Length / 2;
            return (sorted.Length & 1) != 0
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) * 0.5d;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private readonly record struct PlayableSnapshot(
            string Step,
            int ScenarioIndex,
            string ScenarioName,
            int AgentsPerTeam,
            int LiveAgents,
            int Blockers,
            bool FlowEnabled,
            bool FlowDebugEnabled,
            int FlowDebugMode,
            int FlowDebugLines,
            int SteeringCacheLookups,
            int SteeringCacheHits,
            int SteeringCacheStores,
            IReadOnlyList<string> OverlayLines,
            double TickMs);

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}

