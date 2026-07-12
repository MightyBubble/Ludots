using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using MobaDemoMod.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class InputOrderContractTests
    {
        [Test]
        public void HeldStartEnd_UnknownStartOrderType_ThrowsInsteadOfFallingBack()
        {
            var (backend, handler) = BuildHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Attack",
                        Trigger = InputTriggerType.Held,
                        HeldPolicy = HeldPolicy.StartEnd,
                        OrderTypeKey = "beam",
                        TargetType = OrderTargetType.None,
                        RequireTarget = false,
                        IsSkillMapping = false,
                    }
                }
            };

            var system = new InputOrderMappingSystem(snapshot, config);
            var orders = new List<Ludots.Core.Gameplay.GAS.Orders.Order>();
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => orders.Add(order));

            using var world = World.Create();
            system.SetLocalPlayer(world.Create(), 1);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);

            var ex = Assert.Throws<InvalidOperationException>(
                () => system.SetOrderTypeKeyResolver(key => key == "beam" ? 100 : 0));

            Assert.That(ex!.Message, Does.Contain("beam.Start"));
            Assert.That(orders, Is.Empty);
        }

        [Test]
        public void UnknownOrderTypeKey_ThrowsDuringResolverInstall()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Attack", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Attack",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "typoOrder",
                        TargetType = OrderTargetType.None,
                        RequireTarget = false,
                        IsSkillMapping = false
                    }
                }
            };

            using var world = World.Create();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(world.Create(), 1);
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order _) => { });

            var ex = Assert.Throws<InvalidOperationException>(() => system.SetOrderTypeKeyResolver(_ => 0));

            Assert.That(ex!.Message, Does.Contain("typoOrder"));
            Assert.That(ex.Message, Does.Contain("orderTypeKey"));
        }

        [Test]
        public void DuplicateActionId_IsRejectedDuringConstruction()
        {
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Attack",
                        OrderTypeKey = "castAbility"
                    },
                    new()
                    {
                        ActionId = "Attack",
                        OrderTypeKey = "stop"
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => new InputOrderMappingSystem(new FrozenInputActionReader(), config));

            Assert.That(ex!.Message, Does.Contain("duplicates"));
            Assert.That(ex.Message, Does.Contain("Attack"));
        }

        [Test]
        public void EmptyActionId_IsRejectedDuringConstruction()
        {
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "",
                        OrderTypeKey = "castAbility"
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => new InputOrderMappingSystem(new FrozenInputActionReader(), config));

            Assert.That(ex!.Message, Does.Contain("actionId"));
            Assert.That(ex.Message, Does.Contain("non-empty"));
        }

        [Test]
        public void Loader_MissingInputOrderMappingsConfig_IsRejected()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_InputOrderContractTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = new ConfigCatalog();
                catalog.Add(new ConfigCatalogEntry("Input/input_order_mappings.json", ConfigMergePolicy.DeepObject));
                var loader = new InputOrderMappingLoader(pipeline);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => loader.Load(catalog, relativePath: "Input/input_order_mappings.json"));

                Assert.That(ex!.Message, Does.Contain("Input/input_order_mappings.json"));
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void RtsDemo_LocalInputAssets_AreCompleteAndReachable()
        {
            string repoRoot = FindRepoRoot();
            string inputPath = Path.Combine(repoRoot, "mods", "RtsDemoMod", "assets", "Input", "default_input.json");
            string mappingPath = Path.Combine(repoRoot, "mods", "RtsDemoMod", "assets", "Input", "input_order_mappings.json");
            string gamePath = Path.Combine(repoRoot, "mods", "RtsDemoMod", "assets", "game.json");

            Assert.That(File.Exists(inputPath), Is.True, $"Missing RTS input config: {inputPath}");
            Assert.That(File.Exists(mappingPath), Is.True, $"Missing RTS mapping config: {mappingPath}");
            Assert.That(File.Exists(gamePath), Is.True, $"Missing RTS game config: {gamePath}");

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());

            var inputConfig = JsonSerializer.Deserialize<InputConfigRoot>(File.ReadAllText(inputPath), jsonOptions);
            Assert.That(inputConfig, Is.Not.Null);
            Assert.That(inputConfig!.Contexts.Exists(context => string.Equals(context.Id, "Rts_Gameplay", StringComparison.Ordinal)),
                Is.True,
                "RtsDemoMod must register its gameplay context explicitly.");

            using var mappingStream = File.OpenRead(mappingPath);
            var mappingConfig = InputOrderMappingLoader.LoadFromStream(mappingStream);
            var actionIds = inputConfig.Actions.Select(action => action.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in mappingConfig.Mappings)
            {
                Assert.That(actionIds.Contains(mapping.ActionId), Is.True, $"RTS mapping action '{mapping.ActionId}' is not declared in default_input.json.");
            }

            Assert.That(mappingConfig.Mappings.Any(ReferencesOrderTypeKey("moveTo")),
                Is.True,
                "RTS local command path must resolve to an explicit move order.");

            using var gameDoc = JsonDocument.Parse(File.ReadAllText(gamePath));
            var startupContexts = gameDoc.RootElement.GetProperty("startupInputContexts")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray();
            Assert.That(startupContexts, Does.Contain("Rts_Gameplay"));
        }

        [Test]
        public void MobaLocalOrderSource_ResolvesCallerSuppliedTargetCollectionKey()
        {
            const string customCollectionKey = "collection.test.explicit.targets";
            string root = Path.Combine(Path.GetTempPath(), "Ludots_MobaCollectionKeyTests", Guid.NewGuid().ToString("N"));
            string inputDir = Path.Combine(root, "assets", "Input");
            Directory.CreateDirectory(inputDir);
            File.WriteAllText(
                Path.Combine(inputDir, "input_order_mappings.json"),
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "SkillQ",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "castAbility",
                      "argsTemplate": { "i0": 0 },
                      "requireTarget": true,
                      "targetCollectionKey": "collection.test.explicit.targets",
                      "targetType": "Entity",
                      "isSkillMapping": false
                    }
                  ]
                }
                """);

            try
            {
                using var world = World.Create();
                Entity localPlayer = world.Create(new PlayerIdentity { PlayerId = 1 });
                Entity commandSourceTarget = world.Create(new PlayerOwner { PlayerId = 1 });
                Entity explicitTarget = world.Create(new PlayerOwner { PlayerId = 1 });
                var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 8);
                collections.Replace(
                    localPlayer,
                    EntityCollectionDescriptor.Create(
                        EntityCollectionKeys.CommandSource,
                        EntityCollectionSourceKind.UiAcquisition,
                        EntityCollectionRoleKind.CommandSource,
                        localPlayer,
                        commandSourceTarget,
                        "Command source",
                        "Should not be used by this mapping"),
                    new[] { commandSourceTarget },
                    localPlayer);
                collections.Replace(
                    localPlayer,
                    EntityCollectionDescriptor.Create(
                        customCollectionKey,
                        EntityCollectionSourceKind.Explicit,
                        EntityCollectionRoleKind.CommandSource,
                        localPlayer,
                        explicitTarget,
                        "Explicit target",
                        "Mapping-requested collection"),
                    new[] { explicitTarget },
                    localPlayer);

                var input = new FrozenInputActionReader();
                input.SetActionState("SkillQ", Vector3.One, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.AuthoritativeInput.Name] = input,
                    [CoreServiceKeys.LocalPlayerEntity.Name] = localPlayer,
                    [CoreServiceKeys.LocalPlayerId.Name] = 1,
                    [CoreServiceKeys.EntityCollectionStore.Name] = collections,
                    [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionKeys,
                    [CoreServiceKeys.GameConfig.Name] = new GameConfig
                    {
                        Constants = new GameConstants
                        {
                            OrderTypeIds = new Dictionary<string, int>
                            {
                                ["castAbility"] = 101,
                                ["stop"] = 1003,
                            },
                        },
                    },
                    [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
                };

                var vfs = new VirtualFileSystem();
                vfs.Mount("TestMobaMappingMod", root);
                var ctx = new ModContext(
                    "TestMobaMappingMod",
                    vfs,
                    new FunctionRegistry(),
                    new TriggerManager(),
                    new Ludots.Core.Engine.SystemFactoryRegistry(),
                    new TriggerDecoratorRegistry());
                var orders = new OrderQueue();
                var system = new MobaLocalOrderSourceSystem(world, globals, orders, ctx);

                system.Update(0f);

                Assert.That(orders.TryDequeue(out Order order), Is.True);
                Assert.That(order.Target, Is.EqualTo(explicitTarget),
                    "MobaLocalOrderSourceSystem must resolve the entity collection named by the mapping, not the active command-source collection.");
                Assert.That(order.Target, Is.Not.EqualTo(commandSourceTarget));
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void InputOrderMappingLoader_RejectsPascalCaseAndRetiredCollectionField()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "SkillQ",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "castAbility",
                      "argsTemplate": { "i0": 0 },
                      "RequireTarget": true,
                      "EntityCollectionKey": "collection.test.explicit.targets",
                      "TargetType": "Entity",
                      "isSkillMapping": false
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<JsonException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("RequireTarget"));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsAmbiguousSmartCastTargetSources()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "SmartCast",
                  "mappings": [
                    {
                      "actionId": "SkillQ",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "castAbility",
                      "argsTemplate": { "i0": 0 },
                      "requireTarget": false,
                      "targetType": "Position",
                      "isSkillMapping": true,
                      "autoTargetPolicy": "NearestEnemyInRange",
                      "autoTargetRangeCm": 600,
                      "cursorTargetPolicy": "NearestInRange",
                      "cursorTargetRangeCm": 320
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("configured target source must be explicit"));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsCursorTargetPolicyWithoutCursorWorldPoint()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "SmartCast",
                  "mappings": [
                    {
                      "actionId": "SkillQ",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "castAbility",
                      "argsTemplate": { "i0": 0 },
                      "requireTarget": true,
                      "targetType": "Entity",
                      "isSkillMapping": true,
                      "cursorTargetPolicy": "NearestInRange",
                      "cursorTargetRangeCm": 320
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("cursorTargetPolicy requires targetType Position or Direction"));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsAutoTargetPolicyForDirectionTarget()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "SmartCast",
                  "mappings": [
                    {
                      "actionId": "SkillR",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "castAbility",
                      "argsTemplate": { "i0": 3 },
                      "requireTarget": false,
                      "targetType": "Direction",
                      "isSkillMapping": true,
                      "autoTargetPolicy": "NearestEnemyInRange",
                      "autoTargetRangeCm": 760
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("autoTargetPolicy requires targetType Entity or Position"));
        }

        [Test]
        public void InputOrderMapping_DefaultsDoNotInjectCommandSourceCollection()
        {
            var mapping = new InputOrderMapping();

            Assert.Multiple(() =>
            {
                Assert.That(mapping.ActorCollectionKey, Is.EqualTo(string.Empty));
                Assert.That(mapping.TargetCollectionKey, Is.EqualTo(string.Empty));
            });
        }

        [Test]
        public void InputOrderMappingLoader_ActorRoutingRequiresExplicitActorCollection()
        {
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        ArgsTemplate = new OrderArgsTemplate(),
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                        ActorOrderRouting = new ActorOrderRoutingSettings
                        {
                            Candidates = new List<ActorOrderRoutingCandidate>
                            {
                                new()
                                {
                                    OrderTypeKey = "moveTo",
                                    Match = new ActorOrderRoutingMatch(),
                                },
                            },
                        },
                    },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.Validate(config, "test.json"));

            Assert.That(ex!.Message, Does.Contain("actorCollectionKey"));
            Assert.That(ex.Message, Does.Contain("explicitly"));
        }

        [Test]
        public void DoubleTapTrigger_SubmitsOnlyOnSecondPressWithinWindow()
        {
            var (backend, handler) = BuildHandler();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Attack",
                        Trigger = InputTriggerType.DoubleTap,
                        DoubleTapWindowSeconds = 0.25f,
                        OrderTypeKey = "dash",
                        TargetType = OrderTargetType.None,
                        RequireTarget = false,
                        IsSkillMapping = false
                    }
                }
            };

            var orders = new List<Ludots.Core.Gameplay.GAS.Orders.Order>();
            var system = new InputOrderMappingSystem(handler, config);
            system.SetOrderTypeKeyResolver(key => key == "dash" ? 77 : 0);
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => orders.Add(order));

            using var world = World.Create();
            system.SetLocalPlayer(world.Create(), 1);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            system.Update(0.10f);

            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update();
            system.Update(0.05f);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            system.Update(0.10f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(77));
        }

        [Test]
        public void DirectionTargeting_UsesConfiguredCursorTarget_NotImplicitHover()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("SkillR", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.SmartCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillR",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 3 },
                        TargetType = OrderTargetType.Direction,
                        RequireTarget = false,
                        IsSkillMapping = true,
                        CursorTargetPolicy = AutoTargetPolicy.NearestEnemyInRange,
                        CursorTargetRangeCm = 320
                    }
                }
            };

            using var world = World.Create();
            Entity actor = world.Create();
            Entity enemy = world.Create();
            Entity hovered = world.Create();
            var system = new InputOrderMappingSystem(input, config);
            var orders = new List<Ludots.Core.Gameplay.GAS.Orders.Order>();

            system.SetLocalPlayer(actor, 1);
            system.SetOrderTypeKeyResolver(key => key == "castAbility" ? 203 : 0);
            system.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = new Vector3(1960f, 0f, 413f);
                return true;
            });
            system.SetHoveredEntityProvider((out Entity entity) =>
            {
                entity = hovered;
                return true;
            });
            system.SetCursorTargetProvider((Entity resolvedActor, AutoTargetPolicy policy, int rangeCm, Vector3 cursorWorldCm, out Entity target) =>
            {
                target = enemy;
                return resolvedActor == actor &&
                       policy == AutoTargetPolicy.NearestEnemyInRange &&
                       rangeCm == 320 &&
                       cursorWorldCm == new Vector3(1960f, 0f, 413f);
            });
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => orders.Add(order));

            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(203));
            Assert.That(orders[0].Actor, Is.EqualTo(actor));
            Assert.That(orders[0].Target, Is.EqualTo(enemy));
            Assert.That(orders[0].Target, Is.Not.EqualTo(hovered));
            Assert.That(orders[0].Args.Spatial.Mode, Is.EqualTo(Ludots.Core.Gameplay.GAS.Orders.OrderCollectionMode.Single));
            Assert.That(orders[0].Args.Spatial.WorldCm, Is.EqualTo(new Vector3(1960f, 0f, 413f)));
        }

        [Test]
        public void SmartCastEntity_WithAutoTargetPolicy_FailsClosedWhenAutoTargetMisses()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("SkillQ", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.SmartCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        TargetType = OrderTargetType.Entity,
                        RequireTarget = true,
                        IsSkillMapping = true,
                        AutoTargetPolicy = AutoTargetPolicy.NearestEnemyInRange,
                        AutoTargetRangeCm = 500
                    }
                }
            };

            using var world = World.Create();
            Entity actor = world.Create();
            Entity hovered = world.Create();
            var orders = new List<Ludots.Core.Gameplay.GAS.Orders.Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(actor, 1);
            system.SetOrderTypeKeyResolver(key => key == "castAbility" ? 101 : 0);
            system.SetHoveredEntityProvider((out Entity entity) =>
            {
                entity = hovered;
                return true;
            });
            system.SetAutoTargetProvider((Entity resolvedActor, AutoTargetPolicy policy, int rangeCm, out Entity target) =>
            {
                target = default;
                return false;
            });
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => orders.Add(order));

            system.Update(0f);

            Assert.That(orders, Is.Empty,
                "A declared auto-target source must fail closed when it misses; SmartCast must not switch to hover.");
        }

        [Test]
        public void InputOrderMappingSystem_RejectsEntityCursorTargetPolicyInsteadOfIgnoringIt()
        {
            var input = new FrozenInputActionReader();

            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.SmartCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        TargetType = OrderTargetType.Entity,
                        RequireTarget = true,
                        IsSkillMapping = true,
                        CursorTargetPolicy = AutoTargetPolicy.NearestEnemyInRange,
                        CursorTargetRangeCm = 320
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() => new InputOrderMappingSystem(input, config));

            Assert.That(ex!.Message, Does.Contain("cursorTargetPolicy requires targetType Position or Direction"));
        }

        [Test]
        public void ActorOrderRouting_HoveredEntityOrPosition_PrefersHoveredEntity()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        ActorCollectionKey = "collection.test.actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                        ActorOrderRouting = new ActorOrderRoutingSettings
                        {
                            Candidates = new List<ActorOrderRoutingCandidate>
                            {
                                new()
                                {
                                    OrderTypeKey = "setSpawnTarget",
                                    Priority = 10,
                                    TargetType = OrderTargetType.HoveredEntityOrPosition,
                                    Match = new ActorOrderRoutingMatch(),
                                },
                            },
                        },
                    },
                },
            };

            using var world = World.Create();
            Entity producer = world.Create();
            Entity hovered = world.Create();
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.ConfirmActionId = "Confirm";
            system.CancelActionId = "Cancel";
            system.CommandActionId = "PointerCommand";
            system.SetLocalPlayer(producer, 1);
            system.SetOrderTypeKeyResolver(key => key == "setSpawnTarget" ? 106 : 0);
            system.SetActorOrderRoutingResolver((Entity actor, ActorOrderRoutingSettings routing, out ActorOrderRoutingCandidate matchedCandidate) =>
                ActorOrderRoutingMatcher.TryResolveCandidate(world, new TagOps(), actor, routing.Candidates, out matchedCandidate));
            system.SetCollectionEntityListProvider((collectionKey, list) =>
            {
                Assert.That(collectionKey, Is.EqualTo("collection.test.actors"));
                list.Add(producer);
                return true;
            });
            system.SetHoveredEntityProvider((out Entity entity) =>
            {
                entity = hovered;
                return true;
            });
            bool groundCalled = false;
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundCalled = true;
                groundPos = new Vector3(1f, 0f, 2f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(106));
            Assert.That(orders[0].Target, Is.EqualTo(hovered));
            Assert.That(groundCalled, Is.False);
        }

        [Test]
        public void ActorOrderRouting_MixedCollection_ProducerGetsSpawnTargetAndUnitsGetMoveTo()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            int trainAbilityId = AbilityIdRegistry.Register("Ability.Rts.Strategy.War3.TrainFootman");
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        ActorCollectionKey = "collection.test.actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                        ActorOrderRouting = new ActorOrderRoutingSettings
                        {
                            Candidates = new List<ActorOrderRoutingCandidate>
                            {
                                new()
                                {
                                    OrderTypeKey = "setSpawnTarget",
                                    Priority = 10,
                                    TargetType = OrderTargetType.HoveredEntityOrPosition,
                                    Match = new ActorOrderRoutingMatch
                                    {
                                        AbilitySlotIndex = 2,
                                        AbilityIdKeySuffix = ".Train",
                                    },
                                },
                                new()
                                {
                                    OrderTypeKey = "moveTo",
                                    Priority = 0,
                                    Match = new ActorOrderRoutingMatch(),
                                },
                            },
                        },
                    },
                },
            };

            using var world = World.Create();
            Entity producer = world.Create(new AbilityStateBuffer());
            ref AbilityStateBuffer producerAbilities = ref world.Get<AbilityStateBuffer>(producer);
            producerAbilities.AddAbility(AbilityIdRegistry.Register("Ability.Test.Slot0"));
            producerAbilities.AddAbility(AbilityIdRegistry.Register("Ability.Test.Slot1"));
            producerAbilities.AddAbility(trainAbilityId);

            Entity unitA = world.Create();
            Entity unitB = world.Create();
            var orders = new List<Order>();
            var tagOps = new TagOps();
            var system = new InputOrderMappingSystem(input, config);
            system.ConfirmActionId = "Confirm";
            system.CancelActionId = "Cancel";
            system.CommandActionId = "PointerCommand";
            system.SetLocalPlayer(producer, 1);
            system.SetOrderTypeKeyResolver(key =>
                key switch
                {
                    "setSpawnTarget" => 106,
                    "moveTo" => 101,
                    _ => 0,
                });
            system.SetActorOrderRoutingResolver((Entity actor, ActorOrderRoutingSettings routing, out ActorOrderRoutingCandidate matchedCandidate) =>
                ActorOrderRoutingMatcher.TryResolveCandidate(world, tagOps, actor, routing.Candidates, out matchedCandidate));
            system.SetCollectionEntityListProvider((collectionKey, list) =>
            {
                Assert.That(collectionKey, Is.EqualTo("collection.test.actors"));
                list.Add(producer);
                list.Add(unitA);
                list.Add(unitB);
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(500f, 0f, 600f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(3));
            Assert.That(orders.Count(o => o.OrderTypeId == 106), Is.EqualTo(1));
            Assert.That(orders.Count(o => o.OrderTypeId == 101), Is.EqualTo(2));
            Assert.That(orders.Single(o => o.OrderTypeId == 106).Actor, Is.EqualTo(producer));
        }

        [Test]
        public void ActorOrderRouting_RoutedMoveTo_AppliesGroupFormationToMoveSubsetOnly()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            var config = new InputOrderMappingConfig
            {
                GroupMoveFormation = new GroupMoveFormationSettings
                {
                    Mode = GroupMoveFormationMode.Grid,
                    SpacingCm = 120,
                    OrderTypeKeys = new List<string> { "moveTo" },
                },
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        ActorCollectionKey = "collection.test.actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                        ActorOrderRouting = new ActorOrderRoutingSettings
                        {
                            Candidates = new List<ActorOrderRoutingCandidate>
                            {
                                new()
                                {
                                    OrderTypeKey = "moveTo",
                                    Priority = 0,
                                    Match = new ActorOrderRoutingMatch(),
                                },
                            },
                        },
                    },
                },
            };

            using var world = World.Create();
            Entity unitA = world.Create();
            Entity unitB = world.Create();
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.ConfirmActionId = "Confirm";
            system.CancelActionId = "Cancel";
            system.CommandActionId = "PointerCommand";
            system.SetLocalPlayer(unitA, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 101 : 0);
            system.SetActorOrderRoutingResolver((Entity actor, ActorOrderRoutingSettings routing, out ActorOrderRoutingCandidate matchedCandidate) =>
                ActorOrderRoutingMatcher.TryResolveCandidate(world, new TagOps(), actor, routing.Candidates, out matchedCandidate));
            system.SetCollectionEntityListProvider((collectionKey, list) =>
            {
                Assert.That(collectionKey, Is.EqualTo("collection.test.actors"));
                list.Add(unitA);
                list.Add(unitB);
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(1000f, 0f, 1000f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(2));
            Assert.That(orders[0].Args.Spatial.WorldCm, Is.Not.EqualTo(orders[1].Args.Spatial.WorldCm));
            Assert.That(
                Vector3.Distance(orders[0].Args.Spatial.WorldCm, orders[1].Args.Spatial.WorldCm),
                Is.GreaterThan(50f));
        }

        [Test]
        public void ActorOrderRouting_SkillMapping_IsRejectedByLoader()
        {
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = true,
                        ActorOrderRouting = new ActorOrderRoutingSettings
                        {
                            Candidates = new List<ActorOrderRoutingCandidate>
                            {
                                new()
                                {
                                    OrderTypeKey = "castAbility",
                                    Match = new ActorOrderRoutingMatch(),
                                },
                            },
                        },
                    },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.Validate(config, "test.json"));
            Assert.That(ex!.Message, Does.Contain("actorOrderRouting"));
            Assert.That(ex.Message, Does.Contain("isSkillMapping"));
        }

        [Test]
        public void GroupMoveFormation_OrderTypeKeyMatching_IsCaseSensitive()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            var config = new InputOrderMappingConfig
            {
                GroupMoveFormation = new GroupMoveFormationSettings
                {
                    Mode = GroupMoveFormationMode.Grid,
                    SpacingCm = 120,
                    OrderTypeKeys = new List<string> { "MoveTo" },
                },
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        ActorCollectionKey = "collection.test.actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        ArgsTemplate = new OrderArgsTemplate(),
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                    },
                },
            };

            using var world = World.Create();
            Entity unitA = world.Create();
            Entity unitB = world.Create();
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.ConfirmActionId = "Confirm";
            system.CancelActionId = "Cancel";
            system.CommandActionId = "PointerCommand";
            system.SetLocalPlayer(unitA, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 101 : 0);
            system.SetCollectionEntityListProvider((collectionKey, list) =>
            {
                Assert.That(collectionKey, Is.EqualTo("collection.test.actors"));
                list.Add(unitA);
                list.Add(unitB);
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(1000f, 0f, 1000f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(2));
            Assert.That(orders[0].Args.Spatial.WorldCm, Is.EqualTo(orders[1].Args.Spatial.WorldCm));
        }

        [Test]
        public void GroupMoveFormation_GridMode_RequiresOrderTypeKeys()
        {
            var config = new InputOrderMappingConfig
            {
                GroupMoveFormation = new GroupMoveFormationSettings
                {
                    Mode = GroupMoveFormationMode.Grid,
                    SpacingCm = 120,
                },
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        ArgsTemplate = new OrderArgsTemplate(),
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                    },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.Validate(config, "test.json"));
            Assert.That(ex!.Message, Does.Contain("groupMoveFormation.orderTypeKeys"));
        }

        [Test]
        public void Remap_PreservesRfc0065MappingSemanticsExceptOrderAndArgs()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.DoubleTap,
                        DoubleTapWindowSeconds = 0.17f,
                        OrderTypeKey = "moveTo",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 3, F1 = 2.5f },
                        RequireTarget = true,
                        ActorCollectionKey = "collection.test.actors",
                        TargetCollectionKey = "collection.test.targets",
                        TargetType = OrderTargetType.Position,
                        ModifierBehavior = ModifierSubmitBehavior.AlwaysQueued,
                        IsSkillMapping = false,
                        HeldPolicy = HeldPolicy.EveryFrame,
                        CastModeOverride = InteractionModeType.AimCast,
                        AutoTargetPolicy = AutoTargetPolicy.NearestEnemyInRange,
                        AutoTargetRangeCm = 640,
                        ActorOrderRouting = new ActorOrderRoutingSettings
                        {
                            Candidates = new List<ActorOrderRoutingCandidate>
                            {
                                new()
                                {
                                    OrderTypeKey = "setSpawnTarget",
                                    Priority = 20,
                                    TargetType = OrderTargetType.HoveredEntityOrPosition,
                                    Match = new ActorOrderRoutingMatch
                                    {
                                        RequiredAllTags = new List<string> { "producer" },
                                        BlockedAnyTags = new List<string> { "stunned" },
                                        AbilitySlotIndex = 2,
                                        AbilityIdKey = "ability.train",
                                        AbilityIdKeySuffix = ".Train"
                                    }
                                }
                            }
                        }
                    }
                }
            };
            var system = new InputOrderMappingSystem(input, config);
            system.SetOrderTypeKeyResolver(key => key switch
            {
                "moveTo" => 9,
                "attackMove" => 10,
                "setSpawnTarget" => 11,
                _ => 0,
            });
            var overrideArgs = new OrderArgsTemplate { I1 = 9, F2 = 4.5f };

            system.Remap("Command", "attackMove", overrideArgs);

            InputOrderMapping remapped = system.GetMapping("Command")
                ?? throw new InvalidOperationException("Missing remapped Command action.");
            Assert.Multiple(() =>
            {
                Assert.That(remapped.ActionId, Is.EqualTo("Command"));
                Assert.That(remapped.Trigger, Is.EqualTo(InputTriggerType.DoubleTap));
                Assert.That(remapped.DoubleTapWindowSeconds, Is.EqualTo(0.17f));
                Assert.That(remapped.OrderTypeKey, Is.EqualTo("attackMove"));
                Assert.That(remapped.ArgsTemplate.I1, Is.EqualTo(9));
                Assert.That(remapped.ArgsTemplate.F2, Is.EqualTo(4.5f));
                Assert.That(remapped.ArgsTemplate.I0, Is.Null);
                Assert.That(remapped.RequireTarget, Is.True);
                Assert.That(remapped.ActorCollectionKey, Is.EqualTo("collection.test.actors"));
                Assert.That(remapped.TargetCollectionKey, Is.EqualTo("collection.test.targets"));
                Assert.That(remapped.TargetType, Is.EqualTo(OrderTargetType.Position));
                Assert.That(remapped.ModifierBehavior, Is.EqualTo(ModifierSubmitBehavior.AlwaysQueued));
                Assert.That(remapped.HeldPolicy, Is.EqualTo(HeldPolicy.EveryFrame));
                Assert.That(remapped.CastModeOverride, Is.EqualTo(InteractionModeType.AimCast));
                Assert.That(remapped.AutoTargetPolicy, Is.EqualTo(AutoTargetPolicy.NearestEnemyInRange));
                Assert.That(remapped.AutoTargetRangeCm, Is.EqualTo(640));
                Assert.That(remapped.ActorOrderRouting, Is.Not.Null);
                Assert.That(remapped.ActorOrderRouting!.Candidates.Count, Is.EqualTo(1));
                Assert.That(remapped.ActorOrderRouting.Candidates[0].OrderTypeKey, Is.EqualTo("setSpawnTarget"));
                Assert.That(remapped.ActorOrderRouting.Candidates[0].TargetType, Is.EqualTo(OrderTargetType.HoveredEntityOrPosition));
                Assert.That(remapped.ActorOrderRouting.Candidates[0].Match.RequiredAllTags, Is.EquivalentTo(new[] { "producer" }));
                Assert.That(remapped.ActorOrderRouting.Candidates[0].Match.BlockedAnyTags, Is.EquivalentTo(new[] { "stunned" }));
                Assert.That(remapped.ActorOrderRouting.Candidates[0].Match.AbilitySlotIndex, Is.EqualTo(2));
                Assert.That(remapped.ActorOrderRouting.Candidates[0].Match.AbilityIdKey, Is.EqualTo("ability.train"));
                Assert.That(remapped.ActorOrderRouting.Candidates[0].Match.AbilityIdKeySuffix, Is.EqualTo(".Train"));
            });

            remapped.ActorOrderRouting.Candidates[0].Match.RequiredAllTags.Add("mutated");
            InputOrderMapping original = config.Mappings[0];
            Assert.That(original.ActorOrderRouting!.Candidates[0].Match.RequiredAllTags, Is.EquivalentTo(new[] { "producer" }),
                "Remap must deep-copy nested routing data instead of aliasing the source mapping.");
        }

        [Test]
        public void CommandIntentRouting_UnconfiguredCommandActionFailsBeforeCollectionProvider()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                    }
                }
            };

            using var world = World.Create();
            Entity localPlayer = world.Create();
            Entity collectionActor = world.Create();
            var orders = new List<Order>();
            bool collectionProviderCalled = false;
            var system = new InputOrderMappingSystem(input, config);
            system.CommandActionId = "Command";
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 101 : 0);
            system.SetCollectionEntityListProvider((_, list) =>
            {
                collectionProviderCalled = true;
                list.Add(collectionActor);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;

            Assert.That(ex.Message, Does.Contain("Command intent routing"));
            Assert.That(collectionProviderCalled, Is.False,
                "Command actions must fail fast before consulting collection-provider fallback paths.");
            Assert.That(orders, Is.Empty);
        }

        [Test]
        public void CommandIntentRouting_ActiveIntentZeroConsumesCommandWithoutLegacyFallback()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                    }
                }
            };

            using var world = World.Create();
            Entity localPlayer = world.Create();
            Entity actor = world.Create();
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.CommandActionId = "Command";
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 101 : 0);
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(100f, 0f, 200f);
                return true;
            });
            system.SetCollectionEntityListProvider((_, list) =>
            {
                list.Add(actor);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));
            SetGroundCommandTargetFactsProvider(system);

            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var stack = new InteractionContextStack(collectionKeys);
            stack.Push(InteractionContextFrameDescriptor.Create(
                "interaction.context.ability.test",
                EntityCollectionKeys.CommandSource,
                "view.test.command"));
            var commandIntents = CommandIntentProfileTests.Harness.Create(world).Intents;
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 101 });
            var dispatch = new CastDispatchProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            dispatch.Install(CastDispatchProfileTests.Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.all_together",
                Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
            }));
            var schemes = new ControlSchemeRuntime(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                stack,
                commandIntents,
                dispatch,
                orderTypes);
            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 8);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource);
            collections.Replace(localPlayer, in descriptor, new[] { actor }, localPlayer);
            system.SetCommandIntentRouting(
                world,
                stack,
                schemes,
                commandIntents,
                dispatch,
                collections,
                (out Entity owner) =>
                {
                    owner = localPlayer;
                    return true;
                });

            system.Update(0f);

            Assert.That(orders, Is.Empty,
                "A Command action with command intent routing installed and no active command intent must be consumed, not rerouted through legacy moveTo mapping.");
        }

        [Test]
        public void CommandIntentRouting_ProgrammaticCommandActivationUsesCommandSource()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                    }
                }
            };

            using var world = World.Create();
            Entity localPlayer = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity commandActor = world.Create();
            var orders = new List<Order>();
            bool collectionProviderCalled = false;
            var system = new InputOrderMappingSystem(input, config);
            system.CommandActionId = "Command";
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 2 : 0);
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(100f, 0f, 200f);
                return true;
            });
            system.SetCollectionPrimaryEntityProvider((string _, out Entity primary) =>
            {
                collectionProviderCalled = true;
                primary = commandActor;
                return true;
            });
            system.SetCollectionEntityListProvider((_, list) =>
            {
                collectionProviderCalled = true;
                list.Add(commandActor);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 42);
            SetGroundCommandTargetFactsProvider(system);

            var commandHarness = CommandIntentProfileTests.Harness.Create(world);
            commandHarness.Ownership.EnsureOwnership(localPlayer, commandActor);
            commandHarness.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.command.programmatic",
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                Rules = new List<CommandIntentRuleDefinition>
                {
                    CommandIntentProfileTests.Harness.GroundRule(priority: 10, orderTypeKey: "moveTo"),
                },
            }));
            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var stack = new InteractionContextStack(collectionKeys);
            stack.Push(InteractionContextFrameDescriptor.Create(
                InteractionContextIds.Default,
                EntityCollectionKeys.CommandSource,
                "view.test.command"));
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });
            var dispatch = new CastDispatchProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            dispatch.Install(CastDispatchProfileTests.Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.all_together",
                Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
            }));
            var schemes = new ControlSchemeRuntime(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                stack,
                commandHarness.Intents,
                dispatch,
                orderTypes);
            schemes.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition>
                {
                    new()
                    {
                        Id = "scheme.test",
                        InputContexts = new List<string>(),
                        Defaults = new ControlSchemeDefaults
                        {
                            CommandIntentId = "intent.command.programmatic",
                            CastDispatchProfileId = "dispatch.all_together",
                        },
                    }
                },
            });

            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 4);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource);
            collections.Replace(localPlayer, in descriptor, new[] { commandActor }, localPlayer);
            system.SetCommandIntentRouting(
                world,
                stack,
                schemes,
                commandHarness.Intents,
                dispatch,
                collections,
                (out Entity owner) =>
                {
                    owner = localPlayer;
                    return true;
                });

            bool activated = system.TryActivateMappedAction("Command");

            Assert.That(activated, Is.True);
            Assert.That(collectionProviderCalled, Is.False,
                "Programmatic Command activation must use the command-source collection, not collection-provider fallback.");
            Assert.That(orders, Has.Count.EqualTo(1));
            Assert.That(orders[0].Actor, Is.EqualTo(commandActor));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(2));
        }

        [Test]
        public void CommandIntentRouting_DispatchSeesOnlyRoutedActors()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                    }
                }
            };

            using var world = World.Create();
            Entity localPlayer = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity unroutedNearActor = world.Create(WorldPositionCm.FromCm(10, 0));
            Entity routedFarActor = world.Create(new AbilityStateBuffer(), WorldPositionCm.FromCm(5000, 0));
            ref AbilityStateBuffer routedAbilities = ref world.Get<AbilityStateBuffer>(routedFarActor);
            routedAbilities.AddAbility(2);

            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.CommandActionId = "Command";
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 2 : 0);
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(0f, 0f, 0f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 9001);
            SetGroundCommandTargetFactsProvider(system);

            var commandHarness = CommandIntentProfileTests.Harness.Create(world);
            commandHarness.Ownership.EnsureOwnership(localPlayer, unroutedNearActor);
            commandHarness.Ownership.EnsureOwnership(localPlayer, routedFarActor);
            commandHarness.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.command.routed_only",
                GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                Rules = new List<CommandIntentRuleDefinition>
                {
                    new()
                    {
                        Priority = 10,
                        Actor = new CommandIntentActorPredicateDefinition { HasAbilityWithTag = "ability.catalog.weapon" },
                        Target = new CommandIntentTargetPredicateDefinition { HasEntity = false },
                        Route = new CommandIntentRouteDefinition { OrderTypeKey = "moveTo" },
                    },
                },
            }));

            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var stack = new InteractionContextStack(collectionKeys);
            stack.Push(InteractionContextFrameDescriptor.Create(
                InteractionContextIds.Default,
                EntityCollectionKeys.CommandSource,
                "view.test.command"));
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });
            var dispatch = new CastDispatchProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            dispatch.Install(CastDispatchProfileTests.Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.nearest_one",
                Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 1 },
                Scorer = new CastDispatchScorerDefinition
                {
                    Kind = "utility",
                    Considerations = new List<string> { "distanceToTarget:invert" },
                },
                Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
            }));
            var schemes = new ControlSchemeRuntime(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                stack,
                commandHarness.Intents,
                dispatch,
                orderTypes);
            schemes.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition>
                {
                    new()
                    {
                        Id = "scheme.test",
                        InputContexts = new List<string>(),
                        Defaults = new ControlSchemeDefaults
                        {
                            CommandIntentId = "intent.command.routed_only",
                            CastDispatchProfileId = "dispatch.nearest_one",
                        },
                    }
                },
            });

            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 8);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource);
            collections.Replace(localPlayer, in descriptor, new[] { unroutedNearActor, routedFarActor }, localPlayer);
            system.SetCommandIntentRouting(
                world,
                stack,
                schemes,
                commandHarness.Intents,
                dispatch,
                collections,
                (out Entity owner) =>
                {
                    owner = localPlayer;
                    return true;
                });

            system.Update(0f);

            Assert.That(orders, Has.Count.EqualTo(1),
                "Dispatch must rank only actors that kept a CommandIntent route; an unrouted nearer actor must not consume topN capacity.");
            Assert.That(orders[0].Actor, Is.EqualTo(routedFarActor));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(2));
        }

        [Test]
        public void CommandIntentRouting_EntityTargetFactsDriveEntityRouteBeforeGroundRule()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Command", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = false,
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = false,
                    }
                }
            };

            using var world = World.Create();
            Entity localPlayer = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity targetOwner = world.Create(new PlayerIdentity { PlayerId = 2 });
            var commandHarness = CommandIntentProfileTests.Harness.Create(world);
            Entity commandActor = commandHarness.CreateActor(localPlayer, 1);
            Entity clickedTarget = commandHarness.CreateTaggedEntity(targetOwner, "structure.garrisonable");
            commandHarness.InstallStandardProfile();

            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.CommandActionId = "Command";
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key switch
            {
                "castAbility" => 1,
                "moveTo" => 2,
                _ => 0
            });
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(250f, 0f, 400f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 777);
            system.SetCommandIntentTargetFactsProvider((InputOrderMapping _, out CommandIntentTargetFacts facts) =>
            {
                facts = new CommandIntentTargetFacts(clickedTarget, HasEntity: true);
                return true;
            });

            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var stack = new InteractionContextStack(collectionKeys);
            stack.Push(InteractionContextFrameDescriptor.Create(
                InteractionContextIds.Default,
                EntityCollectionKeys.CommandSource,
                "view.test.command"));
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig { Key = "castAbility", OrderTypeId = 1 });
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });
            var dispatch = new CastDispatchProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            dispatch.Install(CastDispatchProfileTests.Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.all_together",
                Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
            }));
            var schemes = new ControlSchemeRuntime(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                stack,
                commandHarness.Intents,
                dispatch,
                orderTypes);
            schemes.Install(new ControlSchemesConfig
            {
                Schemes = new List<ControlSchemeDefinition>
                {
                    new()
                    {
                        Id = "scheme.test",
                        InputContexts = new List<string>(),
                        Defaults = new ControlSchemeDefaults
                        {
                            CommandIntentId = "intent.command.test",
                            CastDispatchProfileId = "dispatch.all_together",
                        },
                    }
                },
            });

            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 4);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource);
            collections.Replace(localPlayer, in descriptor, new[] { commandActor }, localPlayer);
            system.SetCommandIntentRouting(
                world,
                stack,
                schemes,
                commandHarness.Intents,
                dispatch,
                collections,
                (out Entity owner) =>
                {
                    owner = localPlayer;
                    return true;
                });

            system.Update(0f);

            Assert.That(orders, Has.Count.EqualTo(1));
            Assert.That(orders[0].Actor, Is.EqualTo(commandActor));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(1),
                "An entity target fact must hit the entity rule before the ground move rule.");
        }

        private static void SetGroundCommandTargetFactsProvider(InputOrderMappingSystem system)
        {
            system.SetCommandIntentTargetFactsProvider((InputOrderMapping _, out CommandIntentTargetFacts facts) =>
            {
                facts = new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
                return false;
            });
        }

        private static (TestInputBackend backend, PlayerInputHandler handler) BuildHandler()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Attack", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Attack", Path = "<Keyboard>/a", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return (backend, handler);
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new Dictionary<string, bool>();

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private static Func<InputOrderMapping, bool> ReferencesOrderTypeKey(string orderTypeKey)
        {
            return mapping =>
            {
                if (string.Equals(mapping.OrderTypeKey, orderTypeKey, StringComparison.Ordinal))
                {
                    return true;
                }

                if (mapping.ActorOrderRouting?.Candidates == null)
                {
                    return false;
                }

                for (int i = 0; i < mapping.ActorOrderRouting.Candidates.Count; i++)
                {
                    if (string.Equals(mapping.ActorOrderRouting.Candidates[i].OrderTypeKey, orderTypeKey, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            };
        }

        [Test]
        public void ActivateMappedAction_RejectsMissingExplicitActor()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Skill1",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true,
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 }
                    }
                }
            };
            var system = new InputOrderMappingSystem(input, config);

            InputOrderActivationResult result = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(Entity.Null, 1));

            Assert.That(result.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(result.Rejection, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
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
