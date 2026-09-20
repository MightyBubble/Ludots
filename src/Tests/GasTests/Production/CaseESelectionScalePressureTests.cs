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
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

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
    private const string SelectionPreviewKey = "case_e.selection_preview";
    private const string SelectedKey = "selected";
    private const string BenchDir = "docs/benchmarks/case-e-query-completeness";
    private readonly MapId _mapId = new(MapIdValue);

    [Test]
    public void TenKShowcase_SpawnsIntoTheFinalTeamAndSupportsBothPlayers()
    {
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(FindRepoRoot(), backend);
        engine.LoadMap(new MapLoadRequest(new MapId("case_e_selection_10k_field"),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, SchemeId) })));
        MapSession session = engine.CurrentMapSession!;
        Entity first = session.PlayerEntityLookup.Get(1);
        Entity second = session.PlayerEntityLookup.Get(2);
        TickUntil(engine, 240, () => CollectionCount(engine, first, SelectableKey) == 5000 &&
            CollectionCount(engine, second, SelectableKey) == 5000);
        Assert.That(CollectionCount(engine, first, SelectableKey), Is.EqualTo(5000));
        Assert.That(CollectionCount(engine, second, SelectableKey), Is.EqualTo(5000));
        var query = new QueryDescription().WithAll<Team, PlayerOwner, EntityTemplateKeyRef>();
        int marineKey = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)!.GetId("case_e_marine");
        int count = 0;
        foreach (ref var chunk in engine.World.Query(in query))
        {
            var teams = chunk.GetSpan<Team>();
            var owners = chunk.GetSpan<PlayerOwner>();
            var templates = chunk.GetSpan<EntityTemplateKeyRef>();
            foreach (int row in chunk)
            {
                if (templates[row].TemplateKeyId != marineKey) continue;
                Assert.That(teams[row].Id, Is.EqualTo(owners[row].PlayerId));
                count++;
            }
        }
        Assert.That(count, Is.EqualTo(10000));
        var seats = ClientLocalSeatAccess.RequireRegistry(engine);
        foreach (int player in new[] { 1, 2, 1 })
        {
            Entity rep = player == 1 ? first : second;
            seats.SetPossession("seat.0", player, rep);
            Tick(engine, 2);
            PressAt(engine, backend, new Vector2(-20000, -20000));
            Tick(engine, 2);
            backend.SetMousePosition(new Vector2(20000, 20000));
            Tick(engine, 2);
            ReleaseAt(engine, backend);
            Tick(engine, 4);
            Assert.That(CollectionCount(engine, rep, SelectedKey), Is.EqualTo(5000));
            Assert.That(CollectionCount(engine, rep, SelectionPreviewKey), Is.LessThanOrEqualTo(0));
        }
        AssertNoTriggerErrors(engine);
    }

    [TestCase(100, true)]
    [TestCase(1000, false)]
    public void BoxSelectionChain_At10kEntities_SelectsEveryEligibleUnit(int selectableCount, bool enforceLatencyBudget)
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
        var selectable = CreateUnits(engine, teamId: 1, templateKey: marineTemplateKey, count: selectableCount, xStepCm: Math.Max(1, 800 / selectableCount), xStartCm: -(selectableCount * Math.Max(1, 800 / selectableCount)) / 2);
        CreateUnits(engine, teamId: 2, templateKey: marineTemplateKey, count: 10_000 - selectableCount - 7, xStepCm: 3, xStartCm: -15000);

        FireSpawn(engine, selectable[0]);
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectableKey) >= 1);
        int rosterCount = CollectionCount(engine, commander, SelectableKey);
        int worldAmount = CountEligibleMarines(engine);
        Assert.That(worldAmount, Is.EqualTo(selectableCount + 4), "世界构成：100 新增 + 4 授权全部在位");
        Assert.That(rosterCount, Is.EqualTo(worldAmount), "全部符合条件的单位都必须可框选");

        // Include the requested collection snapshot in the change-to-read measurement.
        Stopwatch single = Stopwatch.StartNew();
        engine.World.Set(selectable[1], new Team { Id = 2 });
        CollectionCount(engine, commander, SelectableKey);
        engine.World.Set(selectable[1], new Team { Id = 1 });
        CollectionCount(engine, commander, SelectableKey);
        single.Stop();
        double rosterSingleMs = single.Elapsed.TotalMilliseconds;
        TickUntil(engine, 4, () => false); // 派发后留一拍收敛

        // Defer materialization until the end of the batch.
        Stopwatch stream = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            engine.World.Set(selectable[(i + 2) % selectable.Length], new Team { Id = 1 });
        }
        CollectionCount(engine, commander, SelectableKey);
        stream.Stop();
        double rosterStreamMs = stream.Elapsed.TotalMilliseconds;
        double rosterPerEventMs = rosterStreamMs / 100.0;
        TickUntil(engine, 6, () => CollectionCount(engine, commander, SelectableKey) >= selectableCount + 4);

        // ── 度量 3+正确性：完整拖拽（按下→20 次扫框→抬起提交）──
        var press = new Vector2(-1200f, -120f);
        PressAt(engine, backend, press);
        TickUntil(engine, 20, BoxingActive(engine, commander));

        int fullBandPreview = 0;
        int halfBandPreview = 0;
        var drag = Stopwatch.StartNew();
        double maxTickMs = 0;
        // 全幅框：盖住全部已入候选集的单位（本带 x∈[-400,400] 全在矩形内）；等挂载+首拍 PointerMoved 落定
        backend.SetMousePosition(new Vector2(1200f, 120f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == rosterCount);
        double t0 = drag.Elapsed.TotalMilliseconds;
        if (t0 > maxTickMs) maxTickMs = t0;
        fullBandPreview = CollectionCount(engine, commander, SelectionPreviewKey);
        Assert.That(fullBandPreview, Is.EqualTo(rosterCount),
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
        Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.GreaterThan(0),
            "扫框末次仍保持命中（成员资格实时跟随指针）");

        // 半幅框：指针 -10 → 盖住候选集中 x<0 的单位；矩形边缘的投影舍入容差 ±3，
        // 语义钉的是「命中数随几何收敛、并比全幅严格更少（成员资格双向成立）」
        int expectedHalf = CountEligibleScreenHits(engine, new ScreenRect(-1200, -120, -10, 120));
        backend.SetMousePosition(new Vector2(-10f, 120f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == expectedHalf);
        halfBandPreview = CollectionCount(engine, commander, SelectionPreviewKey);
        Assert.That(halfBandPreview, Is.EqualTo(expectedHalf),
            $"半幅框必须包含框边以内的全部单位（期望 {expectedHalf}，实测 {halfBandPreview}）");
        double dragMs = drag.Elapsed.TotalMilliseconds;
        drag.Stop();

        ReleaseAt(engine, backend);
        TickUntil(engine, 30, BoxingCleared(engine, commander));
        Tick(engine, 2);
        Assert.That(CollectionCount(engine, commander, SelectedKey), Is.EqualTo(halfBandPreview),
            "提交语义随规模成立：半幅框落定 = 半幅命中");
        AssertNoTriggerErrors(engine);

        // ── 围栏断言（宽松，防灾难回归；真实数字进 CSV）──
        Assert.That(rosterSingleMs, Is.LessThan(100.0), $"6.单次 roster 重扫 @10k = {rosterSingleMs:F2}ms 超围栏");
        Assert.That(rosterPerEventMs, Is.LessThan(50.0), $"7.每事件平均 = {rosterPerEventMs:F2}ms 超围栏");
        Assert.That(rosterStreamMs, Is.LessThan(5000.0), $"8.100 事件流 = {rosterStreamMs:F2}ms 超围栏");
        Assert.That(dragMs, Is.LessThan(4000.0), $"9.完整拖拽 = {dragMs:F2}ms 超围栏");
        if (enforceLatencyBudget)
            Assert.That(maxTickMs, Is.LessThan(500.0), $"10.拖拽单拍峰值 = {maxTickMs:F2}ms 超围栏");

        WriteBenchmark(repoRoot, population: 10_000, rosterSingleMs, rosterPerEventMs, rosterStreamMs, dragMs, maxTickMs,
            rosterCount, worldAmount, fullBandPreview, halfBandPreview);
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

    private static int CountEligibleScreenHits(GameEngine engine, ScreenRect rect)
    {
        int template = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)!.GetId("case_e_marine");
        var projector = (IScreenProjector)engine.GetService(CoreServiceKeys.ScreenProjector)!;
        int count = 0;
        foreach (ref var chunk in engine.World.Query(new QueryDescription().WithAll<MapEntity, Team, EntityTemplateKeyRef>()))
            foreach (int row in chunk)
            {
                Entity entity = chunk.Entity(row);
                if (engine.World.Get<Team>(entity).Id == 1 &&
                    engine.World.Get<EntityTemplateKeyRef>(entity).TemplateKeyId == template &&
                    SpatialBoundsUtility.EntityIntersectsScreenRect(engine.World, entity, projector, rect))
                    count++;
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
                w.WriteLine("utc,population,two_transitions_and_reads_ms,batch_per_write_ms,batch100_and_read_ms,drag_total_ms,drag_max_tick_ms,roster_count,world_marines,hover_full,hover_half");
            }

            w.WriteLine($"{DateTime.UtcNow:O},{population},{rosterSingleMs:F3},{rosterPerEventMs:F3},{rosterStreamMs:F3},{dragMs:F3},{maxTickMs:F3},{rosterCount},{worldAmount},{hoverFull},{hoverHalf}");
        }

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
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "SelectionInteractionMod", "CaseESelectionMod" }),
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
