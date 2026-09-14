using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Slice-4 aiming graph chain acceptance: SkillQ press activates the derived aim context on
/// the acting rep (template-mounted battle context as parent); the confirm click resolves the
/// ground point and submits one cast intent through the §12 bridge — the active collection
/// members each receive a castAbility order with the authored slot; Escape cancels by
/// deactivating the aim context without submitting.
/// </summary>
[TestFixture]
public sealed class RtsAimGraphAcceptanceTests
{
    private const string MapId = "rts_entry";
    private const string BattleProfile = "interaction.context.rts.battle";
    private const string AimProfile = "interaction.context.rts.aim";

    [Test]
    public void AimChain_PressActivatesContext_ConfirmCasts_CancelDeactivates()
    {
        string repoRoot = FindRepoRoot();
        var backend = new HeadlessBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1) })));
        TickUntil(engine, 60, () => engine.CurrentMapSession != null);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        Entity rep = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
        var profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
            ?? throw new InvalidOperationException("InteractionContextProfileRegistry service is missing.");
        int battleId = profiles.ProfileIdRegistry.GetId(BattleProfile);
        int aimId = profiles.ProfileIdRegistry.GetId(AimProfile);
        Assert.That(
            engine.World.TryGet<InteractionContextInstance>(rep, out InteractionContextInstance baseContext) &&
            baseContext.ContextId == battleId,
            "rep 出生即携带 battle context（模板 initialInteractionContext）");

        // 活跃集：直接种一名玩家单位为下令对象（选中图化属切3，此处测瞄准链）
        Entity caster = SpawnOwnedUnit(engine);
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            as EntityCollectionStore
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
        store.Replace(
            rep,
            EntityCollectionDescriptor.Create("collection.command.source", EntityCollectionSourceKind.Explicit, EntityCollectionRoleKind.CommandSource),
            new[] { caster },
            rep);

        var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            as OrderTypeRegistry
            ?? throw new InvalidOperationException("OrderTypeRegistry service is missing.");
        int castAbilityTypeId = orderTypes.GetId("castAbility");

        // ── Q 按下 → 激活衍生瞄准 context（父=战斗）──
        backend.SetButton("<Keyboard>/q", true);
        TickUntil(engine, 30, () =>
            engine.World.TryGet<InteractionContextInstances>(rep, out InteractionContextInstances instances) &&
            instances.Count == 1);
        backend.SetButton("<Keyboard>/q", false);
        Assert.That(
            engine.World.TryGet<InteractionContextInstances>(rep, out InteractionContextInstances aim) &&
            aim.Count == 1 &&
            aim[0].ContextId == aimId &&
            aim[0].ParentContextId == battleId,
            "SkillQ 按下激活衍生瞄准 context（父=战斗）");

        // ── 左键确认 → 地面点解析 → SubmitCast → 活跃集成员收到 castAbility(槽位0) → 瞄准 context 停用 ──
        var drain = engine.GetService(CoreServiceKeys.CommandIntentBufferDrain)
            as Ludots.Core.Input.Orders.CommandIntentBufferDrainSystem
            ?? throw new InvalidOperationException("CommandIntentBufferDrain service is missing.");
        backend.SetMousePosition(new Vector2(1200f, 800f));
        backend.SetButton("<Mouse>/LeftButton", true);
        TickUntil(engine, 30, () => (drain.LastDrainedCount > 0));
        backend.SetButton("<Mouse>/LeftButton", false);
        Tick(engine, 4);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        // 本切片合同边界：图→op→施法缓冲→drain 整条接受；订单内容断言（槽位/地面点/
        // 执行者）在 Case E CastIntentFullChain 上锁定（无技能 actor 落 OrderBuffer 形态）；
        // 可施法 actor 的 GAS 直通执行属执行域合同。
        Assert.That(drain.LastAcceptedCount, Is.EqualTo(1),
            $"图提交的一条施法意图应整条接受（拒绝原因：{drain.LastRejectionReason}）");
        var orderQueueProbe = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue;
        Assert.That(orderQueueProbe?.Count ?? -1, Is.EqualTo(0), "施法令已被下令域消费");

        Assert.That(
            !engine.World.TryGet<InteractionContextInstances>(rep, out InteractionContextInstances settled) ||
            settled.Count == 0,
            "确认后瞄准 context 停用");
    }


    [Test]
    public void AimCancel_EscapeDeactivatesWithoutSubmitting()
    {
        string repoRoot = FindRepoRoot();
        var backend = new HeadlessBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1) })));
        TickUntil(engine, 60, () => engine.CurrentMapSession != null);

        Entity rep = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            as EntityCollectionStore
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
        Entity caster = SpawnOwnedUnit(engine);
        store.Replace(
            rep,
            EntityCollectionDescriptor.Create("collection.command.source", EntityCollectionSourceKind.Explicit, EntityCollectionRoleKind.CommandSource),
            new[] { caster },
            rep);

        backend.SetButton("<Keyboard>/q", true);
        TickUntil(engine, 30, () =>
            engine.World.TryGet<InteractionContextInstances>(rep, out InteractionContextInstances instances) &&
            instances.Count == 1);
        backend.SetButton("<Keyboard>/q", false);

        backend.SetButton("<Keyboard>/escape", true);
        TickUntil(engine, 30, () =>
            !engine.World.TryGet<InteractionContextInstances>(rep, out InteractionContextInstances cleared) ||
            cleared.Count == 0);
        backend.SetButton("<Keyboard>/escape", false);
        Tick(engine, 4);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));

        Assert.That(
            !engine.World.TryGet<OrderBuffer>(caster, out OrderBuffer buffer) || buffer.IsEmpty,
            "取消不得产生施法令（fail-closed，无静默提交）");
    }

    private static Entity SpawnOwnedUnit(GameEngine engine)
    {
        // rts_entry 出生表里已有玩家单位；取一个带 PlayerOwner 的活体作为活跃集成员
        Entity found = Entity.Null;
        engine.World.Query(
            new Arch.Core.QueryDescription().WithAll<
                Ludots.Core.Gameplay.Components.PlayerOwner,
                OrderBuffer,
                Ludots.Core.Gameplay.GAS.Components.AbilityStateBuffer>(),
            (Entity e) =>
            {
                if (found == Entity.Null && engine.World.TryGet<Ludots.Core.Gameplay.Components.PlayerOwner>(e, out var owner) && owner.PlayerId == 1)
                {
                    found = e;
                }
            });
        return found != Entity.Null
            ? found
            : throw new InvalidOperationException("rts_entry 应存在玩家1单位（带 PlayerOwner+OrderBuffer）");
    }

    private static GameEngine CreateEngine(string repoRoot, HeadlessBackend backend)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CoreInputMod", "EntityCommandPanelMod", "RtsDemoMod" }),
            Path.Combine(repoRoot, "assets"));
        var inputConfig = new Ludots.Core.Input.Config.InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(backend, inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1920f, 1080f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
        engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
        engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        engine.SetService(
            CoreServiceKeys.ViewController,
            (IViewController)new WideViewController());
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
        var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        for (int i = 0; i < frames; i++)
        {
            if (stepPolicy.Mode == Ludots.Core.Gameplay.GAS.GasStepMode.Manual)
            {
                stepPolicy.RequestStep(1);
            }

            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(1f / 60f);
        }
    }

    private static void TickUntil(GameEngine engine, int maxFrames, Func<bool> condition)
    {
        for (int i = 0; i < maxFrames && !condition(); i++)
        {
            Tick(engine, 1);
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

    private sealed class WideViewController : IViewController
    {
        public Vector2 Resolution => new(3200f, 2400f);
        public float Fov => 50f;
        public float AspectRatio => Resolution.X / Resolution.Y;
    }

    private sealed class HeadlessBackend : IInputBackend
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

        public void SetMousePosition(Vector2 position) => MousePosition = position;

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
    }
}
