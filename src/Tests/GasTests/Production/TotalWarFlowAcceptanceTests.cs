using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Client;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// 全战四阶段端到端验收（#1398 owner 裁决的切片③验收样板）：战役大地图 → 战斗备置 →
    /// RTS 战斗 → TPS 瞄准射击，单地图四上下文迁移。每个阶段的右键语义不同（集结/放置/
    /// 路由围城/瞄准开火），成员装载全走 v2 图自取；阶段迁移 = DeactivateContext+ActivateContext。
    /// </summary>
    [TestFixture]
    public sealed class TotalWarFlowAcceptanceTests
    {
        private const string MapId = "total_war_flow_field";

        [Test]
        public void FourStages_CampaignToDeploymentToRtsToTps_SingleMapContextMigration()
        {
            string repoRoot = FindRepoRoot();
            var backend = new TestBackend();
            using var engine = CreateEngine(repoRoot, backend);
            engine.LoadMap(new MapLoadRequest(
                new Ludots.Core.Map.MapId(MapId),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.default") })));
            for (int i = 0; i < 40 && engine.CurrentMapSession == null; i++) engine.Tick(1f / 60f);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, "map loads");

            var session = engine.CurrentMapSession!;
            Entity commander = session.EntityIndex.GetRequired(session.MapId.Value, "tw-commander", "TwFlow");
            Entity marine1 = session.EntityIndex.GetRequired(session.MapId.Value, "tw-marine-1", "TwFlow");
            Entity marine2 = session.EntityIndex.GetRequired(session.MapId.Value, "tw-marine-2", "TwFlow");
            Entity wolf = session.EntityIndex.GetRequired(session.MapId.Value, "wolf_1", "TwFlow");
            Entity tower = session.EntityIndex.GetRequired(session.MapId.Value, "tower_1", "TwFlow");
            engine.Tick(10);

            var profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                as Ludots.Core.Input.Interaction.InteractionContextProfileRegistry
                ?? throw new InvalidOperationException("profile registry missing");
            var drain = engine.GetService(CoreServiceKeys.CommandIntentBufferDrain)
                as CommandIntentBufferDrainSystem
                ?? throw new InvalidOperationException("drain missing");
            int healthId = AttributeRegistry.GetId("Ballista.Health");
            var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry) as OrderTypeRegistry
                ?? throw new InvalidOperationException("order types missing");
            int moveToId = orderTypes.GetId("moveTo");

            // ── 阶段一：战役大地图——右键空地 = 集结（marines 成员集 moveTo）──
            AssertTopContext(engine, profiles, commander, "interaction.context.tw.campaign");
            // 拖框选中两台 marine（成员集改由 selected 集合供给，候选集=tw.candidates 只含 tw_marine）
            DragSelectBox(engine, backend, new Vector2(-420f, -260f), new Vector2(-180f, 260f));


            backend.SetMousePosition(new Vector2(800f, 0f));
            backend.SetButton("<Mouse>/rightButton", true);
            engine.Tick(1f / 60f);
            backend.SetButton("<Mouse>/rightButton", false);
            TickUntil(engine, 30, () => drain.LastDrainedCount > 0);
            engine.Tick(4);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                "campaign: " + string.Join(" | ", engine.TriggerManager.Errors.Select(e => $"{e.TriggerName}: {e.Exception.Message}")));
            TestContext.Out.WriteLine($"[tw1] campaign drained={drain.LastDrainedCount} accepted={drain.LastAcceptedCount} rejected={drain.LastRejectionReason}");
            Assert.That(drain.LastAcceptedCount, Is.EqualTo(1), "campaign rally intent accepted");
            AssertHasMoveTo(engine, marine1, moveToId, "campaign rally marine1");
            AssertHasMoveTo(engine, marine2, moveToId, "campaign rally marine2");

            // ── 迁移：战役 → 备置 ──
            AdvanceStage(engine);
            AssertTopContext(engine, profiles, commander, "interaction.context.tw.deployment");

            // ── 阶段二：战斗备置——右键空地 = 增援放置（SpawnTemplate）──
            int marinesBefore = CountMarines(engine);
            backend.SetMousePosition(new Vector2(900f, 0f));
            backend.SetButton("<Mouse>/rightButton", true);
            engine.Tick(1f / 60f);
            backend.SetButton("<Mouse>/rightButton", false);
            TickUntil(engine, 30, () => CountMarines(engine) > marinesBefore);
            engine.Tick(4);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                "deployment: " + string.Join(" | ", engine.TriggerManager.Errors.Select(e => $"{e.TriggerName}: {e.Exception.Message}")));
            int marinesAfter = CountMarines(engine);
            TestContext.Out.WriteLine($"[tw2] deployment marines {marinesBefore} -> {marinesAfter}");
            Assert.That(marinesAfter, Is.EqualTo(marinesBefore + 1), "deployment spawns one reinforcement");

            // ── 迁移：备置 → RTS ──
            AdvanceStage(engine);
            AssertTopContext(engine, profiles, commander, "interaction.context.tw.rts");

            // ── 阶段三：RTS 战斗——右键塔 = EQS 围城（三 marine 环位 moveTo → 到位开打）──
            var towerPos = engine.World.Get<WorldPositionCm>(tower).Value;
            var towerScreen = Project(engine, towerPos);
            // 拖框选中三台 marine（2 台集结位 + 1 台增援位；狼/塔不在候选集，框住也无害）
            DragSelectBox(engine, backend, new Vector2(-2000f, -2000f), new Vector2(2000f, 2000f));
            backend.SetMousePosition(towerScreen);
            backend.SetButton("<Mouse>/rightButton", true);
            engine.Tick(1f / 60f);
            backend.SetButton("<Mouse>/rightButton", false);
            TickUntil(engine, 30, () => drain.LastDrainedCount > 0);
            engine.Tick(4);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                "rts: " + string.Join(" | ", engine.TriggerManager.Errors.Select(e => $"{e.TriggerName}: {e.Exception.Message}")));
            TestContext.Out.WriteLine($"[tw3] rts drained={drain.LastDrainedCount} accepted={drain.LastAcceptedCount} rejected={drain.LastRejectionReason}");
            Assert.That(drain.LastAcceptedCount, Is.EqualTo(1), "engage intent accepted");

            int engaged = 0;
            foreach (ref var chunk in engine.World.Query(new QueryDescription().WithAll<OrderBuffer>()))
            {
                foreach (var index in chunk)
                {
                    Entity actor = chunk.Entity(index);
                    ref var buffer = ref chunk.Get<OrderBuffer>(index);
                    if (!buffer.IsEmpty && buffer.ActiveOrder.Order.OrderTypeId == moveToId &&
                        engine.World.Has<AbilityStateBuffer>(actor) && !actor.Equals(commander) && !actor.Equals(wolf))
                    {
                        var anchor = buffer.ActiveOrder.Order.Args.Spatial.WorldCm;
                        float dx = anchor.X - (float)towerPos.X;
                        float dy = anchor.Z - (float)towerPos.Y;
                        float radius = MathF.Sqrt(dx * dx + dy * dy);
                        TestContext.Out.WriteLine($"[tw3] engage anchor=({anchor.X:F0},{anchor.Z:F0}) ringRadius={radius:F0}");
                        Assert.That(radius, Is.EqualTo(800f).Within(2f), "engage anchor on the EQS ring");
                        engaged++;
                    }
                }
            }

            Assert.That(engaged, Is.EqualTo(3), "all three marines (2 placed + 1 deployed) get ring anchors");

            float firstBoltHealth = -1f;
            for (int frame = 0; frame < 1200 && firstBoltHealth < 0f; frame++)
            {
                engine.Tick(1f / 60f);
                if (engine.World.TryGet<AttributeBuffer>(tower, out var tb) && tb.GetCurrent(healthId) < 2000f)
                {
                    firstBoltHealth = tb.GetCurrent(healthId);
                }
            }

            Assert.That(firstBoltHealth, Is.EqualTo(1980f).Within(0.01f), "first siege bolt lands (Q4 armor-300 -20)");

            // ── 迁移：RTS → TPS ──
            AdvanceStage(engine);
            AssertTopContext(engine, profiles, commander, "interaction.context.tw.tps");

            // ── 阶段四：TPS 瞄准射击——右键狼 = rep 单体开火（Q4 armor-0 平减 -80）──
            var wolfPos = engine.World.Get<WorldPositionCm>(wolf).Value;
            backend.SetMousePosition(Project(engine, wolfPos));
            backend.SetButton("<Mouse>/leftButton", true);
            engine.Tick(1f / 60f);
            backend.SetButton("<Mouse>/rightButton", false);
            TickUntil(engine, 30, () => drain.LastDrainedCount > 0);
            engine.Tick(4);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                "tps: " + string.Join(" | ", engine.TriggerManager.Errors.Select(e => $"{e.TriggerName}: {e.Exception.Message}")));
            TestContext.Out.WriteLine($"[tw4] tps drained={drain.LastDrainedCount} accepted={drain.LastAcceptedCount} rejected={drain.LastRejectionReason}");
            Assert.That(drain.LastAcceptedCount, Is.EqualTo(1), "tps fire intent accepted");

            float wolfHealth = -1f;
            for (int frame = 0; frame < 120 && wolfHealth < 0f; frame++)
            {
                engine.Tick(1f / 60f);
                if (engine.World.TryGet<AttributeBuffer>(wolf, out var wb) && wb.GetCurrent(healthId) < 300f)
                {
                    wolfHealth = wb.GetCurrent(healthId);
                }
            }

            TestContext.Out.WriteLine($"[tw4] wolf 300 -> {wolfHealth} (commander single bolt, armor 0, flat -80)");
            Assert.That(wolfHealth, Is.EqualTo(220f).Within(0.01f), "TPS fire: rep single cast settles Q4 flat -80");
        }

        // ── harness ──

        private static void AdvanceStage(Ludots.Core.Engine.GameEngine engine)
        {
            InputHandler(engine).InjectButtonPress("Tw.Advance");
            engine.Tick(1f / 60f);
            engine.Tick(2f / 60f);
            TestContext.Out.WriteLine($"[adv] trigger errors={engine.TriggerManager.Errors.Count}: {string.Join(" | ", engine.TriggerManager.Errors.Select(e => $"{e.TriggerName}: {e.Exception.Message}"))}");
        }

        private static PlayerInputHandler InputHandler(Ludots.Core.Engine.GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.InputHandler) as PlayerInputHandler
                ?? throw new InvalidOperationException("input handler missing");
        }

        private static void AssertTopContext(
            Ludots.Core.Engine.GameEngine engine,
            Ludots.Core.Input.Interaction.InteractionContextProfileRegistry profiles,
            Entity rep,
            string expected)
        {
            string? actual = null;
            for (int i = 0; i < 30 && actual == null; i++)
            {
                engine.Tick(1f / 60f);
                if (engine.World.TryGet<Ludots.Core.Input.Interaction.InteractionContextInstances>(rep, out var instances) &&
                    instances.Count > 0)
                {
                    int contextId = instances[instances.Count - 1].ContextId;
                    actual = profiles.ProfileIdRegistry.GetName(contextId);
                }
                else if (engine.World.TryGet<Ludots.Core.Input.Interaction.InteractionContextInstance>(rep, out var baseInstance))
                {
                    actual = profiles.ProfileIdRegistry.GetName(baseInstance.ContextId);
                }
            }

            Assert.That(actual, Is.EqualTo(expected), $"top context should be {expected}");
        }

        private static void AssertHasMoveTo(Ludots.Core.Engine.GameEngine engine, Entity actor, int moveToId, string message)
        {
            Assert.That(
                engine.World.TryGet<OrderBuffer>(actor, out var buffer) && !buffer.IsEmpty &&
                buffer.ActiveOrder.Order.OrderTypeId == moveToId,
                message);
        }

        private static int CountMarines(Ludots.Core.Engine.GameEngine engine)
        {
            int count = 0;
            foreach (ref var chunk in engine.World.Query(new QueryDescription().WithAll<WorldPositionCm>()))
            {
                foreach (var index in chunk)
                {
                    if (engine.World.Has<AbilityStateBuffer>(chunk.Entity(index)))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void DragSelectBox(
            Ludots.Core.Engine.GameEngine engine,
            TestBackend backend,
            Vector2 cornerA,
            Vector2 cornerB)
        {
            backend.SetMousePosition(cornerA);
            backend.SetButton("<Mouse>/leftButton", true);
            engine.Tick(1f / 60f);
            backend.SetMousePosition(cornerB);
            for (int i = 0; i < 10; i++)
            {
                engine.Tick(1f / 60f);
            }

            backend.SetButton("<Mouse>/leftButton", false);
            for (int i = 0; i < 10; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static Vector2 Project(Ludots.Core.Engine.GameEngine engine, Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 world)
        {
            return new Vector2((float)world.X, (float)world.Y);
        }

        private static void TickUntil(Ludots.Core.Engine.GameEngine engine, int maxFrames, Func<bool> predicate)
        {
            for (int i = 0; i < maxFrames && !predicate(); i++) engine.Tick(1f / 60f);
        }

        private static Ludots.Core.Engine.GameEngine CreateEngine(string repoRoot, TestBackend backend)
        {
            var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "SelectionInteractionMod", "BallistaRouteByTargetMod", "TotalWarFlowShowcaseMod" }),
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
                (Ludots.Core.Presentation.Camera.IViewController)new HeadlessView(1600f, 900f));
            engine.SetService(
                CoreServiceKeys.ScreenRayProvider,
                (Ludots.Platform.Abstractions.IScreenRayProvider)new WindowPointRay());
            engine.SetService(
                CoreServiceKeys.ScreenProjector,
                (Ludots.Platform.Abstractions.IScreenProjector)new WindowPointRay());
            engine.Start();
            return engine;
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent!;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private sealed class WindowPointRay : Ludots.Platform.Abstractions.IScreenRayProvider, Ludots.Platform.Abstractions.IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 position) => new(position.X * 100f, position.Z * 100f);

            public Ludots.Platform.Abstractions.ScreenRay GetRay(Vector2 screenPosition)
            {
                return new Ludots.Platform.Abstractions.ScreenRay(
                    new Vector3(screenPosition.X / 100f, 1000f, screenPosition.Y / 100f),
                    -Vector3.UnitY);
            }
        }

        private sealed class HeadlessView : Ludots.Core.Presentation.Camera.IViewController
        {
            public HeadlessView(float width, float height) => Resolution = new Vector2(width, height);
            public Vector2 Resolution { get; }
            public float Fov => 50f;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }

        private sealed class TestBackend : IInputBackend
        {
            private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);
            public Vector2 MousePosition { get; set; }
            public string BackendId => "tw-test";
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.Contains(devicePath);
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
            public void SetButton(string devicePath, bool down)
            {
                if (down) _buttons.Add(devicePath); else _buttons.Remove(devicePath);
            }

            public void SetMousePosition(Vector2 position) => MousePosition = position;
        }
    }
}
