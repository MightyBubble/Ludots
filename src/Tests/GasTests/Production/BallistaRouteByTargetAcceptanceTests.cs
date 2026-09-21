using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.Map;
using Ludots.Core.Client;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Slice-3 acceptance (#1398): one right-click Command routes per hit-target kind through a
    /// data table — wolf → weapon cast (slot 0), tower → siege cast (slot 1), ground → moveTo.
    /// The whole chain is data: TriggerGraph + lookup table + abilities + Q4 formula effect.
    /// </summary>
    [TestFixture]
    public sealed class BallistaRouteByTargetAcceptanceTests
    {
        private const string ModId = "BallistaRouteByTargetMod";

        [Test]
        public void RightClickWolf_RoutesWeaponCast_AndFormulaSettlesByArmor()
        {
            using var ctx = Boot(out var backend);
            Entity ballista = ctx.Resolve("ballista_1");
            Entity wolf = ctx.Resolve("wolf_1");
            ctx.Tick(10);

            ctx.ClickAt(ctx.Project(wolf));
            ctx.TickUntilDrain();

            Order order = ctx.LatestOrder(ballista, "castAbility");
            Assert.That(order.Args.I0, Is.EqualTo(0), "wolf (biological) routes to weapon slot 0");
            Assert.That(order.Target, Is.EqualTo(wolf), "cast carries the hit entity");
            // 弹道化后伤害随飞行时间到达：轮询到血量稳定
            float wolfHealth = ctx.Health(wolf);
            for (int frame = 0; frame < 120 && wolfHealth >= 300f; frame++)
            {
                ctx.Tick(1);
                wolfHealth = ctx.Health(wolf);
            }

            TestContext.Out.WriteLine($"[S3] wolf first bolt lands -> {wolfHealth} (weapon bolt, armor 0, flat -80)");
            Assert.That(wolfHealth, Is.EqualTo(220f).Within(0.01f), "Q4 formula: armor 0 → flat -80");
        }

        [Test]
        public void RightClickTower_EngageBatchRingsAroundTarget_AndSiegeSettlesAfterArrival()
        {
            using var ctx = Boot(out var backend, "ballista_engage_field");
            Entity ballista1 = ctx.Resolve("ballista_1");
            Entity ballista2 = ctx.Resolve("ballista_2");
            Entity ballista3 = ctx.Resolve("ballista_3");
            Entity tower = ctx.Resolve("tower_1");
            ctx.Tick(10);

            ctx.ClickAt(ctx.Project(tower));
            ctx.TickUntilDrain();

            // EQS ring assignment: three distinct moveTo anchors, all on the 800cm ring
            // around the tower (profile engage.siege.ring), none inside the footprint.
            var ballistas = new[] { ballista1, ballista2, ballista3 };
            var anchors = new System.Numerics.Vector3[3];
            var towerPos = ctx.Engine.World.Get<Ludots.Core.Components.WorldPositionCm>(tower).Value;
            for (int i = 0; i < ballistas.Length; i++)
            {
                Order move = ctx.LatestOrder(ballistas[i], "moveTo");
                Assert.That(move.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm), "engage lands a moveTo anchor");
                anchors[i] = move.Args.Spatial.WorldCm;
                float dx = anchors[i].X - (float)towerPos.X;
                float dy = anchors[i].Z - (float)towerPos.Y;
                float radius = MathF.Sqrt(dx * dx + dy * dy);
                TestContext.Out.WriteLine($"[S3] ballista_{i + 1} anchor=({anchors[i].X:F0},{anchors[i].Z:F0}) ringRadius={radius:F0}");
                Assert.That(radius, Is.EqualTo(800f).Within(2f), "engage anchor sits on the EQS ring (800cm)");
            }

            for (int a = 0; a < anchors.Length; a++)
            {
                for (int b = a + 1; b < anchors.Length; b++)
                {
                    float sep = System.Numerics.Vector3.Distance(anchors[a], anchors[b]);
                    Assert.That(sep, Is.GreaterThan(100f), "engage anchors are distinct ring slots");
                }
            }

            // Staggered arrivals (different travel distances) → one bolt per ballista from
            // its move-then-cast continuation; Q4 formula with armor 300 settles exactly
            // -80*100/400 = -20 per bolt. First observed drop is the fastest ballista's
            // single bolt; the stable terminal state is all three bolts landed.
            float firstBoltHealth = -1f;
            for (int frame = 0; frame < 1200 && firstBoltHealth < 0f; frame++)
            {
                ctx.Tick(1);
                if (ctx.Health(tower) < 2000f)
                {
                    firstBoltHealth = ctx.Health(tower);
                }
            }

            Assert.That(firstBoltHealth, Is.EqualTo(1980f).Within(0.01f), "first arrival lands exactly one siege bolt (Q4 armor-300 mitigation -20)");

            float towerHealth = ctx.Health(tower);
            for (int frame = 0; frame < 1200 && towerHealth > 1940f; frame++)
            {
                ctx.Tick(1);
                towerHealth = ctx.Health(tower);
            }

            TestContext.Out.WriteLine($"[S3] tower 2000 -> {firstBoltHealth} (first bolt) -> {towerHealth} (all three engaged)");
            Assert.That(towerHealth, Is.EqualTo(1940f).Within(0.01f), "each ballista lands exactly one bolt: 3 × -20, stable terminal state");
        }

        [Test]
        public void RightClickGround_RoutesMoveTo()
        {
            using var ctx = Boot(out var backend);
            Entity ballista = ctx.Resolve("ballista_1");
            ctx.Tick(10);

            var screen = ctx.ProjectWorld(800, 0);
            ctx.ClickAt(screen);
            ctx.TickUntilDrain();

            Order order = ctx.LatestOrder(ballista, "moveTo");
            TestContext.Out.WriteLine($"[S3] ground click -> moveTo at {order.Args.Spatial.WorldCm}");
            Assert.That(order.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm), "ground routes moveTo");
        }

        // ── harness ──

        private sealed class Ctx : IDisposable
        {
            public Ludots.Core.Engine.GameEngine Engine = null!;
            public TestInputBackend Backend = null!;
            public int HealthAttr;
            public int CastAbilityTypeId;

            public Entity Resolve(string instanceId)
            {
                var session = Engine.CurrentMapSession ?? throw new InvalidOperationException("map not loaded");
                return session.EntityIndex.GetRequired(
                    session.MapId.Value, instanceId, "BallistaRoute");
            }

            public Entity Spawn(string kind, int xCm, int yCm)
            {
                Entity e = kind switch
                {
                    "ballista_crew" => Engine.World.Create(
                        new Ludots.Core.Components.WorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(xCm, yCm) },
                        Attr(400, 50),
                        new Ludots.Core.Gameplay.GAS.Components.AbilityStateBuffer(),
                        new Ludots.Core.Gameplay.GAS.Components.OrderBuffer(),
                        new Ludots.Core.Gameplay.GAS.Components.BlackboardIntBuffer(),
                        new Ludots.Core.Gameplay.GAS.Components.BlackboardEntityBuffer(),
                        new Ludots.Core.Gameplay.GAS.Components.BlackboardSpatialBuffer(),
                        new Ludots.Core.Gameplay.GAS.Components.ActiveEffectContainer(),
                        new Ludots.Core.Gameplay.GAS.Components.DirtyFlags(),
                        new Ludots.Core.Gameplay.Components.PlayerOwner { PlayerId = 1 },
                        Attr2(600)),
                    "ballista_wolf" => Engine.World.Create(
                        new Ludots.Core.Components.WorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(xCm, yCm) },
                        Attr(300, 0),
                        Tags("Unit.Biological"),
                        new Ludots.Core.Gameplay.GAS.Components.ActiveEffectContainer(),
                        new Ludots.Core.Gameplay.GAS.Components.DirtyFlags()),
                    "ballista_tower" => Engine.World.Create(
                        new Ludots.Core.Components.WorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(xCm, yCm) },
                        Attr(2000, 300),
                        Tags("Structure.Building"),
                        new Ludots.Core.Gameplay.GAS.Components.ActiveEffectContainer(),
                        new Ludots.Core.Gameplay.GAS.Components.DirtyFlags()),
                    _ => throw new ArgumentException($"unknown kind {kind}"),
                };
                Engine.World.Add(e, new Ludots.Core.Components.MapEntity { MapId = new MapId("ballista_route_field") });
                if (kind == "ballista_crew")
                {
                    ref var slots = ref Engine.World.Get<Ludots.Core.Gameplay.GAS.Components.AbilityStateBuffer>(e);
                    slots.AddAbility(Ludots.Core.Gameplay.GAS.Registry.AbilityIdRegistry.GetId("Ability.Ballista.Shot"));
                    slots.AddAbility(Ludots.Core.Gameplay.GAS.Registry.AbilityIdRegistry.GetId("Ability.Ballista.SiegeBolt"));
                    MountBattleContext(e);
                }
                Engine.Tick(1f / 60f);
                return e;
            }

            /// <summary>Template-spawn bypass: mount the battle context via the runtime (proper activation path) + seed the active collection.</summary>
            private void MountBattleContext(Entity ballista)
            {
                var runtime = Engine.GetService(CoreServiceKeys.InteractionContextInstances)
                    as Ludots.Core.Input.Interaction.InteractionContextInstanceRuntime
                    ?? throw new InvalidOperationException("context instance runtime missing");
                var store = Engine.GetService(CoreServiceKeys.EntityCollectionStore)
                    as Ludots.Core.EntityCollections.EntityCollectionStore
                    ?? throw new InvalidOperationException("collection store missing");

                int contextKeyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register("interaction.context.ballista");
                runtime.Activate(ballista, contextKeyId, 0);

                int collectionKeyId = store.KeyRegistry.Register("collection.command.source");

                var descriptor = Ludots.Core.EntityCollections.EntityCollectionDescriptor.Create(
                    "collection.command.source",
                    Ludots.Core.EntityCollections.EntityCollectionSourceKind.Explicit,
                    Ludots.Core.EntityCollections.EntityCollectionRoleKind.CommandSource);
                store.Replace(ballista, collectionKeyId, in descriptor, new[] { ballista }, ballista);
            }

            private static Ludots.Core.Gameplay.GAS.Components.AttributeBuffer Attr(float hp, float armor)
            {
                var a = new Ludots.Core.Gameplay.GAS.Components.AttributeBuffer();
                a.SetBase(Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Ballista.Health"), hp);
                a.SetBase(Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Ballista.Armor"), armor);
                return a;
            }

            private static Ludots.Core.Gameplay.GAS.Components.AttributeBuffer Attr2(float moveSpeed)
            {
                var a = new Ludots.Core.Gameplay.GAS.Components.AttributeBuffer();
                a.SetBase(Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("MoveSpeed"), moveSpeed);
                return a;
            }

            private static Ludots.Core.Gameplay.GAS.Components.GameplayTagContainer Tags(params string[] tags)
            {
                var c = new Ludots.Core.Gameplay.GAS.Components.GameplayTagContainer();
                foreach (string t in tags) c.AddTag(Ludots.Core.Gameplay.GAS.Registry.TagRegistry.Register(t));
                return c;
            }

            public Vector2 Project(Entity worldEntity)
            {
                var projector = Engine.GetService(CoreServiceKeys.ScreenProjector)
                    as Ludots.Platform.Abstractions.IScreenProjector
                    ?? throw new InvalidOperationException("projector missing");
                var pos = Engine.World.Get<Ludots.Core.Components.WorldPositionCm>(worldEntity).Value;
                return projector.WorldToScreen(new Vector3((float)pos.X, 0f, (float)pos.Y));
            }

            public Vector2 ProjectWorld(int xCm, int yCm) => Project(
                Engine.World.Create(new Ludots.Core.Components.WorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(xCm, yCm) }));

            public void ClickAt(Vector2 screen)
            {
                // Real input pipeline (same contract as Case E acceptance): press the bound
                // right-button edge and let the tick capture a fresh pointer snapshot. Manual
                // DispatchMountedTrigger would run the graph against the stale snapshot of the
                // previous tick, silently re-routing every click to the last-seen pointer.
                Backend.SetMousePosition(screen);
                Backend.SetButton("<Mouse>/rightButton", true);
                Engine.Tick(1f / 60f);
                Backend.SetButton("<Mouse>/rightButton", false);
            }

            public void Tick(int frames)
            {
                for (int i = 0; i < frames; i++) Engine.Tick(1f / 60f);
            }

            public void TickUntilDrain()
            {
                var drain = Engine.GetService(CoreServiceKeys.CommandIntentBufferDrain)
                    as Ludots.Core.Input.Orders.CommandIntentBufferDrainSystem
                    ?? throw new InvalidOperationException("drain missing");
                TickUntil(30, () => drain.LastDrainedCount > 0);
                Tick(4);
                TestContext.Out.WriteLine($"[drain] drained={drain.LastDrainedCount} accepted={drain.LastAcceptedCount} rejected={drain.LastRejectionReason}");
                Assert.That(Engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                    "trigger errors: " + string.Join(" | ", Engine.TriggerManager.Errors));
            }

            public void TickUntil(int maxFrames, Func<bool> predicate)
            {
                for (int i = 0; i < maxFrames && !predicate(); i++) Engine.Tick(1f / 60f);
            }

            public Order LatestOrder(Entity actor, string orderTypeKey)
            {
                var orderTypes = Engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                    as OrderTypeRegistry ?? throw new InvalidOperationException("order types missing");
                int typeId = orderTypes.GetId(orderTypeKey);
                Assert.That(typeId, Is.GreaterThan(0), $"{orderTypeKey} registered");
                ref var buffer = ref Engine.World.Get<OrderBuffer>(actor);

                Assert.That(buffer.IsEmpty, Is.False, $"{orderTypeKey} order should be active on actor");
                Order order = buffer.ActiveOrder.Order;
                Assert.That(order.OrderTypeId, Is.EqualTo(typeId), $"active order is {orderTypeKey}");
                return order;
            }

            public float Health(Entity e) => Engine.World.Get<AttributeBuffer>(e).GetCurrent(HealthAttr);

            public void Dispose() => Engine.Dispose();
        }

        private static Ctx Boot(out TestInputBackend backend, string mapId = "ballista_route_field")
        {
            string repoRoot = FindRepoRoot();
            backend = new TestInputBackend();
            var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", ModId }),
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
                (Ludots.Platform.Abstractions.IScreenRayProvider)new WindowPointRayProvider());
            engine.SetService(
                CoreServiceKeys.ScreenProjector,
                (Ludots.Platform.Abstractions.IScreenProjector)new WindowPointRayProvider());
            engine.Start();
            engine.LoadMap(new MapLoadRequest(
                new MapId(mapId),
                MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, "scheme.default") })));
            for (int i = 0; i < 40 && engine.CurrentMapSession == null; i++) engine.Tick(1f / 60f);

            var ctx = new Ctx
            {
                Engine = engine,
                Backend = backend,
                HealthAttr = AttributeRegistry.GetId("Ballista.Health"),
            };
            Assert.That(ctx.HealthAttr, Is.GreaterThan(0), "Ballista.Health registered by fixture");
            // 诊断：startup contexts + trigger mounts
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                TestContext.Out.WriteLine($"[boot] input context[{i}]: {engine.MergedConfig.StartupInputContexts[i]}");
            }
            return ctx;
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
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        /// <summary>
        /// Window pixels ↔ world cm 1:1 (headless contract, same as Case E acceptance).
        /// The screen-ray contract is visual METERS, so GetRay divides by 100 — a raw pixel
        /// origin would inflate the ground hit ×100 and land outside the fixture heightmap.
        /// </summary>
        private sealed class WindowPointRayProvider : Ludots.Platform.Abstractions.IScreenRayProvider, Ludots.Platform.Abstractions.IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 position) => new(position.X, position.Z);

            public Ludots.Platform.Abstractions.ScreenRay GetRay(Vector2 screenPosition)
            {
                return new Ludots.Platform.Abstractions.ScreenRay(
                    new Vector3(screenPosition.X / 100f, 1000f, screenPosition.Y / 100f),
                    -Vector3.UnitY);
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

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly HashSet<string> _buttons = new(StringComparer.Ordinal);
            public Vector2 MousePosition { get; set; }
            public string BackendId => "test";
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
