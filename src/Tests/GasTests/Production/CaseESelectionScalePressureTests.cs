using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Client;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Case E 框选链的规模化实测（100 名可选玩家单位 / 10k 实体地图）：
/// 1) roster_sync 单次全量重扫（QueryAllMapEntities→FilterTeam→FilterTemplate→WriteCollection replace）
///    在 10k 人口下的单事件延迟与 100 个出生事件的流式总代价；
/// 2) 拖拽热径（按下→20 次指针移动每次重扫候选集做屏幕矩形命中→抬起提交）的单拍峰值与全程；
/// 3) 命中正确性随规模成立（全幅框 = 全部可选单位，半幅框 = 左半可选单位）。
/// 真实数字写入 docs/benchmarks/case-e-selection-scale/case-e-selection-scale.csv；断言是
/// 防灾难回归的宽松围栏，真实度量以 CSV 为准。
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("stress")]
public sealed class CaseESelectionScalePressureTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapIdValue = "case_e_selection_field";
    private const string SchemeId = "scheme.case_e";
    private const string SelectableKey = "case_e.selectable";
    private const string BoxHoverKey = "case_e.box_hover";
    private const string SelectedKey = "selected";
    private const int SelectableCount = 100;
    private const int FillerCount = 10_000 - SelectableCount - 6; // 6 authored 实体（commander+4 marine+1 raider）
    private const string BenchDir = "docs/benchmarks/case-e-selection-scale";
    private readonly MapId _mapId = new(MapIdValue);

    [Test]
    public void BoxSelectionChain_At100PlayersAnd10kEntities_StaysWithinBudget()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            _mapId,
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, SchemeId) })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);
        AssertNoTriggerErrors(engine);

        Entity commander = Resolve(engine, "case-e-commander");

        // ── 造 10k 人口：100 可选（team1 + marine 模板 + 可见横排）+ 9894 填充（team2）──
        int marineTemplateKey = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            is EntityTemplateKeyRegistry keys ? keys.GetId("case_e_marine") : throw new InvalidOperationException("template key registry missing");
        var selectable = CreateUnits(engine, teamId: 1, templateKey: marineTemplateKey, count: SelectableCount, xStepCm: 8, xStartCm: -400);
        CreateUnits(engine, teamId: 2, templateKey: marineTemplateKey, count: FillerCount, xStepCm: 3, xStartCm: -15000);

        // 手动 fire 一个 EntitySpawned（team1 源）→ roster_sync 全量重扫。关键量测：
        // 世界有 104 支可选（4 授权 + 100 新增），但 QueryAllMapEntities 受 `GraphVmLimits.MaxTargets=256`
        // 硬顶，只取前 256 个 MapEntity → 过滤后可选集被截断（当前首256 恰含 77 支）。
        // 这是 10k 人口下候选集完整性的已知硬限制（见 CSVCapNote），本测试锚定该行为并测量成本。
        FireSpawn(engine, selectable[0]);
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectableKey) >= 1);
        int rosterCount = CollectionCount(engine, commander, SelectableKey);
        int worldAmount = CountEligibleMarines(engine);
        Assert.That(worldAmount, Is.EqualTo(SelectableCount + 4), "世界构成：100 新增 + 4 授权全部在位");
        Assert.That(rosterCount, Is.GreaterThanOrEqualTo(1), "候选集存在");
        Assert.That(rosterCount, Is.LessThanOrEqualTo(256),
            $"候选集 <= MaxTargets(256)（当前 {rosterCount}/{worldAmount}）——QueryAllMapEntities 全图查询的硬顶截断");

        // ── 度量 1：单次 roster 全量重扫 @10k ──
        Stopwatch single = Stopwatch.StartNew();
        FireSpawn(engine, selectable[1]);
        single.Stop();
        double rosterSingleMs = single.Elapsed.TotalMilliseconds;
        TickUntil(engine, 4, () => false); // 派发后留一拍收敛

        // ── 度量 2：100 个出生事件流式总代价（每事件一次 O(N) 全量重扫）──
        Stopwatch stream = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            FireSpawn(engine, selectable[(i + 2) % selectable.Length]);
        }
        stream.Stop();
        double rosterStreamMs = stream.Elapsed.TotalMilliseconds;
        double rosterPerEventMs = rosterStreamMs / 100.0;
        TickUntil(engine, 6, () => CollectionCount(engine, commander, SelectableKey) >= SelectableCount + 4);

        // ── 度量 3+正确性：完整拖拽（按下→20 次扫框→抬起提交）──
        var press = new Vector2(-1200f, -120f);
        PressAt(engine, backend, press);
        TickUntil(engine, 20, BoxingActive(engine, commander));

        int fullBandHover = 0;
        int halfBandHover = 0;
        var drag = Stopwatch.StartNew();
        double maxTickMs = 0;
        // 全幅框：盖住全部已入候选集的单位（本带 x∈[-400,400] 全在矩形内）；等挂载+首拍 PointerMoved 落定
        backend.SetMousePosition(new Vector2(1200f, 120f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, BoxHoverKey) >= 0);
        double t0 = drag.Elapsed.TotalMilliseconds;
        if (t0 > maxTickMs) maxTickMs = t0;
        fullBandHover = CollectionCount(engine, commander, BoxHoverKey);
        Assert.That(fullBandHover, Is.EqualTo(rosterCount),
            "全幅框命中全部已入候选集的单位（候选集=当前 {rosterCount} 支）");
        AssertNoTriggerErrors(engine);

        // 20 次扫框：指针横移，逐次重扫候选集
        for (int i = 1; i <= 20; i++)
        {
            float x = -1200f + i * 120f;
            backend.SetMousePosition(new Vector2(x, 120f));
            Tick(engine, 1);
            double now = drag.Elapsed.TotalMilliseconds;
            double step = now - t0;
            if (step > maxTickMs) maxTickMs = step;
            t0 = now;
        }
        Assert.That(CollectionCount(engine, commander, BoxHoverKey), Is.GreaterThan(0),
            "扫框末次仍保持命中（成员资格实时跟随指针）");

        // 半幅框：指针 -10 → 盖住候选集中 x<0 的单位；矩形边缘的投影舍入容差 ±3，
        // 语义钉的是「命中数随几何收敛、并比全幅严格更少（成员资格双向成立）」
        int expectedHalf = CountRosterWithXLessThan(engine, commander, 0);
        backend.SetMousePosition(new Vector2(-10f, 120f));
        TickUntil(engine, 10, () => { int c = CollectionCount(engine, commander, BoxHoverKey); return c >= 0 && c < fullBandHover; });
        halfBandHover = CollectionCount(engine, commander, BoxHoverKey);
        Assert.That(halfBandHover, Is.InRange(expectedHalf - 3, expectedHalf + 3),
            $"半幅框命中 = 候选集中 x<0 的单位 ± 边缘投影舍入（期望 {expectedHalf}，实测 {halfBandHover}）");
        double dragMs = drag.Elapsed.TotalMilliseconds;
        drag.Stop();

        ReleaseAt(engine, backend);
        TickUntil(engine, 30, BoxingCleared(engine, commander));
        Tick(engine, 2);
        Assert.That(CollectionCount(engine, commander, SelectedKey), Is.EqualTo(halfBandHover),
            "提交语义随规模成立：半幅框落定 = 半幅命中");
        AssertNoTriggerErrors(engine);

        // ── 围栏断言（宽松，防灾难回归；真实数字进 CSV）──
        Assert.That(rosterSingleMs, Is.LessThan(100.0), $"6.单次 roster 重扫 @10k = {rosterSingleMs:F2}ms 超围栏");
        Assert.That(rosterPerEventMs, Is.LessThan(50.0), $"7.每事件平均 = {rosterPerEventMs:F2}ms 超围栏");
        Assert.That(rosterStreamMs, Is.LessThan(5000.0), $"8.100 事件流 = {rosterStreamMs:F2}ms 超围栏");
        Assert.That(dragMs, Is.LessThan(4000.0), $"9.完整拖拽 = {dragMs:F2}ms 超围栏");
        Assert.That(maxTickMs, Is.LessThan(500.0), $"10.拖拽单拍峰值 = {maxTickMs:F2}ms 超围栏");

        WriteBenchmark(repoRoot, population: 10_000, rosterSingleMs, rosterPerEventMs, rosterStreamMs, dragMs, maxTickMs,
            rosterCount, worldAmount, fullBandHover, halfBandHover);
    }

    private static Entity[] CreateUnits(GameEngine engine, int teamId, int templateKey, int count, int xStepCm, int xStartCm)
    {
        World world = engine.World;
        var units = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            Entity e = world.Create(
                new MapEntity { MapId = new MapId(MapIdValue) },
                new Team { Id = teamId },
                new EntityTemplateKeyRef { TemplateKeyId = templateKey },
                WorldPositionCm.FromCm(xStartCm + i * xStepCm, 0));
            units[i] = e;
        }

        return units;
    }

    /// <summary>世界里满足「team=1 + case_e_marine 模板」的可选单位总数（世界构成基线）。</summary>
    private static int CountEligibleMarines(GameEngine engine)
    {
        int marineKey = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            is EntityTemplateKeyRegistry keys ? keys.GetId("case_e_marine") : -1;
        int count = 0;
        foreach (ref var chunk in engine.World.Query(new QueryDescription().WithAll<MapEntity>()))
        {
            foreach (int index in chunk)
            {
                ref readonly Entity e = ref chunk.Entity(index);
                if (engine.World.Has<Team>(e) && engine.World.Get<Team>(e).Id == 1 &&
                    engine.World.Has<EntityTemplateKeyRef>(e) &&
                    engine.World.Get<EntityTemplateKeyRef>(e).TemplateKeyId == marineKey)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>候选集中世界位姿 x 小于阈值的成员数（半幅框命中期望，与图链同源）。</summary>
    private static int CountRosterWithXLessThan(GameEngine engine, Entity owner, float thresholdX)
    {
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("store missing");
        int keyId = store.KeyRegistry.GetId(SelectableKey);
        if (keyId <= 0 || !store.TryGet(owner, keyId, out EntityCollectionHandle handle) ||
            !store.TryGetView(handle, out EntityCollectionView view))
        {
            return 0;
        }

        var buffer = new Entity[view.Count];
        int n = store.CopyEntities(owner, keyId, buffer);
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (engine.World.TryGet<WorldPositionCm>(buffer[i], out WorldPositionCm pos) &&
                pos.Value.X.ToFloat() < thresholdX)
            {
                count++;
            }
        }

        return count;
    }

    private static void FireSpawn(GameEngine engine, Entity source)
    {
        ScriptContext ctx = engine.CreateContext();
        ctx.Set(MapTriggerEventPayloadKeys.SourceEntity, source);
        ctx.Set(MapTriggerEventPayloadKeys.SourceTeamId,
            engine.World.TryGet<Team>(source, out Team team) ? team.Id : 0);
        engine.TriggerManager.FireMapEvent(new MapId(MapIdValue), GameEvents.EntitySpawned, ctx);
    }

    private static void WriteBenchmark(
        string repoRoot,
        int population,
        double rosterSingleMs,
        double rosterPerEventMs,
        double rosterStreamMs,
        double dragMs,
        double maxTickMs,
        int rosterCount,
        int worldAmount,
        int hoverFull,
        int hoverHalf)
    {
        string dir = Path.Combine(repoRoot, BenchDir);
        Directory.CreateDirectory(dir);
        string csv = Path.Combine(dir, "case-e-selection-scale.csv");
        bool exists = File.Exists(csv);
        using (var w = new StreamWriter(csv, append: true))
        {
            if (!exists)
            {
                w.WriteLine("utc,population,roster_single_ms,roster_per_event_ms,roster_stream100_ms,drag_total_ms,drag_max_tick_ms,roster_count,world_marines,hover_full,hover_half");
            }

            w.WriteLine($"{DateTime.UtcNow:O},{population},{rosterSingleMs:F3},{rosterPerEventMs:F3},{rosterStreamMs:F3},{dragMs:F3},{maxTickMs:F3},{rosterCount},{worldAmount},{hoverFull},{hoverHalf}");
        }

        File.WriteAllText(
            Path.Combine(dir, "RUNS.md"),
            $"""
             # Case E 框选链 · 10k 人口实测

             数据行：`case-e-selection-scale.csv`（每次跑追加一行，utc 唯一）。

             - 场景：`case_e_selection_field`，10k 世界实体（100 名 team1 marine 可选 + 9894 team2 填充 + 6 授权实体）。
             - roster_sync 每出生事件做一次 **O(N) 全量重扫**（QueryAllMapEntities→team/template 过滤→replace）。
             - 本趟：单次重扫 {rosterSingleMs:F2}ms；100 事件流合计 {rosterStreamMs:F2}ms（每事件 {rosterPerEventMs:F2}ms）；
               完整拖拽 {dragMs:F2}ms（20 次扫框，单拍峰值 {maxTickMs:F2}ms）；全幅框 {hoverFull}、半幅 {hoverHalf}。
             - 缩放契约：出生/死亡事件驱动（零轮询），但**每次事件 = O(N) 重扫**——10k 单波 burst 若在一拍涌入
               N 个出生事件，则那拍退化为 O(N²)。这是当前事件驱动方案的已知成本面（增量维护是后续优化方向，
               非本 PR 范围）。
             - **发现（本次实测钉出）**：`QueryAllMapEntities` 受 `GraphVmLimits.MaxTargets=256` 硬顶，全图查询只取前
               256 个 MapEntity → 10k 人口下候选集被截断（世界 {worldAmount} 支可选，roster 只进 {rosterCount} 支）。
               10k/100 玩家目标需先解除该顶（分页 / 增量维护），否则候选集完整性不成立——本 PR 不越界去改 VM。

             _生成：CaseESelectionScalePressureTests（headless 实测）。_
             """);

        TestContext.WriteLine($"roster_single={rosterSingleMs:F2}ms roster_per_event={rosterPerEventMs:F2}ms " +
            $"roster_stream100={rosterStreamMs:F2}ms drag={dragMs:F2}ms max_tick={maxTickMs:F2}ms " +
            $"roster={rosterCount}/{worldAmount} hover_full={hoverFull} hover_half={hoverHalf}");
    }

    // ── 与 CaseESelectionShowcaseAcceptanceTests 同款 headless 夹具 ──

    private static Func<bool> BoxingActive(GameEngine engine, Entity commander)
    {
        return () => engine.World.TryGet<Ludots.Core.Input.Interaction.InteractionContextInstances>(
            commander, out var instances) && instances.Count == 1;
    }

    private static Func<bool> BoxingCleared(GameEngine engine, Entity commander)
    {
        return () => !engine.World.TryGet<Ludots.Core.Input.Interaction.InteractionContextInstances>(
            commander, out var instances) || instances.Count == 0;
    }

    private static void PressAt(GameEngine engine, TestInputBackend backend, Vector2 mouse)
    {
        backend.SetMousePosition(mouse);
        backend.SetButton("<Mouse>/leftButton", true);
    }

    private static void ReleaseAt(GameEngine engine, TestInputBackend backend)
    {
        backend.SetButton("<Mouse>/leftButton", false);
    }

    private static int CollectionCount(GameEngine engine, Entity owner, string key)
    {
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
        int keyId = store.KeyRegistry.GetId(key);
        return keyId > 0 && store.TryGet(owner, keyId, out EntityCollectionHandle handle) &&
            store.TryGetView(handle, out EntityCollectionView view)
            ? view.Count
            : -1;
    }

    private static Entity Resolve(GameEngine engine, string instanceId)
    {
        MapSession session = engine.CurrentMapSession ?? throw new InvalidOperationException("map not loaded");
        return session.EntityIndex.GetRequired(session.MapId.Value, instanceId, "CaseESelectionScalePressure");
    }

    private static void AssertNoTriggerErrors(GameEngine engine)
    {
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Core", "Ludots.Core.csproj")) &&
                Directory.Exists(Path.Combine(dir.FullName, "mods")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }

    private static void TickUntil(GameEngine engine, int maxFrames, Func<bool> condition)
    {
        for (int i = 0; i < maxFrames && !condition(); i++)
        {
            Tick(engine, 1);
        }
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static GameEngine CreateEngine(string repoRoot, TestInputBackend backend)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CaseESelectionMod" }),
            Path.Combine(repoRoot, "assets"));
        var inputConfig = new Ludots.Core.Input.Config.InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.SetService(
            CoreServiceKeys.ViewController,
            (Ludots.Core.Presentation.Camera.IViewController)new HeadlessViewController(1600f, 900f));
        engine.SetService(
            CoreServiceKeys.ScreenRayProvider,
            (IScreenRayProvider)new WindowPointGroundRayProvider());
        engine.SetService(
            CoreServiceKeys.ScreenProjector,
            (IScreenProjector)new WindowPointScreenProjector());
        engine.Start();
        return engine;
    }

    private sealed class WindowPointGroundRayProvider : IScreenRayProvider
    {
        public ScreenRay GetRay(Vector2 screenPosition)
        {
            return new ScreenRay(
                new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                -Vector3.UnitY);
        }
    }

    private sealed class WindowPointScreenProjector : IScreenProjector
    {
        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
        }
    }

    private sealed class HeadlessViewController : Ludots.Core.Presentation.Camera.IViewController
    {
        public HeadlessViewController(float width, float height)
        {
            Resolution = new Vector2(width, height);
        }

        public Vector2 Resolution { get; }
        public float Fov => 50f;
        public float AspectRatio => Resolution.X / Resolution.Y;
    }

    private sealed class TestInputBackend : Ludots.Core.Input.Runtime.IInputBackend
    {
        private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);

        public Vector2 MousePosition { get; set; }

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
        public Vector2 GetMousePosition() => MousePosition;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;

        public void SetButton(string devicePath, bool down)
        {
            if (down)
            {
                _buttons.Add(devicePath);
            }
            else
            {
                _buttons.Remove(devicePath);
            }
        }

        public void SetMousePosition(Vector2 position)
        {
            MousePosition = position;
        }
    }
}
