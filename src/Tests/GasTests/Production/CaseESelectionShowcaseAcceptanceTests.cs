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
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Case E (#1398 S3) 框选全链 headless 验收，对照 tmp/case-e-config-report.html 的七步：
/// 01 进图出生 / 02 模板 initialInteractionContext 挂 Instance / 03 Profile triggers 门控 /
/// 04 InputActionFired 触发衍生 context / 05 presenter 观察 ContextActivated + 集合变化高亮 /
/// 06 框结束修饰键语义透传事件 key 写 selected 集合。引擎零改动，全部行为来自
/// CaseESelectionMod 的配置资产。
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
    private const int AttachmentSlotBit = 1 << 1; // semantic slot "attachment"

    [Test]
    public void BoxSelectFullChain_SpawnContextTriggerPresenterCommitAndModifierSemantics()
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
        PressAt(engine, backend, new Vector2(200f, 200f), new Vector2(-1100f, -200f));
        TickUntil(engine, 20, () =>
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances activated) &&
            activated.Count == 1);
        Assert.That(
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances boxing) &&
            boxing.Count == 1 &&
            boxing[0].ContextId == boxingProfileId &&
            boxing[0].ParentContextId == battleProfileId,
            "按下即激活衍生 boxing context（父=战斗）");
        Assert.That(engine.CurrentMapSession!.Variables!.ReadFloat("case_e_press_x"), Is.EqualTo(-1100f).Within(0.001f),
            "框开始 ground point 经事件载荷透传并写入地图变量");
        Assert.That(engine.CurrentMapSession.Variables.ReadFloat("case_e_press_y"), Is.EqualTo(-200f).Within(0.001f));

        // ── 05①b：presenter 观察者——ContextActivated 出现框指示 ──
        Assert.That(presenterRuntime.GetActiveByDefinition(boxingMarkerDefId).Count, Is.EqualTo(1),
            "ContextActivated 事件驱动框指示 presenter 创建");

        // 拖拽中（>8px 行程）——门控系统在此 tick 挂上 boxing 的 triggers
        backend.SetMousePosition(new Vector2(760f, 520f));
        TickUntil(engine, 10, () => false);

        // ── 06：抬起（Drag 判定完成）→ 命中集 + replace 语义 → 事件 key → selected 集合 ──
        ReleaseAt(engine, backend, new Vector2(-600f, 0f));
        TickUntil(engine, 30, () =>
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances settled) ||
            settled.Count == 0);
        Tick(engine, 4);
        AssertNoTriggerErrors(engine);

        AssertSelected(engine, commander, "无修饰 → replace 语义", marine1, marine2);
        Assert.That(
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances cleared) ||
            cleared.Count == 0,
            "框结束 DeactivateContext 清空衍生 context 实例集");
        Assert.That(presenterRuntime.GetActiveByDefinition(boxingMarkerDefId).Count, Is.EqualTo(0),
            "context scope 销毁自动清掉框指示 presenter（§8.3 结构性生命周期）");
        AssertRingOn(engine, presenterRuntime, marineDefId, "命中单位高亮", marine1, marine2);
        AssertRingOff(engine, presenterRuntime, marineDefId, "未命中单位不高亮", marine3, marine4);

        // ── 06 加选：QueueModifier → add 语义 ──
        DragBox(
            engine,
            backend,
            commander,
            new Vector2(400f, 260f),
            new Vector2(-1100f, -200f),
            new Vector2(300f, 0f),
            "QueueModifier");
        AssertSelected(engine, commander, "QueueModifier → add 语义并集", marine1, marine2, marine3);
        AssertRingOn(engine, presenterRuntime, marineDefId, "加选后新命中单位高亮", marine1, marine2, marine3);

        // ── 06 减选：ModifierSubtract → subtract 语义 ──
        DragBox(
            engine,
            backend,
            commander,
            new Vector2(300f, 300f),
            new Vector2(-900f, -200f),
            new Vector2(-300f, 0f),
            "ModifierSubtract");
        AssertSelected(engine, commander, "ModifierSubtract → subtract 语义差集", marine1, marine3);
        AssertRingOn(engine, presenterRuntime, marineDefId, "减选后仅剩命中单位保持高亮", marine1, marine3);
        AssertRingOff(engine, presenterRuntime, marineDefId, "被减去的单位取消高亮", marine2);

        // ── 04 零长框 = Tap 点选：Tap 判定完成 → tap_commit 以点位置命中 ──
        TapAt(engine, backend, commander, new Vector2(900f, 300f), new Vector2(900f, 0f));
        AssertSelected(engine, commander, "零长框走 Tap 点选并 replace", marine4);
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

    private static void PressAt(GameEngine engine, TestInputBackend backend, Vector2 mouse, Vector2 groundCm)
    {
        backend.SetMousePosition(mouse);
        SetGroundOverride(engine, groundCm);
        backend.SetButton("<Mouse>/leftButton", true);
    }

    private static void ReleaseAt(GameEngine engine, TestInputBackend backend, Vector2 groundCm)
    {
        SetGroundOverride(engine, groundCm);
        backend.SetButton("<Mouse>/leftButton", false);
    }

    /// <summary>
    /// 完整一次框选：按下（含 ground 覆盖）→ 拖拽行程 → 抬起（含 ground 覆盖）。
    /// 修饰键动作注入是单帧脉冲，pacemaker 又会跨帧攒逻辑 tick，逐帧重注入保证
    /// InputActionFired 冻结快照读到的 IsDown 在整个释放窗口内成立。
    /// </summary>
    private static void DragBox(
        GameEngine engine,
        TestInputBackend backend,
        Entity commander,
        Vector2 releaseMouse,
        Vector2 pressGround,
        Vector2 releaseGround,
        string? heldModifierAction = null)
    {
        PressAt(engine, backend, new Vector2(200f, 200f), pressGround);
        TickUntil(engine, 20, BoxingActive(engine, commander));
        backend.SetMousePosition(releaseMouse);
        TickUntil(engine, 10, () => false);

        ReleaseAt(engine, backend, releaseGround);
        TickUntilWithInjection(engine, 30, heldModifierAction, BoxingCleared(engine, commander));
        Tick(engine, 4);
    }

    private static void TapAt(GameEngine engine, TestInputBackend backend, Entity commander, Vector2 mouse, Vector2 groundCm)
    {
        PressAt(engine, backend, mouse, groundCm);
        TickUntil(engine, 20, BoxingActive(engine, commander));
        ReleaseAt(engine, backend, groundCm);
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

    private static void SetGroundOverride(GameEngine engine, Vector2 worldCm)
    {
        if (engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride) is not AuthoritativeGroundPointerOverride groundOverride)
        {
            throw new InvalidOperationException("AuthoritativeGroundPointerOverride service is missing.");
        }

        InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(
            engine.GlobalContext,
            nameof(CaseESelectionShowcaseAcceptanceTests));
        groundOverride.Set(bindings.CommandActionId, worldCm);
    }

    private static void AssertSelected(GameEngine engine, Entity owner, string message, params Entity[] expected)
    {
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
        int keyId = store.KeyRegistry.GetId(SelectedKey);
        Assert.That(keyId, Is.GreaterThan(0), "selected 集合 key 已注册");
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
