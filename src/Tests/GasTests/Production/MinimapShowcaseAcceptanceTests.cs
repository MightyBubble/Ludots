using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using MinimapControlMod;
using MinimapControlMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
public sealed class MinimapShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "minimap_showcase";

    private static readonly string[] AcceptanceMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FourXDemoMod",
        "MinimapControlMod",
        "MinimapShowcaseMod",
    };

    private sealed record SignalView(
        string Label,
        int TeamId,
        MinimapSignalKind Kind,
        MinimapSignalFlags Flags,
        float WorldXcm,
        float WorldYcm,
        float NormalizedX,
        float NormalizedY);

    private sealed record CellView(
        int CellX,
        int CellY,
        int Total,
        int Friendly,
        int Hostile,
        int Neutral,
        int Structures,
        int Objectives,
        int Resources,
        int Hazards);

    private sealed record SnapshotView(
        string MapId,
        string SelectedLabel,
        int PerspectiveTeamId,
        MinimapZoomBand ZoomBand,
        float CenterXcm,
        float CenterYcm,
        float HalfExtentCm,
        float MinWorldXcm,
        float MinWorldYcm,
        float MaxWorldXcm,
        float MaxWorldYcm,
        IReadOnlyList<SignalView> VisibleSignals,
        IReadOnlyList<CellView> StrategicCells);

    [Test]
    public void MinimapShowcase_WritesAcceptanceArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "minimap-showcase");
        string screensDir = Path.Combine(artifactDir, "screens");
        Directory.CreateDirectory(artifactDir);
        Directory.CreateDirectory(screensDir);

        var timeline = new List<string>();
        var traces = new List<object>();
        var frameTimesMs = new List<double>();

        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        RuntimeFacade runtime = RuntimeFacade.Resolve(engine);

        Tick(engine, 2, frameTimesMs);
        Assert.That(runtime.Visible, Is.True);
        Assert.That(runtime.SignalCount, Is.GreaterThanOrEqualTo(18));

        SnapshotView strategic = runtime.CaptureSnapshot();
        Assert.That(strategic.ZoomBand, Is.EqualTo(MinimapZoomBand.Strategic));
        WriteSnapshotSvg(strategic, Path.Combine(screensDir, "001_strategic_overview.svg"));
        timeline.Add("[T+001] Strategic overview locked to the whole theater; capitals, resources, hazards, and objective sectors aggregate into empire-scale cells.");
        traces.Add(new
        {
            step = "001_strategic_overview",
            band = strategic.ZoomBand.ToString(),
            selected = strategic.SelectedLabel,
            visible_signals = strategic.VisibleSignals.Count,
            strategic_cells = strategic.StrategicCells.Count,
            screenshot = "screens/001_strategic_overview.svg"
        });

        SelectNamedEntity(engine, "Frontier Bastion");
        runtime.SetViewport(9400f, 3600f, 7000f);
        Tick(engine, 1, frameTimesMs);
        SnapshotView regional = runtime.CaptureSnapshot();
        Assert.That(regional.ZoomBand, Is.EqualTo(MinimapZoomBand.Regional));
        Assert.That(regional.SelectedLabel, Is.EqualTo("Frontier Bastion"));
        WriteSnapshotSvg(regional, Path.Combine(screensDir, "002_regional_frontier.svg"));
        timeline.Add("[T+002] Regional zoom centers the frontier; bastion, fleet, border watch, and nearby neutral nodes remain readable without flooding the panel with every actor.");
        traces.Add(new
        {
            step = "002_regional_frontier",
            band = regional.ZoomBand.ToString(),
            selected = regional.SelectedLabel,
            visible_signals = regional.VisibleSignals.Count,
            strategic_cells = regional.StrategicCells.Count,
            screenshot = "screens/002_regional_frontier.svg"
        });

        SelectNamedEntity(engine, "Imperial Vanguard Fleet");
        runtime.SetViewport(7200f, 2900f, 1800f);
        Tick(engine, 1, frameTimesMs);
        SnapshotView tactical = runtime.CaptureSnapshot();
        Assert.That(tactical.ZoomBand, Is.EqualTo(MinimapZoomBand.Tactical));
        Assert.That(tactical.SelectedLabel, Is.EqualTo("Imperial Vanguard Fleet"));
        WriteSnapshotSvg(tactical, Path.Combine(screensDir, "003_tactical_vanguard.svg"));
        timeline.Add("[T+003] Tactical zoom collapses down to the selected fleet pocket; individual actors, alerts, and selection focus render as local command detail.");
        traces.Add(new
        {
            step = "003_tactical_vanguard",
            band = tactical.ZoomBand.ToString(),
            selected = tactical.SelectedLabel,
            visible_signals = tactical.VisibleSignals.Count,
            strategic_cells = tactical.StrategicCells.Count,
            screenshot = "screens/003_tactical_vanguard.svg"
        });

        File.WriteAllText(
            Path.Combine(artifactDir, "trace.jsonl"),
            string.Join(Environment.NewLine, traces.Select(trace => JsonSerializer.Serialize(trace))));
        File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, strategic, regional, tactical, frameTimesMs));
        File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
    }

    [Test]
    public void MinimapControlRuntime_RefreshAndRender_StayWithinZeroAllocBudget()
    {
        var frameTimesMs = new List<double>();
        using var engine = CreateEngine();
        LoadMap(engine, MapId, frameTimesMs);

        RuntimeFacade runtime = RuntimeFacade.Resolve(engine);
        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");

        for (int i = 0; i < 24; i++)
        {
            overlay.Clear();
            runtime.Refresh(engine);
            runtime.Render(overlay);
            Tick(engine, 1, frameTimesMs);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 96; i++)
        {
            overlay.Clear();
            runtime.Refresh(engine);
            runtime.Render(overlay);
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocatedBytes, Is.LessThanOrEqualTo(2048L), $"Expected zero-allocation SoA hot path budget, got {allocatedBytes} bytes.");
    }

    private static string BuildBattleReport(
        IReadOnlyList<string> timeline,
        SnapshotView strategic,
        SnapshotView regional,
        SnapshotView tactical,
        IReadOnlyList<double> frameTimesMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario: minimap-showcase");
        sb.AppendLine();
        sb.AppendLine("## Header");
        sb.AppendLine("- build: GasTests / MinimapShowcase_WritesAcceptanceArtifacts");
        sb.AppendLine("- map: minimap_showcase");
        sb.AppendLine("- clock: FixedFrame @ 60 Hz");
        sb.AppendLine("- screenshots: `screens/001_strategic_overview.svg`, `screens/002_regional_frontier.svg`, `screens/003_tactical_vanguard.svg`");
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        for (int i = 0; i < timeline.Count; i++)
        {
            sb.AppendLine(timeline[i]);
        }

        sb.AppendLine();
        sb.AppendLine("## Outcome");
        sb.AppendLine("- result: success");
        sb.AppendLine("- failure_branch: minimap overlay failed to switch zoom band, lost selection perspective, or exceeded the overlay hot-path budget");
        sb.AppendLine($"- final_band: {tactical.ZoomBand}");
        sb.AppendLine($"- final_selected: {tactical.SelectedLabel}");
        sb.AppendLine($"- strategic_visible: {strategic.VisibleSignals.Count}");
        sb.AppendLine($"- regional_visible: {regional.VisibleSignals.Count}");
        sb.AppendLine($"- tactical_visible: {tactical.VisibleSignals.Count}");
        sb.AppendLine();
        sb.AppendLine("## Summary Stats");
        sb.AppendLine($"- signal_pool: {strategic.VisibleSignals.Count}");
        sb.AppendLine($"- strategic_cells: {strategic.StrategicCells.Count}");
        sb.AppendLine($"- perspective_team: {strategic.PerspectiveTeamId}");
        sb.AppendLine($"- median_tick_ms: {Median(frameTimesMs):0.000}");
        sb.AppendLine($"- max_tick_ms: {(frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max()):0.000}");
        return sb.ToString();
    }

    private static string BuildPathMermaid()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "flowchart TD",
            "    A[\"Load minimap_showcase and enable MinimapControlMod runtime\"] --> B[\"Strategic viewport captures empire-scale cells\"]",
            "    B --> C[\"Select Frontier Bastion and zoom to regional viewport\"]",
            "    C --> D[\"Regional viewport keeps frontier structures, fleets, and neutral nodes readable\"]",
            "    D --> E[\"Select Imperial Vanguard Fleet and zoom to tactical viewport\"]",
            "    E --> F[\"Tactical viewport exposes local fleet pocket detail\"]",
            "    F --> G[\"Write screenshots, trace, and battle report\"]"
        });
    }

    private static void WriteSnapshotSvg(SnapshotView snapshot, string path)
    {
        const int width = 1200;
        const int height = 860;
        const int fieldX = 80;
        const int fieldY = 100;
        const int fieldSize = 620;

        var shapes = new List<string>
        {
            $"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#091018\" />",
            $"<rect x=\"40\" y=\"40\" width=\"1120\" height=\"780\" rx=\"24\" fill=\"#12202d\" stroke=\"#4d728a\" stroke-width=\"2\" />",
            $"<rect x=\"{fieldX}\" y=\"{fieldY}\" width=\"{fieldSize}\" height=\"{fieldSize}\" rx=\"10\" fill=\"#08141d\" stroke=\"#365264\" stroke-width=\"2\" />"
        };

        int gridStep = fieldSize / 4;
        for (int i = 1; i < 4; i++)
        {
            int offset = gridStep * i;
            shapes.Add($"<line x1=\"{fieldX + offset}\" y1=\"{fieldY}\" x2=\"{fieldX + offset}\" y2=\"{fieldY + fieldSize}\" stroke=\"#274051\" stroke-width=\"1\" />");
            shapes.Add($"<line x1=\"{fieldX}\" y1=\"{fieldY + offset}\" x2=\"{fieldX + fieldSize}\" y2=\"{fieldY + offset}\" stroke=\"#274051\" stroke-width=\"1\" />");
        }

        if (snapshot.ZoomBand == MinimapZoomBand.Strategic)
        {
            int cellSize = fieldSize / 12;
            foreach (CellView cell in snapshot.StrategicCells)
            {
                string fill = "#243544";
                if (cell.Hostile > cell.Friendly && cell.Hostile >= cell.Neutral) fill = "#572626";
                else if (cell.Friendly >= cell.Neutral) fill = "#174056";
                if (cell.Objectives > 0) fill = "#5d4830";
                if (cell.Resources > 0) fill = "#21554a";
                if (cell.Hazards > 0) fill = "#61332d";

                int x = fieldX + (cell.CellX * cellSize) + 2;
                int y = fieldY + (cell.CellY * cellSize) + 2;
                shapes.Add($"<rect x=\"{x}\" y=\"{y}\" width=\"{cellSize - 4}\" height=\"{cellSize - 4}\" rx=\"4\" fill=\"{fill}\" stroke=\"#162633\" stroke-width=\"1\" />");
                shapes.Add($"<text x=\"{x + 8}\" y=\"{y + 20}\" fill=\"#f7fafc\" font-size=\"12\" font-family=\"Consolas, monospace\">{cell.Total}</text>");
            }
        }

        foreach (SignalView signal in snapshot.VisibleSignals)
        {
            int x = fieldX + (int)MathF.Round(signal.NormalizedX * fieldSize);
            int y = fieldY + (int)MathF.Round(signal.NormalizedY * fieldSize);
            string color = ResolveSvgColor(signal.Flags);
            string icon = ResolveSvgIcon(signal.Kind);
            if ((signal.Flags & MinimapSignalFlags.Selected) != 0)
            {
                shapes.Add($"<rect x=\"{x - 12}\" y=\"{y - 14}\" width=\"26\" height=\"26\" rx=\"6\" fill=\"none\" stroke=\"#f6d56e\" stroke-width=\"2\" />");
            }

            shapes.Add($"<text x=\"{x - 4}\" y=\"{y + 5}\" fill=\"{color}\" font-size=\"16\" font-family=\"Consolas, monospace\">{EscapeSvg(icon)}</text>");
            if ((signal.Flags & MinimapSignalFlags.Alert) != 0)
            {
                shapes.Add($"<circle cx=\"{x + 14}\" cy=\"{y - 8}\" r=\"4\" fill=\"#ff695d\" />");
            }
        }

        string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="{{width}}" height="{{height}}" viewBox="0 0 {{width}} {{height}}">
  {{string.Join(Environment.NewLine + "  ", shapes)}}
  <text x="760" y="120" fill="#f7fafc" font-size="34" font-family="Consolas, monospace">4X Minimap Showcase</text>
  <text x="760" y="160" fill="#f6d56e" font-size="24" font-family="Consolas, monospace">Band: {{snapshot.ZoomBand}}</text>
  <text x="760" y="204" fill="#dde8f2" font-size="22" font-family="Consolas, monospace">Selected: {{EscapeSvg(snapshot.SelectedLabel)}}</text>
  <text x="760" y="248" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Perspective Team: {{snapshot.PerspectiveTeamId}}</text>
  <text x="760" y="280" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Viewport: center=({{snapshot.CenterXcm:0}}, {{snapshot.CenterYcm:0}}) extent={{snapshot.HalfExtentCm:0}}</text>
  <text x="760" y="330" fill="#f7fafc" font-size="20" font-family="Consolas, monospace">Visible Signals</text>
  <text x="760" y="360" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">{{snapshot.VisibleSignals.Count}} in current band</text>
  <text x="760" y="410" fill="#f7fafc" font-size="20" font-family="Consolas, monospace">Bounds</text>
  <text x="760" y="440" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">min=({{snapshot.MinWorldXcm:0}}, {{snapshot.MinWorldYcm:0}})</text>
  <text x="760" y="470" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">max=({{snapshot.MaxWorldXcm:0}}, {{snapshot.MaxWorldYcm:0}})</text>
  <text x="760" y="530" fill="#f7fafc" font-size="20" font-family="Consolas, monospace">Layer Semantics</text>
  <text x="760" y="560" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Strategic = sector aggregation</text>
  <text x="760" y="590" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Regional = structures + frontier traffic</text>
  <text x="760" y="620" fill="#9eb2c2" font-size="18" font-family="Consolas, monospace">Tactical = local actor detail</text>
  <text x="760" y="680" fill="#f7fafc" font-size="20" font-family="Consolas, monospace">Legend</text>
  <text x="760" y="710" fill="#68d2ff" font-size="18" font-family="Consolas, monospace">Friendly</text>
  <text x="900" y="710" fill="#f06f68" font-size="18" font-family="Consolas, monospace">Hostile</text>
  <text x="1030" y="710" fill="#7be0ca" font-size="18" font-family="Consolas, monospace">Resource</text>
  <text x="760" y="740" fill="#f6d56e" font-size="18" font-family="Consolas, monospace">Objective / Selected</text>
</svg>
""";
        File.WriteAllText(path, svg);
    }

    private static string ResolveSvgIcon(MinimapSignalKind kind)
    {
        return kind switch
        {
            MinimapSignalKind.Capital => "C",
            MinimapSignalKind.Settlement => "S",
            MinimapSignalKind.Fleet => "F",
            MinimapSignalKind.Army => "A",
            MinimapSignalKind.Scout => "s",
            MinimapSignalKind.Objective => "O",
            MinimapSignalKind.Resource => "R",
            MinimapSignalKind.Hazard => "!",
            _ => "P",
        };
    }

    private static string ResolveSvgColor(MinimapSignalFlags flags)
    {
        if ((flags & MinimapSignalFlags.Selected) != 0) return "#f6d56e";
        if ((flags & MinimapSignalFlags.Objective) != 0) return "#f6d56e";
        if ((flags & MinimapSignalFlags.Resource) != 0) return "#7be0ca";
        if ((flags & MinimapSignalFlags.Hazard) != 0) return "#ff7564";
        if ((flags & MinimapSignalFlags.Hostile) != 0) return "#f06f68";
        if ((flags & MinimapSignalFlags.Friendly) != 0) return "#68d2ff";
        return "#d7e3ed";
    }

    private sealed class RuntimeFacade
    {
        private readonly object _instance;
        private readonly Func<object, bool> _getVisible;
        private readonly Func<object, int> _getSignalCount;
        private readonly Action<object, GameEngine> _refresh;
        private readonly Action<object, ScreenOverlayBuffer> _render;
        private readonly Action<object, float, float, float> _setViewport;
        private readonly Func<object, object> _captureSnapshot;

        private RuntimeFacade(
            object instance,
            Func<object, bool> getVisible,
            Func<object, int> getSignalCount,
            Action<object, GameEngine> refresh,
            Action<object, ScreenOverlayBuffer> render,
            Action<object, float, float, float> setViewport,
            Func<object, object> captureSnapshot)
        {
            _instance = instance;
            _getVisible = getVisible;
            _getSignalCount = getSignalCount;
            _refresh = refresh;
            _render = render;
            _setViewport = setViewport;
            _captureSnapshot = captureSnapshot;
        }

        public bool Visible => _getVisible(_instance);

        public int SignalCount => _getSignalCount(_instance);

        public static RuntimeFacade Resolve(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(MinimapControlServiceKeys.Runtime.Name, out object? runtime) || runtime == null)
            {
                throw new InvalidOperationException("MinimapControlRuntime missing.");
            }

            Type runtimeType = runtime.GetType();
            return new RuntimeFacade(
                runtime,
                CompilePropertyGetter<bool>(runtimeType, "Visible"),
                CompilePropertyGetter<int>(runtimeType, "SignalCount"),
                CompileAction<GameEngine>(runtimeType, "Refresh"),
                CompileAction<ScreenOverlayBuffer>(runtimeType, "Render"),
                CompileAction<float, float, float>(runtimeType, "SetViewport"),
                CompileFunc(runtimeType, "CaptureDebugSnapshot"));
        }

        public void Refresh(GameEngine engine) => _refresh(_instance, engine);

        public void Render(ScreenOverlayBuffer overlay) => _render(_instance, overlay);

        public void SetViewport(float centerXcm, float centerYcm, float halfExtentCm) =>
            _setViewport(_instance, centerXcm, centerYcm, halfExtentCm);

        public SnapshotView CaptureSnapshot() => MapSnapshot(_captureSnapshot(_instance));

        private static Func<object, T> CompilePropertyGetter<T>(Type runtimeType, string propertyName)
        {
            PropertyInfo property = runtimeType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(runtimeType.FullName, propertyName);
            var instance = Expression.Parameter(typeof(object), "instance");
            var body = Expression.Convert(
                Expression.Property(Expression.Convert(instance, runtimeType), property),
                typeof(T));
            return Expression.Lambda<Func<object, T>>(body, instance).Compile();
        }

        private static Action<object, TArg> CompileAction<TArg>(Type runtimeType, string methodName)
        {
            MethodInfo method = runtimeType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, [typeof(TArg)])
                ?? throw new MissingMethodException(runtimeType.FullName, methodName);
            var instance = Expression.Parameter(typeof(object), "instance");
            var arg = Expression.Parameter(typeof(TArg), "arg");
            var body = Expression.Call(Expression.Convert(instance, runtimeType), method, arg);
            return Expression.Lambda<Action<object, TArg>>(body, instance, arg).Compile();
        }

        private static Action<object, TArg1, TArg2, TArg3> CompileAction<TArg1, TArg2, TArg3>(Type runtimeType, string methodName)
        {
            MethodInfo method = runtimeType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, [typeof(TArg1), typeof(TArg2), typeof(TArg3)])
                ?? throw new MissingMethodException(runtimeType.FullName, methodName);
            var instance = Expression.Parameter(typeof(object), "instance");
            var arg1 = Expression.Parameter(typeof(TArg1), "arg1");
            var arg2 = Expression.Parameter(typeof(TArg2), "arg2");
            var arg3 = Expression.Parameter(typeof(TArg3), "arg3");
            var body = Expression.Call(Expression.Convert(instance, runtimeType), method, arg1, arg2, arg3);
            return Expression.Lambda<Action<object, TArg1, TArg2, TArg3>>(body, instance, arg1, arg2, arg3).Compile();
        }

        private static Func<object, object> CompileFunc(Type runtimeType, string methodName)
        {
            MethodInfo method = runtimeType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
                ?? throw new MissingMethodException(runtimeType.FullName, methodName);
            var instance = Expression.Parameter(typeof(object), "instance");
            var body = Expression.Convert(
                Expression.Call(Expression.Convert(instance, runtimeType), method),
                typeof(object));
            return Expression.Lambda<Func<object, object>>(body, instance).Compile();
        }
    }

    private static SnapshotView MapSnapshot(object snapshot)
    {
        return new SnapshotView(
            GetProperty<string>(snapshot, "MapId"),
            GetProperty<string>(snapshot, "SelectedLabel"),
            GetProperty<int>(snapshot, "PerspectiveTeamId"),
            (MinimapZoomBand)Convert.ToByte(GetProperty<object>(snapshot, "ZoomBand")),
            GetProperty<float>(snapshot, "CenterXcm"),
            GetProperty<float>(snapshot, "CenterYcm"),
            GetProperty<float>(snapshot, "HalfExtentCm"),
            GetProperty<float>(snapshot, "MinWorldXcm"),
            GetProperty<float>(snapshot, "MinWorldYcm"),
            GetProperty<float>(snapshot, "MaxWorldXcm"),
            GetProperty<float>(snapshot, "MaxWorldYcm"),
            GetList(snapshot, "VisibleSignals", MapSignal),
            GetList(snapshot, "StrategicCells", MapCell));
    }

    private static SignalView MapSignal(object signal)
    {
        return new SignalView(
            GetProperty<string>(signal, "Label"),
            GetProperty<int>(signal, "TeamId"),
            (MinimapSignalKind)Convert.ToByte(GetProperty<object>(signal, "Kind")),
            (MinimapSignalFlags)Convert.ToUInt16(GetProperty<object>(signal, "Flags")),
            GetProperty<float>(signal, "WorldXcm"),
            GetProperty<float>(signal, "WorldYcm"),
            GetProperty<float>(signal, "NormalizedX"),
            GetProperty<float>(signal, "NormalizedY"));
    }

    private static CellView MapCell(object cell)
    {
        return new CellView(
            GetProperty<int>(cell, "CellX"),
            GetProperty<int>(cell, "CellY"),
            GetProperty<int>(cell, "Total"),
            GetProperty<int>(cell, "Friendly"),
            GetProperty<int>(cell, "Hostile"),
            GetProperty<int>(cell, "Neutral"),
            GetProperty<int>(cell, "Structures"),
            GetProperty<int>(cell, "Objectives"),
            GetProperty<int>(cell, "Resources"),
            GetProperty<int>(cell, "Hazards"));
    }

    private static IReadOnlyList<T> GetList<T>(object instance, string propertyName, Func<object, T> mapper)
    {
        object value = GetProperty<object>(instance, propertyName);
        if (value is not IEnumerable enumerable)
        {
            return Array.Empty<T>();
        }

        var items = new List<T>();
        foreach (object? entry in enumerable)
        {
            if (entry != null)
            {
                items.Add(mapper(entry));
            }
        }

        return items;
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        object? value = property.GetValue(instance);
        if (value is T typed)
        {
            return typed;
        }

        if (value == null)
        {
            return default!;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    private static void SelectNamedEntity(GameEngine engine, string entityName)
    {
        Entity entity = FindEntityByName(engine.World, entityName);
        Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{entityName}' was not found.");

        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new InvalidOperationException("SelectionRuntime missing.");
        Entity viewer = engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) && viewerObj is Entity local
            ? local
            : entity;

        selection.ReplaceSelection(viewer, SelectionSetKeys.LivePrimary, stackalloc[] { entity });
        selection.TryBindView(viewer, SelectionViewKeys.Primary, viewer, SelectionSetKeys.LivePrimary);
        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = viewer;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        InstallInput(engine);
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController(1920f, 1080f));
        engine.SetService(CoreServiceKeys.ScreenProjector, new WorldMappedScreenProjector());
        engine.SetService(CoreServiceKeys.ScreenRayProvider, new WorldMappedScreenRayProvider());
        return engine;
    }

    private static void InstallInput(GameEngine engine)
    {
        var backend = new TestInputBackend();
        engine.SetService(CoreServiceKeys.InputBackend, backend);
        engine.GlobalContext["Tests.MinimapShowcase.InputBackend"] = backend;
    }

    private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs)
    {
        engine.LoadMap(mapId);
        engine.Start();
        Tick(engine, 3, frameTimesMs);
    }

    private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
    {
        var backend = engine.GlobalContext["Tests.MinimapShowcase.InputBackend"] as TestInputBackend;
        for (int i = 0; i < frames; i++)
        {
            if (backend != null)
            {
                backend.SetMouseWheel(0f);
            }

            long start = Stopwatch.GetTimestamp();
            engine.Tick(DeltaTime);
            long end = Stopwatch.GetTimestamp();
            frameTimesMs.Add((end - start) * 1000d / Stopwatch.Frequency);
        }
    }

    private static Entity FindEntityByName(World world, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name entityName) =>
        {
            if (found == Entity.Null && string.Equals(entityName.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });
        return found;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) * 0.5d
            : ordered[middle];
    }

    private static string EscapeSvg(string? value)
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

    private sealed class TestInputBackend : IInputBackend
    {
        private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
        private Vector2 _mousePosition;
        private float _mouseWheel;

        public void SetButton(string path, bool isDown) => _buttons[path] = isDown;
        public void SetMousePosition(Vector2 position) => _mousePosition = position;
        public void SetMouseWheel(float wheel) => _mouseWheel = wheel;

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
        public Vector2 GetMousePosition() => _mousePosition;
        public float GetMouseWheel() => _mouseWheel;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private sealed class StubViewController : IViewController
    {
        public StubViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }
        public float Fov => 60f;
        public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
    }

    private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
    {
        public ScreenRay GetRay(Vector2 screenPosition)
        {
            return new ScreenRay(
                new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                -Vector3.UnitY);
        }
    }

    private sealed class WorldMappedScreenProjector : IScreenProjector
    {
        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
        }
    }
}
