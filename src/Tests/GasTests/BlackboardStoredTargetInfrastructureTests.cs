using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [NonParallelizable]
    [TestFixture]
    public sealed class BlackboardStoredTargetInfrastructureTests
    {
        private static BlackboardStoredTargetKeys CreateTestKeys()
        {
            int kind = OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Kind");
            int position = OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Position");
            int entity = OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.Entity");
            int hexQ = OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.HexQ");
            int hexR = OrderBlackboardKeyRegistry.Register("Test.SpawnTarget.HexR");
            return new BlackboardStoredTargetKeys(kind, position, entity, hexQ, hexR);
        }

        [Test]
        public void BlackboardStoredTargetOps_PointEntityAndHexRoundTrip()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            Entity host = world.Create(
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            Entity targetUnit = world.Create();

            BlackboardStoredTargetOps.SetPoint(world, host, new Vector3(1200f, 0f, 800f), in keys);
            Assert.That(BlackboardStoredTargetOps.TryRead(world, host, in keys, out BlackboardStoredTargetSnapshot pointTarget), Is.True);
            Assert.That(pointTarget.Kind, Is.EqualTo(BlackboardStoredTargetKind.Point));
            Assert.That(pointTarget.WorldPositionCm.X, Is.EqualTo(1200f).Within(0.01f));

            BlackboardStoredTargetOps.SetHex(world, host, 3, -2, in keys);
            Assert.That(BlackboardStoredTargetOps.TryRead(world, host, in keys, out BlackboardStoredTargetSnapshot hexTarget), Is.True);
            Assert.That(hexTarget.Kind, Is.EqualTo(BlackboardStoredTargetKind.HexCell));
            Assert.That(hexTarget.HexQ, Is.EqualTo(3));
            Assert.That(hexTarget.HexR, Is.EqualTo(-2));

            BlackboardStoredTargetOps.SetEntity(world, host, targetUnit, in keys);
            Assert.That(BlackboardStoredTargetOps.TryRead(world, host, in keys, out BlackboardStoredTargetSnapshot entityTarget), Is.True);
            Assert.That(entityTarget.Kind, Is.EqualTo(BlackboardStoredTargetKind.Entity));
            Assert.That(entityTarget.TargetEntity, Is.EqualTo(targetUnit));
        }

        [Test]
        public void InstantCompleteOrderSystem_CommitsPointTargetAndCompletes()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            const int setSpawnTargetOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setSpawnTarget",
                OrderTypeId = setSpawnTargetOrderTypeId,
                InstantComplete = true,
                PersistentStoredTargetKeys = keys,
            });

            Entity host = world.Create(OrderBuffer.CreateEmpty(), new BlackboardIntBuffer(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer());
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(host);
            buffer.SetActiveDirect(new Order
            {
                OrderId = 1,
                OrderTypeId = setSpawnTargetOrderTypeId,
                Target = Entity.Null,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(500f, 0f, 700f),
                    },
                },
            }, priority: 40);

            var system = new InstantCompleteOrderSystem(world, orderTypes);
            system.Update(default);

            Assert.That(buffer.HasActive, Is.False);
            Assert.That(BlackboardStoredTargetOps.TryRead(world, host, in keys, out BlackboardStoredTargetSnapshot target), Is.True);
            Assert.That(target.Kind, Is.EqualTo(BlackboardStoredTargetKind.Point));
            Assert.That(target.WorldPositionCm.X, Is.EqualTo(500f).Within(0.01f));
        }

        [Test]
        public void InstantCompleteOrderSystem_WhenStoredTargetCapacityIsFull_PreservesOldTargetAndActiveOrder()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            const int setSpawnTargetOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setSpawnTarget",
                OrderTypeId = setSpawnTargetOrderTypeId,
                InstantComplete = true,
                PersistentStoredTargetKeys = keys,
            });

            Entity host = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            Entity oldTarget = world.Create();
            BlackboardStoredTargetOps.SetEntity(world, host, oldTarget, in keys);
            ref BlackboardSpatialBuffer spatial = ref world.Get<BlackboardSpatialBuffer>(host);
            for (int i = 0; i < BlackboardSpatialBuffer.MAX_ENTRIES; i++)
            {
                spatial.SetPoint(-1_000 - i, new Vector3(i, 0f, i));
            }
            Assert.That(spatial.EntryCount, Is.EqualTo(BlackboardSpatialBuffer.MAX_ENTRIES));
            Assert.That(spatial.HasKey(keys.TargetPositionKey), Is.False);

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(host);
            buffer.SetActiveDirect(new Order
            {
                OrderId = 88,
                OrderTypeId = setSpawnTargetOrderTypeId,
                Target = Entity.Null,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(500f, 0f, 700f),
                    },
                },
            }, priority: 40);
            Assert.That(buffer.ActiveOrder.Order.Target, Is.EqualTo(Entity.Null));
            Assert.That(buffer.ActiveOrder.Order.Args.Spatial.Mode, Is.EqualTo(OrderCollectionMode.Single));
            var system = new InstantCompleteOrderSystem(world, orderTypes);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => system.Update(default))!;

            Assert.That(error.Message, Does.StartWith("GAS.STORED_TARGET.ERR.BlackboardCapacityExceeded"));
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderId, Is.EqualTo(88));
            Assert.That(BlackboardStoredTargetOps.TryRead(
                world,
                host,
                in keys,
                out BlackboardStoredTargetSnapshot stored), Is.True);
            Assert.That(stored.Kind, Is.EqualTo(BlackboardStoredTargetKind.Entity));
            Assert.That(stored.TargetEntity, Is.EqualTo(oldTarget));
            Assert.That(world.Get<BlackboardSpatialBuffer>(host).EntryCount,
                Is.EqualTo(BlackboardSpatialBuffer.MAX_ENTRIES));
        }

        [Test]
        public void InstantCompleteOrderSystem_WhenStoredTargetIntCapacityIsFull_PreservesOldTargetAndActiveOrder()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            const int setSpawnTargetOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setSpawnTarget",
                OrderTypeId = setSpawnTargetOrderTypeId,
                InstantComplete = true,
                PersistentStoredTargetKeys = keys,
            });

            Entity host = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            Entity oldTarget = world.Create();
            BlackboardStoredTargetOps.SetEntity(world, host, oldTarget, in keys);
            ref BlackboardIntBuffer ints = ref world.Get<BlackboardIntBuffer>(host);
            for (int i = ints.Count; i < GasConstants.MAX_BLACKBOARD_ENTRIES; i++)
            {
                ints.Set(-1_000 - i, i);
            }
            Assert.That(ints.Count, Is.EqualTo(GasConstants.MAX_BLACKBOARD_ENTRIES));

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(host);
            buffer.SetActiveDirect(new Order
            {
                OrderId = 89,
                OrderTypeId = setSpawnTargetOrderTypeId,
                Target = Entity.Null,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.Hex,
                        Mode = OrderCollectionMode.Single,
                        A0 = 4,
                        A1 = -3,
                    },
                },
            }, priority: 40);
            var system = new InstantCompleteOrderSystem(world, orderTypes);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => system.Update(default))!;

            Assert.That(error.Message, Does.StartWith(BlackboardStoredTargetOps.CapacityExceededError));
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderId, Is.EqualTo(89));
            Assert.That(BlackboardStoredTargetOps.TryRead(
                world,
                host,
                in keys,
                out BlackboardStoredTargetSnapshot stored), Is.True);
            Assert.That(stored.Kind, Is.EqualTo(BlackboardStoredTargetKind.Entity));
            Assert.That(stored.TargetEntity, Is.EqualTo(oldTarget));
            Assert.That(world.Get<BlackboardIntBuffer>(host).Count,
                Is.EqualTo(GasConstants.MAX_BLACKBOARD_ENTRIES));
        }

        [Test]
        public void InstantCompleteOrderSystem_WhenStoredTargetEntityCapacityIsFull_PreservesOldTargetAndActiveOrder()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            const int setSpawnTargetOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setSpawnTarget",
                OrderTypeId = setSpawnTargetOrderTypeId,
                InstantComplete = true,
                PersistentStoredTargetKeys = keys,
            });

            Entity host = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            Vector3 oldPoint = new(120f, 0f, 340f);
            BlackboardStoredTargetOps.SetPoint(world, host, oldPoint, in keys);
            Entity newTarget = world.Create();
            ref BlackboardEntityBuffer entities = ref world.Get<BlackboardEntityBuffer>(host);
            for (int i = 0; i < BlackboardEntityBuffer.MAX_ENTRIES; i++)
            {
                entities.Set(-1_000 - i, newTarget);
            }
            Assert.That(entities.Count, Is.EqualTo(BlackboardEntityBuffer.MAX_ENTRIES));

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(host);
            buffer.SetActiveDirect(new Order
            {
                OrderId = 90,
                OrderTypeId = setSpawnTargetOrderTypeId,
                Target = newTarget,
            }, priority: 40);
            var system = new InstantCompleteOrderSystem(world, orderTypes);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => system.Update(default))!;

            Assert.That(error.Message, Does.StartWith(BlackboardStoredTargetOps.CapacityExceededError));
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderId, Is.EqualTo(90));
            Assert.That(BlackboardStoredTargetOps.TryRead(
                world,
                host,
                in keys,
                out BlackboardStoredTargetSnapshot stored), Is.True);
            Assert.That(stored.Kind, Is.EqualTo(BlackboardStoredTargetKind.Point));
            Assert.That(stored.WorldPositionCm, Is.EqualTo(oldPoint));
            Assert.That(world.Get<BlackboardEntityBuffer>(host).Count,
                Is.EqualTo(BlackboardEntityBuffer.MAX_ENTRIES));
        }

        [Test]
        public void InstantCompleteOrderSystem_WhenEntityTargetDied_PreservesOldTargetAndActiveOrder()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            const int setSpawnTargetOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setSpawnTarget",
                OrderTypeId = setSpawnTargetOrderTypeId,
                InstantComplete = true,
                PersistentStoredTargetKeys = keys,
            });

            Entity host = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer());
            Vector3 oldPoint = new(120f, 0f, 340f);
            BlackboardStoredTargetOps.SetPoint(world, host, oldPoint, in keys);
            Entity deadTarget = world.Create();
            world.Destroy(deadTarget);

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(host);
            buffer.SetActiveDirect(new Order
            {
                OrderId = 91,
                OrderTypeId = setSpawnTargetOrderTypeId,
                Target = deadTarget,
            }, priority: 40);
            var system = new InstantCompleteOrderSystem(world, orderTypes);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => system.Update(default))!;

            Assert.That(error.Message, Does.StartWith("GAS.STORED_TARGET.ERR.InvalidTargetEntity"));
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderId, Is.EqualTo(91));
            Assert.That(BlackboardStoredTargetOps.TryRead(
                world,
                host,
                in keys,
                out BlackboardStoredTargetSnapshot stored), Is.True);
            Assert.That(stored.Kind, Is.EqualTo(BlackboardStoredTargetKind.Point));
            Assert.That(stored.WorldPositionCm, Is.EqualTo(oldPoint));
        }

        [TestCase(nameof(BlackboardIntBuffer))]
        [TestCase(nameof(BlackboardSpatialBuffer))]
        [TestCase(nameof(BlackboardEntityBuffer))]
        public void InstantCompleteOrderSystem_MissingStoredTargetBlackboard_HardFailsBeforeMutation(string missingComponent)
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            const int setSpawnTargetOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setSpawnTarget",
                OrderTypeId = setSpawnTargetOrderTypeId,
                InstantComplete = true,
                PersistentStoredTargetKeys = keys,
            });

            Entity host = world.Create(OrderBuffer.CreateEmpty());
            if (missingComponent != nameof(BlackboardIntBuffer))
            {
                world.Add(host, new BlackboardIntBuffer());
            }
            if (missingComponent != nameof(BlackboardSpatialBuffer))
            {
                world.Add(host, new BlackboardSpatialBuffer());
            }
            if (missingComponent != nameof(BlackboardEntityBuffer))
            {
                world.Add(host, new BlackboardEntityBuffer());
            }
            world.Get<OrderBuffer>(host).SetActiveDirect(new Order
            {
                OrderId = 77,
                OrderTypeId = setSpawnTargetOrderTypeId,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(500f, 0f, 700f),
                    },
                },
            }, priority: 40);
            var system = new InstantCompleteOrderSystem(world, orderTypes);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(default))!;

            Assert.That(ex.Message, Does.StartWith("GAS.ORDER_BLACKBOARD.ERR.MissingState"));
            ref OrderBuffer result = ref world.Get<OrderBuffer>(host);
            Assert.That(result.HasActive, Is.True);
            Assert.That(result.ActiveOrder.Order.OrderId, Is.EqualTo(77));
            Assert.That(world.Has<BlackboardIntBuffer>(host), Is.EqualTo(missingComponent != nameof(BlackboardIntBuffer)));
            Assert.That(world.Has<BlackboardSpatialBuffer>(host), Is.EqualTo(missingComponent != nameof(BlackboardSpatialBuffer)));
            Assert.That(world.Has<BlackboardEntityBuffer>(host), Is.EqualTo(missingComponent != nameof(BlackboardEntityBuffer)));
        }

        [Test]
        public void EntityBuilder_OrderBuffer_InstallsStoredTargetBlackboards()
        {
            using World world = World.Create();
            var template = new EntityTemplate
            {
                Id = "order_actor",
                Components =
                {
                    ["OrderBuffer"] = JsonNode.Parse("{}")!,
                },
            };
            var templates = new Dictionary<string, EntityTemplate>
            {
                [template.Id] = template,
            };

            Entity entity = new EntityBuilder(world, templates)
                .UseTemplate(template.Id)
                .Build();

            Assert.That(world.Has<OrderBuffer>(entity), Is.True);
            Assert.That(world.Has<BlackboardIntBuffer>(entity), Is.True);
            Assert.That(world.Has<BlackboardSpatialBuffer>(entity), Is.True);
            Assert.That(world.Has<BlackboardEntityBuffer>(entity), Is.True);
        }

        [Test]
        public void TemplateBatchSpawner_OrderBuffer_InstallsStoredTargetBlackboardsInArchetype()
        {
            using World world = World.Create();
            var template = new EntityTemplate
            {
                Id = "batch_order_actor",
                Components =
                {
                    ["Name"] = JsonNode.Parse("{ \"Value\": \"BatchOrderActor\" }")!,
                    ["WorldPositionCm"] = JsonNode.Parse("{ \"Value\": { \"X\": 0, \"Y\": 0 } }")!,
                    ["FacingDirection"] = JsonNode.Parse("{ \"AngleRad\": 0 }")!,
                    ["OrderBuffer"] = JsonNode.Parse("{}")!,
                },
            };
            var spawner = new TemplateEntityBatchSpawner(
                world,
                new EntityTemplateKeyRegistry(),
                scratchCapacity: 1);
            var requests = new[]
            {
                new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(default, hasWorldPosition: false),
            };

            bool created = spawner.TryCreateBatch(
                template.Id,
                template,
                requests,
                TemplateBatchSpawnFeatures.None,
                out ReadOnlySpan<Entity> entities);

            Assert.That(created, Is.True);
            Assert.That(entities.Length, Is.EqualTo(1));
            Assert.That(world.Has<OrderBuffer>(entities[0]), Is.True);
            Assert.That(world.Has<BlackboardIntBuffer>(entities[0]), Is.True);
            Assert.That(world.Has<BlackboardSpatialBuffer>(entities[0]), Is.True);
            Assert.That(world.Has<BlackboardEntityBuffer>(entities[0]), Is.True);
        }

        [Test]
        public void SubmitOrderFromBlackboardHandler_SubmitsMoveOrderForPointTarget()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 101, AllowQueuedMode = true });
            orderTypes.Register(new OrderTypeConfig { Key = "castAbility", OrderTypeId = 100, IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex, AllowQueuedMode = true });

            Entity source = world.Create(new BlackboardIntBuffer(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer());
            Entity spawned = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer(),
                new PlayerOwner { PlayerId = 1 });
            BlackboardStoredTargetOps.SetPoint(world, source, new Vector3(900f, 0f, 1200f), in keys);

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var runtime = new BuiltinHandlerExecutionContext
            {
                OrderTypeRegistry = orderTypes,
                CurrentStep = 1,
                StepRateHz = 30,
            };

            var template = new EffectTemplateData
            {
                SubmitOrderFromBlackboard = new SubmitOrderFromBlackboardDescriptor
                {
                    SourceSlot = RelationEntitySlot.Source,
                    TargetSlot = RelationEntitySlot.Target,
                    StoredTargetKeys = keys,
                    PointMoveOrderTypeId = 101,
                    EntityOrderTypeId = 100,
                    EntityOrderIntArg0 = 1,
                    SubmitMode = OrderSubmitMode.Immediate,
                },
            };

            var context = new EffectContext
            {
                Source = source,
                Target = spawned,
            };

            var mergedParams = new EffectConfigParams();
            registry.Invoke(
                BuiltinHandlerId.SubmitOrderFromBlackboard,
                world,
                Entity.Null,
                ref context,
                in mergedParams,
                in template,
                runtime);

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(spawned);
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(101));
            Assert.That(buffer.ActiveOrder.Order.Args.Spatial.WorldCm.X, Is.EqualTo(900f).Within(0.01f));
        }

        [Test]
        public void InstantCompleteOrderSystem_CommitsEntityTargetAndCompletes()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            const int setSpawnTargetOrderTypeId = 106;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "setSpawnTarget",
                OrderTypeId = setSpawnTargetOrderTypeId,
                InstantComplete = true,
                PersistentStoredTargetKeys = keys,
            });

            Entity host = world.Create(OrderBuffer.CreateEmpty(), new BlackboardIntBuffer(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer());
            Entity garrisonTarget = world.Create();
            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(host);
            buffer.SetActiveDirect(new Order
            {
                OrderId = 1,
                OrderTypeId = setSpawnTargetOrderTypeId,
                Actor = host,
                Target = garrisonTarget,
            }, priority: 40);

            var system = new InstantCompleteOrderSystem(world, orderTypes);
            system.Update(default);

            Assert.That(buffer.HasActive, Is.False);
            Assert.That(BlackboardStoredTargetOps.TryRead(world, host, in keys, out BlackboardStoredTargetSnapshot target), Is.True);
            Assert.That(target.Kind, Is.EqualTo(BlackboardStoredTargetKind.Entity));
            Assert.That(target.TargetEntity, Is.EqualTo(garrisonTarget));
        }

        [Test]
        public void SubmitOrderFromBlackboardHandler_SubmitsCastOrderForEntityTarget()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "castAbility", OrderTypeId = 100, IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex, AllowQueuedMode = true });

            Entity source = world.Create(new BlackboardIntBuffer(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer());
            Entity spawned = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardSpatialBuffer(),
                new BlackboardEntityBuffer(),
                new PlayerOwner { PlayerId = 1 });
            Entity garrison = world.Create();
            BlackboardStoredTargetOps.SetEntity(world, source, garrison, in keys);

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var runtime = new BuiltinHandlerExecutionContext
            {
                OrderTypeRegistry = orderTypes,
                CurrentStep = 1,
                StepRateHz = 30,
            };

            var template = new EffectTemplateData
            {
                SubmitOrderFromBlackboard = new SubmitOrderFromBlackboardDescriptor
                {
                    SourceSlot = RelationEntitySlot.Source,
                    TargetSlot = RelationEntitySlot.Target,
                    StoredTargetKeys = keys,
                    PointMoveOrderTypeId = 101,
                    EntityOrderTypeId = 100,
                    EntityOrderIntArg0 = 1,
                    SubmitMode = OrderSubmitMode.Immediate,
                },
            };

            var context = new EffectContext
            {
                Source = source,
                Target = spawned,
            };

            var mergedParams = new EffectConfigParams();
            registry.Invoke(
                BuiltinHandlerId.SubmitOrderFromBlackboard,
                world,
                Entity.Null,
                ref context,
                in mergedParams,
                in template,
                runtime);

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(spawned);
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(100));
            Assert.That(buffer.ActiveOrder.Order.Target, Is.EqualTo(garrison));
            Assert.That(buffer.ActiveOrder.Order.Args.I0, Is.EqualTo(1));
        }

        [Test]
        public void BlackboardStoredTargetOps_TryRead_PointWithoutSpatial_ReturnsFalse()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            Entity host = world.Create(new BlackboardIntBuffer());

            ref BlackboardIntBuffer ints = ref world.Get<BlackboardIntBuffer>(host);
            ints.Set(keys.TargetKindKey, (int)BlackboardStoredTargetKind.Point);

            Assert.That(BlackboardStoredTargetOps.TryRead(world, host, in keys, out _), Is.False);
        }

        [Test]
        public void SubmitOrderFromBlackboardHandler_ThrowsWhenOrderActorHasNoPlayerIdentity()
        {
            using World world = World.Create();
            BlackboardStoredTargetKeys keys = CreateTestKeys();
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 101, AllowQueuedMode = true });

            Entity source = world.Create(new BlackboardIntBuffer(), new BlackboardSpatialBuffer(), new BlackboardEntityBuffer());
            Entity spawned = world.Create(OrderBuffer.CreateEmpty());
            BlackboardStoredTargetOps.SetPoint(world, source, new Vector3(100f, 0f, 200f), in keys);

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var runtime = new BuiltinHandlerExecutionContext
            {
                OrderTypeRegistry = orderTypes,
                CurrentStep = 1,
                StepRateHz = 30,
            };

            var template = new EffectTemplateData
            {
                SubmitOrderFromBlackboard = new SubmitOrderFromBlackboardDescriptor
                {
                    SourceSlot = RelationEntitySlot.Source,
                    TargetSlot = RelationEntitySlot.Target,
                    StoredTargetKeys = keys,
                    PointMoveOrderTypeId = 101,
                    EntityOrderTypeId = 100,
                    EntityOrderIntArg0 = 1,
                    SubmitMode = OrderSubmitMode.Immediate,
                },
            };

            var context = new EffectContext { Source = source, Target = spawned };
            var mergedParams = new EffectConfigParams();

            Assert.Throws<InvalidOperationException>(() => registry.Invoke(
                BuiltinHandlerId.SubmitOrderFromBlackboard,
                world,
                Entity.Null,
                ref context,
                in mergedParams,
                in template,
                runtime));
        }
    }

    [TestFixture]
    public sealed class ActorOrderRoutingMatcherTests
    {
        [Test]
        public void TryMatch_EmptyMatch_AlwaysMatchesAliveActor()
        {
            using World world = World.Create();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            Entity actor = world.Create(new AbilityStateBuffer());
            Assert.That(world.IsAlive(actor), Is.True);
            Assert.That(ActorOrderRoutingMatcher.TryMatch(world, tagOps, actor, new ActorOrderRoutingMatch()), Is.True);
        }

        [Test]
        public void TryResolveOrderTypeKey_SelectsHighestPriorityMatchingCandidate()
        {
            using World world = World.Create();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            Entity producer = world.Create(new AbilityStateBuffer());
            ref AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(producer);
            int trainAbilityId = AbilityIdRegistry.Register("Ability.Rts.Strategy.War3.TrainFootman");
            abilities.AddAbility(AbilityIdRegistry.Register("Ability.Test.Slot0"));
            abilities.AddAbility(AbilityIdRegistry.Register("Ability.Test.Slot1"));
            abilities.AddAbility(trainAbilityId);

            var candidates = new List<ActorOrderRoutingCandidate>
            {
                new()
                {
                    OrderTypeKey = "setSpawnTarget",
                    Priority = 10,
                    Match = new ActorOrderRoutingMatch
                    {
                        AbilitySlotIndex = 2,
                        AbilityIdKey = "Ability.Rts.Strategy.War3.TrainFootman",
                    },
                },
                new()
                {
                    OrderTypeKey = "moveTo",
                    Priority = 0,
                    Match = new ActorOrderRoutingMatch(),
                },
            };

            Assert.That(
                ActorOrderRoutingMatcher.TryResolveOrderTypeKey(world, tagOps, producer, candidates, out string orderTypeKey),
                Is.True);
            Assert.That(orderTypeKey, Is.EqualTo("setSpawnTarget"));
        }

        [Test]
        public void TryMatch_UsesAbilityFormSlotOverride()
        {
            using World world = World.Create();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            Entity producer = world.Create(new AbilityStateBuffer(), new AbilityFormSlotBuffer());
            ref AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(producer);
            int holdAbilityId = AbilityIdRegistry.Register("Ability.Rts.Strategy.Shared.Hold");
            int trainAbilityId = AbilityIdRegistry.Register("Ability.Rts.Strategy.War3.TrainFootman");
            abilities.AddAbility(holdAbilityId);
            abilities.AddAbility(holdAbilityId);
            abilities.AddAbility(holdAbilityId);

            ref AbilityFormSlotBuffer formSlots = ref world.Get<AbilityFormSlotBuffer>(producer);
            formSlots.SetOverride(2, trainAbilityId);

            var match = new ActorOrderRoutingMatch
            {
                AbilitySlotIndex = 2,
                AbilityIdKeySuffix = ".Train",
            };

            Assert.That(ActorOrderRoutingMatcher.TryMatch(world, tagOps, producer, match), Is.True);
        }

        [Test]
        public void TryResolveCandidate_WarpGateTag_SkipsTrainSpawnTargetCandidate()
        {
            using World world = World.Create();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            int warpGateTagId = TagRegistry.Register("Progression.Rts.WarpGate");

            Entity gateway = world.Create(new AbilityStateBuffer(), new GameplayTagContainer(), new TagCountContainer());
            ref AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(gateway);
            int trainAbilityId = AbilityIdRegistry.Register("Ability.Rts.Strategy.Sc2.TrainZealot");
            abilities.AddAbility(AbilityIdRegistry.Register("Ability.Test.Slot0"));
            abilities.AddAbility(AbilityIdRegistry.Register("Ability.Test.Slot1"));
            abilities.AddAbility(trainAbilityId);

            ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(gateway);
            tags.AddTag(warpGateTagId);

            var candidates = new List<ActorOrderRoutingCandidate>
            {
                new()
                {
                    OrderTypeKey = "setSpawnTarget",
                    Priority = 10,
                    Match = new ActorOrderRoutingMatch
                    {
                        AbilitySlotIndex = 2,
                        AbilityIdKeySuffix = ".Train",
                        BlockedAnyTags = new List<string> { "Progression.Rts.WarpGate" },
                    },
                },
                new()
                {
                    OrderTypeKey = "moveTo",
                    Priority = 0,
                    Match = new ActorOrderRoutingMatch(),
                },
            };

            Assert.That(
                ActorOrderRoutingMatcher.TryResolveCandidate(world, tagOps, gateway, candidates, out ActorOrderRoutingCandidate matched),
                Is.True);
            Assert.That(matched.OrderTypeKey, Is.EqualTo("moveTo"));
        }
    }
}
