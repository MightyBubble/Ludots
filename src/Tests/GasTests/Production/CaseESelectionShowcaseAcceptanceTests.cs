using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Client;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Case E (#1398 D6) 框选全链 headless 验收（忠实形态），对照 tmp/case-e-config-report.html
/// 的七步：01 进图出生 / 02 模板 initialInteractionContext 挂 Instance / 03 Profile triggers 门控 /
/// 04 InputActionFired 触发衍生 context（框起角=press 屏幕像素 + 候选集世界侧刷新）/
/// 05 presenter 观察 ContextActivated + 集合变化高亮 / 06 框结束对「可框选单位」候选集
/// （case_e.selectable 集合 key）做屏幕矩形命中（ScreenRegionToEntities）+ 修饰键语义
/// 透传事件 key 写 selected 集合。引擎零改动，全部行为来自 CaseESelectionMod 的配置资产。
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class CaseESelectionShowcaseAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "case_e_selection_field";
    private const string BattleProfile = "interaction.context.case_e.battle";
    private const string BoxingProfile = "interaction.context.case_e.boxing";
    private const string MarinePresenter = "presenter.case_e.marine_root";
    private const string BoxingMarkerPresenter = "presenter.case_e.boxing_marker";
    private const string SelectedKey = "selected";
    private const string SelectableKey = "case_e.selectable";
    private const int AttachmentSlotBit = 1 << 1; // semantic slot "attachment"

    [Test]
    public void BoxSelectFullChain_SpawnContextTriggerPresenterRectHitRosterAndModifierSemantics()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);

        AssertNoTriggerErrors(engine);

        // ── 01/02：进图出生 + 模板 initialInteractionContext 挂战斗 context Instance ──
        Entity commander = Resolve(engine, "case-e-commander");
        var profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
            ?? throw new InvalidOperationException("InteractionContextProfileRegistry service is missing.");
        int battleProfileId = profiles.ProfileIdRegistry.GetId(BattleProfile);
        int boxingProfileId = profiles.ProfileIdRegistry.GetId(BoxingProfile);
        Assert.That(
            engine.World.TryGet<InteractionContextInstance>(commander, out InteractionContextInstance baseContext) &&
            baseContext.ContextId == battleProfileId &&
            baseContext.Source == InteractionContextInstanceSource.TemplateSpawn,
            "指挥官 rep 出生即携带战斗 context Instance（模板 initialInteractionContext）");

        Entity marine1 = Resolve(engine, "case-e-marine-1");
        Entity marine2 = Resolve(engine, "case-e-marine-2");
        Entity marine3 = Resolve(engine, "case-e-marine-3");
        Entity marine4 = Resolve(engine, "case-e-marine-4");
        Entity raider1 = Resolve(engine, "case-e-raider-1");
        Entity raider2 = Resolve(engine, "case-e-raider-2");

        var presenterRuntime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
            ?? throw new InvalidOperationException("PresenterEntityRuntime service is missing.");
        var presenterDefinitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry service is missing.");
        int marineDefId = presenterDefinitions.GetId(MarinePresenter);
        int boxingMarkerDefId = presenterDefinitions.GetId(BoxingMarkerPresenter);
        TickUntil(engine, 40, () => presenterRuntime.GetActiveByDefinition(marineDefId).Count == 4);
        Assert.That(presenterRuntime.GetActiveByDefinition(boxingMarkerDefId).Count, Is.EqualTo(0),
            "框指示 presenter 在按下前不存在");

        // ── 04：按下（BoxSelectBegin raw 边沿）→ InputActionFired → 激活衍生「正在框选」context ──
        // 窗口像素 ↔ 世界 cm 1:1 伪件下，marines 1-4 屏幕位置 = (-900,0)/(-300,0)/(300,0)/(900,0)。
        PressAt(engine, backend, new Vector2(-1200f, -100f));
        TickUntil(engine, 20, () =>
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances activated) &&
            activated.Count == 1);
        Assert.That(
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances boxing) &&
            boxing.Count == 1 &&
            boxing[0].ContextId == boxingProfileId &&
            boxing[0].ParentContextId == battleProfileId,
            "按下即激活衍生 boxing context（父=战斗）");
        Assert.That(engine.CurrentMapSession!.Variables!.ReadFloat("case_e_press_px"), Is.EqualTo(-1200f),
            "框起角=press 指针窗口像素（D1 事实层）写入地图变量，非派生地面点");
        Assert.That(engine.CurrentMapSession.Variables.ReadFloat("case_e_press_py"), Is.EqualTo(-100f));

        // ── 04b：候选集入参——box_begin 世界侧取全体地图实体，敌我（teamId=1）+ 模板
        //（case_e_marine）过滤后写入 case_e.selectable 集合（owner=rep）──
        TickUntil(engine, 20, () => CollectionCount(engine, commander, SelectableKey) == 4);
        AssertCollection(engine, commander, SelectableKey, "候选集=可框选单位（敌我+模板过滤）",
            marine1, marine2, marine3, marine4);

        // ── 05①b：presenter 观察者——ContextActivated 出现框指示 ──
        Assert.That(presenterRuntime.GetActiveByDefinition(boxingMarkerDefId).Count, Is.EqualTo(1),
            "ContextActivated 事件驱动框指示 presenter 创建");

        // 拖拽中（>8px 行程）——门控系统在此 tick 挂上 boxing 的 triggers
        backend.SetMousePosition(new Vector2(-300f, 100f));
        TickUntil(engine, 10, () => false);

        // ── 06：抬起（Drag 判定完成）→ 矩形 [press,release] 命中 + replace 语义 → selected ──
        ReleaseAt(engine, backend);
        TickUntil(engine, 30, () =>
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances settled) ||
            settled.Count == 0);
        Tick(engine, 4);
        AssertNoTriggerErrors(engine);

        AssertCollection(engine, commander, SelectedKey, "无修饰 → replace 语义", marine1, marine2);
        Assert.That(
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances cleared) ||
            cleared.Count == 0,
            "框结束 DeactivateContext 清空衍生 context 实例集");
        Assert.That(presenterRuntime.GetActiveByDefinition(boxingMarkerDefId).Count, Is.EqualTo(0),
            "context scope 销毁自动清掉框指示 presenter（§8.3 结构性生命周期）");
        AssertRingOn(engine, presenterRuntime, marineDefId, "负 X 象限单位命中高亮", marine1, marine2);
        AssertRingOff(engine, presenterRuntime, marineDefId, "框外单位不高亮", marine3, marine4);
        Assert.That(CollectionContains(engine, commander, SelectedKey, marine2),
            "边界归属：单位屏幕包围盒与框边相交（ScreenRect.Intersects 含端）→ 归属框内");

        // ── 06 加选：QueueModifier → add 语义（框到 marine2/3 并集）──
        DragBox(
            engine,
            backend,
            commander,
            new Vector2(-350f, -100f),
            new Vector2(400f, 100f),
            "QueueModifier");
        AssertCollection(engine, commander, SelectedKey, "QueueModifier → add 语义并集", marine1, marine2, marine3);
        AssertRingOn(engine, presenterRuntime, marineDefId, "加选后新命中单位高亮", marine1, marine2, marine3);

        // ── 06 减选：ModifierSubtract → subtract 语义（框到 marine1 差集）──
        DragBox(
            engine,
            backend,
            commander,
            new Vector2(-1000f, -100f),
            new Vector2(-600f, 100f),
            "ModifierSubtract");
        AssertCollection(engine, commander, SelectedKey, "ModifierSubtract → subtract 语义差集", marine2, marine3);
        AssertRingOn(engine, presenterRuntime, marineDefId, "减选后仅剩命中单位保持高亮", marine2, marine3);
        AssertRingOff(engine, presenterRuntime, marineDefId, "被减去的单位取消高亮", marine1);

        // ── 06 敌我过滤：矩形盖住全图（含敌方 raiders 屏幕位置）→ 命中仍只有己方可框选单位 ──
        DragBox(
            engine,
            backend,
            commander,
            new Vector2(-1500f, -400f),
            new Vector2(1500f, 2200f));
        AssertCollection(engine, commander, SelectedKey, "矩形盖住敌方单位仍被候选集过滤（敌我）",
            marine1, marine2, marine3, marine4);
        Assert.That(
            !CollectionContains(engine, commander, SelectedKey, raider1) &&
            !CollectionContains(engine, commander, SelectedKey, raider2),
            "敌方单位在矩形内也永不入选（候选集=case_e.selectable，敌我在集合侧过滤）");

        // ── 04 零位移 = 点选：BoxSelectEnd 裸边沿，零位移矩形与单位屏幕包围盒相交即命中（无判定器） ──
        TapAt(engine, backend, commander, new Vector2(900f, 0f));
        AssertCollection(engine, commander, SelectedKey, "零位移走点选（零位移矩形×单位屏幕包围盒）并 replace", marine4);
        Assert.That(
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances afterTap) ||
            afterTap.Count == 0,
            "点选同样停用 boxing context");
        AssertRingOn(engine, presenterRuntime, marineDefId, "点选命中单位高亮", marine4);
        AssertRingOff(engine, presenterRuntime, marineDefId, "点选替换后其余单位取消高亮", marine1, marine2, marine3);
    }

    private static void AssertNoTriggerErrors(GameEngine engine)
    {
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));
    }

    private static Entity Resolve(GameEngine engine, string instanceId)
    {
        MapSession session = engine.CurrentMapSession ?? throw new InvalidOperationException("map not loaded");
        return session.EntityIndex.GetRequired(session.MapId.Value, instanceId, "CaseESelectionAcceptance");
    }

    // The window-point ray/projector fakes map window pixels to world cm 1:1, so the
    // authored mouse position doubles as both the ground point (ScreenPointToGround
    // chain) and the projected unit position (ScreenRegionToEntities chain) — the same
    // LogicView-ray chain production uses.
    private static void PressAt(GameEngine engine, TestInputBackend backend, Vector2 mouse)
    {
        backend.SetMousePosition(mouse);
        backend.SetButton("<Mouse>/leftButton", true);
    }

    private static void ReleaseAt(GameEngine engine, TestInputBackend backend)
    {
        backend.SetButton("<Mouse>/leftButton", false);
    }

    /// <summary>
    /// 完整一次框选：按下 → 拖拽行程 → 抬起（框矩形 = press..release 指针窗口像素）。
    /// 修饰键动作注入是单帧脉冲，pacemaker 又会跨帧攒逻辑 tick，逐帧重注入保证
    /// InputActionFired 冻结快照读到的 IsDown 在整个释放窗口内成立。
    /// </summary>
    private static void DragBox(
        GameEngine engine,
        TestInputBackend backend,
        Entity commander,
        Vector2 pressMouse,
        Vector2 releaseMouse,
        string? heldModifierAction = null)
    {
        PressAt(engine, backend, pressMouse);
        TickUntil(engine, 20, BoxingActive(engine, commander));
        backend.SetMousePosition(releaseMouse);
        TickUntil(engine, 10, () => false);

        ReleaseAt(engine, backend);
        TickUntilWithInjection(engine, 30, heldModifierAction, BoxingCleared(engine, commander));
        Tick(engine, 4);
    }

    private static void TapAt(GameEngine engine, TestInputBackend backend, Entity commander, Vector2 mouse)
    {
        PressAt(engine, backend, mouse);
        TickUntil(engine, 20, BoxingActive(engine, commander));
        ReleaseAt(engine, backend);
        TickUntil(engine, 30, BoxingCleared(engine, commander));
        Tick(engine, 4);
    }

    private static Func<bool> BoxingActive(GameEngine engine, Entity commander)
    {
        return () =>
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances instances) &&
            instances.Count == 1;
    }

    private static Func<bool> BoxingCleared(GameEngine engine, Entity commander)
    {
        return () =>
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances instances) ||
            instances.Count == 0;
    }

    private static void TickUntil(GameEngine engine, int maxFrames, Func<bool> condition)
    {
        for (int i = 0; i < maxFrames && !condition(); i++)
        {
            Tick(engine, 1);
        }
    }

    private static void TickUntilWithInjection(GameEngine engine, int maxFrames, string? actionId, Func<bool> condition)
    {
        for (int i = 0; i < maxFrames && !condition(); i++)
        {
            if (actionId != null)
            {
                HoldModifier(engine, actionId);
            }

            Tick(engine, 1);
        }
    }

    private static void HoldModifier(GameEngine engine, string actionId)
    {
        InputHandler(engine).InjectButtonPress(actionId);
    }

    private static PlayerInputHandler InputHandler(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.InputHandler) as PlayerInputHandler
            ?? throw new InvalidOperationException("PlayerInputHandler service is missing.");
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

    private static bool CollectionContains(GameEngine engine, Entity owner, string key, Entity entity)
    {
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
        int keyId = store.KeyRegistry.GetId(key);
        if (keyId <= 0 || !store.TryGet(owner, keyId, out EntityCollectionHandle handle) ||
            !store.TryGetView(handle, out EntityCollectionView view))
        {
            return false;
        }

        var buffer = new Entity[view.Count];
        int count = store.CopyEntities(handle, 0, buffer);
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] == entity)
            {
                return true;
            }
        }

        return false;
    }

    private static void AssertCollection(GameEngine engine, Entity owner, string key, string message, params Entity[] expected)
    {
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
        int keyId = store.KeyRegistry.GetId(key);
        Assert.That(keyId, Is.GreaterThan(0), $"{message}：集合 key 已注册");
        Assert.That(store.TryGet(owner, keyId, out EntityCollectionHandle handle), Is.True, message);
        Assert.That(store.TryGetView(handle, out EntityCollectionView view), Is.True, message);
        Assert.That(view.Count, Is.EqualTo(expected.Length), $"{message}：成员数");

        var actual = new List<Entity>(view.Count);
        var buffer = new Entity[view.Count];
        store.CopyEntities(handle, 0, buffer);
        actual.AddRange(buffer);
        actual.Sort((a, b) => a.Id.CompareTo(b.Id));
        var sorted = new List<Entity>(expected);
        sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
        Assert.That(actual, Is.EqualTo(sorted), message);
    }

    private static void AssertRingOn(
        GameEngine engine,
        PresenterEntityRuntime runtime,
        int marineDefId,
        string message,
        params Entity[] units)
    {
        foreach (Entity unit in units)
        {
            Entity presenter = RequirePresenter(runtime, marineDefId, unit);
            uint mask = engine.World.Get<PresenterState>(presenter).BehaviorActiveMask;
            Assert.That((mask & AttachmentSlotBit) != 0, Is.True, $"{message}：{unit} 的选择环行为应激活");
        }
    }

    private static void AssertRingOff(
        GameEngine engine,
        PresenterEntityRuntime runtime,
        int marineDefId,
        string message,
        params Entity[] units)
    {
        foreach (Entity unit in units)
        {
            Entity presenter = RequirePresenter(runtime, marineDefId, unit);
            uint mask = engine.World.Get<PresenterState>(presenter).BehaviorActiveMask;
            Assert.That((mask & AttachmentSlotBit) != 0, Is.False, $"{message}：{unit} 的选择环行为应关闭");
        }
    }

    private static Entity RequirePresenter(PresenterEntityRuntime runtime, int defId, Entity owner)
    {
        foreach (Entity candidate in runtime.GetActiveByOwnerDefinition(defId, owner))
        {
            return candidate;
        }

        throw new InvalidOperationException($"No presenter {defId} instance owned by {owner}.");
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
        engine.SetService(CoreServiceKeys.InputBackend, (Ludots.Core.Input.Runtime.IInputBackend)backend);
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

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
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

    private sealed class WindowPointGroundRayProvider : IScreenRayProvider
    {
        public ScreenRay GetRay(System.Numerics.Vector2 screenPosition)
        {
            return new ScreenRay(
                new System.Numerics.Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                -System.Numerics.Vector3.UnitY);
        }
    }

    /// <summary>World cm ↔ window px 1:1（与 WindowPointGroundRayProvider 同一约定），供 ScreenRegionToEntities 的投影链。</summary>
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

        public void SetMousePosition(Vector2 position) => MousePosition = position;
    }
}
