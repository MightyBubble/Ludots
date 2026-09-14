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
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Slice-5 arpg revival acceptance: the dead showcase lives again as pure data on the new
/// chain — map binds the player to the hero rep, the template mounts the battle context whose
/// self-roster graph maintains the active collection, and skill keys 1–6 submit cast intents
/// through the §12 bridge. No mod gameplay code, no per-mod mapping installer.
/// </summary>
[TestFixture]
public sealed class ArpgRevivalAcceptanceTests
{
    private const string MapId = "arpg_entry";

    [Test]
    public void ArpgRevival_SelfRosterAndSkillCastThroughPureDataChain()
    {
        string repoRoot = FindRepoRoot();
        var backend = new HeadlessBackend();
        using GameEngine engine = CreateEngine(repoRoot, backend);
        engine.LoadMap(new MapLoadRequest(
            new MapId(MapId),
            MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1) })));
        TickUntil(engine, 60, () => engine.CurrentMapSession != null);

        Entity hero = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
        Assert.That(engine.World.IsAlive(hero), "地图绑定玩家代表=英雄本体（直接占有，rep 即受控体）");

        var profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
            ?? throw new InvalidOperationException("InteractionContextProfileRegistry service is missing.");
        int battleId = profiles.ProfileIdRegistry.GetId("interaction.context.arpg.battle");
        Assert.That(
            engine.World.TryGet<InteractionContextInstance>(hero, out InteractionContextInstance baseContext) &&
            baseContext.ContextId == battleId,
            "英雄出生即携带 battle context（模板 initialInteractionContext）");

        // self-roster：活跃集=英雄自身
        var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            as EntityCollectionStore
            ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
        int activeKeyId = store.KeyRegistry.GetId("arpg.active");
        TickUntil(engine, 60, () =>
            activeKeyId > 0 &&
            store.TryGet(hero, activeKeyId, out var h) &&
            store.TryGetView(h, out var v) &&
            v.Count == 1);
        Assert.That(
            store.TryGet(hero, activeKeyId, out var handle) && store.TryGetView(handle, out var view) && view.Count == 1,
            "self-roster：活跃集恰好=英雄自身（直接占有=rep 自为下令对象，无内核特例）");

        // 技能 2（槽位1）→ 图 → SubmitCast → drain → 英雄收到 castAbility
        var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            as OrderTypeRegistry
            ?? throw new InvalidOperationException("OrderTypeRegistry service is missing.");
        int castAbilityTypeId = orderTypes.GetId("castAbility");
        var drain = engine.GetService(CoreServiceKeys.CommandIntentBufferDrain)
            as Ludots.Core.Input.Orders.CommandIntentBufferDrainSystem
            ?? throw new InvalidOperationException("CommandIntentBufferDrain service is missing.");

        backend.SetButton("<Keyboard>/digit2", true);
        TickUntil(engine, 30, () => drain.LastDrainedCount > 0);
        backend.SetButton("<Keyboard>/digit2", false);
        Tick(engine, 4);
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join(" | ", engine.TriggerManager.Errors));
        Assert.That(drain.LastAcceptedCount, Is.EqualTo(1),
            $"一条施法意图整条接受（拒绝原因：{drain.LastRejectionReason}）");

        // 合同边界与 rts 试点一致：drain 整条接受（图→op→施法缓冲→§12 路由）；可施法
        // actor 的 GAS 直通执行属执行域合同，订单内容断言在 Case E（无技能 actor 形态）锁定。
        var orderQueueProbe = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue;
        Assert.That(orderQueueProbe?.Count ?? -1, Is.EqualTo(0), "施法令已被下令域消费");
    }

    private static GameEngine CreateEngine(string repoRoot, HeadlessBackend backend)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "ArpgDemoMod" }),
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
        for (int i = 0; i < frames; i++)
        {
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
