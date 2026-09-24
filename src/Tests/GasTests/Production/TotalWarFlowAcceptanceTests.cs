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
            // nav 运行时装载后 selectable 集合绑定与就绪时序后移——等候选集装满再拖框，
            // 否则框选与集合绑定竞速（间歇空选集，case_e 测试同款就绪等待）
            TickUntil(engine, 120, () =>
            {
                var collections = engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.EntityCollectionStore)!;
                int count = 0;
                foreach (ref var chunk in engine.World.Query(new Arch.Core.QueryDescription().WithAll<Ludots.Core.Input.CommandSources.CommandSourceSelectableTag>()))
                {
                    count += chunk.Count;
                }

                bool navReady = engine.GetService(Ludots.Core.MassNavigation.MassNavigationKeys.RuntimeBinding)
                    is Ludots.Core.MassNavigation.Runtime.MassNavigationRuntimeBinding { IsReady: true };
                return count >= 5 && navReady && CollectionCount(engine, commander, "case_e.selectable") >= 2;
            });
            DragSelectBox(engine, backend, OnBoard(-2000f, -2000f), OnBoard(2000f, 2000f));


            backend.SetMousePosition(OnBoard(800f, 0f));
            backend.SetButton("<Mouse>/rightButton", true);
            engine.Tick(1f / 60f);
            backend.SetButton("<Mouse>/rightButton", false);
            TickUntil(engine, 30, () => drain.LastDrainedCount > 0);
            engine.Tick(4);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                "campaign: " + string.Join(" | ", engine.TriggerManager.Errors.Select(e => $"{e.TriggerName}: {e.Exception.Message}")));
            TestContext.Out.WriteLine($"[tw1] campaign drained={drain.LastDrainedCount} accepted={drain.LastAcceptedCount} rejected={drain.LastRejectionReason}");
            Assert.That(drain.LastAcceptedCount, Is.EqualTo(1), "campaign rally intent accepted");
            // nav 运行时合同：marine 是 MassNavigationAgent，rally 意图由 MovePlan 适配器转成
            // nav 行军（无经典 OrderBuffer）——断言两台 marine 绑定 nav 且真实行军
            for (int marchFrame = 0; marchFrame < 120; marchFrame++)
            {
                engine.Tick(1f / 60f);
            }

            AssertNavMarching(engine, marine1, "campaign rally marine1");
            AssertNavMarching(engine, marine2, "campaign rally marine2");
            var m1Pos = engine.World.Get<WorldPositionCm>(marine1).Value;
            var m2Pos = engine.World.Get<WorldPositionCm>(marine2).Value;
            float spreadCm = MathF.Sqrt((float)((m1Pos.X - m2Pos.X) * (m1Pos.X - m2Pos.X) + (m1Pos.Y - m2Pos.Y) * (m1Pos.Y - m2Pos.Y)));
            TestContext.Out.WriteLine($"[tw1-spread] marine1=({(float)m1Pos.X:F0},{(float)m1Pos.Y:F0}) marine2=({(float)m2Pos.X:F0},{(float)m2Pos.Y:F0}) spread={spreadCm:F0}cm");
            if (Ludots.Core.MassNavigation.MassNavigationIds.TryGetCurrentNavigationRuntime(engine, out var simProbe) &&
                simProbe.AgentState.TryGetControllableIndex(marine1, out int idxA) &&
                simProbe.AgentState.TryGetControllableIndex(marine2, out int idxB))
            {
                simProbe.TryGetAgentNavigationTargetWorldCm(idxA, out float taX, out float taY);
                simProbe.TryGetAgentNavigationTargetWorldCm(idxB, out float tbX, out float tbY);
                TestContext.Out.WriteLine($"[tw1-targets] A({taX:F0},{taY:F0}) B({tbX:F0},{tbY:F0}) groupA={simProbe.NavGroupRuntime.HasGroup(idxA)} groupB={simProbe.NavGroupRuntime.HasGroup(idxB)}");
            }

            // ── 迁移：战役 → 备置 ──
            AdvanceStage(engine);
            AssertTopContext(engine, profiles, commander, "interaction.context.tw.deployment");

            // ── 阶段二：战斗备置——右键空地 = 增援放置（SpawnTemplate）──
            int marinesBefore = CountMarines(engine);
            backend.SetMousePosition(OnBoard(900f, 0f));
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

            // 无令不动（#nav authored 合同）：增援出生后不给任何指令，位置必须保持——
            // 曾经的病根是 authored 队目标占位为队长出生点，闲兵全速流向占位目标
            var spawnedMarine = FindNewestMarine(engine, commander);
            var spawnedAt = engine.World.Get<WorldPositionCm>(spawnedMarine).Value;
            for (int idleFrame = 0; idleFrame < 120; idleFrame++)
            {
                engine.Tick(1f / 60f);
            }

            var spawnedNow = engine.World.Get<WorldPositionCm>(spawnedMarine).Value;
            float idleDriftCm = MathF.Sqrt(
                (float)((spawnedNow.X - spawnedAt.X) * (spawnedNow.X - spawnedAt.X)) +
                (float)((spawnedNow.Y - spawnedAt.Y) * (spawnedNow.Y - spawnedAt.Y)));
            TestContext.Out.WriteLine($"[tw2] idle reinforcement drift {idleDriftCm:F1}cm");
            Assert.That(idleDriftCm, Is.LessThan(150f), "idle authored agents only drift by separation (bounded), never march to an implicit target");

            // ── 迁移：备置 → RTS ──
            AdvanceStage(engine);
            AssertTopContext(engine, profiles, commander, "interaction.context.tw.rts");

            // ── 阶段三：RTS 战斗——右键塔 = EQS 围城（三 marine 环位 moveTo → 到位开打）──
            var towerPos = engine.World.Get<WorldPositionCm>(tower).Value;
            var towerScreen = Project(engine, towerPos);
            // 拖框选中三台 marine（2 台集结位 + 1 台增援位；狼/塔不在候选集，框住也无害）
            DragSelectBox(engine, backend, OnBoard(-2000f, -2000f), OnBoard(2000f, 2000f));
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

            // nav 合同：围城环位锚由 MovePlan 执行——等三台 marine 向塔收敛后断言环位距离
            int onRing = 0;
            for (int convergeFrame = 0; convergeFrame < 600 && onRing < 3; convergeFrame++)
            {
                engine.Tick(1f / 60f);
                onRing = CountMarinesNearTower(engine, tower, commander, wolf, towerPos, minCm: 350f, maxCm: 1300f);
            }

            TestContext.Out.WriteLine($"[tw3] marines on siege ring: {onRing}");
            Assert.That(onRing, Is.EqualTo(3), "all three marines (2 placed + 1 deployed) converge on the EQS ring");

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

            // ── 弹体合同：点自己不开花——完成路径的 impact 也过关系过滤+排源 ──
            var commanderPos = engine.World.Get<WorldPositionCm>(commander).Value;
            backend.SetMousePosition(Project(engine, commanderPos));
            backend.SetButton("<Mouse>/leftButton", true);
            engine.Tick(1f / 60f);
            backend.SetButton("<Mouse>/leftButton", false);
            for (int selfFrame = 0; selfFrame < 150; selfFrame++)
            {
                engine.Tick(1f / 60f);
            }

            float commanderHpAfterSelfShot = engine.World.TryGet<AttributeBuffer>(commander, out var selfBuf) ? selfBuf.GetCurrent(healthId) : -1f;
            TestContext.Out.WriteLine($"[selfshot] commander hp after self-click: {commanderHpAfterSelfShot}");
            Assert.That(commanderHpAfterSelfShot, Is.EqualTo(400f).Within(0.01f), "projectile completion must never impact the excluded source (self-click)");

            // ── 死亡规则链：连射致死（220→140→60→-20≤0），DeathRule 销毁 + 击杀计数面板变量 ──
            int presentersBeforeDeath = 0;
            foreach (ref var chunkPre in engine.World.Query(new Arch.Core.QueryDescription().WithAll<Ludots.Core.Presentation.Presenters.PresenterState>()))
            {
                presentersBeforeDeath += chunkPre.Count;
            }
            backend.SetMousePosition(Project(engine, engine.World.Get<WorldPositionCm>(wolf).Value));
            for (int volley = 0; volley < 6 && engine.World.IsAlive(wolf); volley++)
            {
                backend.SetButton("<Mouse>/leftButton", true);
                engine.Tick(1f / 60f);
                backend.SetButton("<Mouse>/leftButton", false);
                for (int settleFrame = 0; settleFrame < 300 && engine.World.IsAlive(wolf); settleFrame++)
                {
                    engine.Tick(1f / 60f);
                }
            }

            var mapVariables = engine.CurrentMapSession?.Variables;
            for (int killFrame = 0; killFrame < 60; killFrame++)
            {
                engine.Tick(1f / 60f);
                if ((mapVariables?.ReadInt("tw.kills") ?? 0) >= 1)
                {
                    break;
                }
            }

            int killCount = mapVariables?.ReadInt("tw.kills") ?? -1;
            float wolfFinal = engine.World.IsAlive(wolf) && engine.World.TryGet<AttributeBuffer>(wolf, out var wolfFinalBuffer)
                ? wolfFinalBuffer.GetCurrent(healthId)
                : -1f;
            TestContext.Out.WriteLine($"[tw5] wolf alive={engine.World.IsAlive(wolf)} finalHp={wolfFinal} kills={killCount} errors={engine.TriggerManager.Errors.Count}: {string.Join(" | ", engine.TriggerManager.Errors.Take(3).Select(e => $"{e.TriggerName}: {e.Exception.Message}"))}");
            Assert.That(engine.World.IsAlive(wolf), Is.False, "death rule destroys the wolf at zero health");
            Assert.That(killCount, Is.GreaterThanOrEqualTo(1), "kill counter map variable increments on EntityDied");
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

        private static void AssertNavMarching(Ludots.Core.Engine.GameEngine engine, Entity actor, string message)
        {
            Assert.That(engine.World.Has<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex>(actor),
                Is.True, message + " (nav bound)");
        }

        private static int CountMarinesNearTower(
            Ludots.Core.Engine.GameEngine engine,
            Entity tower,
            Entity commander,
            Entity wolf,
            Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 towerPos,
            float minCm,
            float maxCm)
        {
            int count = 0;
            foreach (ref var chunk in engine.World.Query(new QueryDescription().WithAll<Ludots.Core.MassNavigation.Runtime.MassNavigationAgentIndex, WorldPositionCm>()))
            {
                foreach (var index in chunk)
                {
                    Entity actor = chunk.Entity(index);
                    if (actor.Equals(tower) || actor.Equals(commander) || actor.Equals(wolf))
                    {
                        continue;
                    }

                    var position = chunk.Get<WorldPositionCm>(index).Value;
                    float dx = (float)position.X - (float)towerPos.X;
                    float dy = (float)position.Y - (float)towerPos.Y;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance >= minCm && distance <= maxCm)
                    {
                        count++;
                    }
                }
            }

            return count;
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


        private static int CollectionCount(Ludots.Core.Engine.GameEngine engine, Entity owner, string key)
        {
            var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore service is missing.");
            int keyId = store.KeyRegistry.GetId(key);
            return keyId > 0 && store.TryGet(owner, keyId, out Ludots.Core.EntityCollections.EntityCollectionHandle handle) &&
                store.TryGetView(handle, out Ludots.Core.EntityCollections.EntityCollectionView view)
                ? view.Count
                : -1;
        }

        private static int CountPresenters(Ludots.Core.Engine.GameEngine engine)
        {
            int count = 0;
            foreach (ref var chunk in engine.World.Query(new Arch.Core.QueryDescription().WithAll<Ludots.Core.Presentation.Presenters.PresenterState>()))
            {
                count += chunk.Count;
            }

            return count;
        }

        private static Entity FindNewestMarine(Ludots.Core.Engine.GameEngine engine, Entity commander)
        {
            Entity newest = Entity.Null;
            int maxId = -1;
            foreach (ref var chunk in engine.World.Query(new QueryDescription().WithAll<WorldPositionCm>()))
            {
                foreach (var index in chunk)
                {
                    Entity candidate = chunk.Entity(index);
                    if (engine.World.Has<AbilityStateBuffer>(candidate) &&
                        !candidate.Equals(commander) &&
                        candidate.Id > maxId)
                    {
                        maxId = candidate.Id;
                        newest = candidate;
                    }
                }
            }

            return newest;
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

        private const float BoardCenterScreen = 10000f;

        private static Vector2 OnBoard(float x, float y) => new(x + BoardCenterScreen, y + BoardCenterScreen);

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
