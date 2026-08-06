using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using MobaDemoMod.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.InputRouting
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
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });

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
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order _) => OrderSubmitResult.Queued);

            var ex = Assert.Throws<InvalidOperationException>(() => system.SetOrderTypeKeyResolver(_ => 0));

            Assert.That(ex!.Message, Does.Contain("typoOrder"));
            Assert.That(ex.Message, Does.Contain("orderTypeKey"));
        }

        [Test]
        public void OrderPayloadKindMismatch_ThrowsDuringRegistryInstall()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "StopButNamedCast",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        TargetType = OrderTargetType.None,
                        RequireTarget = false,
                        OrderPayload = InputOrderPayloadTemplate.Stop()
                    }
                }
            };

            var system = new InputOrderMappingSystem(input, config);
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: 4));
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "castAbility",
                OrderTypeId = 100,
            }.UseCastAbilityPayload());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                system.SetOrderTypeRegistry(orderTypes, "test:assets/Input/input_order_mappings.json"));

            Assert.That(ex!.Message, Does.Contain("StopButNamedCast"));
            Assert.That(ex.Message, Does.Contain("requires orderPayload.kind CastAbility"));
            Assert.That(ex.Message, Does.Contain("declares Stop"));
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
        public void AbilityQualifiedSkillMapping_SameActionKeepsActorSpecificHeldStartEnd()
        {
            AbilityIdRegistry.Clear();
            int defaultAbilityId = AbilityIdRegistry.Register("Ability.Test.DefaultR");
            int guidedAbilityId = AbilityIdRegistry.Register("Ability.Test.GuidedLaser");

            var input = new FrozenInputActionReader();
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
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(3),
                        TargetType = OrderTargetType.Direction,
                        RequireTarget = false,
                    },
                    new()
                    {
                        ActionId = "SkillR",
                        AbilityIdKey = "Ability.Test.GuidedLaser",
                        Trigger = InputTriggerType.Held,
                        HeldPolicy = HeldPolicy.StartEnd,
                        CastModeOverride = InteractionModeType.SmartCast,
                        OrderTypeKey = "castAbility",
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(3),
                        TargetType = OrderTargetType.Direction,
                        RequireTarget = false,
                    },
                },
            };
            InputOrderMappingLoader.ResolveAbilityIdKeys(config, AbilityIdRegistry.GetId, "test.json");

            using var world = World.Create();
            Entity defaultActor = world.Create();
            Entity guidedActor = world.Create();
            Entity activeActor = guidedActor;
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(guidedActor, 1);
            system.SetActorProvider((out Entity actor) => { actor = activeActor; return true; });
            system.SetSkillMappingAbilityProvider((Entity actor, int slotIndex, out int abilityId) =>
            {
                Assert.That(slotIndex, Is.EqualTo(3));
                abilityId = actor == guidedActor ? guidedAbilityId : defaultAbilityId;
                return true;
            });
            system.SetOrderTypeKeyResolver(key => key switch
            {
                "castAbility" => 10,
                "castAbility.Start" => 11,
                "castAbility.End" => 12,
                _ => 0,
            });
            system.SetOrderSubmitHandler((in Order order) =>
            {
                orders.Add(order);
                return OrderSubmitResult.Queued;
            });

            input.SetActionState("SkillR", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            system.Update(0f);
            input.SetActionState("SkillR", Vector3.Zero, isDown: false, pressedThisFrame: false, releasedThisFrame: true);
            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(2));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(11));
            Assert.That(orders[1].OrderTypeId, Is.EqualTo(12));
            Assert.That(orders[0].Args.I0, Is.EqualTo(3));
            Assert.That(orders[1].Args.I0, Is.EqualTo(3));

            orders.Clear();
            activeActor = defaultActor;
            input.SetActionState("SkillR", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(10));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsAbilityQualifiedSameActionWithDifferentSlots()
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 2 },
                      "requireTarget": false,
                      "targetType": "Direction"
                    },
                    {
                      "actionId": "SkillR",
                      "abilityIdKey": "Ability.Test.GuidedLaser",
                      "trigger": "Held",
                      "heldPolicy": "StartEnd",
                      "orderTypeKey": "castAbility",
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 3 },
                      "requireTarget": false,
                      "targetType": "Direction"
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("SkillR"));
            Assert.That(ex.Message, Does.Contain("same orderPayload.abilitySlot"));
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
            string inputPath = Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "assets", "Input", "default_input.json");
            string mappingPath = Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "assets", "Input", "input_order_mappings.json");
            string gamePath = Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "assets", "game.json");

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
        public void DefaultCommandIntentProfile_RoutesGroundAndInspectableEntityHitsToMove()
        {
            string repoRoot = FindRepoRoot();
            string profilePath = Path.Combine(repoRoot, "assets", "Configs", "Input", "command_intent_profiles.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            CommandIntentProfilesConfig config = JsonSerializer.Deserialize<CommandIntentProfilesConfig>(
                    File.ReadAllText(profilePath),
                    options)
                ?? throw new InvalidOperationException("Default command intent profile config failed to parse.");

            CommandIntentProfileDefinition profile = config.Profiles.Single(p =>
                string.Equals(p.Id, "intent.command.default", StringComparison.Ordinal));
            Assert.That(
                profile.Rules.Any(rule =>
                    rule.Target?.HasEntity == false &&
                    string.Equals(rule.Route?.OrderTypeKey, "moveTo", StringComparison.Ordinal)),
                Is.True,
                "Default command must keep explicit ground movement.");
            Assert.That(
                profile.Rules.Any(rule =>
                    rule.Target?.HasEntity == true &&
                    string.Equals(rule.Route?.OrderTypeKey, "moveTo", StringComparison.Ordinal)),
                Is.True,
                "Default command must explicitly treat an inspectable entity under the pointer as a valid move destination, so right-clicking neutral showcase props is not silently dropped.");
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 0 },
                      "requireTarget": true,
                      "targetCollectionKey": "collection.test.explicit.targets",
                      "targetType": "Entity"
                    }
                  ]
                }
                """);
            File.WriteAllText(
                Path.Combine(inputDir, "target_layout_profiles.json"),
                """
                {
                  "targetLayoutProfiles": []
                }
                """);

            try
            {
                using var world = World.Create();
                Entity localPlayer = world.Create(new PlayerIdentity { PlayerId = 1 });
                Entity commandSourceTarget = world.Create(new PlayerOwner { PlayerId = 1 });
                Entity explicitTarget = world.Create(new PlayerOwner { PlayerId = 1 });
                var controlHarness = CreateControlDomain(world);
                controlHarness.Ownership.EnsureOwnership(localPlayer, commandSourceTarget);
                var players = new PlayerEntityLookup();
                players.Register(1, localPlayer);
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
                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: 8));
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "castAbility",
                    OrderTypeId = 101,
                }.UseCastAbilityPayload());
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "moveTo",
                    OrderTypeId = 102,
                    PayloadKind = OrderPayloadKind.MoveToWorldCm
                });
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "stop",
                    OrderTypeId = 103,
                    PayloadKind = OrderPayloadKind.Stop
                });

                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.AuthoritativeInput.Name] = input,
                    [CoreServiceKeys.LocalPlayerEntity.Name] = localPlayer,
                    [CoreServiceKeys.LocalPlayerId.Name] = 1,
                    [CoreServiceKeys.EntityCollectionStore.Name] = collections,
                    [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionKeys,
                    [CoreServiceKeys.PlayerEntityLookup.Name] = players,
                    [CoreServiceKeys.ControlDomainQuery.Name] = controlHarness.Domains,
                    [CoreServiceKeys.OrderTypeRegistry.Name] = orderTypes,
                    [CoreServiceKeys.GameConfig.Name] = new GameConfig
                    {
                        GasRuntimeCapacity = new GasRuntimeCapacityConfig
                        {
                            CommandIntentScratchCapacity = 64,
                        },
                        Constants = new GameConstants
                        {
                            OrderTypeIds = new Dictionary<string, int>
                            {
                                ["castAbility"] = 101,
                                ["moveTo"] = 102,
                                ["stop"] = 103,
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
                var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
                var system = new MobaLocalOrderSourceSystem(world, globals, orders, ctx);

                system.Update(0f);

                Assert.That(orders.TryDequeue(out Order order), Is.True);
                Assert.That(order.Target, Is.EqualTo(explicitTarget),
                    "MobaLocalOrderSourceSystem must resolve the entity collection named by the mapping, not the active command-source collection.");
                Assert.That(order.Target, Is.Not.EqualTo(commandSourceTarget));

                var mapping = (InputOrderMappingSystem)globals[CoreServiceKeys.ActiveInputOrderMapping.Name];
                InputOrderActivationResult accepted = mapping.ActivateMappedAction(
                    "SkillQ",
                    new InputOrderActivationContext(commandSourceTarget, 1));
                Assert.That(accepted.State, Is.EqualTo(InputOrderActivationState.Submitted));
                Assert.That(accepted.OrderId, Is.GreaterThan(0));
                Assert.That(orders.TryDequeue(out Order programmaticOrder), Is.True);
                Assert.That(programmaticOrder.Actor, Is.EqualTo(commandSourceTarget));
                Assert.That(programmaticOrder.OrderId, Is.EqualTo(accepted.OrderId));

                Entity foreignActor = world.Create(new PlayerOwner { PlayerId = 2 });
                InputOrderActivationResult rejected = mapping.ActivateMappedAction(
                    "SkillQ",
                    new InputOrderActivationContext(foreignActor, 1));
                Assert.That(rejected.State, Is.EqualTo(InputOrderActivationState.Rejected));
                Assert.That(rejected.Rejection, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
                Assert.That(orders.Count, Is.Zero);
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 0 },
                      "RequireTarget": true,
                      "EntityCollectionKey": "collection.test.explicit.targets",
                      "TargetType": "Entity"
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<JsonException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("RequireTarget"));
        }

        [Test]
        public void TypedCastAbilityPayload_SubmitsConfiguredAbilitySlotWithoutAuthoredArgSlots()
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 2 },
                      "requireTarget": false,
                      "targetType": "None"
                    }
                  ]
                }
                """));

            InputOrderMappingConfig config = InputOrderMappingLoader.LoadFromStream(stream);
            var input = new FrozenInputActionReader();
            input.SetActionState("SkillQ", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            using var world = World.Create();
            Entity actor = world.Create();
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(actor, 1);
            system.SetActorProvider((out Entity resolvedActor) =>
            {
                resolvedActor = actor;
                return true;
            });
            system.SetOrderTypeKeyResolver(key => key == "castAbility" ? 101 : 0);
            system.SetOrderSubmitHandler((in Order order) =>
            {
                orders.Add(order);
                return OrderSubmitResult.Queued;
            });

            system.Update(0f);

            Assert.That(orders, Has.Count.EqualTo(1));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(101));
            Assert.That(orders[0].Args.I0, Is.EqualTo(2));
            var primaryActions = new string[4];
            Assert.That(system.CopyPrimarySkillActionIds(primaryActions), Is.EqualTo(1));
            Assert.That(primaryActions[2], Is.EqualTo("SkillQ"));
        }

        [Test]
        public void InputOrderMapping_DoesNotResolveAbilitySlotWithoutTypedPayload()
        {
            var mapping = new InputOrderMapping
            {
                OrderPayload = InputOrderPayloadTemplate.None()
            };

            Assert.That(mapping.TryResolveAbilitySlot(out _), Is.False);
        }

        [Test]
        public void InputOrderMappingLoader_RejectsAuthoredArgSlots()
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
                      "requireTarget": false,
                      "targetType": "None"
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("mappings[0].argsTemplate"));
            Assert.That(ex.Message, Does.Contain("orderPayload"));
        }

        [Test]
        public void InputOrderMappingLoader_RequiresAuthoredOrderPayload()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "Move",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "moveTo",
                      "requireTarget": true,
                      "targetType": "Position"
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("mappings[0].orderPayload"));
            Assert.That(ex.Message, Does.Contain("required"));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsDuplicateArgSlotAndTypedPayload()
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 1 },
                      "requireTarget": false,
                      "targetType": "None"
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("mappings[0]"));
            Assert.That(ex.Message, Does.Contain("argsTemplate"));
            Assert.That(ex.Message, Does.Contain("orderPayload"));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsAuthoredSkillMappingFlag()
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 0 },
                      "requireTarget": false,
                      "targetType": "None",
                      "isSkillMapping": true
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("mappings[0].isSkillMapping"));
            Assert.That(ex.Message, Does.Contain("derived"));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsInvalidTypedOrderPayloadFields()
        {
            AssertInvalidInputMappingJson(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "SkillQ",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "castAbility",
                      "orderPayload": { "kind": "CastAbility" },
                      "requireTarget": false,
                      "targetType": "None"
                    }
                  ]
                }
                """,
                "orderPayload.abilitySlot");

            AssertInvalidInputMappingJson(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "Move",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "moveTo",
                      "orderPayload": { "kind": "MoveToWorldCm", "abilitySlot": 0 },
                      "requireTarget": true,
                      "targetType": "Position"
                    }
                  ]
                }
                """,
                "abilitySlot is only valid");

            AssertInvalidInputMappingJson(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "Stop",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "stop",
                      "orderPayload": { "kind": "Stop" },
                      "requireTarget": true,
                      "targetType": "Position"
                    }
                  ]
                }
                """,
                "kind Stop must not declare a target requirement");
        }

        [Test]
        public void InputOrderMappingLoader_RejectsAuthoredGroupMoveTargetLayout()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "Move",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "moveTo",
                      "orderPayload": { "kind": "MoveToWorldCm" },
                      "requireTarget": true,
                      "targetType": "Position"
                    }
                  ],
                  "groupMoveTargetLayout": {
                    "mode": "Grid",
                    "spacingCm": 120,
                    "orderTypeKeys": [ "moveTo" ]
                  }
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("groupMoveTargetLayout"));
            Assert.That(ex.Message, Does.Contain("target_layout_profiles.json"));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsEmbeddedTargetLayoutProfiles()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "Move",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "moveTo",
                      "orderPayload": { "kind": "MoveToWorldCm" },
                      "requireTarget": true,
                      "targetType": "Position",
                      "targetLayoutProfileId": "layout.move.grid"
                    }
                  ],
                  "targetLayoutProfiles": [
                    {
                      "id": "layout.move.grid",
                      "mode": "Grid",
                      "spacingCm": 120,
                      "orderTypeKeys": [ "moveTo" ]
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("targetLayoutProfiles"));
            Assert.That(ex.Message, Does.Contain("target_layout_profiles.json"));
        }

        [Test]
        public void InputOrderMappingLoader_LoadsTargetLayoutProfilesFromSeparateConfig()
        {
            using var layoutStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "targetLayoutProfiles": [
                    {
                      "id": "layout.move.grid",
                      "mode": "Grid",
                      "spacingCm": 120,
                      "orderTypeKeys": [ "moveTo" ]
                    }
                  ]
                }
                """));
            TargetLayoutProfileConfig layouts = InputOrderMappingLoader.LoadTargetLayoutProfilesFromStream(
                layoutStream,
                "test:assets/Input/target_layout_profiles.json");

            using var mappingStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "Move",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "moveTo",
                      "orderPayload": { "kind": "MoveToWorldCm" },
                      "requireTarget": true,
                      "targetType": "Position",
                      "targetLayoutProfileId": "layout.move.grid"
                    }
                  ]
                }
                """));

            InputOrderMappingConfig config = InputOrderMappingLoader.LoadFromStream(
                mappingStream,
                layouts.TargetLayoutProfiles,
                "test:assets/Input/input_order_mappings.json");

            Assert.That(layouts.TargetLayoutProfiles.Count, Is.EqualTo(1));
            Assert.That(config.Mappings[0].TargetLayoutProfileIndex, Is.EqualTo(0));
        }

        [Test]
        public void InputOrderMappingLoader_RejectsAuthoredActorOrderRouting()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "interactionMode": "TargetFirst",
                  "mappings": [
                    {
                      "actionId": "Command",
                      "trigger": "PressedThisFrame",
                      "orderTypeKey": "moveTo",
                      "orderPayload": { "kind": "MoveToWorldCm" },
                      "requireSelection": true,
                      "targetType": "Position",
                      "actorOrderRouting": {
                        "candidates": [
                          {
                            "orderTypeKey": "moveTo",
                            "priority": 0,
                            "match": {}
                          }
                        ]
                      }
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));

            Assert.That(ex!.Message, Does.Contain("mappings[0].actorOrderRouting"));
            Assert.That(ex.Message, Does.Contain("CommandIntent"));
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 0 },
                      "requireTarget": false,
                      "targetType": "Position",
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 0 },
                      "requireTarget": true,
                      "targetType": "Entity",
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
                      "orderPayload": { "kind": "CastAbility", "abilitySlot": 3 },
                      "requireTarget": false,
                      "targetType": "Direction",
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
        public void InputOrderMapping_RuntimeContract_DoesNotExposeActorOrderRouting()
        {
            Assert.That(typeof(InputOrderMapping).GetProperty("ActorOrderRouting"), Is.Null);
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
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });

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
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(3),
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
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });

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
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
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
            system.SetOrderSubmitHandler((in Ludots.Core.Gameplay.GAS.Orders.Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });

            system.Update(0f);

            Assert.That(orders, Is.Empty,
                "A declared auto-target source must fail closed when it misses; SmartCast must not switch to hover.");
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(actor));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedValidation));
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
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
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
        public void TargetLayoutProfile_OrderTypeKeyMatching_IsCaseSensitive()
        {
            var targetLayoutProfiles = new List<TargetLayoutProfileDefinition>
            {
                new()
                {
                    Id = "layout.move.grid",
                    Mode = TargetLayoutMode.Grid,
                    SpacingCm = 120,
                    OrderTypeKeys = new List<string> { "MoveTo" },
                },
            };
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        ActorCollectionKey = "collection.test.actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        TargetLayoutProfileId = "layout.move.grid",
                        IsSkillMapping = false,
                    },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.Validate(config, targetLayoutProfiles, "test.json"));
            Assert.That(ex!.Message, Does.Contain("orderTypeKey 'moveTo'"));
            Assert.That(ex.Message, Does.Contain("orderTypeKeys"));
        }

        [Test]
        public void TargetLayoutProfile_GridMode_RequiresExplicitSpacingInSeparateConfig()
        {
            using var layoutStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "targetLayoutProfiles": [
                    {
                      "id": "layout.move.grid",
                      "mode": "Grid",
                      "orderTypeKeys": [ "moveTo" ]
                    }
                  ]
                }
                """));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.LoadTargetLayoutProfilesFromStream(
                    layoutStream,
                    "test:assets/Input/target_layout_profiles.json"));
            Assert.That(ex!.Message, Does.Contain("spacingCm"));
        }

        [Test]
        public void TargetLayoutProfile_SeparateConfig_RequiresRootArray()
        {
            using var layoutStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.LoadTargetLayoutProfilesFromStream(
                    layoutStream,
                    "test:assets/Input/target_layout_profiles.json"));
            Assert.That(ex!.Message, Does.Contain("targetLayoutProfiles"));
            Assert.That(ex.Message, Does.Contain("explicitly defined"));
        }

        [Test]
        public void TargetLayoutProfile_GridMode_RequiresOrderTypeKeys()
        {
            var targetLayoutProfiles = new List<TargetLayoutProfileDefinition>
            {
                new()
                {
                    Id = "layout.move.grid",
                    Mode = TargetLayoutMode.Grid,
                    SpacingCm = 120,
                },
            };
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
                        TargetLayoutProfileId = "layout.move.grid",
                    },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.Validate(config, targetLayoutProfiles, "test.json"));
            Assert.That(ex!.Message, Does.Contain("targetLayoutProfiles[0].orderTypeKeys"));
        }

        [Test]
        public void TargetLayoutProfile_GridMode_RejectsNonPositiveSpacing()
        {
            var targetLayoutProfiles = new List<TargetLayoutProfileDefinition>
            {
                new()
                {
                    Id = "layout.move.grid",
                    Mode = TargetLayoutMode.Grid,
                    SpacingCm = 0,
                    OrderTypeKeys = new List<string> { "moveTo" },
                },
            };
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
                        TargetLayoutProfileId = "layout.move.grid",
                    },
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                InputOrderMappingLoader.Validate(config, targetLayoutProfiles, "test.json"));
            Assert.That(ex!.Message, Does.Contain("targetLayoutProfiles[0].spacingCm"));
        }

        [Test]
        public void Remap_PreservesRfc0065MappingSemanticsExceptOrderAndTypedPayload()
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
                        OrderPayload = InputOrderPayloadTemplate.MoveToWorldCm(),
                        RequireTarget = true,
                        ActorCollectionKey = "collection.test.actors",
                        TargetCollectionKey = "collection.test.targets",
                        TargetType = OrderTargetType.Position,
                        ModifierBehavior = ModifierSubmitBehavior.AlwaysQueued,
                        IsSkillMapping = false,
                        HeldPolicy = HeldPolicy.EveryFrame,
                        CastModeOverride = InteractionModeType.AimCast,
                        AutoTargetPolicy = AutoTargetPolicy.NearestEnemyInRange,
                        AutoTargetRangeCm = 640
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
            var overridePayload = InputOrderPayloadTemplate.MoveToWorldCm();

            system.Remap("Command", "attackMove", overridePayload);

            InputOrderMapping remapped = system.GetMapping("Command")
                ?? throw new InvalidOperationException("Missing remapped Command action.");
            Assert.Multiple(() =>
            {
                Assert.That(remapped.ActionId, Is.EqualTo("Command"));
                Assert.That(remapped.Trigger, Is.EqualTo(InputTriggerType.DoubleTap));
                Assert.That(remapped.DoubleTapWindowSeconds, Is.EqualTo(0.17f));
                Assert.That(remapped.OrderTypeKey, Is.EqualTo("attackMove"));
                Assert.That(remapped.OrderPayload.Kind, Is.EqualTo(InputOrderPayloadKind.MoveToWorldCm));
                Assert.That(remapped.RequireTarget, Is.True);
                Assert.That(remapped.ActorCollectionKey, Is.EqualTo("collection.test.actors"));
                Assert.That(remapped.TargetCollectionKey, Is.EqualTo("collection.test.targets"));
                Assert.That(remapped.TargetType, Is.EqualTo(OrderTargetType.Position));
                Assert.That(remapped.ModifierBehavior, Is.EqualTo(ModifierSubmitBehavior.AlwaysQueued));
                Assert.That(remapped.HeldPolicy, Is.EqualTo(HeldPolicy.EveryFrame));
                Assert.That(remapped.CastModeOverride, Is.EqualTo(InteractionModeType.AimCast));
                Assert.That(remapped.AutoTargetPolicy, Is.EqualTo(AutoTargetPolicy.NearestEnemyInRange));
                Assert.That(remapped.AutoTargetRangeCm, Is.EqualTo(640));
            });
        }

        [Test]
        public void RemapAndPreferences_PreserveExternalTargetLayoutProfiles()
        {
            var input = new FrozenInputActionReader();
            var targetLayoutProfiles = new List<TargetLayoutProfileDefinition>
            {
                new()
                {
                    Id = "layout.move.grid",
                    Mode = TargetLayoutMode.Grid,
                    SpacingCm = 120,
                    OrderTypeKeys = new List<string> { "moveTo", "attackMove" },
                },
            };
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Command",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        OrderPayload = InputOrderPayloadTemplate.MoveToWorldCm(),
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        TargetLayoutProfileId = "layout.move.grid",
                    },
                },
            };
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"input-preferences-{Guid.NewGuid():N}.json");
            try
            {
                var system = new InputOrderMappingSystem(input, config, targetLayoutProfiles: targetLayoutProfiles);
                system.SetOrderTypeKeyResolver(key => key switch
                {
                    "moveTo" => 9,
                    "attackMove" => 10,
                    _ => 0,
                });
                system.Remap("Command", "attackMove", InputOrderPayloadTemplate.MoveToWorldCm());
                system.SaveUserPreferences(path);

                var reloaded = new InputOrderMappingSystem(input, config, targetLayoutProfiles: targetLayoutProfiles);
                reloaded.SetOrderTypeKeyResolver(key => key switch
                {
                    "moveTo" => 9,
                    "attackMove" => 10,
                    _ => 0,
                });
                reloaded.LoadUserPreferences(path);

                InputOrderMapping mapping = reloaded.GetMapping("Command")
                    ?? throw new InvalidOperationException("Missing reloaded Command mapping.");
                Assert.That(mapping.OrderTypeKey, Is.EqualTo("attackMove"));
                Assert.That(mapping.TargetLayoutProfileId, Is.EqualTo("layout.move.grid"));
                Assert.That(mapping.TargetLayoutProfileIndex, Is.EqualTo(0));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
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
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> list, int capacity, out OrderSubmitResult rejection) =>
            {
                collectionProviderCalled = true;
                list.Add(collectionActor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });

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
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> list, int capacity, out OrderSubmitResult rejection) =>
            {
                list.Add(actor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });
            SetGroundCommandTargetFactsProvider(system);

            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var stack = new InteractionContextStack(collectionKeys);
            stack.Push(InteractionContextFrameDescriptor.Create(
                "interaction.context.ability.test",
                EntityCollectionKeys.CommandSource,
                "view.test.command"));
            var commandIntents = CommandIntentProfileTests.Harness.Create(world).Intents;
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedByRule));
        }

        [TestCase(1, 16, true)]
        [TestCase(16, 16, true)]
        [TestCase(17, 16, false)]
        [TestCase(
            InputOrderMappingSystem.DefaultCommandIntentScratchCapacity,
            InputOrderMappingSystem.DefaultCommandIntentScratchCapacity,
            true)]
        [TestCase(
            InputOrderMappingSystem.DefaultCommandIntentScratchCapacity + 1,
            InputOrderMappingSystem.DefaultCommandIntentScratchCapacity,
            false)]
        public void CommandIntentRouting_SelectionCapacityBoundary_SubmitsOrRejectsWholeCommand(
            int selectionCount,
            int scratchCapacity,
            bool expectSubmitted)
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
            Entity[] actors = new Entity[selectionCount];
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i] = world.Create();
            }

            var submitted = new List<Order>(expectSubmitted ? selectionCount : 0);
            int nextOrderId = 100;
            var system = new InputOrderMappingSystem(input, config, commandIntentScratchCapacity: scratchCapacity);
            system.CommandActionId = "Command";
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 2 : 0);
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = nextOrderId++);
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(100f, 0f, 200f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) =>
            {
                submitted.Add(order);
                return OrderSubmitResult.Queued;
            });
            SetGroundCommandTargetFactsProvider(system);

            var commandHarness = CommandIntentProfileTests.Harness.Create(world);
            commandHarness.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.command.capacity",
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
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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
                            CommandIntentId = "intent.command.capacity",
                            CastDispatchProfileId = "dispatch.all_together",
                        },
                    }
                },
            });

            var collections = new EntityCollectionStore(
                collectionKeys,
                initialCollectionCapacity: 4,
                initialRowCapacity: Math.Max(4, selectionCount));
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource);
            collections.Replace(localPlayer, in descriptor, actors, localPlayer);
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
            system.SetOrderBatchSubmitHandler((Span<Order> batch) =>
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    batch[i].OrderId = nextOrderId++;
                    submitted.Add(batch[i]);
                }

                return OrderSubmitResult.Queued;
            });

            Assert.DoesNotThrow(() => system.Update(0f));

            if (expectSubmitted)
            {
                Assert.That(submitted.Count, Is.EqualTo(selectionCount));
                Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Submitted));
                Assert.That(system.LastActivationResult.OrderId, Is.EqualTo(submitted[0].OrderId));
                Assert.That(submitted.Select(static order => order.OrderId), Is.Unique);
            }
            else
            {
                Assert.That(submitted, Is.Empty);
                Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
                Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            }
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
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> list, int capacity, out OrderSubmitResult rejection) =>
            {
                collectionProviderCalled = true;
                list.Add(commandActor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });
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
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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

            system.SetActivationActorValidator((actor, _) => world.IsAlive(actor));
            InputOrderActivationResult activation = system.ActivateMappedAction(
                "Command",
                new InputOrderActivationContext(commandActor, playerId: 1));

            Assert.That(activation.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(activation.Actor, Is.EqualTo(commandActor));
            Assert.That(activation.OrderId, Is.EqualTo(42));
            Assert.That(collectionProviderCalled, Is.False,
                "Programmatic Command activation must use the command-source collection, not collection-provider fallback.");
            Assert.That(orders, Has.Count.EqualTo(1));
            Assert.That(orders[0].Actor, Is.EqualTo(commandActor));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(2));
        }

        [Test]
        public void CommandIntentRouting_ExpandsAfterCastDispatchAndRejectsClusteredBatchAtomically()
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
            Entity firstSource = world.Create();
            Entity secondSource = world.Create();
            Entity firstMember = world.Create();
            Entity secondMember = world.Create();
            var admissionResults = new OrderAdmissionResultBuffer(128, 128);
            var queue = new OrderQueue(capacity: 64, admissionResults);
            for (int i = 0; i < 63; i++)
            {
                var filler = new Order { OrderTypeId = 2 };
                Assert.That(queue.TryEnqueue(in filler), Is.True);
            }

            var system = new InputOrderMappingSystem(input, config);
            system.CommandActionId = "Command";
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 2 : 0);
            system.SetGroundPositionProvider((out Vector3 groundPos) =>
            {
                groundPos = new Vector3(100f, 0f, 200f);
                return true;
            });
            system.SetOrderSubmitHandler((in Order _) =>
            {
                Assert.Fail("Expanded command intent must use the clustered batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            system.SetOrderBatchSubmitHandler((Span<Order> _) =>
            {
                Assert.Fail("Expanded command intent must not use the shared batch handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            system.SetOrderClusterBatchSubmitHandler((Span<Order> orders) => queue.TryEnqueueClusteredBatch(orders));
            var expander = new TestCommandActorExpander(
                new Dictionary<Entity, Entity>
                {
                    [firstSource] = firstMember,
                    [secondSource] = secondMember,
                });
            system.SetCommandActorExpander(expander);
            SetGroundCommandTargetFactsProvider(system);

            var commandHarness = CommandIntentProfileTests.Harness.Create(world);
            commandHarness.Ownership.EnsureOwnership(localPlayer, firstSource);
            commandHarness.Ownership.EnsureOwnership(localPlayer, secondSource);
            commandHarness.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.command.atomic_batch",
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
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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
                            CommandIntentId = "intent.command.atomic_batch",
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
            collections.Replace(localPlayer, in descriptor, new[] { firstSource, secondSource }, localPlayer);
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

            Assert.DoesNotThrow(() => system.Update(0f));

            Assert.That(queue.Count, Is.EqualTo(63),
                "The expanded fan-out must be rejected as one batch when the OrderQueue has only one free slot.");
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.OrderId, Is.GreaterThan(0));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(
                admissionResults.TryGet(system.LastActivationResult.OrderId, OrderAdmissionStage.GlobalIntake, out var outcome),
                Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(expander.ExpandCallCount, Is.EqualTo(2),
                "CastDispatch must select the two sources before the command router expands either source into members.");
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
            system.SetOrderSubmitHandler((in Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });
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
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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
        public void CommandIntentRouting_UnauthorizedActorRejectsSharedIdDispatchAtomically()
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
                    },
                },
            };

            using var world = World.Create();
            Entity localPlayer = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity authorizedActor = world.Create();
            Entity foreignActor = world.Create();
            int identityAssignments = 0;
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config)
            {
                CommandActionId = "Command",
            };
            system.SetLocalPlayer(localPlayer, 1);
            system.SetOrderTypeKeyResolver(key => key == "moveTo" ? 2 : 0);
            system.SetGroundPositionProvider((out Vector3 position) =>
            {
                position = new Vector3(50f, 0f, 75f);
                return true;
            });
            system.SetActivationActorValidator((actor, playerId) =>
                playerId == 1 && actor == authorizedActor);
            system.SetOrderIdentityAssigner((ref Order order) =>
            {
                identityAssignments++;
                order.OrderId = 7000 + identityAssignments;
            });
            system.SetOrderSubmitHandler((in Order order) =>
            {
                orders.Add(order);
                return OrderSubmitResult.Queued;
            });
            SetGroundCommandTargetFactsProvider(system);

            var commandHarness = CommandIntentProfileTests.Harness.Create(world);
            commandHarness.Ownership.EnsureOwnership(localPlayer, authorizedActor);
            commandHarness.Ownership.EnsureOwnership(localPlayer, foreignActor);
            commandHarness.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
            {
                Id = "intent.command.atomic_authorization",
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
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });
            var dispatch = new CastDispatchProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            dispatch.Install(CastDispatchProfileTests.Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.atomic_authorization",
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
                        Id = "scheme.atomic_authorization",
                        InputContexts = new List<string>(),
                        Defaults = new ControlSchemeDefaults
                        {
                            CommandIntentId = "intent.command.atomic_authorization",
                            CastDispatchProfileId = "dispatch.atomic_authorization",
                        },
                    },
                },
            });

            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 8);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource);
            collections.Replace(localPlayer, in descriptor, new[] { authorizedActor, foreignActor }, localPlayer);
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

            Assert.That(orders, Is.Empty);
            Assert.That(identityAssignments, Is.Zero);
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(foreignActor));
            Assert.That(system.LastActivationResult.OrderId, Is.Zero);
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
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
            system.SetOrderSubmitHandler((in Order order) => { orders.Add(order); return OrderSubmitResult.Queued; });
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
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
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
            Assert.That(orders[0].Args.I0, Is.EqualTo(0),
                "CommandIntent byAbilityTag routes must resolve to the concrete ability slot before order submission.");
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

        private sealed class TestCommandActorExpander : ICommandActorExpander
        {
            private readonly IReadOnlyDictionary<Entity, Entity> _membersBySource;

            public TestCommandActorExpander(IReadOnlyDictionary<Entity, Entity> membersBySource)
            {
                _membersBySource = membersBySource;
            }

            public int MaxExpandedActorsPerSource => 1;
            public int MaxExpandedActorCount => _membersBySource.Count;
            public int ExpandCallCount { get; private set; }

            public int Expand(Entity source, Span<Entity> destination)
            {
                ExpandCallCount++;
                destination[0] = _membersBySource[source];
                return 1;
            }
        }

        private static Func<InputOrderMapping, bool> ReferencesOrderTypeKey(string orderTypeKey)
        {
            return mapping => string.Equals(mapping.OrderTypeKey, orderTypeKey, StringComparison.Ordinal);
        }

        [Test]
        public void ActivateMappedAction_PropagatesTypedSubmitRejectionWithActorAndOrderId()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Skill1",
                        OrderTypeKey = "castAbility",
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true,
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
                    },
                },
            };
            using var world = World.Create();
            Entity actor = world.Create();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(actor, 1);
            system.SetOrderTypeKeyResolver(_ => 7);
            system.SetActivationActorValidator((candidate, playerId) =>
                candidate == actor && playerId == 1 && world.IsAlive(candidate));
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 77);
            system.SetOrderSubmitHandler((in Order _) => OrderSubmitResult.RejectedQueueFull);

            InputOrderActivationResult result = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(actor, 1));

            Assert.That(result.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(result.Actor, Is.EqualTo(actor));
            Assert.That(result.OrderId, Is.EqualTo(77));
            Assert.That(result.Rejection, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        }

        [Test]
        public void Update_CollectionFanOutSuccess_ReturnsAssignedBatchOrderId()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Move", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Move",
                        ActorCollectionKey = "actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                    },
                },
            };
            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(firstActor, 1);
            system.SetActorProvider((out Entity actor) => { actor = firstActor; return true; });
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> list, int capacity, out OrderSubmitResult rejection) =>
            {
                list.Add(firstActor);
                list.Add(secondActor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 position) =>
            {
                position = new Vector3(10f, 0f, 20f);
                return true;
            });
            system.SetOrderTypeKeyResolver(_ => 8);
            system.SetActivationActorValidator((actor, playerId) =>
                playerId == 1 && (actor == firstActor || actor == secondActor));
            system.SetOrderSubmitHandler((in Order _) =>
            {
                Assert.Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            system.SetOrderBatchSubmitHandler((Span<Order> batch) =>
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    batch[i].OrderId = 700;
                }

                return OrderSubmitResult.Queued;
            });

            system.Update(0f);

            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(firstActor));
            Assert.That(system.LastActivationResult.OrderId, Is.EqualTo(700));
        }

        [Test]
        public void Update_CollectionFanOutQueueFull_ReturnsTypedRejectionInsteadOfThrowing()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Move", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Move",
                        ActorCollectionKey = "actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                    },
                },
            };
            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            var queue = new OrderQueue(capacity: 1, admissionResults);
            var seed = new Order { OrderTypeId = 8 };
            Assert.That(queue.TryEnqueue(in seed), Is.True);
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(firstActor, 1);
            system.SetActorProvider((out Entity actor) => { actor = firstActor; return true; });
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> list, int capacity, out OrderSubmitResult rejection) =>
            {
                list.Add(firstActor);
                list.Add(secondActor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 position) =>
            {
                position = new Vector3(10f, 0f, 20f);
                return true;
            });
            system.SetOrderTypeKeyResolver(_ => 8);
            system.SetActivationActorValidator((actor, playerId) =>
                playerId == 1 && (actor == firstActor || actor == secondActor));
            system.SetOrderSubmitHandler((in Order _) =>
            {
                Assert.Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            system.SetOrderBatchSubmitHandler((Span<Order> batch) => queue.TryEnqueueSharedBatch(batch));

            Assert.DoesNotThrow(() => system.Update(0f));

            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(firstActor));
            Assert.That(system.LastActivationResult.OrderId, Is.GreaterThan(0));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            Assert.That(
                admissionResults.TryGet(system.LastActivationResult.OrderId, OrderAdmissionStage.GlobalIntake, out var outcome),
                Is.True);
            Assert.That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        }

        [Test]
        public void Update_CollectionFanOutRuleRejected_PreservesBatchHandlerReason()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Move", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Move",
                        ActorCollectionKey = "actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                    },
                },
            };
            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(firstActor, 1);
            system.SetActorProvider((out Entity actor) => { actor = firstActor; return true; });
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> list, int capacity, out OrderSubmitResult rejection) =>
            {
                list.Add(firstActor);
                list.Add(secondActor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 position) =>
            {
                position = new Vector3(10f, 0f, 20f);
                return true;
            });
            system.SetOrderTypeKeyResolver(_ => 8);
            system.SetActivationActorValidator((actor, playerId) =>
                playerId == 1 && (actor == firstActor || actor == secondActor));
            system.SetOrderSubmitHandler((in Order _) =>
            {
                Assert.Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            system.SetOrderBatchSubmitHandler((Span<Order> batch) =>
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    batch[i].OrderId = 900 + i;
                }

                return OrderSubmitResult.RejectedByRule;
            });

            system.Update(0f);

            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(firstActor));
            Assert.That(system.LastActivationResult.OrderId, Is.EqualTo(900));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedByRule));
        }

        [Test]
        public void ActivateMappedAction_AimingPinsActorAndRejectsWhenItDiesBeforeConfirm()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.AimCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Skill1",
                        OrderTypeKey = "castAbility",
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true,
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
                    },
                },
            };
            using var world = World.Create();
            Entity actorA = world.Create();
            Entity actorB = world.Create();
            Entity providerActor = actorA;
            int submitted = 0;
            var system = new InputOrderMappingSystem(input, config)
            {
                ConfirmActionId = "Confirm",
                CancelActionId = "Cancel",
                CommandActionId = "Command",
            };
            system.SetLocalPlayer(actorA, 1);
            system.SetActorProvider((out Entity actor) => { actor = providerActor; return true; });
            system.SetActivationActorValidator((candidate, _) => world.IsAlive(candidate));
            system.SetOrderTypeKeyResolver(_ => 7);
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 88);
            system.SetOrderSubmitHandler((in Order _) => { submitted++; return OrderSubmitResult.Queued; });

            InputOrderActivationResult entered = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(actorA, 1));
            Assert.That(entered.State, Is.EqualTo(InputOrderActivationState.EnteredAiming));
            Assert.That(entered.Actor, Is.EqualTo(actorA));

            providerActor = actorB;
            world.Destroy(actorA);
            input.SetActionState("Confirm", Vector3.Zero, true, true, false);
            system.Update(0f);

            Assert.That(submitted, Is.Zero);
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(actorA));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
        }

        [Test]
        public void ActivateMappedAction_SameActionDifferentActor_DoesNotOverwriteExistingAimingSession()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.AimCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Skill1",
                        OrderTypeKey = "castAbility",
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true,
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
                    },
                },
            };
            using var world = World.Create();
            Entity actorA = world.Create();
            Entity actorB = world.Create();
            var submitted = new List<Order>();
            var system = new InputOrderMappingSystem(input, config)
            {
                ConfirmActionId = "Confirm",
                CancelActionId = "Cancel",
                CommandActionId = "Command",
            };
            system.SetLocalPlayer(actorA, 1);
            system.SetActorProvider((out Entity actor) => { actor = actorA; return true; });
            system.SetActivationActorValidator((candidate, _) => world.IsAlive(candidate));
            system.SetOrderTypeKeyResolver(_ => 7);
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 88 + submitted.Count);
            system.SetOrderSubmitHandler((in Order order) =>
            {
                submitted.Add(order);
                return OrderSubmitResult.Queued;
            });

            InputOrderActivationResult entered = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(actorA, 1));
            Assert.That(entered.State, Is.EqualTo(InputOrderActivationState.EnteredAiming));
            Assert.That(entered.Actor, Is.EqualTo(actorA));

            InputOrderActivationResult rejected = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(actorB, 1));
            Assert.That(rejected.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(rejected.Actor, Is.EqualTo(actorB));
            Assert.That(system.IsAiming, Is.True);

            input.SetActionState("Confirm", Vector3.Zero, true, true, false);
            system.Update(0f);

            Assert.That(submitted.Count, Is.EqualTo(1));
            Assert.That(submitted[0].Actor, Is.EqualTo(actorA));
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
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0)
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

        [Test]
        public void ActivateMappedAction_RejectsActorOutsidePlayerControlDomain()
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
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0)
                    }
                }
            };
            using var world = World.Create();
            Entity controlledActor = world.Create();
            Entity foreignActor = world.Create();
            int submitted = 0;
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(controlledActor, 1);
            system.SetOrderTypeKeyResolver(_ => 7);
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 91);
            system.SetOrderSubmitHandler((in Order _) => { submitted++; return OrderSubmitResult.Queued; });
            system.SetActivationActorValidator((actor, playerId) =>
                playerId == 1 && actor == controlledActor && world.IsAlive(actor));

            InputOrderActivationResult accepted = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(controlledActor, 1));
            Assert.That(accepted.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(accepted.OrderId, Is.EqualTo(91));

            InputOrderActivationResult result = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(foreignActor, 1));

            Assert.That(result.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(result.Actor, Is.EqualTo(foreignActor));
            Assert.That(result.OrderId, Is.Zero);
            Assert.That(result.Rejection, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
            Assert.That(system.LastActivationResult, Is.EqualTo(result));
            Assert.That(submitted, Is.EqualTo(1));

            InputOrderActivationResult missing = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(Entity.Null, 1));
            Assert.That(missing.OrderId, Is.Zero);
            Assert.That(system.LastActivationResult, Is.EqualTo(missing));
        }

        [Test]
        public void Update_CollectionFanOutRejectsEntireDispatchBeforeOrderIdAssignment()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Move", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Move",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        ActorCollectionKey = "actors",
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                    }
                }
            };
            using var world = World.Create();
            Entity controlledActor = world.Create();
            Entity foreignActor = world.Create();
            var submitted = new List<Order>();
            int identityAssignments = 0;
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(controlledActor, 1);
            system.SetActorProvider((out Entity actor) => { actor = controlledActor; return true; });
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> actors, int capacity, out OrderSubmitResult rejection) =>
            {
                actors.Add(controlledActor);
                actors.Add(foreignActor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 position) =>
            {
                position = new Vector3(10f, 0f, 20f);
                return true;
            });
            system.SetOrderTypeKeyResolver(_ => 8);
            system.SetActivationActorValidator((actor, playerId) =>
                actor == controlledActor && playerId == 1 && world.IsAlive(actor));
            system.SetOrderIdentityAssigner((ref Order order) =>
            {
                identityAssignments++;
                order.OrderId = 93 + identityAssignments;
            });
            system.SetOrderSubmitHandler((in Order _) =>
            {
                Assert.Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            system.SetOrderBatchSubmitHandler((Span<Order> _) =>
            {
                Assert.Fail("Unauthorized collection fan-out must reject before atomic batch submission.");
                return OrderSubmitResult.RejectedValidation;
            });

            system.Update(0f);

            Assert.That(submitted, Is.Empty);
            Assert.That(identityAssignments, Is.Zero);
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(foreignActor));
            Assert.That(system.LastActivationResult.OrderId, Is.Zero);
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
        }

        [Test]
        public void Update_CollectionFanOutUsesOneAuthorizationSnapshotBeforeSubmission()
        {
            var input = new FrozenInputActionReader();
            input.SetActionState("Move", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Move",
                        ActorCollectionKey = "actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                    },
                },
            };

            using var world = World.Create();
            Entity firstActor = world.Create();
            Entity secondActor = world.Create();
            var submitted = new List<Order>();
            int identityAssignments = 0;
            bool authorizationOpen = true;
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(firstActor, 1);
            system.SetActorProvider((out Entity actor) => { actor = firstActor; return true; });
            system.SetCollectionEntityListProvider((string collectionKey, List<Entity> list, int capacity, out OrderSubmitResult rejection) =>
            {
                list.Add(firstActor);
                list.Add(secondActor);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            system.SetGroundPositionProvider((out Vector3 position) =>
            {
                position = new Vector3(10f, 0f, 20f);
                return true;
            });
            system.SetOrderTypeKeyResolver(_ => 8);
            system.SetActivationActorValidator((actor, playerId) =>
                authorizationOpen && playerId == 1 && (actor == firstActor || actor == secondActor));
            system.SetOrderSubmitHandler((in Order _) =>
            {
                Assert.Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            system.SetOrderBatchSubmitHandler((Span<Order> batch) =>
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    var submittedOrder = batch[i];
                    identityAssignments++;
                    submittedOrder.OrderId = identityAssignments;
                    submitted.Add(submittedOrder);
                }

                authorizationOpen = false;
                return OrderSubmitResult.Queued;
            });

            system.Update(0f);

            Assert.That(submitted, Has.Count.EqualTo(2));
            Assert.That(identityAssignments, Is.EqualTo(2));
            Assert.That(submitted[0].Actor, Is.EqualTo(firstActor));
            Assert.That(submitted[1].Actor, Is.EqualTo(secondActor));
        }

        [Test]
        public void InputOrderActorAuthorization_UsesPlayerRepresentativeOwnershipAndControlGrants()
        {
            using var world = World.Create();
            Entity playerOne = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity playerTwo = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity ownedActor = world.Create();
            Entity grantedActor = world.Create();
            Entity foreignActor = world.Create();
            var harness = CreateControlDomain(world);
            harness.Ownership.EnsureOwnership(playerOne, ownedActor);
            harness.Ownership.EnsureOwnership(playerTwo, foreignActor);
            harness.Relationships.EnsureLink(playerOne, grantedActor, harness.ControlsTypeId);
            var players = new PlayerEntityLookup();
            players.Register(1, playerOne);
            players.Register(2, playerTwo);

            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, playerOne, 1), Is.True);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, ownedActor, 1), Is.True);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, grantedActor, 1), Is.True);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, foreignActor, 1), Is.False);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, ownedActor, 2), Is.False);
        }

        [Test]
        public void ActivateMappedAction_AimingRejectsWhenPinnedActorLosesPlayerAuthorization()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.AimCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Skill1",
                        OrderTypeKey = "castAbility",
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true,
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
                    },
                },
            };
            using var world = World.Create();
            Entity actor = world.Create();
            bool authorized = true;
            int submitted = 0;
            var system = new InputOrderMappingSystem(input, config)
            {
                ConfirmActionId = "Confirm",
                CancelActionId = "Cancel",
                CommandActionId = "Command",
            };
            system.SetOrderTypeKeyResolver(_ => 7);
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 92);
            system.SetOrderSubmitHandler((in Order _) => { submitted++; return OrderSubmitResult.Queued; });
            system.SetActivationActorValidator((candidate, playerId) =>
                authorized && candidate == actor && playerId == 1 && world.IsAlive(candidate));

            InputOrderActivationResult entered = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(actor, 1));
            Assert.That(entered.State, Is.EqualTo(InputOrderActivationState.EnteredAiming));

            authorized = false;
            input.SetActionState("Confirm", Vector3.Zero, true, true, false);
            system.Update(0f);

            Assert.That(submitted, Is.Zero);
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(actor));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedInvalidActor));
        }

        [Test]
        public void ActivateMappedAction_ExplicitContext_DoesNotRequireSetLocalPlayer()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Skill1",
                        OrderTypeKey = "castAbility",
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = true,
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
                    },
                },
            };
            using var world = World.Create();
            Entity actor = world.Create();
            var submitted = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            // Intentionally no SetLocalPlayer: explicit context alone must authorize the activation.
            system.SetOrderTypeKeyResolver(_ => 7);
            system.SetActivationActorValidator((candidate, playerId) =>
                candidate == actor && playerId == 1 && world.IsAlive(candidate));
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 42);
            system.SetOrderSubmitHandler((in Order order) =>
            {
                submitted.Add(order);
                return OrderSubmitResult.Queued;
            });

            InputOrderActivationResult result = system.ActivateMappedAction(
                "Skill1",
                new InputOrderActivationContext(actor, 1));

            Assert.That(result.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(result.Actor, Is.EqualTo(actor));
            Assert.That(result.OrderId, Is.EqualTo(42));
            Assert.That(submitted.Count, Is.EqualTo(1));
            Assert.That(submitted[0].Actor, Is.EqualTo(actor));
            Assert.That(submitted[0].PlayerId, Is.EqualTo(1));
        }

        [Test]
        public void SmartCast_MutableActorProvider_UsesSameActorForTargetAndOrder()
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
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
                        TargetType = OrderTargetType.Entity,
                        RequireTarget = true,
                        IsSkillMapping = true,
                        AutoTargetPolicy = AutoTargetPolicy.NearestEnemyInRange,
                        AutoTargetRangeCm = 500
                    }
                }
            };
            using var world = World.Create();
            Entity actorA = world.Create();
            Entity actorB = world.Create();
            Entity enemy = world.Create();
            int resolveCount = 0;
            Entity autoTargetActor = default;
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(actorA, 1);
            system.SetActorProvider((out Entity actor) =>
            {
                // First resolve should pin the activation; a second resolve must not be used.
                actor = resolveCount++ == 0 ? actorA : actorB;
                return true;
            });
            system.SetOrderTypeKeyResolver(key => key == "castAbility" ? 101 : 0);
            system.SetAutoTargetProvider((Entity resolvedActor, AutoTargetPolicy policy, int rangeCm, out Entity target) =>
            {
                autoTargetActor = resolvedActor;
                target = enemy;
                return true;
            });
            system.SetOrderSubmitHandler((in Order order) =>
            {
                orders.Add(order);
                return OrderSubmitResult.Queued;
            });

            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(autoTargetActor, Is.EqualTo(actorA),
                "Auto-target resolution must use the same pinned actor as the submitted order.");
            Assert.That(orders[0].Actor, Is.EqualTo(actorA));
            Assert.That(orders[0].Target, Is.EqualTo(enemy));
            Assert.That(resolveCount, Is.EqualTo(1),
                "One SmartCast activation must resolve the primary actor once.");
        }

        [Test]
        public void Update_SmartCastBuildFailure_OverwritesStaleSubmittedActivationResult()
        {
            var input = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.SmartCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillSelf",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(0),
                        TargetType = OrderTargetType.None,
                        RequireTarget = false,
                        IsSkillMapping = true
                    },
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        OrderPayload = InputOrderPayloadTemplate.CastAbility(1),
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
            var orders = new List<Order>();
            var system = new InputOrderMappingSystem(input, config);
            system.SetLocalPlayer(actor, 1);
            system.SetOrderTypeKeyResolver(_ => 101);
            system.SetOrderIdentityAssigner((ref Order order) => order.OrderId = 55);
            system.SetAutoTargetProvider((Entity _, AutoTargetPolicy __, int ___, out Entity target) =>
            {
                target = default;
                return false;
            });
            system.SetOrderSubmitHandler((in Order order) =>
            {
                orders.Add(order);
                return OrderSubmitResult.Queued;
            });

            input.SetActionState("SkillSelf", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            system.Update(0f);
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Submitted));
            Assert.That(system.LastActivationResult.OrderId, Is.EqualTo(55));

            input.SetActionState("SkillSelf", Vector3.Zero, isDown: false, pressedThisFrame: false, releasedThisFrame: false);
            input.SetActionState("SkillQ", Vector3.Zero, isDown: true, pressedThisFrame: true, releasedThisFrame: false);
            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(1), "Failed SmartCast must not submit a second order.");
            Assert.That(system.LastActivationResult.State, Is.EqualTo(InputOrderActivationState.Rejected));
            Assert.That(system.LastActivationResult.Actor, Is.EqualTo(actor));
            Assert.That(system.LastActivationResult.Rejection, Is.EqualTo(OrderSubmitResult.RejectedValidation));
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
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

        private static void AssertInvalidInputMappingJson(string json, string expectedMessage)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var ex = Assert.Throws<InvalidOperationException>(() => InputOrderMappingLoader.LoadFromStream(stream));
            Assert.That(ex!.Message, Does.Contain(expectedMessage));
        }

        private static ControlDomainHarness CreateControlDomain(World world)
        {
            var types = new RelationshipTypeRegistry();
            var relationships = new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
            int ownsTypeId = types.Register("Owns");
            int controlsTypeId = types.Register("Controls");
            var ownership = new OwnershipResolver(relationships, ownsTypeId);
            return new ControlDomainHarness(
                relationships,
                ownership,
                new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId),
                controlsTypeId);
        }

        private readonly record struct ControlDomainHarness(
            RelationshipRuntime Relationships,
            OwnershipResolver Ownership,
            ControlDomainQuery Domains,
            int ControlsTypeId);
    }
}
