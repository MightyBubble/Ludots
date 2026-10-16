using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using SkiaSharp;

namespace CrowdSimulationTests.Showcases;

/// <summary>
/// 按启动器预设的 Mod 链起真实引擎、进种子图，脚本由会话宿主自己跑。
/// <see cref="Advance"/> 每次只推进一个会话 tick，测试在 tick 之间读状态。
/// </summary>
internal sealed class CrowdShowcaseHost : IDisposable
{
    private const string MapId = "crowd_simulation_s1337";
    private const float FrameSeconds = 1f / 60f;
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(60);

    private static readonly string[] Chain =
    {
        Path.Combine("mods", "LudotsCoreMod"),
        Path.Combine("mods", "capabilities", "navigation", "CrowdSimulationMod"),
        Path.Combine("mods", "capabilities", "navigation", "CrowdSimulationUiMod"),
        Path.Combine("mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
        Path.Combine("mods", "showcases", "crowd_simulation", "CrowdSimulationDemoContentMod"),
        Path.Combine("mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
        Path.Combine("mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod"),
        Path.Combine("mods", "showcases", "crowd_simulation", "CrowdSimulationShowcaseStageMod"),
    };

    private readonly SKSurface _uiSurface;

    private CrowdShowcaseHost(GameEngine engine, CrowdSimSession session, JsonArray script, UIRoot ui, UiSurfaceHost uiHost, SKSurface uiSurface)
    {
        Engine = engine;
        Session = session;
        Script = script;
        Ui = ui;
        UiHost = uiHost;
        _uiSurface = uiSurface;
    }

    public GameEngine Engine { get; }
    /// <summary>和 Raylib 宿主同一套 UI 服务；HUD 由链上的 CrowdSimulationUiMod 在呈现帧里挂上。</summary>
    public UIRoot Ui { get; }
    public UiSurfaceHost UiHost { get; }
    public CrowdSimSession Session { get; }
    /// <summary>合并后的 session.script。舞台清空 S4 的脚本，场景 Mod 整段替换。</summary>
    public JsonArray Script { get; }

    public static CrowdShowcaseHost Start(string sceneMod)
    {
        string repo = RepoRoot();
        var mods = Chain.Select(m => Path.Combine(repo, m)).ToList();
        mods.Add(SceneDir(repo, sceneMod));
        var engine = new GameEngine();
        var uiSurface = SKSurface.Create(new SKImageInfo(1280, 720));
        try
        {
            engine.InitializeWithConfigPipeline(mods, Path.Combine(repo, "assets"));
            engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
            engine.SetService(CoreServiceKeys.ViewController, new StubView());
            var renderer = new SkiaUiRenderer();
            renderer.SetCanvas(uiSurface.Canvas);
            var ui = new UIRoot(renderer);
            ui.Resize(1280, 720);
            var textMeasurer = new SkiaTextMeasurer();
            var imageSizeProvider = new SkiaImageSizeProvider();
            var uiHost = new UiSurfaceHost(ui, textMeasurer, imageSizeProvider);
            engine.SetService(CoreServiceKeys.UIRoot, ui);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, textMeasurer);
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, imageSizeProvider);
            engine.SetService(CoreServiceKeys.UiSurfaceHost, uiHost);
            engine.Start();
            engine.LoadMap(MapId);
            var session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)
                ?? throw new InvalidOperationException($"{sceneMod}: 进图后没有群体会话。");
            if (session.TickCount != 0) throw new InvalidOperationException($"{sceneMod}: 进图时会话已在 tick {session.TickCount}。");
            var debug = CrowdSimulationDebugConfig.Load(MergedConfig(engine, "CrowdSimulationDebug.json"));
            var rows = CrowdSimulationDebugConfig.SessionScript(debug.Session, rel => CrowdSimulationRuntime.OpenAsset(engine, rel))
                ?? throw new InvalidOperationException($"{sceneMod}: 合并后的 CrowdSimulationDebug.json 没有会话脚本。");
            var script = CrowdSimulationRuntime.ComposeDemoScript(session.Config.Deploy.InitialUnits, rows);
            return new CrowdShowcaseHost(engine, session, script, ui, uiHost, uiSurface);
        }
        catch
        {
            engine.Dispose();
            uiSurface.Dispose();
            throw;
        }
    }

    /// <summary>按 Raylib 合成器的规则：UI 脏了才渲染，然后读 HUD 节点及其子孙的文字。</summary>
    public string HudText(string elementId)
    {
        if (Ui.IsDirty) Ui.Render();
        UiNode node = UiHost.Scene?.FindByElementId(elementId)
            ?? throw new InvalidOperationException($"UI 文档里没有 #{elementId}。");
        var sb = new StringBuilder();
        AppendText(node, sb);
        return sb.ToString();
    }

    private static void AppendText(UiNode node, StringBuilder sb)
    {
        if (!string.IsNullOrEmpty(node.TextContent))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(node.TextContent);
        }

        for (int i = 0; i < node.Children.Count; i++) AppendText(node.Children[i], sb);
    }

    /// <summary>按目录登记的 DeepObject 合并读出本次 Mod 链上的配置。</summary>
    public static JsonObject MergedConfig(GameEngine engine, string path)
    {
        var entry = ConfigPipeline.RequireEntry(engine.ConfigCatalog, path, ConfigMergePolicy.DeepObject);
        JsonObject merged = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in entry, engine.ConfigConflictReport)
            ?? throw new InvalidOperationException($"{path} 合并为空。");
        if (path is "CrowdSimulationDebugView.json" or "CrowdSimulationPlayerView.json")
        {
            JsonObject palette = CrowdPresentationPalette.LoadMerged(
                engine.ConfigPipeline, engine.ConfigCatalog, engine.ConfigConflictReport);
            merged = CrowdPresentationPalette.MergeInto(merged, palette, path);
        }

        return merged;
    }

    /// <summary>脚本里 <paramref name="type"/> 行（可选按玩家）的 tick，按脚本顺序。</summary>
    public int[] TicksOf(string type, int? player = null)
        => Script.Select(r => r!.AsObject())
            .Where(r => r["cmd"]!["type"]!.GetValue<string>() == type &&
                        (player == null || r["cmd"]!["player"]?.GetValue<int>() == player))
            .Select(r => r["tick"]!.GetValue<int>())
            .ToArray();

    public JsonObject CommandAt(string type, int index)
        => Script.Select(r => r!["cmd"]!.AsObject()).Where(c => c["type"]!.GetValue<string>() == type).ElementAt(index);

    /// <summary>推进一个会话 tick；路径答复没到时引擎帧会停在同一 tick，继续出帧直到 tick 前进。</summary>
    public void Advance()
    {
        int tick = Session.TickCount;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < StallLimit)
        {
            Engine.Tick(FrameSeconds);
            if (Session.TickCount == tick + 1) return;
            if (Session.TickCount != tick)
                throw new InvalidOperationException($"一帧推进了 {Session.TickCount - tick} 个 tick（从 {tick}）。");
            if (Session.Planner?.PendingCount > 0) System.Threading.Thread.Sleep(1);
        }

        throw new InvalidOperationException($"tick {tick} 停摆超过 {StallLimit.TotalSeconds} 秒。");
    }

    /// <summary>推进到会话 tick 等于 <paramref name="tick"/>；每推进一个 tick 后调 <paramref name="each"/>。</summary>
    public void AdvanceTo(int tick, Action? each = null)
    {
        if (tick < Session.TickCount) throw new InvalidOperationException($"会话已在 tick {Session.TickCount}，回不到 {tick}。");
        while (Session.TickCount < tick)
        {
            Advance();
            each?.Invoke();
        }
    }

    /// <summary>推进直到 <paramref name="done"/> 成立，最多 <paramref name="max"/> tick；返回是否成立。</summary>
    public bool AdvanceUntil(Func<bool> done, int max, Action? each = null)
    {
        for (int t = 0; t < max; t++)
        {
            if (done()) return true;
            Advance();
            each?.Invoke();
        }

        return done();
    }

    /// <summary>按宿主每帧的顺序写全场视觉缓冲（视野迷雾、登记的投影器），再按本机座位选地形贴花。</summary>
    public GroundDecalView? GroundDecal(string seatId = "seat.0")
    {
        var buffer = new GlobalFieldVisualBuffer(recordCapacity: 16, cellCapacity: 1 << 20, dirtyRectCapacity: 16);
        buffer.BeginFrame();
        if (Engine.TryGetService(CoreServiceKeys.VisionFogFieldStore, out FogFieldStore fogFields))
        {
            new FogGlobalFieldVisualProjector().Project(fogFields, buffer);
        }

        if (Engine.TryGetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry, out GlobalFieldVisualProjectorRegistry? registry) && registry != null)
        {
            foreach (var projector in registry.Projectors) projector.Project(buffer);
        }

        bool seatResolved = false;
        int scope = 0;
        if (Engine.TryGetService(CoreServiceKeys.SeatVisionScope, out ISeatVisionScope? seats) && seats != null)
        {
            seatResolved = seats.TryGetScopeKeyId(seatId, out scope);
        }

        if (!TerrainDecalSelection.TrySelect(buffer, seatResolved, scope, out var decal)) return null;
        foreach (ref readonly var record in buffer.GetRecords())
        {
            if (record.IsActive && record.Descriptor.Id == decal.Id)
                return new GroundDecalView(decal, buffer.GetCells(record).ToArray());
        }

        throw new InvalidOperationException($"选中的贴花 {decal.Id} 不在本帧缓冲里。");
    }

    public void Dispose()
    {
        Engine.Dispose();
        _uiSurface.Dispose();
    }

    private static string SceneDir(string repo, string sceneMod)
        => Path.Combine(repo, "mods", "showcases", "crowd_simulation", sceneMod);

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }

    private sealed class StubView : Ludots.Core.Presentation.Camera.IViewController
    {
        public System.Numerics.Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }
}

/// <summary>地面贴花的一帧数据，按渲染端的读法取格值。</summary>
internal sealed record GroundDecalView(GlobalFieldVisualDescriptor Descriptor, GlobalFieldVisualCell[] Cells)
{
    /// <summary>边界外没有贴花(null)；边界内没列出的格，纹理里是清屏值 0(迷雾的 Unseen)。</summary>
    public byte? ByteAt(int x, int y)
    {
        IntRect b = Descriptor.BoundsCells;
        if (x < b.Left || x >= b.Right || y < b.Top || y >= b.Bottom) return null;
        foreach (var cell in Cells)
        {
            if (cell.Cell.X == x && cell.Cell.Y == y) return cell.ByteValue;
        }

        return 0;
    }
}
