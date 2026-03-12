using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Navigation2DPlaygroundMod;
using Navigation2DPlaygroundMod.Input;
using Navigation2DPlaygroundMod.Systems;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public sealed class Navigation2DPlaygroundTimedAvoidanceAcceptanceTests
    {
        private static readonly QueryDescription _dynamicAgentsQuery = new QueryDescription()
            .WithAll<NavAgent2D, Position2D, Velocity2D, NavPlaygroundTeam>()
            .WithNone<NavPlaygroundBlocker>();

        private static readonly QueryDescription _blockerQuery = new QueryDescription()
            .WithAll<Position2D, NavPlaygroundBlocker>();

        private static readonly QueryDescription _scenarioEntitiesQuery = new QueryDescription()
            .WithAll<NavPlaygroundTeam>();

        private static readonly QueryDescription _flowGoalQuery = new QueryDescription()
            .WithAll<NavFlowGoal2D>();

        private const float DeltaTime = 1f / 60f;
        private const int AcceptanceAgentsPerTeam = 64;
        private const int FinalTick = 2400;
        private const int TraceStrideTicks = 30;
        private const int CaptureStrideTicks = 60;
        private const float MovingSpeedSquaredThreshold = 400f;
        private const int MeaningfulEncounterCenterCount = 8;
        private const float MajorityCrossedFractionTarget = 0.50f;
        private const float FinalCrossedFractionTarget = 0.85f;
        private const float FinalCenterFractionLimit = 0.06f;
        private const float FinalCenterStoppedFractionLimit = 0.02f;
        private const float CenterHalfWidthCm = 1200f;
        private const float CenterHalfHeightCm = 2600f;
        private const float WorldMinX = -14000f;
        private const float WorldMaxX = 14000f;
        private const float WorldMinY = -9000f;
        private const float WorldMaxY = 9000f;
        private const int ImageWidth = 1600;
        private const int ImageHeight = 900;

        [Test]
        public void Navigation2DPlayground_PassThroughAcceptance_WritesScreensAndProvesFullCrossing()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "navigation2d-playground-pass-through-full-run");
            string screensDir = Path.Combine(artifactDir, "screens");
            Directory.CreateDirectory(screensDir);

            var timeline = new List<AvoidanceSnapshot>();
            var captureFrames = new List<CaptureFrame>();
            var frameTimesMs = new List<double>();
            var engine = new GameEngine();
            try
            {
                engine.InitializeWithConfigPipeline(
                    new List<string>
                    {
                        Path.Combine(modsRoot, "LudotsCoreMod"),
                        Path.Combine(modsRoot, "CoreInputMod"),
                        Path.Combine(modsRoot, "Navigation2DPlaygroundMod")
                    },
                    assetsRoot);

                _ = InstallDummyInput(engine);
                engine.Start();
                engine.LoadMap(engine.MergedConfig.StartupMapId);
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));

                Navigation2DPlaygroundState.CurrentScenarioIndex = 0;
                Navigation2DPlaygroundState.AgentsPerTeam = AcceptanceAgentsPerTeam;
                RespawnPlaygroundScenario(engine, scenarioIndex: 0, agentsPerTeam: AcceptanceAgentsPerTeam);
                TickEngine(engine, 2, frameTimesMs);

                var navRuntime = engine.GetService(CoreServiceKeys.Navigation2DRuntime);
                var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
                Assert.That(navRuntime, Is.Not.Null);
                Assert.That(overlay, Is.Not.Null);
                Assert.That(engine.GetService(Navigation2DPlaygroundKeys.ScenarioName), Is.EqualTo("Pass Through"));
                Assert.That(engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam), Is.EqualTo(AcceptanceAgentsPerTeam));
                AssertOverlayLines(overlay);

                CaptureAndRecord(engine, navRuntime, screensDir, frameTimesMs, timeline, captureFrames, tick: 0, step: "start");

                for (int tick = 1; tick <= FinalTick; tick++)
                {
                    TickEngine(engine, 1, frameTimesMs);
                    if (tick % TraceStrideTicks == 0)
                    {
                        bool captureImage = tick % CaptureStrideTicks == 0 || tick == FinalTick;
                        CaptureAndRecord(engine, navRuntime, screensDir, frameTimesMs, timeline, captureFrames, tick, captureImage ? $"t{tick:000}" : $"sample_{tick:000}", captureImage);
                    }
                }
                AvoidanceAcceptanceResult acceptance = EvaluateAcceptance(timeline);

                string battleReport = BuildBattleReport(timeline, captureFrames, frameTimesMs, acceptance);
                string traceJsonl = BuildTraceJsonl(timeline);
                string pathMmd = BuildPathMermaid();
                string visibleChecklist = BuildVisibleChecklist(captureFrames);

                File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), battleReport);
                File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), traceJsonl);
                File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), pathMmd);
                File.WriteAllText(Path.Combine(artifactDir, "visible-checklist.md"), visibleChecklist);
                WriteTimelineSheet(captureFrames, Path.Combine(screensDir, "timeline.png"));

                Assert.That(acceptance.Success, Is.True, acceptance.FailureSummary);
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static void RespawnPlaygroundScenario(GameEngine engine, int scenarioIndex, int agentsPerTeam)
        {
            GameConfig? gameConfig = engine.GetService(CoreServiceKeys.GameConfig);
            var playgroundConfig = Navigation2DPlaygroundScenarioSpawner.GetPlaygroundConfig(gameConfig);
            Navigation2DPlaygroundState.CurrentScenarioIndex = Navigation2DPlaygroundScenarioSpawner.ClampScenarioIndex(playgroundConfig, scenarioIndex);
            Navigation2DPlaygroundState.AgentsPerTeam = agentsPerTeam;
            engine.World.Destroy(in _scenarioEntitiesQuery);
            engine.World.Destroy(in _flowGoalQuery);
            var scenario = Navigation2DPlaygroundScenarioSpawner.GetScenario(playgroundConfig, Navigation2DPlaygroundState.CurrentScenarioIndex);
            var summary = Navigation2DPlaygroundScenarioSpawner.SpawnScenario(engine.World, scenario, agentsPerTeam);
            Navigation2DPlaygroundControlSystem.PublishScenarioServices(engine, playgroundConfig, summary, agentsPerTeam, Navigation2DPlaygroundState.CurrentScenarioIndex);
        }

        private static void CaptureAndRecord(
            GameEngine engine,
            Navigation2DRuntime navRuntime,
            string screensDir,
            IReadOnlyList<double> frameTimesMs,
            List<AvoidanceSnapshot> timeline,
            List<CaptureFrame> captureFrames,
            int tick,
            string step,
            bool captureImage = true)
        {
            var snapshot = SampleAvoidanceSnapshot(engine, navRuntime, tick, step, frameTimesMs.Count > 0 ? frameTimesMs[^1] : 0d);
            timeline.Add(snapshot);
            if (!captureImage)
            {
                return;
            }

            string fileName = $"{tick:000}_{step}.png";
            string path = Path.Combine(screensDir, fileName);
            WriteSnapshotImage(snapshot, path);
            captureFrames.Add(new CaptureFrame(snapshot.Tick, snapshot.Step, fileName, snapshot.CenterCount, snapshot.CenterStoppedAgents, snapshot.Team0CrossedFraction, snapshot.Team1CrossedFraction));
        }

        private static AvoidanceSnapshot SampleAvoidanceSnapshot(GameEngine engine, Navigation2DRuntime navRuntime, int tick, string step, double tickMs)
        {
            var team0 = new List<Vector2>();
            var team1 = new List<Vector2>();
            var blockers = new List<Vector2>();
            int movingAgents = 0;
            int centerCount = 0;
            int centerMovingAgents = 0;
            int centerStoppedAgents = 0;

            foreach (ref var chunk in engine.World.Query(in _dynamicAgentsQuery))
            {
                var positions = chunk.GetSpan<Position2D>();
                var velocities = chunk.GetSpan<Velocity2D>();
                var teams = chunk.GetSpan<NavPlaygroundTeam>();
                foreach (var entityIndex in chunk)
                {
                    Vector2 position = positions[entityIndex].Value.ToVector2();
                    if (teams[entityIndex].Id == 0)
                    {
                        team0.Add(position);
                    }
                    else if (teams[entityIndex].Id == 1)
                    {
                        team1.Add(position);
                    }

                    bool isMoving = velocities[entityIndex].Linear.ToVector2().LengthSquared() > MovingSpeedSquaredThreshold;
                    if (isMoving)
                    {
                        movingAgents++;
                    }

                    if (MathF.Abs(position.X) <= CenterHalfWidthCm && MathF.Abs(position.Y) <= CenterHalfHeightCm)
                    {
                        centerCount++;
                        if (isMoving)
                        {
                            centerMovingAgents++;
                        }
                        else
                        {
                            centerStoppedAgents++;
                        }
                    }
                }
            }

            foreach (ref var chunk in engine.World.Query(in _blockerQuery))
            {
                var positions = chunk.GetSpan<Position2D>();
                foreach (var entityIndex in chunk)
                {
                    blockers.Add(positions[entityIndex].Value.ToVector2());
                }
            }

            return new AvoidanceSnapshot(
                Tick: tick,
                Step: step,
                ScenarioName: engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown",
                AgentsPerTeam: engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam),
                LiveAgents: engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal),
                FlowEnabled: navRuntime.FlowEnabled,
                FlowDebugEnabled: navRuntime.FlowDebugEnabled,
                TickMs: tickMs,
                Team0Positions: team0,
                Team1Positions: team1,
                BlockerPositions: blockers,
                Team0MedianPrimary: Median(team0.Select(p => p.X).ToArray()),
                Team1MedianPrimary: Median(team1.Select(p => p.X).ToArray()),
                Team0CrossedFraction: Fraction(team0, p => p.X > 0f),
                Team1CrossedFraction: Fraction(team1, p => p.X < 0f),
                CenterCount: centerCount,
                CenterMovingAgents: centerMovingAgents,
                CenterStoppedAgents: centerStoppedAgents,
                MovingAgents: movingAgents,
                FlowActiveTiles: navRuntime.FlowCount > 0 ? navRuntime.Flows.Sum(f => f.ActiveTileCount) : 0,
                FlowFrontierProcessed: navRuntime.FlowCount > 0 ? navRuntime.Flows.Sum(f => f.InstrumentedFrontierProcessedFrame) : 0,
                FlowBudgetClamped: navRuntime.FlowCount > 0 && navRuntime.Flows.Any(f => f.InstrumentedWindowBudgetClampedFrame),
                FlowWorldClamped: navRuntime.FlowCount > 0 && navRuntime.Flows.Any(f => f.InstrumentedWindowWorldClampedFrame));
        }

        private static void WriteSnapshotImage(AvoidanceSnapshot snapshot, string path)
        {
            using var surface = SKSurface.Create(new SKImageInfo(ImageWidth, ImageHeight));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(12, 16, 24));

            using var fillCenter = new SKPaint { Color = new SKColor(50, 90, 130, 48), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var strokeCenter = new SKPaint { Color = new SKColor(80, 180, 255, 140), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
            using var axisPaint = new SKPaint { Color = new SKColor(90, 100, 120), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
            using var team0Paint = new SKPaint { Color = new SKColor(64, 220, 110), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var team1Paint = new SKPaint { Color = new SKColor(255, 88, 88), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var blockerPaint = new SKPaint { Color = new SKColor(90, 150, 255), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 24f };
            using var minorTextPaint = new SKPaint { Color = new SKColor(180, 190, 205), IsAntialias = true, TextSize = 18f };

            SKRect centerRect = ToScreenRect(-CenterHalfWidthCm, -CenterHalfHeightCm, CenterHalfWidthCm, CenterHalfHeightCm);
            canvas.DrawRect(centerRect, fillCenter);
            canvas.DrawRect(centerRect, strokeCenter);
            canvas.DrawLine(ToScreen(new Vector2(WorldMinX, 0f)), ToScreen(new Vector2(WorldMaxX, 0f)), axisPaint);
            canvas.DrawLine(ToScreen(new Vector2(0f, WorldMinY)), ToScreen(new Vector2(0f, WorldMaxY)), axisPaint);

            foreach (Vector2 blocker in snapshot.BlockerPositions)
            {
                DrawAgent(canvas, blockerPaint, blocker, radiusPx: 6f);
            }

            foreach (Vector2 agent in snapshot.Team0Positions)
            {
                DrawAgent(canvas, team0Paint, agent, radiusPx: 3.8f);
            }

            foreach (Vector2 agent in snapshot.Team1Positions)
            {
                DrawAgent(canvas, team1Paint, agent, radiusPx: 3.8f);
            }

            canvas.DrawText($"Navigation2D Timed Avoidance | {snapshot.Step} | tick={snapshot.Tick}", 24, 34, textPaint);
            canvas.DrawText($"Scenario={snapshot.ScenarioName}  Agents/team={snapshot.AgentsPerTeam}  Live={snapshot.LiveAgents}", 24, 66, minorTextPaint);
            canvas.DrawText($"MedianX T0={snapshot.Team0MedianPrimary:F0}  T1={snapshot.Team1MedianPrimary:F0}  Crossed T0={snapshot.Team0CrossedFraction:P0}  T1={snapshot.Team1CrossedFraction:P0}", 24, 94, minorTextPaint);
            canvas.DrawText($"CenterCount={snapshot.CenterCount}  CenterMove={snapshot.CenterMovingAgents}  CenterStop={snapshot.CenterStoppedAgents}  MovingAgents={snapshot.MovingAgents}", 24, 122, minorTextPaint);
            canvas.DrawText($"FlowActiveTiles={snapshot.FlowActiveTiles}  Frontier={snapshot.FlowFrontierProcessed}", 24, 150, minorTextPaint);
            canvas.DrawText($"BudgetClamp={snapshot.FlowBudgetClamped}  WorldClamp={snapshot.FlowWorldClamped}  Tick={snapshot.TickMs:F3}ms", 24, 178, minorTextPaint);

            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            data.SaveTo(stream);
        }

        private static void WriteTimelineSheet(IReadOnlyList<CaptureFrame> frames, string path)
        {
            if (frames.Count == 0)
            {
                return;
            }

            const int thumbWidth = 800;
            const int thumbHeight = 450;
            int columns = frames.Count >= 24 ? 4 : frames.Count >= 12 ? 3 : 2;
            int rows = (int)Math.Ceiling(frames.Count / (double)columns);
            using var surface = SKSurface.Create(new SKImageInfo(columns * thumbWidth, rows * thumbHeight + 60));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(8, 10, 16));
            using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 28f };
            canvas.DrawText("Navigation2D full pass-through screenshot timeline", 20, 36, titlePaint);

            for (int i = 0; i < frames.Count; i++)
            {
                string sourcePath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, frames[i].FileName);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                using SKBitmap bitmap = SKBitmap.Decode(sourcePath);
                int col = i % columns;
                int row = i / columns;
                SKRect dest = new SKRect(col * thumbWidth, row * thumbHeight + 60, (col + 1) * thumbWidth, (row + 1) * thumbHeight + 60);
                canvas.DrawBitmap(bitmap, dest);
            }

            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            data.SaveTo(stream);
        }

        private static string BuildTraceJsonl(IReadOnlyList<AvoidanceSnapshot> timeline)
        {
            var lines = new List<string>(timeline.Count);
            for (int i = 0; i < timeline.Count; i++)
            {
                var snapshot = timeline[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"nav2d-timed-{i + 1:000}",
                    tick = snapshot.Tick,
                    step = snapshot.Step,
                    scenario = snapshot.ScenarioName,
                    agents_per_team = snapshot.AgentsPerTeam,
                    live_agents = snapshot.LiveAgents,
                    team0_median_x = Math.Round(snapshot.Team0MedianPrimary, 2),
                    team1_median_x = Math.Round(snapshot.Team1MedianPrimary, 2),
                    team0_crossed_fraction = Math.Round(snapshot.Team0CrossedFraction, 4),
                    team1_crossed_fraction = Math.Round(snapshot.Team1CrossedFraction, 4),
                    center_count = snapshot.CenterCount,
                    center_moving_agents = snapshot.CenterMovingAgents,
                    center_stopped_agents = snapshot.CenterStoppedAgents,
                    moving_agents = snapshot.MovingAgents,
                    flow_active_tiles = snapshot.FlowActiveTiles,
                    flow_frontier_processed = snapshot.FlowFrontierProcessed,
                    flow_budget_clamped = snapshot.FlowBudgetClamped,
                    flow_world_clamped = snapshot.FlowWorldClamped,
                    tick_ms = Math.Round(snapshot.TickMs, 4),
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(IReadOnlyList<AvoidanceSnapshot> timeline, IReadOnlyList<CaptureFrame> captureFrames, IReadOnlyList<double> frameTimesMs, AvoidanceAcceptanceResult acceptance)
        {
            var sb = new StringBuilder();
            AvoidanceSnapshot final = timeline[^1];
            double medianTickMs = Median(frameTimesMs.ToArray());
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            string evidenceImages = string.Join(", ", captureFrames.Select(frame => $"`screens/{frame.FileName}`").Append("`screens/timeline.png`"));

            sb.AppendLine("# Scenario Card: navigation2d-playground-pass-through-full-run");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: verify the playable `Pass Through` crowd really completes a full head-on encounter from approach to contact to crossing to separation.");
            sb.AppendLine("- Gameplay domain: real `GameEngine` + `Navigation2DPlaygroundMod` + unified config/input/UI pipeline, with a long screenshot sequence instead of a pre-contact timeout slice.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`");
            sb.AppendLine($"- Scenario: `{timeline[0].ScenarioName}`");
            sb.AppendLine($"- Agents per team: `{AcceptanceAgentsPerTeam}`");
            sb.AppendLine($"- Clock profile: fixed `1/60s`, final tick `{FinalTick}`, trace stride `{TraceStrideTicks}`, capture stride `{CaptureStrideTicks}`");
            sb.AppendLine($"- Evidence images: {evidenceImages}");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Boot the real playable mod through `ConfigPipeline`.");
            sb.AppendLine("2. Force the `Pass Through` scenario and deterministic agent count through the existing playground state + reset input.");
            sb.AppendLine("3. Simulate long enough to capture approach, collision, crossing, and post-clear separation.");
            sb.AppendLine("4. Render screenshot frames and fail if the run never forms a real encounter or never finishes the pass-through.");
            sb.AppendLine();
            sb.AppendLine("## Key Events");
            sb.AppendLine($"- first meaningful contact tick: `{FormatTick(acceptance.FirstContactTick)}`");
            sb.AppendLine($"- peak center occupancy: `{acceptance.PeakCenterCount}` at tick `{acceptance.PeakCenterTick}`");
            sb.AppendLine($"- first mutual cross tick: `{FormatTick(acceptance.FirstCrossTick)}`");
            sb.AppendLine($"- majority crossed tick: `{FormatTick(acceptance.MajorityCrossTick)}`");
            sb.AppendLine($"- center cleared tick: `{FormatTick(acceptance.ClearTick)}`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            foreach (AvoidanceSnapshot snapshot in timeline.Where(t => t.Tick == 0 || t.Tick % CaptureStrideTicks == 0 || t.Tick == FinalTick))
            {
                sb.AppendLine($"- [T+{snapshot.Tick:000}] {snapshot.Step} | MedianX T0={snapshot.Team0MedianPrimary:F0} T1={snapshot.Team1MedianPrimary:F0} | Crossed T0={snapshot.Team0CrossedFraction:P0} T1={snapshot.Team1CrossedFraction:P0} | Center={snapshot.CenterCount} move={snapshot.CenterMovingAgents} stop={snapshot.CenterStoppedAgents} | Moving={snapshot.MovingAgents} | Tick={snapshot.TickMs:F3}ms");
            }
            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine($"- success: {(acceptance.Success ? "yes" : "no")}");
            sb.AppendLine($"- verdict: {acceptance.Verdict}");
            if (acceptance.FailedChecks.Count > 0)
            {
                foreach (string failedCheck in acceptance.FailedChecks)
                {
                    sb.AppendLine($"- failed-check: {failedCheck}");
                }
            }

            sb.AppendLine($"- tail summary: medianX T0=`{final.Team0MedianPrimary:F0}` T1=`{final.Team1MedianPrimary:F0}`; crossed=`{final.Team0CrossedFraction:P0}`/`{final.Team1CrossedFraction:P0}`; center=`{final.CenterCount}` stopped=`{final.CenterStoppedAgents}`.");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- trace samples: `{timeline.Count}`");
            sb.AppendLine($"- screenshot captures: `{captureFrames.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            sb.AppendLine("- reusable wiring: `ConfigPipeline`, `PlayerInputHandler`, `CoreInputMod`, `Navigation2DPlaygroundState`, `ScreenOverlayBuffer`, `Navigation2DRuntime`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Boot real playable Navigation2D playground] --> B[Force PassThrough + deterministic agents/team]",
                "    B --> C[Run long simulation through full encounter]",
                "    C --> D[Capture screenshot sequence + trace metrics]",
                "    D --> E{Meaningful contact, crossing, and clear all observed?}",
                "    E -->|yes| F[Write battle-report + trace + path + PNG timeline]",
                "    E -->|no| X[Fail acceptance: full pass-through not proven]"
            }) + Environment.NewLine;
        }

        private static string BuildVisibleChecklist(IReadOnlyList<CaptureFrame> frames)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Visible Checklist: navigation2d-playground-pass-through-full-run");
            sb.AppendLine();
            sb.AppendLine("- Review the PNG sequence in chronological order; it should now show approach, contact, crossing, and post-clear separation from start to finish.");
            sb.AppendLine("- `screens/timeline.png`: quick visual strip for human acceptance review of the whole encounter.");
            sb.AppendLine("- Final frames should place most green agents on the right half-space and most red agents on the left half-space, with no stationary knot left in the center box.");
            sb.AppendLine();
            foreach (CaptureFrame frame in frames)
            {
                sb.AppendLine($"- `{frame.FileName}`: center={frame.CenterCount}, centerStopped={frame.CenterStoppedAgents}, crossed={frame.Team0CrossedFraction:P0}/{frame.Team1CrossedFraction:P0}");
            }

            return sb.ToString();
        }

        private static AvoidanceAcceptanceResult EvaluateAcceptance(IReadOnlyList<AvoidanceSnapshot> timeline)
        {
            var failures = new List<string>();
            AvoidanceSnapshot start = timeline.First(snapshot => snapshot.Tick == 0);
            AvoidanceSnapshot final = timeline.First(snapshot => snapshot.Tick == FinalTick);
            AvoidanceSnapshot peak = timeline.OrderByDescending(snapshot => snapshot.CenterCount).First();
            bool hasContact = TryFindSnapshot(timeline, snapshot => snapshot.CenterCount >= MeaningfulEncounterCenterCount, out AvoidanceSnapshot firstContact);
            bool hasFirstCross = TryFindSnapshot(timeline, snapshot => MathF.Min(snapshot.Team0CrossedFraction, snapshot.Team1CrossedFraction) > 0f, out AvoidanceSnapshot firstCross);
            bool hasMajorityCross = TryFindSnapshot(timeline, snapshot => MathF.Min(snapshot.Team0CrossedFraction, snapshot.Team1CrossedFraction) >= MajorityCrossedFractionTarget, out AvoidanceSnapshot majorityCross);
            bool hasClear = TryFindSnapshot(timeline, snapshot =>
                snapshot.Tick >= peak.Tick
                && snapshot.Team0MedianPrimary > 0f
                && snapshot.Team1MedianPrimary < 0f
                && snapshot.Team0CrossedFraction >= FinalCrossedFractionTarget
                && snapshot.Team1CrossedFraction >= FinalCrossedFractionTarget
                && snapshot.CenterCount <= GetCenterCountLimit(snapshot.LiveAgents)
                && snapshot.CenterStoppedAgents <= GetCenterStoppedCountLimit(snapshot.LiveAgents),
                out AvoidanceSnapshot clear);

            float finalCenterFraction = final.LiveAgents == 0 ? 0f : final.CenterCount / (float)final.LiveAgents;
            float finalCenterStoppedFraction = final.LiveAgents == 0 ? 0f : final.CenterStoppedAgents / (float)final.LiveAgents;

            AddAcceptanceCheck(start.Team0MedianPrimary < -3000f, $"Team 0 should spawn well left of center, but median X was {start.Team0MedianPrimary:F0}.", failures);
            AddAcceptanceCheck(start.Team1MedianPrimary > 3000f, $"Team 1 should spawn well right of center, but median X was {start.Team1MedianPrimary:F0}.", failures);
            AddAcceptanceCheck(hasContact, "The sequence never produced a meaningful center encounter.", failures);
            AddAcceptanceCheck(peak.CenterCount >= MeaningfulEncounterCenterCount, $"Peak center occupancy only reached {peak.CenterCount}, so the sides never truly collided.", failures);
            AddAcceptanceCheck(hasFirstCross, "No sampled frame shows both teams beginning to cross.", failures);
            AddAcceptanceCheck(hasMajorityCross, $"No sampled frame shows both teams reaching {MajorityCrossedFractionTarget:P0} crossed fraction.", failures);
            AddAcceptanceCheck(hasClear, "No sampled frame shows the center cleared after the crossing.", failures);
            AddAcceptanceCheck(final.Team0MedianPrimary > 0f, $"Team 0 median is still {final.Team0MedianPrimary:F0} at the tail; it never reached the opposite side.", failures);
            AddAcceptanceCheck(final.Team1MedianPrimary < 0f, $"Team 1 median is still {final.Team1MedianPrimary:F0} at the tail; it never reached the opposite side.", failures);
            AddAcceptanceCheck(final.Team0CrossedFraction >= FinalCrossedFractionTarget, $"Team 0 only reached {final.Team0CrossedFraction:P0} crossed fraction by the tail.", failures);
            AddAcceptanceCheck(final.Team1CrossedFraction >= FinalCrossedFractionTarget, $"Team 1 only reached {final.Team1CrossedFraction:P0} crossed fraction by the tail.", failures);
            AddAcceptanceCheck(finalCenterFraction < FinalCenterFractionLimit, $"Center box still contains {final.CenterCount}/{final.LiveAgents} agents at the tail ({finalCenterFraction:P0}).", failures);
            AddAcceptanceCheck(finalCenterStoppedFraction < FinalCenterStoppedFractionLimit, $"Center box still contains {final.CenterStoppedAgents}/{final.LiveAgents} stationary agents at the tail ({finalCenterStoppedFraction:P0}).", failures);

            string verdict = failures.Count == 0
                ? $"Full pass-through passes: contact starts at t{firstContact.Tick}, peak center is {peak.CenterCount} at t{peak.Tick}, both sides cross by t{majorityCross.Tick}, center clears by t{clear.Tick}, and the tail at t{final.Tick} remains separated."
                : "Full pass-through fails: the long screenshot run still does not prove a clean encounter-to-separation sequence.";
            string failureSummary = failures.Count == 0
                ? verdict
                : string.Join(Environment.NewLine, failures);

            return new AvoidanceAcceptanceResult(
                Success: failures.Count == 0,
                Verdict: verdict,
                FailureSummary: failureSummary,
                FailedChecks: failures.ToArray(),
                FinalCenterFraction: finalCenterFraction,
                FinalCenterStoppedFraction: finalCenterStoppedFraction,
                PeakCenterCount: peak.CenterCount,
                PeakCenterTick: peak.Tick,
                FirstContactTick: hasContact ? firstContact.Tick : null,
                FirstCrossTick: hasFirstCross ? firstCross.Tick : null,
                MajorityCrossTick: hasMajorityCross ? majorityCross.Tick : null,
                ClearTick: hasClear ? clear.Tick : null);
        }

        private static bool TryFindSnapshot(IReadOnlyList<AvoidanceSnapshot> timeline, Func<AvoidanceSnapshot, bool> predicate, out AvoidanceSnapshot snapshot)
        {
            for (int i = 0; i < timeline.Count; i++)
            {
                if (predicate(timeline[i]))
                {
                    snapshot = timeline[i];
                    return true;
                }
            }

            snapshot = default;
            return false;
        }

        private static int GetCenterCountLimit(int liveAgents)
        {
            return Math.Max(6, (int)Math.Ceiling(liveAgents * FinalCenterFractionLimit));
        }

        private static int GetCenterStoppedCountLimit(int liveAgents)
        {
            return Math.Max(1, (int)Math.Ceiling(liveAgents * FinalCenterStoppedFractionLimit));
        }

        private static string FormatTick(int? tick)
        {
            return tick.HasValue ? tick.Value.ToString() : "n/a";
        }

        private static void AddAcceptanceCheck(bool passed, string failure, List<string> failures)
        {
            if (!passed)
            {
                failures.Add(failure);
            }
        }
        private static SKPoint ToScreen(Vector2 world)
        {
            float x = (world.X - WorldMinX) / (WorldMaxX - WorldMinX) * ImageWidth;
            float y = (world.Y - WorldMinY) / (WorldMaxY - WorldMinY) * ImageHeight;
            return new SKPoint(x, ImageHeight - y);
        }

        private static SKRect ToScreenRect(float minX, float minY, float maxX, float maxY)
        {
            SKPoint a = ToScreen(new Vector2(minX, minY));
            SKPoint b = ToScreen(new Vector2(maxX, maxY));
            return SKRect.Create(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        }

        private static void DrawAgent(SKCanvas canvas, SKPaint paint, Vector2 world, float radiusPx)
        {
            SKPoint point = ToScreen(world);
            canvas.DrawCircle(point.X, point.Y, radiusPx, paint);
        }

        private static float Fraction(IReadOnlyList<Vector2> values, Func<Vector2, bool> predicate)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (predicate(values[i]))
                {
                    count++;
                }
            }

            return count / (float)values.Count;
        }

        private static float Median(float[] values)
        {
            if (values.Length == 0)
            {
                return 0f;
            }

            Array.Sort(values);
            int mid = values.Length / 2;
            return (values.Length & 1) != 0
                ? values[mid]
                : (values[mid - 1] + values[mid]) * 0.5f;
        }

        private static double Median(double[] values)
        {
            if (values.Length == 0)
            {
                return 0d;
            }

            Array.Sort(values);
            int mid = values.Length / 2;
            return (values.Length & 1) != 0
                ? values[mid]
                : (values[mid - 1] + values[mid]) * 0.5d;
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

        private static void AssertOverlayLines(ScreenOverlayBuffer overlay)
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

            string dump = string.Join(" || ", lines);
            Assert.That(dump.Contains("Navigation2D Playground", StringComparison.Ordinal), Is.True, dump);
            Assert.That(dump.Contains("FlowEnabled=", StringComparison.Ordinal), Is.True, dump);
            Assert.That(dump.Contains("CacheLookups=", StringComparison.Ordinal), Is.True, dump);
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

        private readonly record struct AvoidanceSnapshot(
            int Tick,
            string Step,
            string ScenarioName,
            int AgentsPerTeam,
            int LiveAgents,
            bool FlowEnabled,
            bool FlowDebugEnabled,
            double TickMs,
            IReadOnlyList<Vector2> Team0Positions,
            IReadOnlyList<Vector2> Team1Positions,
            IReadOnlyList<Vector2> BlockerPositions,
            float Team0MedianPrimary,
            float Team1MedianPrimary,
            float Team0CrossedFraction,
            float Team1CrossedFraction,
            int CenterCount,
            int CenterMovingAgents,
            int CenterStoppedAgents,
            int MovingAgents,
            int FlowActiveTiles,
            int FlowFrontierProcessed,
            bool FlowBudgetClamped,
            bool FlowWorldClamped);

        private readonly record struct CaptureFrame(
            int Tick,
            string Step,
            string FileName,
            int CenterCount,
            int CenterStoppedAgents,
            float Team0CrossedFraction,
            float Team1CrossedFraction);

        private sealed record AvoidanceAcceptanceResult(
            bool Success,
            string Verdict,
            string FailureSummary,
            IReadOnlyList<string> FailedChecks,
            float FinalCenterFraction,
            float FinalCenterStoppedFraction,
            int PeakCenterCount,
            int PeakCenterTick,
            int? FirstContactTick,
            int? FirstCrossTick,
            int? MajorityCrossTick,
            int? ClearTick);

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

