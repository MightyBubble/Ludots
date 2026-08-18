using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class CityRallySpawnTargetAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "city_rally";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "EntityCommandPanelMod",
            "RtsDemoMod",
            "CityRallyShowcaseMod",
        };

        [Test]
        public void PointRally_TrainSoldier_SpawnedUnitReceivesMoveOrder()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity city = FindEntity(world, "城池");
            BlackboardStoredTargetKeys spawnTargetKeys = ResolveSpawnTargetKeys(engine);
            Vector3 rallyPoint = BuildRallyPoint(world, city, offsetX: 3200f, offsetZ: 4800f);

            SubmitSetSpawnTarget(engine, city, rallyPoint);
            TickUntil(
                engine,
                frameTimesMs,
                () => BlackboardStoredTargetOps.TryRead(world, city, in spawnTargetKeys, out BlackboardStoredTargetSnapshot stored) &&
                      stored.Kind == BlackboardStoredTargetKind.Point &&
                      Vector3.Distance(stored.WorldPositionCm, rallyPoint) < 1f,
                maxFrames: 30,
                "城池 should persist the point rally target on its blackboard.");

            var soldierIdsBefore = SnapshotEntityIdsByName(world, "兵士");
            CastAbility(engine, city, city, slot: 2);
            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByName(world, "兵士") == soldierIdsBefore.Count + 1,
                maxFrames: 600,
                "城池 should finish training a 兵士.");

            Entity soldier = FindNewestEntityByName(world, "兵士", soldierIdsBefore);
            int moveToOrderTypeId = RequireOrderTypeId(engine, "moveTo");
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<OrderBuffer>(soldier) &&
                      world.Get<OrderBuffer>(soldier).HasActive &&
                      world.Get<OrderBuffer>(soldier).ActiveOrder.Order.OrderTypeId == moveToOrderTypeId,
                maxFrames: 60,
                "Spawned 兵士 should receive a moveTo order from ApplySpawnTargetOrder.");

            ref Order activeOrder = ref world.Get<OrderBuffer>(soldier).ActiveOrder.Order;
            Assert.That(activeOrder.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            Assert.That(activeOrder.Args.Spatial.WorldCm.X, Is.EqualTo(rallyPoint.X).Within(1f));
            Assert.That(activeOrder.Args.Spatial.WorldCm.Z, Is.EqualTo(rallyPoint.Z).Within(1f));
            Assert.That(world.TryGet(soldier, out PlayerOwner owner), Is.True);
            Assert.That(owner.PlayerId, Is.GreaterThan(0));
        }

        [Test]
        public void RallyPoint_IsReusable_SecondTrainStillMovesToRallyPoint()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity city = FindEntity(world, "城池");
            BlackboardStoredTargetKeys spawnTargetKeys = ResolveSpawnTargetKeys(engine);
            Vector3 rallyPoint = BuildRallyPoint(world, city, offsetX: 2400f, offsetZ: 5600f);

            SubmitSetSpawnTarget(engine, city, rallyPoint);
            TickUntil(
                engine,
                frameTimesMs,
                () => BlackboardStoredTargetOps.TryRead(world, city, in spawnTargetKeys, out BlackboardStoredTargetSnapshot stored) &&
                      stored.Kind == BlackboardStoredTargetKind.Point &&
                      Vector3.Distance(stored.WorldPositionCm, rallyPoint) < 1f,
                maxFrames: 30,
                "城池 should persist the point rally target.");

            var soldierIdsBefore = SnapshotEntityIdsByName(world, "兵士");
            CastAbility(engine, city, city, slot: 2);
            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByName(world, "兵士") == soldierIdsBefore.Count + 1,
                maxFrames: 600,
                "城池 should finish training the first 兵士.");

            Entity first = FindNewestEntityByName(world, "兵士", soldierIdsBefore);
            int moveToOrderTypeId = RequireOrderTypeId(engine, "moveTo");
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<OrderBuffer>(first) &&
                      world.Get<OrderBuffer>(first).HasActive &&
                      world.Get<OrderBuffer>(first).ActiveOrder.Order.OrderTypeId == moveToOrderTypeId,
                maxFrames: 60,
                "First spawned 兵士 should receive the moveTo order.");

            // Re-train: the persisted rally target must still apply to the next unit.
            var secondIdsBefore = SnapshotEntityIdsByName(world, "兵士");
            CastAbility(engine, city, city, slot: 2);
            TickUntil(
                engine,
                frameTimesMs,
                () => CountEntitiesByName(world, "兵士") == secondIdsBefore.Count + 1,
                maxFrames: 600,
                "城池 should finish training the second 兵士.");

            Entity second = FindNewestEntityByName(world, "兵士", secondIdsBefore);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<OrderBuffer>(second) &&
                      world.Get<OrderBuffer>(second).HasActive &&
                      world.Get<OrderBuffer>(second).ActiveOrder.Order.OrderTypeId == moveToOrderTypeId,
                maxFrames: 60,
                "Second spawned 兵士 should also receive the moveTo order to the same rally point.");

            ref Order secondOrder = ref world.Get<OrderBuffer>(second).ActiveOrder.Order;
            Assert.That(secondOrder.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            Assert.That(secondOrder.Args.Spatial.WorldCm.X, Is.EqualTo(rallyPoint.X).Within(1f));
            Assert.That(secondOrder.Args.Spatial.WorldCm.Z, Is.EqualTo(rallyPoint.Z).Within(1f));
        }

        private static int RequireOrderTypeId(GameEngine engine, string key)
        {
            var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry) as OrderTypeRegistry
                ?? throw new InvalidOperationException("OrderTypeRegistry service is missing.");
            return orderTypes.GetId(key);
        }

        private static BlackboardStoredTargetKeys ResolveSpawnTargetKeys(GameEngine engine)
        {
            var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry) as OrderTypeRegistry
                ?? throw new InvalidOperationException("OrderTypeRegistry service is missing.");
            int setSpawnTargetOrderTypeId = orderTypes.GetId("setSpawnTarget");
            OrderTypeConfig config = orderTypes.Get(setSpawnTargetOrderTypeId);
            Assert.That(config.PersistentStoredTargetKeys.IsConfigured, Is.True);
            return config.PersistentStoredTargetKeys;
        }

        private static Vector3 BuildRallyPoint(World world, Entity entity, float offsetX, float offsetZ)
        {
            Vector2 position = ReadWorldPosition(world, entity);
            return new Vector3(position.X + offsetX, 0f, position.Y + offsetZ);
        }

        private static void SubmitSetSpawnTarget(GameEngine engine, Entity entity, Vector3 rallyPoint)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = RequireOrderTypeId(engine, "setSpawnTarget"),
                PlayerId = 1,
                Actor = entity,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = rallyPoint,
                    },
                },
                SubmitMode = OrderSubmitMode.Immediate,
            });

            Assert.That(enqueued, Is.True, "setSpawnTarget order should enqueue.");
        }

        private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = RequireOrderTypeId(engine, "castAbility"),
                PlayerId = 1,
                Actor = actor,
                Target = target,
                Args = new OrderArgs { I0 = slot },
                SubmitMode = OrderSubmitMode.Immediate,
            });

            Assert.That(enqueued, Is.True);
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs)
        {
            engine.LoadMap(mapId);
            Tick(engine, 5, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
            for (int i = 0; i < frames; i++)
            {
                if (stepPolicy.Mode == GasStepMode.Manual)
                {
                    stepPolicy.RequestStep(1);
                }

                var stopwatch = Stopwatch.StartNew();
                engine.Tick(DeltaTime);
                stopwatch.Stop();
                frameTimesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static void TickUntil(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> condition,
            int maxFrames,
            string because)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.Fail(because);
        }

        private static Vector2 ReadWorldPosition(World world, Entity entity)
        {
            ref readonly var position = ref world.Get<WorldPositionCm>(entity);
            return new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat());
        }

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Missing entity '{entityName}'.");
            }

            return result;
        }

        private static int CountEntitiesByName(World world, string entityName)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });
            return count;
        }

        private static HashSet<int> SnapshotEntityIdsByName(World world, string entityName)
        {
            var ids = new HashSet<int>();
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(entity.Id);
                }
            });
            return ids;
        }

        private static Entity FindNewestEntityByName(World world, string entityName, HashSet<int> baselineIds)
        {
            Entity newest = Entity.Null;
            int newestId = -1;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (!string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase) ||
                    baselineIds.Contains(entity.Id))
                {
                    return;
                }

                if (entity.Id > newestId)
                {
                    newestId = entity.Id;
                    newest = entity;
                }
            });

            if (newest == Entity.Null)
            {
                throw new InvalidOperationException($"Missing newly spawned entity '{entityName}'.");
            }

            return newest;
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
