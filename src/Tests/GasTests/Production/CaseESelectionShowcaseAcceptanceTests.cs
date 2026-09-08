using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Client;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Case E 框选全链 headless 验收（忠实形态），对照 case-e-config-report.html
/// 的七步：01 进图出生 / 02 模板 initialInteractionContext 挂 Instance / 03 Profile triggers 门控 /
/// 04 语义动作直绑触发衍生 context（框起角落操作者 rep 黑板；候选集由 battle context 挂载的 roster_sync 维护）/
/// 05 boxing context 持续过程：ScreenRect 框 + 存活期命中写 case_e.selection_preview 预览集 →
/// presenter 观察成员变化高亮 / 06 抬起（BoxSelectEnd）对「可框选单位」候选集做屏幕矩形命中
/// + 修饰键语义透传事件 key 写 selected 集合（与候选、预览三套集合分离）。
/// 输入合同：按下=BoxSelectBegin、抬起=BoxSelectEnd（firesOn=release），无 Tap/Drag 判定器。
/// 键位表 CaseE.Controls（含 PointerPos）由 battle 档案 inputContextId 经座位投影激活；startupInputContexts / scheme.inputContexts 不硬推玩法键。
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
    private const string BoxSelectRectanglePresenter = "presenter.case_e.box_select_rectangle";
    private const string SelectionMarkerPresenter = "presenter.case_e.selection_marker";
    private const string SelectionPreviewMarkerPresenter = "presenter.case_e.selection_preview_marker";
    private const string SelectedKey = "selected";
    private const string SelectableKey = "case_e.selectable";
    private const string SelectionPreviewKey = "case_e.selection_preview";

    [Test]
    public void SwitchingPossession_DrivesOnlyTheCurrentPlayersSelection()
    {
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(FindRepoRoot(), backend);
        engine.LoadMap(new MapLoadRequest(new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        Entity first = Resolve(engine, "case-e-commander");
        Entity second = Resolve(engine, "case-e-raider-1");
        engine.World.Add(second, engine.World.Get<InteractionContextInstance>(first));
        engine.GetService(CoreServiceKeys.PlayerEntityLookup)!.Register(2, second);
        Tick(engine, 4);

        var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)!;
        int candidateKey = collections.KeyRegistry.GetId(SelectableKey);
        collections.BindSource(second, candidateKey, collections.RequireSource(first, candidateKey));
        DragBox(engine, backend, first, new Vector2(-1200f, -100f), new Vector2(1200f, 100f));
        Assert.That(CollectionCount(engine, first, SelectedKey), Is.EqualTo(4));
        Assert.That(CollectionCount(engine, second, SelectedKey), Is.LessThanOrEqualTo(0));

        ClientLocalSeatAccess.RequireRegistry(engine).SetPossession("seat.0", 2, second);
        Tick(engine, 2);
        DragBox(engine, backend, second, new Vector2(-1200f, -100f), new Vector2(-300f, 100f));
        Assert.That(CollectionCount(engine, second, SelectedKey), Is.EqualTo(2));
        Assert.That(CollectionCount(engine, first, SelectedKey), Is.EqualTo(4));
        AssertNoTriggerErrors(engine);
    }

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    [TestCase(6, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(6, true)]
    public void FastBoxRelease_ClearsGestureAndPreview(int heldFrames, bool moveOnRelease)
    {
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(FindRepoRoot(), backend);
        engine.LoadMap(new MapLoadRequest(new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        Entity commander = Resolve(engine, "case-e-commander");
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        Tick(engine, 4);
        var runtime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)!;
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
        int marker = definitions.GetId(BoxSelectRectanglePresenter);
        int preview = definitions.GetId(SelectionPreviewMarkerPresenter);
        Entity[] marines = { Resolve(engine, "case-e-marine-1"), Resolve(engine, "case-e-marine-2"),
            Resolve(engine, "case-e-marine-3"), Resolve(engine, "case-e-marine-4") };
        var trace = new List<string>();

        for (int gesture = 0; gesture < 4; gesture++)
        {
            PressAt(engine, backend, new Vector2(-1200f, -100f));
            Tick(engine, 1);
            backend.SetMousePosition(new Vector2(-300f, 100f));
            Tick(engine, heldFrames - 1);
            if (moveOnRelease) backend.SetMousePosition(new Vector2(400f, 100f));
            ReleaseAt(engine, backend);
            Tick(engine, 8);

            Assert.Multiple(() =>
            {
                Assert.That(BoxingCleared(engine, commander)(), Is.True, $"Gesture {gesture}: released gesture remains active");
                Assert.That(CollectionCount(engine, commander, SelectedKey), Is.EqualTo(moveOnRelease ? 3 : 2), $"Gesture {gesture}: release must commit the final rectangle");
                Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.LessThanOrEqualTo(0), $"Gesture {gesture}: preview collection remains populated");
                Assert.That(CountPresentersOwnedBy(runtime, marker, commander, engine.World), Is.Zero, $"Gesture {gesture}: rectangle remains alive");
                foreach (Entity marine in marines)
                    Assert.That(CountVisibleMarkersOwnedBy(runtime, preview, marine, engine.World), Is.Zero, $"Gesture {gesture}: yellow marker remains visible");
            });
            AssertNoTriggerErrors(engine);
            int yellowCount = 0;
            foreach (Entity marine in marines)
                yellowCount += CountVisibleMarkersOwnedBy(runtime, preview, marine, engine.World);
            trace.Add(JsonSerializer.Serialize(new { gesture, heldFrames, moveOnRelease,
                boxingActive = BoxingActive(engine, commander)(), previewCount = CollectionCount(engine, commander, SelectionPreviewKey),
                rectangleCount = CountPresentersOwnedBy(runtime, marker, commander, engine.World), yellowCount,
                selectedCount = CollectionCount(engine, commander, SelectedKey) }));
        }
        WriteFastGestureEvidence($"release-{heldFrames}-{moveOnRelease}", trace);
    }

    [TestCase(1, 1, false)]
    [TestCase(1, 2, false)]
    [TestCase(2, 1, false)]
    [TestCase(3, 1, false)]
    [TestCase(2, 1, true)]
    [TestCase(3, 1, true)]
    public void RepeatedFastGestures_EndWithNoResiduals(int heldFrames, int releasedFrames, bool restartHeld)
    {
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(FindRepoRoot(), backend);
        engine.LoadMap(new MapLoadRequest(new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        Entity commander = Resolve(engine, "case-e-commander");
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        Tick(engine, 4);
        for (int gesture = 0; gesture < 20; gesture++)
        {
            PressAt(engine, backend, new Vector2(-1200f, -100f));
            Tick(engine, heldFrames);
            backend.SetMousePosition(new Vector2(gesture % 2 == 0 ? -300f : 400f, 100f));
            ReleaseAt(engine, backend);
            Tick(engine, releasedFrames);
        }
        if (restartHeld)
        {
            PressAt(engine, backend, new Vector2(-1200f, -100f));
            Tick(engine, 8);
            Assert.That(BoxingActive(engine, commander)(), Is.True, "The previous release must not close the new held gesture.");
            backend.SetMousePosition(new Vector2(400f, 100f));
            Tick(engine, 4);
            Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.EqualTo(3));
            ReleaseAt(engine, backend);
        }
        Tick(engine, 8);
        var runtime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)!;
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)!;
        Assert.That(BoxingCleared(engine, commander)(), Is.True);
        Assert.That(CollectionCount(engine, commander, SelectedKey), Is.EqualTo(3));
        Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.LessThanOrEqualTo(0));
        int rectangleCount = CountPresentersOwnedBy(runtime, definitions.GetId(BoxSelectRectanglePresenter), commander, engine.World);
        Assert.That(rectangleCount, Is.Zero);
        int yellowCount = 0;
        foreach (string id in new[] { "case-e-marine-1", "case-e-marine-2", "case-e-marine-3", "case-e-marine-4" })
            yellowCount += CountVisibleMarkersOwnedBy(runtime, definitions.GetId(SelectionPreviewMarkerPresenter), Resolve(engine, id), engine.World);
        Assert.That(yellowCount, Is.Zero);
        AssertNoTriggerErrors(engine);
        WriteFastGestureEvidence($"repeat-{heldFrames}-{releasedFrames}-{restartHeld}", new[] { JsonSerializer.Serialize(new {
            gestures = restartHeld ? 21 : 20, heldFrames, releasedFrames, restartHeld, boxingActive = BoxingActive(engine, commander)(),
            previewCount = CollectionCount(engine, commander, SelectionPreviewKey), rectangleCount, yellowCount,
            selectedCount = CollectionCount(engine, commander, SelectedKey) }) });
    }

    private static void WriteFastGestureEvidence(string name, IEnumerable<string> rows)
    {
        string directory = Path.Combine(FindRepoRoot(), "artifacts", "acceptance", "case-e-fast-release");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, $"{name}.jsonl"), rows);
    }

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
        Assert.That(engine.MergedConfig.StartupInputContexts, Does.Not.Contain("CaseE.Controls"),
            "框选键位必须由实体投影，默认镜头键位可以在启动时启用");
        int caseEControlsId = profiles.InputContextIdRegistry.GetId("CaseE.Controls");
        Assert.That(baseContext.InputContextId, Is.EqualTo(caseEControlsId),
            "battle 档案 inputContextId=CaseE.Controls 应写入挂载实例");
        var inputHandler = engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("InputHandler service is missing.");
        TickUntil(engine, 10, () => inputHandler.HasContext("CaseE.Controls"));
        Assert.That(inputHandler.HasContext("CaseE.Controls"), Is.True,
            "占有座位后投影系统应从实体 battle 挂载推上 CaseE.Controls");

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
        int boxSelectRectangleDefId = presenterDefinitions.GetId(BoxSelectRectanglePresenter);
        int selectionMarkerDefId = presenterDefinitions.GetId(SelectionMarkerPresenter);
        int selectionPreviewMarkerDefId = presenterDefinitions.GetId(SelectionPreviewMarkerPresenter);
        TickUntil(engine, 40, () => presenterRuntime.GetActiveByDefinition(marineDefId).Count == 4);
        // 框选矩形由全局选择规则订阅 ContextActivated 创建，
        // ContextDeactivated 销毁——按下前不存在。
        Assert.That(CountPresentersOwnedBy(presenterRuntime, boxSelectRectangleDefId, commander, engine.World), Is.EqualTo(0),
            "框指示 presenter 按下前不存在（全局规则建销，非常驻）");
        ScreenOverlayBuffer screenOverlayBefore = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer
            ?? throw new InvalidOperationException("ScreenOverlayBuffer service is missing.");
        Assert.That(HasScreenRect(screenOverlayBefore, x: -1200, y: -100, width: 900, height: 200), Is.False,
            "按下前 boxing context 未激活 → scope 无矩形（visibility 绑定=0）");

        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        AssertNoTriggerErrors(engine);
        AssertCollection(engine, commander, SelectableKey, "候选集随 battle context 维护（敌我+模板过滤），框之前就有",
            marine1, marine2, marine3, marine4);

        // ── 04：按下（BoxSelectBegin）→ 图入口 action 直绑 → 激活衍生「正在框选」context ──
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
        Assert.That(
            engine.World.TryGet<BlackboardFloatBuffer>(commander, out BlackboardFloatBuffer pressBoard) &&
            pressBoard.TryGet(ConfigKeyRegistry.GetId("case_e.press.px"), out float pressPx) &&
            pressPx == -1200f,
            "框起角 X=press 指针窗口像素写入操作者 rep 黑板，禁止 map var");
        Assert.That(
            pressBoard.TryGet(ConfigKeyRegistry.GetId("case_e.press.py"), out float pressPy) &&
            pressPy == -100f,
            "框起角 Y 同挂操作者 rep 黑板");

        // ── 05①b：全局规则——ContextActivated→创建 boxing 框指示（scope=想 subject stable id）──
        Assert.That(CountPresentersOwnedBy(presenterRuntime, boxSelectRectangleDefId, commander, engine.World), Is.EqualTo(1),
            "ContextActivated 事件被全局规则壳消费 → 创建框指示 presenter");

        // 拖拽中——门控系统在此 tick 挂上 boxing 的 triggers（抬起边沿 BoxSelectEnd）
        backend.SetMousePosition(new Vector2(-300f, 100f));
        TickUntil(engine, 10, () => false);

        // ── 05②（D8）：screen-space rect presenter 跟随拖拽矩形（数据驱动）──
        ScreenOverlayBuffer screenOverlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer
            ?? throw new InvalidOperationException("ScreenOverlayBuffer service is missing.");
        Assert.That(
            HasScreenRect(screenOverlay, x: -1200, y: -100, width: 900, height: 200),
            "矩形框 = rep 黑板起角 + 当前活指针的屏幕矩形");
        backend.SetMousePosition(new Vector2(0f, 200f));
        Tick(engine, 5);
        Assert.That(
            HasScreenRect(screenOverlay, x: -1200, y: -100, width: 1200, height: 300),
            "指针继续移动，矩形框随之扩大（跟随当前拖拽数据）");

        // ── 05③：PointerMoved 输入边沿命中 → case_e.selection_preview（预览集 ≠ selected）──
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 2);
        AssertCollection(engine, commander, SelectionPreviewKey, "拖拽中预览命中=当前矩形覆盖的可框选单位",
            marine1, marine2);
        Assert.That(CollectionCount(engine, commander, SelectedKey), Is.LessThanOrEqualTo(0),
            "拖拽中不得写入已选中集合（候选/预览/已选中三套分离）");
        AssertPreviewOn(engine, presenterRuntime, selectionPreviewMarkerDefId, "预览环跟命中集", marine1, marine2);
        AssertPreviewOff(engine, presenterRuntime, selectionPreviewMarkerDefId, "框外单位无预览环", marine3, marine4);

        backend.SetMousePosition(new Vector2(-300f, 100f));
        Tick(engine, 2);
        AssertCollection(engine, commander, SelectionPreviewKey, "指针回缩后预览集随之收缩", marine1, marine2);

        // ── 06：抬起（BoxSelectEnd / firesOn=release）→ 矩形 [press,release] 命中 + replace 语义 → selected ──
        ReleaseAt(engine, backend);
        TickUntil(engine, 30, () =>
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances settled) ||
            settled.Count == 0);
        Tick(engine, 4);
        AssertNoTriggerErrors(engine);

        AssertCollection(engine, commander, SelectedKey, "无修饰 → replace 语义", marine1, marine2);
        Assert.That(
            CollectionCount(engine, commander, SelectionPreviewKey) <= 0,
            "框结束停用 boxing → onDeactivated 槽 selection_preview_clear 清空预览集");
        AssertPreviewOff(engine, presenterRuntime, selectionPreviewMarkerDefId, "预览环随预览集清空", marine1, marine2, marine3, marine4);
        Assert.That(
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances cleared) ||
            cleared.Count == 0,
            "框结束 DeactivateContext 清空衍生 context 实例集");
        Assert.That(CountPresentersOwnedBy(presenterRuntime, boxSelectRectangleDefId, commander, engine.World), Is.EqualTo(0),
            "ContextDeactivated → 全局规则销毁框指示 presenter");
        int overlayCountAfterScopeDeath = screenOverlay.Count;
        Tick(engine, 6);
        Assert.That(screenOverlay.Count, Is.EqualTo(overlayCountAfterScopeDeath),
            "框结束→boxing context 停用→框指示销毁→ScreenRect 不再产出新矩形");
        AssertMarkerVisible(engine, presenterRuntime, selectionMarkerDefId, "负 X 象限单位命中高亮", marine1, marine2);
        AssertMarkerHidden(engine, presenterRuntime, selectionMarkerDefId, "框外单位不高亮", marine3, marine4);
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
        AssertMarkerVisible(engine, presenterRuntime, selectionMarkerDefId, "加选后新命中单位高亮", marine1, marine2, marine3);

        // ── 06 减选：ModifierSubtract → subtract 语义（框到 marine1 差集）──
        DragBox(
            engine,
            backend,
            commander,
            new Vector2(-1000f, -100f),
            new Vector2(-600f, 100f),
            "ModifierSubtract");
        AssertCollection(engine, commander, SelectedKey, "ModifierSubtract → subtract 语义差集", marine2, marine3);
        AssertMarkerVisible(engine, presenterRuntime, selectionMarkerDefId, "减选后仅剩命中单位保持高亮", marine2, marine3);
        AssertMarkerHidden(engine, presenterRuntime, selectionMarkerDefId, "被减去的单位取消高亮", marine1);

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

        // ── 04 零位移 = 点选：按下+抬起同一点，零位移矩形与单位屏幕包围盒相交即命中（无 Tap/Drag 判定器） ──
        TapAt(engine, backend, commander, new Vector2(900f, 0f));
        AssertCollection(engine, commander, SelectedKey, "零位移按下抬起＝点选（零位移矩形×单位屏幕包围盒）并 replace", marine4);
        Assert.That(
            !engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances afterTap) ||
            afterTap.Count == 0,
            "点选同样停用 boxing context");
        AssertMarkerVisible(engine, presenterRuntime, selectionMarkerDefId, "点选命中单位高亮", marine4);
        AssertMarkerHidden(engine, presenterRuntime, selectionMarkerDefId, "点选替换后其余单位取消高亮", marine1, marine2, marine3);
    }

    [Test]
    public void CandidateDies_MidDrag_ExcludedFromPreviewAndCommit()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);
        AssertNoTriggerErrors(engine);

        Entity commander = Resolve(engine, "case-e-commander");
        Entity marine1 = Resolve(engine, "case-e-marine-1");
        Entity marine2 = Resolve(engine, "case-e-marine-2");
        Entity marine3 = Resolve(engine, "case-e-marine-3");
        Entity marine4 = Resolve(engine, "case-e-marine-4");

        // 候选集就位，按下拖到命中 marine1/marine2，预览集就绪
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        PressAt(engine, backend, new Vector2(-1200f, -100f));
        TickUntil(engine, 20, BoxingActive(engine, commander));
        backend.SetMousePosition(new Vector2(-300f, 100f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 2);
        AssertCollection(engine, commander, SelectionPreviewKey, "拖拽中 marine1/2 命中预览", marine1, marine2);

        // 候选 marine2 在框内被击杀 → 候选集 EntityDied 收缩；预览集要等下一次指针移动重扫
        engine.World.Destroy(marine2);
        TickUntil(engine, 30, () => CollectionCount(engine, commander, SelectableKey) == 3);
        AssertCollection(engine, commander, SelectableKey, "死亡驱动 selectable 收缩（EntityDied）", marine1, marine3, marine4);
        AssertCollection(engine, commander, SelectionPreviewKey, "预览集此时仍是死前命中集（下次移动才重扫）", marine1, marine2);

        // 再动一次指针 → selection_preview_tick 重扫最新候选集 → 死者出预览
        backend.SetMousePosition(new Vector2(-200f, 100f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 1);
        AssertCollection(engine, commander, SelectionPreviewKey, "死亡后的首次指针移动把死者剔出预览", marine1);

        // 松手 → box_commit 重扫最新候选集 → selected 不含死者
        ReleaseAt(engine, backend);
        TickUntil(engine, 30, BoxingCleared(engine, commander));
        Tick(engine, 4);
        AssertCollection(engine, commander, SelectedKey, "提交时重查最新候选集，死者不入选", marine1);
        Assert.That(CollectionContains(engine, commander, SelectedKey, marine2), Is.False,
            "拖拽中死亡的候选永不被提交选中");
        AssertNoTriggerErrors(engine);
    }

    /// <summary>
    /// 指针离开后再次回到框内（入框→出框→再入框）：selection_preview 是纯集合 diff，成员资格双向
    /// 跟随；环实例 创建→销毁→再创建（实例生灭，非一次行为）。
    /// </summary>
    [Test]
    public void CandidateReEntersBoxAfterLeaving_PreviewFollowsMembershipBothDirections()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);
        AssertNoTriggerErrors(engine);

        Entity commander = Resolve(engine, "case-e-commander");
        Entity marine1 = Resolve(engine, "case-e-marine-1");
        Entity marine2 = Resolve(engine, "case-e-marine-2");

        var presenterRuntime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
            ?? throw new InvalidOperationException("PresenterEntityRuntime service is missing.");
        var presenterDefinitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry service is missing.");
        int selectionPreviewMarkerDefId = presenterDefinitions.GetId(SelectionPreviewMarkerPresenter);

        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        PressAt(engine, backend, new Vector2(-1200f, -100f));
        TickUntil(engine, 20, BoxingActive(engine, commander));

        // 入框：指针左区盖住 only marine1（press=-1200 → pointer=-850 的矩形含 x=-900）
        backend.SetMousePosition(new Vector2(-850f, 0f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 1);
        AssertCollection(engine, commander, SelectionPreviewKey, "入框第一段命中 marine1", marine1);
        AssertPreviewOn(engine, presenterRuntime, selectionPreviewMarkerDefId, "再入框后黄环重新创建", marine1);

        // 出框：指针离开 marine1 → 0
        backend.SetMousePosition(new Vector2(-1500f, -300f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 0);
        Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.EqualTo(0), "出框预览清空");

        // 再入框：指针回到 marine1/marine2 区 → 1/2
        backend.SetMousePosition(new Vector2(-200f, 100f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 2);
        AssertCollection(engine, commander, SelectionPreviewKey, "再入框命中 marine1/2，成员资格重新建立", marine1, marine2);
        AssertPreviewOn(engine, presenterRuntime, selectionPreviewMarkerDefId, "成员资格重入 → 黄环实例再生", marine1, marine2);
        AssertNoTriggerErrors(engine);
    }

    /// <summary>
    /// DeactivateContext（图内 op）在移除 context 组件的同一变更点同步跑
    /// onDeactivated 槽——框选结算不再隔一帧。release 当拍即写 selected（不再等门控下一 tick
    /// 的世界扫描）；随后 reconcile 只做延迟卸载、不重复跑槽（selected 不变化、selection_preview 不复活）。
    /// </summary>
    [Test]
    public void BoxSelect_SettlesSelectedSameTickAsDeactivateContext_NoRepeat()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);
        AssertNoTriggerErrors(engine);

        Entity commander = Resolve(engine, "case-e-commander");
        Entity marine1 = Resolve(engine, "case-e-marine-1");
        Entity marine2 = Resolve(engine, "case-e-marine-2");
        Entity marine3 = Resolve(engine, "case-e-marine-3");
        Entity marine4 = Resolve(engine, "case-e-marine-4");

        // 候选集就位（battle roster_sync：铺底 + 出生事件驱动）
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        AssertNoTriggerErrors(engine);

        // 按下 → 拖到命中 marine1/marine2（预览集就位）
        PressAt(engine, backend, new Vector2(-1200f, -100f));
        TickUntil(engine, 20, BoxingActive(engine, commander));
        backend.SetMousePosition(new Vector2(-300f, 100f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 2);
        AssertCollection(engine, commander, SelectionPreviewKey, "拖拽预览命中就位", marine1, marine2);

        // 抬起（firesOn=release）→ box_commit → DeactivateContext：onDeactivated 槽在同拍执行。
        // BoxingCleared 只在 DeactivateContext 移除 boxing 实例的那一 tick 翻转——停在这里立刻断言，
        // 不再多走一帧：selected 已写入、selection_preview 已清空（旧语义要等下一帧扫描才结算）。
        ReleaseAt(engine, backend);
        TickUntil(engine, 30, BoxingCleared(engine, commander));
        AssertCollection(engine, commander, SelectedKey, "DeactivateContext 当拍即结算（selected 不再等下一 tick）", marine1, marine2);
        Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.LessThanOrEqualTo(0),
            "selection_preview_clear 也在同拍执行，预览集同拍清空");
        AssertNoTriggerErrors(engine);

        // 重复防跑：门控下一 tick 只做延迟卸载 + reconcile，槽不因世界扫描再跑一次。
        int selectedCount = CollectionCount(engine, commander, SelectedKey);
        Tick(engine, 4);
        Assert.That(CollectionCount(engine, commander, SelectedKey), Is.EqualTo(selectedCount),
            "reconcile 不再重复跑 onDeactivated 槽（selected 不因槽重跑而变化）");
        Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.LessThanOrEqualTo(0),
            "selection_preview 不被重复写回");
        AssertNoTriggerErrors(engine);
    }

    /// <summary>
    /// 候选集从 MapHeartbeat 轮询改为 EntitySpawned/EntityDied 事件驱动。
    /// 死亡时 roster_sync 重build图谱（QueryAllMapEntities → team/template 过滤 → replace），
    /// 已销毁实体从 MapEntity 查询消失，故候选集自然剔除死者。验证：4 支建材中销毁一支 →
    /// 候选集缩到 3，且死者不在其中（零轮询：EntityDied 触发才是刷新源）。
    /// </summary>
    [Test]
    public void MarineDeath_RemovesFromSelectableRoster()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);
        AssertNoTriggerErrors(engine);

        Entity commander = Resolve(engine, "case-e-commander");
        Entity marine1 = Resolve(engine, "case-e-marine-1");
        Entity marine2 = Resolve(engine, "case-e-marine-2");
        Entity marine3 = Resolve(engine, "case-e-marine-3");
        Entity marine4 = Resolve(engine, "case-e-marine-4");

        // 候选集就位（地图级 mount 收 EntitySpawned，铺底 + 出生事件刷新）
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        AssertCollection(engine, commander, SelectableKey, "候选集四支就位", marine1, marine2, marine3, marine4);

        // 销毁一支建材 → EntityDied 驱动 roster_sync 重建 → 候选集剔除死者
        engine.World.Destroy(marine4);
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 3);
        AssertNoTriggerErrors(engine);
        AssertCollection(engine, commander, SelectableKey, "销毁后候选集缩到 3，死者被剔除", marine1, marine2, marine3);
        Assert.That(CollectionContains(engine, commander, SelectableKey, marine4), Is.False,
            "已销毁实体不再出现在候选集（EntityDied 重建剔除）");
    }

    /// <summary>
    /// 死亡补发合同：主体在拖拽途中被销毁，未走显式 Deactivate——destroy 边界
    /// 必须补跑并激活的 onDeactivated 槽（selection_preview_clear 清预览），且整链不得抛错。
    /// </summary>
    [Test]
    public void CommanderDeath_MidGesture_PerformsDeactivatedSlotCleanup()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);
        AssertNoTriggerErrors(engine);

        Entity commander = Resolve(engine, "case-e-commander");
        Entity marine1 = Resolve(engine, "case-e-marine-1");
        Entity marine2 = Resolve(engine, "case-e-marine-2");

        // 候选集与 boxing 就位，拖到预览命中两支 marine
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        PressAt(engine, backend, new Vector2(-1200f, -100f));
        TickUntil(engine, 20, () =>
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances boxingNow) &&
            boxingNow.Count == 1);
        backend.SetMousePosition(new Vector2(-300f, 100f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 2);
        AssertCollection(engine, commander, SelectionPreviewKey, "死亡前拖拽预览已就位", marine1, marine2);

        // 拖拽途中主体销毁：未显式停用 → destroy 边界补跑 onDeactivated（清预览），不得抛错
        engine.World.Destroy(commander);
        Tick(engine, 2);
        AssertNoTriggerErrors(engine);
        Assert.That(CollectionCount(engine, commander, SelectionPreviewKey) <= 0,
            "主体死亡补发 onDeactivated → selection_preview_clear 清空预览（死亡路径槽执行）");
    }

    [Test]
    public void GlobalCollectionDecoration_RingsShowAndHideWithCollectionMembership()
    {
        string repoRoot = FindRepoRoot();
        var backend = new TestInputBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.case_e") })));
        TickUntil(engine, 40, () => engine.CurrentMapSession != null);
        AssertNoTriggerErrors(engine);

        Entity commander = Resolve(engine, "case-e-commander");
        Entity marine1 = Resolve(engine, "case-e-marine-1");
        Entity marine2 = Resolve(engine, "case-e-marine-2");
        Entity marine3 = Resolve(engine, "case-e-marine-3");

        var presenterRuntime = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
            ?? throw new InvalidOperationException("PresenterEntityRuntime service is missing.");
        var presenterDefinitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("PresenterDefinitionRegistry service is missing.");
        int selectionPreviewMarkerDefId = presenterDefinitions.GetId(SelectionPreviewMarkerPresenter);
        int selectionMarkerDefId = presenterDefinitions.GetId(SelectionMarkerPresenter);
        int marineDefId = presenterDefinitions.GetId(MarinePresenter);

        // 候选集就位；marine 模板上环槽与规则早已删除（只留 body 资产）
        TickUntil(engine, 60, () => CollectionCount(engine, commander, SelectableKey) == 4);
        TickUntil(engine, 40, () => presenterRuntime.GetActiveByDefinition(marineDefId).Count == 4);
        Assert.That(presenterRuntime.GetActiveByDefinition(marineDefId).Count, Is.EqualTo(4),
            "marine root presenter 照常出生（body）");

        // 按下 → 拖到命中 marine1/marine2 → 成员实体上出现预览环实例
        PressAt(engine, backend, new Vector2(-1200f, -100f));
        TickUntil(engine, 20, () =>
            engine.World.TryGet<InteractionContextInstances>(commander, out InteractionContextInstances boxingNow) &&
            boxingNow.Count == 1);
        backend.SetMousePosition(new Vector2(-300f, 100f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 2);
        AssertPreviewOn(engine, presenterRuntime, selectionPreviewMarkerDefId, "成员入预览集→黄环实例挂到成员", marine1, marine2);
        AssertPreviewOff(engine, presenterRuntime, selectionPreviewMarkerDefId, "未命中成员无环实例", marine3);
        AssertMarkerHidden(engine, presenterRuntime, selectionMarkerDefId, "预览阶段不连蓝环", marine1, marine2, marine3);

        backend.SetMousePosition(new Vector2(-900f, 0f));
        TickUntil(engine, 10, () => CollectionCount(engine, commander, SelectionPreviewKey) == 1);
        AssertPreviewOff(engine, presenterRuntime, selectionPreviewMarkerDefId, "成员离开预览集后隐藏黄环", marine2);
        Assert.That(CountPresentersOwnedBy(presenterRuntime, selectionPreviewMarkerDefId, marine2, engine.World), Is.EqualTo(1));
        AssertPreviewOn(engine, presenterRuntime, selectionPreviewMarkerDefId, "仍在命中集者保留黄环", marine1);

        // 抬起落定 → selected 集合落定（蓝环挂到成员），boxing 停用清空预览集
        ReleaseAt(engine, backend);
        TickUntil(engine, 30, BoxingCleared(engine, commander));
        Tick(engine, 2);
        TickUntil(engine, 20, () => CollectionCount(engine, commander, SelectedKey) >= 1);
        Assert.That(CollectionCount(engine, commander, SelectionPreviewKey), Is.LessThanOrEqualTo(0),
            "停用 boxing → onDeactivated 槽清空预览集");
        AssertPreviewOff(engine, presenterRuntime, selectionPreviewMarkerDefId,
            "预览集清空后所有黄环隐藏", marine1, marine2, marine3);
        AssertMarkerVisible(engine, presenterRuntime, selectionMarkerDefId, "selected 落定→蓝环实例挂到成员", marine1);
        AssertNoTriggerErrors(engine);
    }

    private static bool HasScreenRect(ScreenOverlayBuffer overlay, int x, int y, int width, int height)
    {
        ReadOnlySpan<Ludots.Core.Presentation.Hud.ScreenOverlayItem> span = overlay.GetSpan();
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly var item = ref span[i];
            if (item.Kind == ScreenOverlayItemKind.Rect &&
                item.X == x && item.Y == y &&
                item.Width == width && item.Height == height)
            {
                return true;
            }
        }

        return false;
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
    /// 输入动作冻结快照读到的 IsDown 在整个释放窗口内成立。
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

    private static void AssertMarkerVisible(
        GameEngine engine,
        PresenterEntityRuntime runtime,
        int markerDefId,
        string message,
        params Entity[] units)
    {
        foreach (Entity unit in units)
        {
            Assert.That(CountVisibleMarkersOwnedBy(runtime, markerDefId, unit, engine.World), Is.EqualTo(1),
                $"{message}：{unit} 应显示一个蓝环");
        }
    }

    private static void AssertMarkerHidden(
        GameEngine engine,
        PresenterEntityRuntime runtime,
        int markerDefId,
        string message,
        params Entity[] units)
    {
        foreach (Entity unit in units)
        {
            Assert.That(CountVisibleMarkersOwnedBy(runtime, markerDefId, unit, engine.World), Is.EqualTo(0),
                $"{message}：{unit} 不应显示蓝环");
        }
    }

    private static void AssertPreviewOn(
        GameEngine engine,
        PresenterEntityRuntime runtime,
        int markerDefId,
        string message,
        params Entity[] units)
    {
        foreach (Entity unit in units)
        {
            Assert.That(CountVisibleMarkersOwnedBy(runtime, markerDefId, unit, engine.World), Is.EqualTo(1),
                $"{message}：{unit} 应显示一个黄环");
        }
    }

    private static void AssertPreviewOff(
        GameEngine engine,
        PresenterEntityRuntime runtime,
        int markerDefId,
        string message,
        params Entity[] units)
    {
        foreach (Entity unit in units)
        {
            Assert.That(CountVisibleMarkersOwnedBy(runtime, markerDefId, unit, engine.World), Is.EqualTo(0),
                $"{message}：{unit} 不应显示黄环");
        }
    }

    private static int CountVisibleMarkersOwnedBy(PresenterEntityRuntime runtime, int defId, Entity owner, World world)
    {
        if (!runtime.TryGetActiveByOwner(owner, out var bucket)) return 0;
        Assert.That(PresenterParamKeyRegistry.TryGetId("case_e.marker.visible", out int visibleKey), Is.True);
        int count = 0;
        for (int i = 0; i < bucket.Count; i++)
        {
            Entity candidate = bucket[i];
            if (!world.IsAlive(candidate) || !world.Has<PresenterState>(candidate) ||
                world.Get<PresenterState>(candidate).DefId != defId) continue;
            Assert.That(runtime.TryResolveInt(candidate, visibleKey, out int visible), Is.True);
            if (visible != 0) count++;
        }
        return count;
    }

    private static int CountPresentersOwnedBy(PresenterEntityRuntime runtime, int defId, Entity owner, World world)
    {
        // 装饰器实例是「无规则」的 Scoped presenter，不进 _byDefinition/_byOwnerDefinition
        // 索引（那是给规则承载者定义的按需优化）；实例统一落在 owner 桶里，从桶里过滤。
        if (!runtime.TryGetActiveByOwner(owner, out var bucket))
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < bucket.Count; i++)
        {
            Entity candidate = bucket[i];
            if (!world.IsAlive(candidate) || !world.Has<PresenterState>(candidate))
            {
                continue;
            }

            if (world.Get<PresenterState>(candidate).DefId == defId)
            {
                count++;
            }
        }

        return count;
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
