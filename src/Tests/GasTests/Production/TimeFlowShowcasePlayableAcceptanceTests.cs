using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Tests;
using NUnit.Framework;
using TimeFlowShowcaseMod;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
[NonParallelizable]
public sealed class TimeFlowShowcasePlayableAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "TimeFlowMod",
        "TimeFlowShowcaseMod"
    };

    [Test]
    public void TimeFlowShowcase_PlayableFlow_WritesAcceptanceArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "timeflow-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(screensDir);

        var frames = new List<CaptureFrame>();
        var timeline = new List<string>();
        var frameTimesMs = new List<double>();

        using var engine = CreateEngine(repoRoot);
        object runtime = engine.GlobalContext.TryGetValue(TimeFlowShowcaseServiceKeys.Service.Name, out object? runtimeObj) && runtimeObj != null
            ? runtimeObj
            : throw new InvalidOperationException("TimeFlowShowcase runtime service was not installed.");

        foreach (string mapId in TimeFlowShowcaseIds.AllMapIds)
        {
            LoadScenario(engine, mapId, frameTimesMs);
            Capture(runtime, frames, mapId, "loaded", frameTimesMs);
            timeline.Add($"[LOAD] {mapId} -> {frames[^1].Snapshot.Phase} | profile={frames[^1].Snapshot.TimeFlow.ActiveProfileId}");

            TickUntil(engine, frameTimesMs, () => ReachedFocalState(ReadSnapshot(runtime)), 240);
            Capture(runtime, frames, mapId, "focal", frameTimesMs);
            Assert.That(ReachedFocalState(frames[^1].Snapshot), Is.True, $"Scenario {mapId} failed to reach its focal timeflow state.");
            timeline.Add($"[FOCAL] {mapId} -> {frames[^1].Snapshot.Phase} | sim={frames[^1].Snapshot.TimeFlow.SimulationScalePermille} | profile={frames[^1].Snapshot.TimeFlow.ActiveProfileId}");

            TickUntil(engine, frameTimesMs, () => ReachedResolvedState(ReadSnapshot(runtime)), 360);
            Capture(runtime, frames, mapId, "resolved", frameTimesMs);
            Assert.That(ReachedResolvedState(frames[^1].Snapshot), Is.True, $"Scenario {mapId} failed to reach its resolved state.");
            timeline.Add($"[DONE] {mapId} -> {frames[^1].Snapshot.Phase} | events={frames[^1].Snapshot.RecentEvents.Count}");
        }

        File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(frames));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, frames, frameTimesMs));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
        WriteScreens(frames, screensDir);
    }

    private static GameEngine CreateEngine(string repoRoot)
    {
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        engine.Start();
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        return engine;
    }

    private static void LoadScenario(GameEngine engine, string mapId, List<double> frameTimesMs)
    {
        if (engine.CurrentMapSession != null)
        {
            engine.UnloadMap(engine.CurrentMapSession.MapId.Value);
            Tick(engine, 2, frameTimesMs);
        }

        engine.LoadMap(mapId);
        Tick(engine, 4, frameTimesMs);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), $"Trigger errors while loading {mapId}.");
    }

    private static void Capture(object runtime, List<CaptureFrame> frames, string mapId, string step, IReadOnlyList<double> frameTimesMs)
    {
        SnapshotView snapshot = ReadSnapshot(runtime)
            ?? throw new InvalidOperationException($"Snapshot missing for map '{mapId}'.");
        frames.Add(new CaptureFrame(
            MapId: mapId,
            Step: step,
            Snapshot: snapshot,
            TickMs: frameTimesMs.Count == 0 ? 0d : frameTimesMs[^1]));
    }

    private static bool ReachedFocalState(SnapshotView? snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        return snapshot.MapId switch
        {
            TimeFlowShowcaseIds.AtbWaitMapId => snapshot.Phase == "ATB.CommandPause",
            TimeFlowShowcaseIds.DotaUltMapId => snapshot.Phase == "Dota.BulletTime",
            TimeFlowShowcaseIds.BreakFeverMapId => snapshot.Phase == "Break.Fever",
            TimeFlowShowcaseIds.SentinelPauseMapId => snapshot.Phase == "Sentinel.CommandPause",
            TimeFlowShowcaseIds.Ck3MacroMapId => snapshot.Phase == "CK3.EventPause" || snapshot.Phase == "CK3.Speed4",
            TimeFlowShowcaseIds.BadNorthMapId => snapshot.Phase == "BadNorth.RevectorPause" || snapshot.Phase == "BadNorth.Finish",
            _ => false
        };
    }

    private static bool ReachedResolvedState(SnapshotView? snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        return snapshot.MapId switch
        {
            TimeFlowShowcaseIds.AtbWaitMapId => snapshot.Phase == "ATB.Realtime" && snapshot.RecentEvents.Any(e => e.Contains("resume realtime", StringComparison.OrdinalIgnoreCase)),
            TimeFlowShowcaseIds.DotaUltMapId => snapshot.Phase == "Dota.AutoBattle" && snapshot.RecentEvents.Any(e => e.Contains("Bullet time expired", StringComparison.OrdinalIgnoreCase)),
            TimeFlowShowcaseIds.BreakFeverMapId => snapshot.Phase == "Break.Build" && snapshot.RecentEvents.Any(e => e.Contains("Fever window ended", StringComparison.OrdinalIgnoreCase)),
            TimeFlowShowcaseIds.SentinelPauseMapId => snapshot.Phase == "Sentinel.Realtime" && snapshot.RecentEvents.Any(e => e.Contains("resume", StringComparison.OrdinalIgnoreCase)),
            TimeFlowShowcaseIds.Ck3MacroMapId => snapshot.Phase == "CK3.Complete",
            TimeFlowShowcaseIds.BadNorthMapId => snapshot.Phase == "BadNorth.Complete",
            _ => false
        };
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        for (int i = 0; i < frames; i++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            engine.Tick(DeltaTime);
            frameTimesMs.Add((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000d / System.Diagnostics.Stopwatch.Frequency);
        }
    }

    private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxFrames)
    {
        if (predicate())
        {
            return;
        }

        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1, frameTimesMs);
            if (predicate())
            {
                return;
            }
        }
    }

    private static string BuildTraceJsonl(IReadOnlyList<CaptureFrame> frames)
    {
        var lines = new List<string>(frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            CaptureFrame frame = frames[i];
            lines.Add(JsonSerializer.Serialize(new
            {
                event_id = $"timeflow-showcase-{i + 1:000}",
                map_id = frame.MapId,
                step = frame.Step,
                    scenario = frame.Snapshot.ScenarioKind,
                phase = frame.Snapshot.Phase,
                profile = frame.Snapshot.TimeFlow.ActiveProfileId,
                sim_scale_permille = frame.Snapshot.TimeFlow.SimulationScalePermille,
                gas_scale_permille = frame.Snapshot.TimeFlow.GasScalePermille,
                physics_scale_permille = frame.Snapshot.TimeFlow.PhysicsScalePermille,
                navigation_scale_permille = frame.Snapshot.TimeFlow.NavigationScalePermille,
                loop_mode = frame.Snapshot.TimeFlow.LoopMode.ToString(),
                actors = frame.Snapshot.Actors.Select(actor => new
                {
                    actor.Name,
                    actor.Team,
                    actor.Health,
                    actor.Charge,
                    actor.Energy,
                    actor.WaitTicks,
                    actor.OrdersQueued,
                    actor.X,
                    actor.Y
                }),
                recent_events = frame.Snapshot.RecentEvents,
                tick_ms = Math.Round(frame.TickMs, 4),
                status = "done"
            }));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<CaptureFrame> frames, IReadOnlyList<double> frameTimesMs)
    {
        double medianTickMs = Median(frameTimesMs);
        double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();

        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: timeflow-showcase");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: open a time mini-game pack and understand six representative pause / bullet-time / multi-rate patterns without reading a debug console.");
        sb.AppendLine("- Gameplay domain: shared Core time domains, TimeFlow capability profiles, pacemaker coordination, GAS step pacing, and player-facing showcase presentation.");
        sb.AppendLine();
        sb.AppendLine("## Determinism Inputs");
        sb.AppendLine("- Mods: `LudotsCoreMod`, `TimeFlowMod`, `TimeFlowShowcaseMod`");
        sb.AppendLine($"- Maps: `{string.Join("`, `", TimeFlowShowcaseIds.AllMapIds)}`");
        sb.AppendLine("- Clock: fixed `1/60s` via `GameEngine.Tick()`.");
        sb.AppendLine("- Input source: none; each mini-game demonstrates its timing idea through a deterministic production-runtime script.");
        sb.AppendLine();
        sb.AppendLine("## Scenarios");
        sb.AppendLine("- `timeflow_atb_wait`: wait-mode ATB pause/resume.");
        sb.AppendLine("- `timeflow_dota_manual_ult`: auto-battle energy freeze into bullet time.");
        sb.AppendLine("- `timeflow_break_fever`: break meter build into fever burst window.");
        sb.AppendLine("- `timeflow_sentinel_pause`: WT-ready command pause and resume.");
        sb.AppendLine("- `timeflow_ck3_macro`: macro pause plus 1x/2x/3x/event-pause/4x ladder.");
        sb.AppendLine("- `timeflow_bad_north`: active pause, resume, re-pause, and lane correction.");
        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine("- `artifacts/acceptance/timeflow-showcase/trace.jsonl`");
        sb.AppendLine("- `artifacts/acceptance/timeflow-showcase/battle-report.md`");
        sb.AppendLine("- `artifacts/acceptance/timeflow-showcase/path.mmd`");
        sb.AppendLine("- `artifacts/acceptance/timeflow-showcase/screens/*.svg`");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        foreach (string entry in timeline)
        {
            sb.AppendLine($"- {entry}");
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- success: yes");
        sb.AppendLine($"- frames captured: `{frames.Count}`");
        sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
        sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
        sb.AppendLine("- verdict: all six mini-games reached both their focal timing state and their resolved timing payoff on the shared Core timeflow path.");
        return sb.ToString();
    }

    private static string BuildPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[Load timeflow_atb_wait] --> B[ATB gauge fills -> wait pause -> resume]",
            "    B --> C[Load timeflow_dota_manual_ult]",
            "    C --> D[Energy full -> ult freeze -> bullet time -> baseline]",
            "    D --> E[Load timeflow_break_fever]",
            "    E --> F[Break build -> fever burst -> restore baseline]",
            "    F --> G[Load timeflow_sentinel_pause]",
            "    G --> H[WT ready -> command pause -> resume]",
            "    H --> I[Load timeflow_ck3_macro]",
            "    I --> J[Pause -> 1x/2x/3x -> event pause -> 4x -> complete]",
            "    J --> K[Load timeflow_bad_north]",
            "    K --> L[Active pause -> resume -> revector pause -> final resume]"
        }) + Environment.NewLine;
    }

    private static void WriteScreens(IReadOnlyList<CaptureFrame> frames, string screensDir)
    {
        for (int i = 0; i < frames.Count; i++)
        {
            CaptureFrame frame = frames[i];
            string path = Path.Combine(screensDir, $"{i + 1:000}_{frame.MapId}_{frame.Step}.svg");
            File.WriteAllText(path, BuildSnapshotSvg(frame));
        }

        File.WriteAllText(Path.Combine(screensDir, "timeline.svg"), BuildTimelineSvg(frames));
    }

    private static string BuildSnapshotSvg(CaptureFrame frame)
    {
        TimeFlowScenarioKind kind = ParseScenarioKind(frame.Snapshot.ScenarioKind);
        TimeFlowMiniGameDescriptor descriptor = TimeFlowShowcaseMiniGames.Describe(kind);
        string cast = DescribeCast(frame.Snapshot);
        string beat = DescribeBeat(frame.Snapshot, kind);
        string shift = DescribeTimeShift(frame.Snapshot.TimeFlow);
        string actors = string.Join(" | ", frame.Snapshot.Actors.Select(actor =>
            $"{actor.Name} side {actor.Team} hp={actor.Health:0} charge={actor.Charge:0} energy={actor.Energy:0} wt={actor.WaitTicks} orders={actor.OrdersQueued}"));
        string events = string.Join(" | ", frame.Snapshot.RecentEvents);

        return $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="900" viewBox="0 0 1600 900">
  <rect width="1600" height="900" fill="#0b1218" />
  <rect x="40" y="40" width="1520" height="820" rx="20" fill="#14212e" stroke="#5aa5d8" stroke-width="2" />
  <text x="72" y="102" fill="#f7d36d" font-size="34" font-family="Consolas, monospace">Mini-Game Snapshot | {{Escape(descriptor.MenuTitle)}} | {{Escape(frame.Step)}}</text>
  <text x="72" y="148" fill="#ffffff" font-size="24" font-family="Consolas, monospace">{{Escape(descriptor.Pitch)}}</text>
  <text x="72" y="194" fill="#bccdde" font-size="20" font-family="Consolas, monospace">Goal: {{Escape(descriptor.Goal)}}</text>
  <text x="72" y="230" fill="#bccdde" font-size="20" font-family="Consolas, monospace">Watch For: {{Escape(descriptor.WatchFor)}}</text>
  <text x="72" y="286" fill="#8eeac8" font-size="22" font-family="Consolas, monospace">Current Beat: {{Escape(beat)}}</text>
  <text x="72" y="322" fill="#ffffff" font-size="22" font-family="Consolas, monospace">{{Escape(shift)}}</text>
  <text x="72" y="358" fill="#ffffff" font-size="22" font-family="Consolas, monospace">Now: {{Escape(frame.Snapshot.StatusLine)}}</text>
  <text x="72" y="394" fill="#bccdde" font-size="20" font-family="Consolas, monospace">Cast: {{Escape(cast)}}</text>
  <text x="72" y="430" fill="#bccdde" font-size="20" font-family="Consolas, monospace">Win State: {{Escape(descriptor.Success)}}</text>
  <text x="72" y="486" fill="#f7d36d" font-size="24" font-family="Consolas, monospace">Actors</text>
  <text x="72" y="524" fill="#bccdde" font-size="18" font-family="Consolas, monospace">{{Escape(actors)}}</text>
  <text x="72" y="602" fill="#f7d36d" font-size="24" font-family="Consolas, monospace">Story Log</text>
  <text x="72" y="640" fill="#bccdde" font-size="18" font-family="Consolas, monospace">{{Escape(events)}}</text>
  <text x="72" y="718" fill="#bccdde" font-size="18" font-family="Consolas, monospace">Tech: phase={{Escape(frame.Snapshot.Phase)}} | profile={{Escape(frame.Snapshot.TimeFlow.ActiveProfileId)}} | sim={{frame.Snapshot.TimeFlow.SimulationScalePermille}} | gas={{frame.Snapshot.TimeFlow.GasScalePermille}} | nav={{frame.Snapshot.TimeFlow.NavigationScalePermille}} | tickMs={{frame.TickMs:F3}}</text>
</svg>
""";
    }

    private static string BuildTimelineSvg(IReadOnlyList<CaptureFrame> frames)
    {
        var rows = new List<string>();
        int y = 96;
        for (int i = 0; i < frames.Count; i++)
        {
            CaptureFrame frame = frames[i];
            rows.Add($"""  <rect x="40" y="{y - 34}" width="1520" height="68" rx="12" fill="#14212e" stroke="#35536b" stroke-width="1.5" />""");
            rows.Add($"""  <text x="72" y="{y}" fill="#f7d36d" font-size="22" font-family="Consolas, monospace">{Escape($"{i + 1:000} {frame.MapId} {frame.Step}")}</text>""");
            rows.Add($"""  <text x="520" y="{y}" fill="#ffffff" font-size="18" font-family="Consolas, monospace">{Escape($"phase={frame.Snapshot.Phase} | profile={frame.Snapshot.TimeFlow.ActiveProfileId} | sim={frame.Snapshot.TimeFlow.SimulationScalePermille}")}</text>""");
            y += 84;
        }

        return $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="{{Math.Max(240, y + 20)}}" viewBox="0 0 1600 {{Math.Max(240, y + 20)}}">
  <rect width="1600" height="{{Math.Max(240, y + 20)}}" fill="#080a10" />
  <text x="20" y="36" fill="#ffffff" font-size="28" font-family="Consolas, monospace">TimeFlow showcase timeline</text>
{{string.Join(Environment.NewLine, rows)}}
</svg>
""";
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repo root from test output directory.");
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        double[] sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return (sorted.Length & 1) != 0
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) * 0.5d;
    }

    private static SnapshotView? ReadSnapshot(object runtime)
    {
        object? raw = runtime.GetType().GetMethod("GetSnapshot", BindingFlags.Instance | BindingFlags.Public)?.Invoke(runtime, Array.Empty<object>());
        if (raw == null)
        {
            return null;
        }

        object? rawTimeFlow = ReadProperty(raw, "TimeFlow");
        var actors = new List<ActorView>();
        foreach (object actor in ReadEnumerable(ReadProperty(raw, "Actors")))
        {
            actors.Add(new ActorView(
                Name: ReadString(actor, "Name"),
                Team: ReadInt(actor, "Team"),
                Health: ReadFloat(actor, "Health"),
                Charge: ReadFloat(actor, "Charge"),
                Energy: ReadFloat(actor, "Energy"),
                WaitTicks: ReadInt(actor, "WaitTicks"),
                OrdersQueued: ReadInt(actor, "OrdersQueued"),
                X: ReadFloat(actor, "X"),
                Y: ReadFloat(actor, "Y")));
        }

        return new SnapshotView(
            MapId: ReadString(raw, "MapId"),
            ScenarioKind: ReadString(raw, "ScenarioKind"),
            ScenarioTitle: ReadString(raw, "ScenarioTitle"),
            InspirationLine: ReadString(raw, "InspirationLine"),
            Phase: ReadString(raw, "Phase"),
            FixedTick: ReadInt(raw, "FixedTick"),
            PresentationFrame: ReadInt(raw, "PresentationFrame"),
            StatusLine: ReadString(raw, "StatusLine"),
            TimeFlow: new TimeFlowView(
                ActiveProfileId: ReadString(rawTimeFlow, "ActiveProfileId"),
                SimulationScalePermille: ReadInt(rawTimeFlow, "SimulationScalePermille"),
                GasScalePermille: ReadInt(rawTimeFlow, "GasScalePermille"),
                PhysicsScalePermille: ReadInt(rawTimeFlow, "PhysicsScalePermille"),
                NavigationScalePermille: ReadInt(rawTimeFlow, "NavigationScalePermille"),
                PhysicsTargetHz: ReadInt(rawTimeFlow, "PhysicsTargetHz"),
                PhysicsMaxStepsPerFixedTick: ReadInt(rawTimeFlow, "PhysicsMaxStepsPerFixedTick"),
                NavigationTargetHz: ReadInt(rawTimeFlow, "NavigationTargetHz"),
                NavigationMaxStepsPerFixedTick: ReadInt(rawTimeFlow, "NavigationMaxStepsPerFixedTick"),
                LoopMode: ReadString(rawTimeFlow, "LoopMode")),
            Actors: actors,
            RecentEvents: ReadEnumerable(ReadProperty(raw, "RecentEvents")).Select(item => item.ToString() ?? string.Empty).ToArray());
    }

    private static IEnumerable<object> ReadEnumerable(object? value)
    {
        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private static object? ReadProperty(object? instance, string name)
    {
        return instance?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
    }

    private static string ReadString(object? instance, string name)
    {
        return ReadProperty(instance, name)?.ToString() ?? string.Empty;
    }

    private static int ReadInt(object? instance, string name)
    {
        object? value = ReadProperty(instance, name);
        return value is int i ? i : 0;
    }

    private static float ReadFloat(object? instance, string name)
    {
        object? value = ReadProperty(instance, name);
        return value switch
        {
            float f => f,
            double d => (float)d,
            _ => 0f
        };
    }

    private static TimeFlowScenarioKind ParseScenarioKind(string raw)
    {
        return Enum.TryParse<TimeFlowScenarioKind>(raw, ignoreCase: true, out TimeFlowScenarioKind parsed)
            ? parsed
            : TimeFlowScenarioKind.AtbWait;
    }

    private static string DescribeBeat(SnapshotView snapshot, TimeFlowScenarioKind kind)
    {
        return (kind, snapshot.Phase) switch
        {
            (TimeFlowScenarioKind.AtbWait, "ATB.Realtime") => "Gauge race in live realtime.",
            (TimeFlowScenarioKind.AtbWait, "ATB.CommandPause") => "Command window open and time is frozen.",

            (TimeFlowScenarioKind.DotaManualUlt, "Dota.AutoBattle") => "Auto-battle trading at normal speed.",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.UltFreeze") => "Ultimate confirm freeze frame.",
            (TimeFlowScenarioKind.DotaManualUlt, "Dota.BulletTime") => "Slow-motion aftermath after the ult.",

            (TimeFlowScenarioKind.BreakFever, "Break.Build") => "Building the break meter.",
            (TimeFlowScenarioKind.BreakFever, "Break.Fever") => "Fever burst is active.",

            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.Realtime") => "WT timers ticking in realtime.",
            (TimeFlowScenarioKind.SentinelCommandPause, "Sentinel.CommandPause") => "Ready pilot pause window.",

            (TimeFlowScenarioKind.Ck3Macro, "CK3.Pause") => "Realm clock paused for planning.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed1") => "Campaign moving at 1x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed2") => "Campaign moving at 2x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed3") => "Campaign moving at 3x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.EventPause") => "Event card stopped the realm clock.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Speed4") => "Quiet stretch fast-forward at 4x.",
            (TimeFlowScenarioKind.Ck3Macro, "CK3.Complete") => "Campaign ladder finished.",

            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.ActivePause") => "Opening active-pause command window.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Realtime") => "Squads marching in live combat.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.RevectorPause") => "Emergency pause for lane correction.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Finish") => "Final realtime cleanup after the retarget.",
            (TimeFlowScenarioKind.BadNorthActivePause, "BadNorth.Complete") => "Island defense stabilized.",

            _ => snapshot.StatusLine
        };
    }

    private static string DescribeTimeShift(TimeFlowView timeFlow)
    {
        if (timeFlow.SimulationScalePermille <= 0)
        {
            return "Time shift: full stop across simulation, GAS, physics, and navigation.";
        }

        if (timeFlow.SimulationScalePermille < 1000)
        {
            string world = $"{timeFlow.SimulationScalePermille / 10f:0}%";
            if (timeFlow.NavigationScalePermille != timeFlow.SimulationScalePermille)
            {
                string nav = $"{timeFlow.NavigationScalePermille / 10f:0}%";
                return $"Time shift: world slowed to {world}; navigation is separately throttled to {nav}.";
            }

            return $"Time shift: world slowed to {world} while the battle keeps advancing.";
        }

        return "Time shift: baseline realtime, no global slowdown active.";
    }

    private static string DescribeCast(SnapshotView snapshot)
    {
        return string.Join("  vs  ", snapshot.Actors
            .GroupBy(actor => actor.Team)
            .OrderBy(group => group.Key)
            .Select(group => string.Join(", ", group.Select(actor => actor.Name))));
    }

    private sealed record CaptureFrame(
        string MapId,
        string Step,
        SnapshotView Snapshot,
        double TickMs);

    private sealed record SnapshotView(
        string MapId,
        string ScenarioKind,
        string ScenarioTitle,
        string InspirationLine,
        string Phase,
        int FixedTick,
        int PresentationFrame,
        string StatusLine,
        TimeFlowView TimeFlow,
        IReadOnlyList<ActorView> Actors,
        IReadOnlyList<string> RecentEvents);

    private sealed record TimeFlowView(
        string ActiveProfileId,
        int SimulationScalePermille,
        int GasScalePermille,
        int PhysicsScalePermille,
        int NavigationScalePermille,
        int PhysicsTargetHz,
        int PhysicsMaxStepsPerFixedTick,
        int NavigationTargetHz,
        int NavigationMaxStepsPerFixedTick,
        string LoopMode);

    private sealed record ActorView(
        string Name,
        int Team,
        float Health,
        float Charge,
        float Energy,
        int WaitTicks,
        int OrdersQueued,
        float X,
        float Y);
}
