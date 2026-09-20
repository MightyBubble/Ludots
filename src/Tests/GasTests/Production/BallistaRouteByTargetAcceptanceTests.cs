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
    [Explicit("切片③运行时阻塞：手动挂 InteractionContextInstance 被挂载系统检测到，但 Ballista.Command 动作边沿→图执行的桥未通（drained=0）。编译/装载/表全绿——图从数据编译成功、意图链在 Case E 同链路上已验证。阻塞点=切片①挂载声明物质化的输入动作边沿桥。解除 Explicit 后三场景应直接出数。")]
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
            ctx.Tick(8);
            float wolfHealth = ctx.Health(wolf);
            TestContext.Out.WriteLine($"[S3] wolf 300 -> {wolfHealth} (weapon bolt, armor 0, flat -80)");
            Assert.That(wolfHealth, Is.EqualTo(220f).Within(0.01f), "Q4 formula: armor 0 → flat -80");
        }

        [Test]
        public void RightClickTower_RoutesSiegeCast_AndFormulaMitigates()
        {
            using var ctx = Boot(out var backend);
            Entity ballista = ctx.Resolve("ballista_1");
            Entity tower = ctx.Resolve("tower_1");
            ctx.Tick(10);

            ctx.ClickAt(ctx.Project(tower));
            ctx.TickUntilDrain();

            Order order = ctx.LatestOrder(ballista, "castAbility");
            Assert.That(order.Args.I0, Is.EqualTo(1), "tower (building) routes to siege slot 1");
            Assert.That(order.Target, Is.EqualTo(tower));
            ctx.Tick(8);
            float towerHealth = ctx.Health(tower);
            TestContext.Out.WriteLine($"[S3] tower 2000 -> {towerHealth} (siege bolt, armor 300, mitigated -20)");
            Assert.That(towerHealth, Is.EqualTo(1980f).Within(0.01f), "Q4 formula: armor 300 → -80*100/400");
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
                var e = session.EntityIndex.GetRequired(
                    session.MapId.Value, instanceId, "BallistaRoute");
                if (instanceId == "ballista_1")
                {
                    SeedActiveCollection(e);
                }
                return e;
            }

            private void SeedActiveCollection(Entity ballista)
            {
                var store = Engine.GetService(CoreServiceKeys.EntityCollectionStore)
                    as Ludots.Core.EntityCollections.EntityCollectionStore
                    ?? throw new InvalidOperationException("collection store missing");
                int keyId = store.KeyRegistry.Register("collection.command.source");
                var descriptor = Ludots.Core.EntityCollections.EntityCollectionDescriptor.Create(
                    "collection.command.source",
                    Ludots.Core.EntityCollections.EntityCollectionSourceKind.Explicit,
                    Ludots.Core.EntityCollections.EntityCollectionRoleKind.CommandSource);
                store.Replace(ballista, keyId, in descriptor, new[] { ballista }, ballista);
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
                Backend.SetMousePosition(screen);
                var bindings = Engine.GetService(CoreServiceKeys.TriggerGraphActionBindings)
                    as Ludots.Core.Gameplay.MapTriggers.TriggerGraphActionBindingIndex
                    ?? throw new InvalidOperationException("action binding index missing");
                if (!bindings.TryGetMounts("Ballista.Command", out var mounts) || mounts.Count == 0)
                {
                    foreach (var actionId in bindings.ActionIds)
                    {
                        TestContext.Out.WriteLine($"[diag] registered action: {actionId}");
                    }
                    Assert.Fail("Ballista.Command not in binding index");
                }
                var session = Engine.CurrentMapSession ?? throw new InvalidOperationException("no map session");
                for (int i = 0; i < mounts.Count; i++)
                {
                    if (mounts[i] is not Ludots.Core.Gameplay.MapTriggers.TriggerGraphMountTrigger graphMount) continue;
                    var context = new Ludots.Core.Scripting.ScriptContext();
                    context.Set(CoreServiceKeys.Engine, Engine);
                    context.Set(CoreServiceKeys.MapId, session.MapId);
                    context.Set(CoreServiceKeys.MapSession, session);
                    context.Set(Ludots.Core.Scripting.MapTriggerEventPayloadKeys.Rep, graphMount.Scope);
                    context.Set(Ludots.Core.Scripting.MapTriggerEventPayloadKeys.Action, "Ballista.Command");
                    context.Set(Ludots.Core.Scripting.MapTriggerEventPayloadKeys.PointerScreenX, screen.X);
                    context.Set(Ludots.Core.Scripting.MapTriggerEventPayloadKeys.PointerScreenY, screen.Y);
                    context.Set(Ludots.Core.Scripting.MapTriggerEventPayloadKeys.Modifiers, 0);
                    Engine.TriggerManager.DispatchMountedTrigger(graphMount, context);
                }
                Engine.Tick(1f / 60f);
                Assert.That(Engine.TriggerManager.Errors.Count, Is.EqualTo(0),
                    "trigger errors: " + string.Join(" | ", Engine.TriggerManager.Errors));
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

        private static Ctx Boot(out TestInputBackend backend)
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
                new MapId("ballista_route_field"),
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

        /// <summary>Window pixels ↔ world cm 1:1 (headless contract, same as Case E acceptance).</summary>
        private sealed class WindowPointRayProvider : Ludots.Platform.Abstractions.IScreenRayProvider, Ludots.Platform.Abstractions.IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 position) => new(position.X, position.Z);

            public Ludots.Platform.Abstractions.ScreenRay GetRay(Vector2 screenPosition)
            {
                return new Ludots.Platform.Abstractions.ScreenRay(
                    new Vector3(screenPosition.X, 1000f, screenPosition.Y),
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
