using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Association;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class InteractionSelectionConvergenceTests
    {
        [Test]
        public void GasInputResponseSystem_UsesSharedInteractionBindings()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var ambientTarget = world.Create();
            var requestTarget = world.Create();
            var local = world.Create();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.AbilityInputRequestQueue.Name] = new InputRequestQueue(),
                [CoreServiceKeys.InputResponseBuffer.Name] = new InputResponseBuffer(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };
            CreateCommandSourceRuntime(world, globals);
            SeedCommandSource(world, globals, local, ambientTarget);

            var system = new GasInputResponseSystem(world, globals);
            var requests = (InputRequestQueue)globals[CoreServiceKeys.AbilityInputRequestQueue.Name];
            var responses = (InputResponseBuffer)globals[CoreServiceKeys.InputResponseBuffer.Name];
            requests.TryEnqueue(new InputRequest { RequestId = 9, RequestTagId = 501, Target = requestTarget });

            SetConfirmSnapshot(globals, new Vector2(0f, 0f), pressedThisFrame: true, isDown: true);
            input.Update();
            system.Update(0f);

            That(responses.TryConsume(9, out var response), Is.True);
            That(response.Target, Is.EqualTo(requestTarget));
            That(response.Target, Is.Not.EqualTo(ambientTarget));
            That(response.ResponseTagId, Is.EqualTo(501));
        }

        [Test]
        public void AbilityExecSystem_TargetCollectionGate_ConsumesInputResponseTargetContext()
        {
            using var world = World.Create();
            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new AbilityStateBuffer());
            var enemy = world.Create();
            var targetContext = world.Create();

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(9001);

            var order = new Order
            {
                OrderId = 7,
                Actor = actor,
                OrderTypeId = 100,
                Args = new OrderArgs { I0 = 0 }
            };
            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbI = ref world.Get<BlackboardIntBuffer>(actor);
            bbI.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var defs = new AbilityDefinitionRegistry();
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.TargetCollectionGate, tick: 0, tagId: 77);
            spec.SetItem(1, ExecItemKind.EventGate, tick: 1, tagId: 999);
            var def = new AbilityDefinition { ExecSpec = spec };
            defs.Register(9001, in def);

            var inputRequests = new InputRequestQueue();
            var inputResponses = new InputResponseBuffer();
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                inputRequests,
                inputResponses,
                new EffectRequestQueue(),
                snapshotCapacity: 16,
                defs,
                castAbilityOrderTypeId: 100,
                orderTypeRegistry: new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity)),
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.True);
            That(inputRequests.Count, Is.EqualTo(1));
            ref var waitingExec = ref world.Get<AbilityExecInstance>(actor);
            That(waitingExec.State, Is.EqualTo(AbilityExecRunState.GateWaiting));
            That(waitingExec.WaitRequestId, Is.EqualTo(7));

            var response = new InputResponse
            {
                RequestId = 7,
                ResponseTagId = 77,
                Target = enemy,
                TargetContext = targetContext,
            };
            That(inputResponses.TryAdd(response), Is.True);

            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref var exec = ref world.Get<AbilityExecInstance>(actor);
            That(exec.State, Is.EqualTo(AbilityExecRunState.Running));
            That(exec.Target, Is.EqualTo(enemy));
            That(exec.TargetContext, Is.EqualTo(targetContext));
        }

        [Test]
        public void AbilityExecSystem_TargetCollectionGate_WhenInputQueueFull_FailsOrderAndDoesNotWait()
        {
            using var world = World.Create();
            const int castOrderTypeId = 100;
            const int abilityId = 9001;

            var actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new AbilityStateBuffer());

            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            abilities.AddAbility(abilityId);

            var order = new Order
            {
                OrderId = 17,
                Actor = actor,
                OrderTypeId = castOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var blackboard = ref world.Get<BlackboardIntBuffer>(actor);
            blackboard.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.TargetCollectionGate, tick: 0, tagId: 77);

            var definitions = new AbilityDefinitionRegistry();
            var definition = new AbilityDefinition { ExecSpec = spec };
            definitions.Register(abilityId, in definition);

            var inputRequests = new InputRequestQueue(capacity: 16);
            for (int i = 0; i < inputRequests.Capacity; i++)
            {
                var request = new InputRequest { RequestId = 1000 + i, RequestTagId = 77 };
                That(inputRequests.TryEnqueue(in request), Is.True);
            }

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castOrderTypeId,
                Label = "Cast",
                Priority = 100,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
            });
            var presentationEvents = new GasPresentationEventBuffer(capacity: 8);

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                inputRequests,
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                snapshotCapacity: 16,
                definitions,
                castAbilityOrderTypeId: castOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            That(inputRequests.Count, Is.EqualTo(inputRequests.Capacity));
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(17));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Failed));
            That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(OrderFailureReason.SubmissionQueueFull));

            bool castFailed = false;
            foreach (ref readonly var evt in presentationEvents.Events)
            {
                if (evt.Kind != GasPresentationEventKind.CastFailed) continue;
                castFailed = true;
                That(evt.FailReason, Is.EqualTo(AbilityCastFailReason.PreconditionFailed));
            }
            That(castFailed, Is.True);
        }

        [Test]
        public void InputOrderMapping_PositionCommand_FansOutAcrossExplicitActorCollection()
        {
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
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
                        IsSkillMapping = false,
                    },
                },
            };

            using var world = World.Create();
            var local = world.Create();
            var first = world.Create();
            var second = world.Create();
            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetLocalPlayer(local, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "moveTo" ? 1002 : 0);
            mapping.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = new Vector3(320f, 0f, 640f);
                return true;
            });
            mapping.SetCollectionEntityListProvider((string collectionKey, List<Entity> entities, int capacity, out OrderSubmitResult rejection) =>
            {
                That(collectionKey, Is.EqualTo("collection.test.actors"));
                entities.Clear();
                entities.Add(first);
                entities.Add(second);
                rejection = OrderSubmitResult.Activated;
                return true;
            });

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order _) =>
            {
                Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            mapping.SetOrderBatchSubmitHandler((Span<Order> batch) =>
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    orders.Add(batch[i]);
                }

                return OrderSubmitResult.Queued;
            });

            input.InjectButtonPress("Command");
            input.Update();
            mapping.Update(0f);

            That(orders.Count, Is.EqualTo(2));
            That(orders[0].Actor, Is.EqualTo(first));
            That(orders[1].Actor, Is.EqualTo(second));
            That(orders[0].Args.Spatial.WorldCm, Is.EqualTo(new Vector3(320f, 0f, 640f)));
            That(orders[1].Args.Spatial.WorldCm, Is.EqualTo(new Vector3(320f, 0f, 640f)));
        }

        [Test]
        public void InputOrderMapping_PositionMoveCommand_WithGroupTargetLayout_AssignsOffsetTargetsAcrossExplicitActorCollection()
        {
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                GroupMoveTargetLayout = new GroupMoveTargetLayoutSettings
                {
                    Mode = GroupMoveTargetLayoutMode.Grid,
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
                        OrderTypeKey = "moveTo",
                        RequireTarget = true,
                        TargetType = OrderTargetType.Position,
                        IsSkillMapping = false,
                    },
                },
            };

            using var world = World.Create();
            var local = world.Create();
            var first = world.Create();
            var second = world.Create();
            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetLocalPlayer(local, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "moveTo" ? 1002 : 0);
            mapping.SetGroundPositionProvider((out Vector3 worldCm) =>
            {
                worldCm = new Vector3(320f, 0f, 640f);
                return true;
            });
            mapping.SetCollectionEntityListProvider((string collectionKey, List<Entity> entities, int capacity, out OrderSubmitResult rejection) =>
            {
                That(collectionKey, Is.EqualTo("collection.test.actors"));
                entities.Clear();
                entities.Add(first);
                entities.Add(second);
                rejection = OrderSubmitResult.Activated;
                return true;
            });

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order _) =>
            {
                Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            mapping.SetOrderBatchSubmitHandler((Span<Order> batch) =>
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    orders.Add(batch[i]);
                }

                return OrderSubmitResult.Queued;
            });

            input.InjectButtonPress("Command");
            input.Update();
            mapping.Update(0f);

            That(orders.Count, Is.EqualTo(2));
            That(orders[0].Actor, Is.EqualTo(first));
            That(orders[1].Actor, Is.EqualTo(second));
            That(orders[0].Args.Spatial.WorldCm, Is.EqualTo(new Vector3(260f, 0f, 640f)));
            That(orders[1].Args.Spatial.WorldCm, Is.EqualTo(new Vector3(380f, 0f, 640f)));
        }

        [Test]
        public void InputOrderMapping_StopCommand_FansOutAcrossExplicitActorCollection()
        {
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Stop",
                        ActorCollectionKey = "collection.test.actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "stop",
                        RequireTarget = false,
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = false,
                    },
                },
            };

            using var world = World.Create();
            var local = world.Create();
            var first = world.Create();
            var second = world.Create();
            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetLocalPlayer(local, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "stop" ? 1003 : 0);
            mapping.SetCollectionEntityListProvider((string collectionKey, List<Entity> entities, int capacity, out OrderSubmitResult rejection) =>
            {
                That(collectionKey, Is.EqualTo("collection.test.actors"));
                entities.Clear();
                entities.Add(first);
                entities.Add(second);
                rejection = OrderSubmitResult.Activated;
                return true;
            });

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order _) =>
            {
                Fail("Multi-actor collection fan-out must use the atomic batch submit handler.");
                return OrderSubmitResult.RejectedValidation;
            });
            mapping.SetOrderBatchSubmitHandler((Span<Order> batch) =>
            {
                for (int i = 0; i < batch.Length; i++)
                {
                    orders.Add(batch[i]);
                }

                return OrderSubmitResult.Queued;
            });

            input.InjectButtonPress("Stop");
            input.Update();
            mapping.Update(0f);

            That(orders.Count, Is.EqualTo(2));
            That(orders[0].Actor, Is.EqualTo(first));
            That(orders[1].Actor, Is.EqualTo(second));
            That(orders[0].OrderTypeId, Is.EqualTo(1003));
            That(orders[1].OrderTypeId, Is.EqualTo(1003));
        }

        [Test]
        public void InputOrderMapping_MultiActorCollectionWithoutBatchHandler_FailsFast()
        {
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Stop",
                        ActorCollectionKey = "collection.test.actors",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "stop",
                        RequireTarget = false,
                        TargetType = OrderTargetType.None,
                        IsSkillMapping = false,
                    },
                },
            };

            using var world = World.Create();
            var local = world.Create();
            var first = world.Create();
            var second = world.Create();
            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetLocalPlayer(local, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "stop" ? 1003 : 0);
            mapping.SetCollectionEntityListProvider((string collectionKey, List<Entity> entities, int capacity, out OrderSubmitResult rejection) =>
            {
                That(collectionKey, Is.EqualTo("collection.test.actors"));
                entities.Clear();
                entities.Add(first);
                entities.Add(second);
                rejection = OrderSubmitResult.Activated;
                return true;
            });
            mapping.SetOrderSubmitHandler((in Order _) =>
            {
                Fail("Multi-actor collection fan-out must not silently fall back to direct per-order submission.");
                return OrderSubmitResult.RejectedValidation;
            });

            input.InjectButtonPress("Stop");
            input.Update();

            var ex = Throws<InvalidOperationException>(() => mapping.Update(0f));

            That(ex!.Message, Does.Contain("atomic batch submit handler"));
        }

        [Test]
        public void CommandSourceAcquisitionSystem_ClickAndScreenDrag_UpdateSelectionBuffer_SelectedTag_AndPrimaryEntity()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var second = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var third = world.Create(WorldPositionCm.FromCm(3400, 2200), new VisualTransform { Position = new Vector3(34f, 0f, 22f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);

            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            Click(system, globals, input, new Vector2(1600f, 1200f));

            AssertCommandSource(globals, local, first);
            That(EntityCollectionContextRuntime.TryGetPrimary(world, globals, local, EntityCollectionKeys.CommandSource, out var currentPrimary), Is.True);
            That(currentPrimary, Is.EqualTo(first));

            DragSelect(system, globals, input, new Vector2(1500f, 1100f), new Vector2(3500f, 2300f));

            AssertCommandSource(globals, local, first, second, third);
            That(EntityCollectionContextRuntime.TryGetPrimary(world, globals, local, EntityCollectionKeys.CommandSource, out currentPrimary), Is.True);
            That(currentPrimary, Is.EqualTo(first), "Primary selected entity should stay deterministic after box select.");
        }

        [Test]
        public void CommandSourceAcquisitionSystem_AcquisitionPublishesCommandSourceCollection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var second = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            Click(system, globals, input, new Vector2(1600f, 1200f));

            AssertCommandSource(globals, local, first);

            input.InjectAction(CommandSourceModifierActionIds.Additive, Vector3.One);
            Click(system, globals, input, new Vector2(2600f, 1600f));

            AssertCommandSource(globals, local, first, second);

            input.InjectAction(CommandSourceModifierActionIds.Additive, Vector3.Zero);
            input.InjectAction(CommandSourceModifierActionIds.Toggle, Vector3.One);
            Click(system, globals, input, new Vector2(1600f, 1200f));

            AssertCommandSource(globals, local, second);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_ClickEmptyGround_ClearsSelection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            SeedCommandSource(world, globals, local, first);

            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);
            Click(system, globals, input, new Vector2(5200f, 4200f));

            AssertCommandSource(globals, local);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_AcquisitionPreviewPublishesCommandSource()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var selected = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var acquired = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            SeedCommandSource(world, globals, local, selected);
            var collections = (EntityCollectionStore)globals[CoreServiceKeys.EntityCollectionStore.Name];
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            DragSelect(system, globals, input, new Vector2(2500f, 1500f), new Vector2(2700f, 1700f));

            AssertCommandSource(globals, local, acquired);
            That(collections.TryGet(local, EntityCollectionKeys.UiCommandAcquisition, out EntityCollectionHandle handle), Is.True);
            That(collections.TryGetView(handle, out EntityCollectionView view), Is.True);
            That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.UiAcquisition));
            That(view.Role, Is.EqualTo(EntityCollectionRoleKind.AcquisitionPreview));
            That(view.Count, Is.EqualTo(1));
            That(collections.TryGetEntityAt(handle, 0, out Entity row), Is.True);
            That(row, Is.EqualTo(acquired));
        }

        [Test]
        public void CommandSourceAcquisitionSystem_PointerHoverWithoutButtonSnapshot_WritesHoverCollection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var hovered = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            input.InjectAction("PointerPos", new Vector3(1600f, 1200f, 0f));
            input.Update();
            system.Update(0f);

            That(EntityCollectionContextRuntime.TryGetPrimary(world, globals, local, EntityCollectionKeys.HoveredEntity, out Entity actual), Is.True);
            That(actual, Is.EqualTo(hovered));
        }

        [Test]
        public void CommandSourceAcquisitionSystem_RuntimeDisabledEntity_IsNotSelectable()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            _ = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag(),
                CommandSourceSelectableState.Disabled);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            Click(system, globals, input, new Vector2(1600f, 1200f));

            AssertCommandSource(globals, local);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_TargetRelationFilter_GatesClickAcquireButKeepsHover()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
                var local = world.Create(new Team { Id = 1 });
                var formation = world.Create(
                    WorldPositionCm.FromCm(1600, 1200),
                    new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new CommandSourceSelectableTag(),
                    new Team { Id = 2 });

                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.AuthoritativeInput.Name] = input,
                    [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                    [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                    [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                    [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                    [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                    [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                    [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
                };
                CreateCommandSourceRuntime(world, globals, "Friendly");
                CommandSourceDomainHarness domains = InstallCommandSourceDomainServices(world, globals);
                Entity teamOne = world.Create(new TeamIdentity { TeamId = 1 });
                Entity teamTwo = world.Create(new TeamIdentity { TeamId = 2 });
                domains.Relationships.EnsureLink(local, teamOne, domains.MemberOfTypeId);
                domains.Relationships.EnsureLink(formation, teamTwo, domains.MemberOfTypeId);
                var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

                Click(system, globals, input, new Vector2(1600f, 1200f));

                AssertCommandSource(globals, local);
                That(EntityCollectionContextRuntime.TryGetPrimary(world, globals, local, EntityCollectionKeys.HoveredEntity, out Entity hovered), Is.True);
                That(hovered, Is.EqualTo(formation));

                world.Set(formation, new Team { Id = 1 });
                domains.Relationships.RemoveLink(formation, teamTwo, domains.MemberOfTypeId);
                domains.Relationships.EnsureLink(formation, teamOne, domains.MemberOfTypeId);
                Click(system, globals, input, new Vector2(1600f, 1200f));

                AssertCommandSource(globals, local, formation);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void CommandSourceAcquisitionSystem_TargetRelationFilter_FiltersBoxSelection()
        {
            using var world = World.Create();
            TeamManager.Clear();

            try
            {
                var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
                var local = world.Create(new Team { Id = 1 });
                var first = world.Create(
                    WorldPositionCm.FromCm(1600, 1200),
                    new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new CommandSourceSelectableTag(),
                    new Team { Id = 1 });
                var second = world.Create(
                    WorldPositionCm.FromCm(2600, 1600),
                    new VisualTransform { Position = new Vector3(26f, 0f, 16f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new CommandSourceSelectableTag(),
                    new Team { Id = 2 });
                _ = world.Create(
                    WorldPositionCm.FromCm(3000, 1900),
                    new VisualTransform { Position = new Vector3(30f, 0f, 19f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new CommandSourceSelectableTag());
                var third = world.Create(
                    WorldPositionCm.FromCm(3400, 2200),
                    new VisualTransform { Position = new Vector3(34f, 0f, 22f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new CommandSourceSelectableTag(),
                    new Team { Id = 1 });

                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.AuthoritativeInput.Name] = input,
                    [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                    [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                    [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                    [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                    [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                    [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                    [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
                };
                CreateCommandSourceRuntime(world, globals, "Friendly");
                CommandSourceDomainHarness domains = InstallCommandSourceDomainServices(world, globals);
                Entity teamOne = world.Create(new TeamIdentity { TeamId = 1 });
                Entity teamTwo = world.Create(new TeamIdentity { TeamId = 2 });
                domains.Relationships.EnsureLink(local, teamOne, domains.MemberOfTypeId);
                domains.Relationships.EnsureLink(first, teamOne, domains.MemberOfTypeId);
                domains.Relationships.EnsureLink(second, teamTwo, domains.MemberOfTypeId);
                domains.Relationships.EnsureLink(third, teamOne, domains.MemberOfTypeId);
                var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

                DragSelect(system, globals, input, new Vector2(1500f, 1100f), new Vector2(3500f, 2300f));

                AssertCommandSource(globals, local, first, third);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void CommandSourceAcquisitionSystem_ClickUsesFootprintPolygonInsteadOfProjectedOriginOnly()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag(),
                new SpatialBounds { Kind = SpatialBoundsKind.Footprint2D },
                CreateRectFootprint(0, 0, 300, 200));

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            Click(system, globals, input, new Vector2(1700f, 1200f));

            AssertCommandSource(globals, local, entity);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_DragSelectUsesFootprintIntersection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(3000, 1500),
                new VisualTransform { Position = new Vector3(30f, 0f, 15f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag(),
                new SpatialBounds { Kind = SpatialBoundsKind.Footprint2D, LocalCenterXCm = -250 },
                CreateRectFootprint(0, 0, 600, 300));

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            DragSelect(system, globals, input, new Vector2(2500f, 1300f), new Vector2(2850f, 1700f));

            AssertCommandSource(globals, local, entity);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_Box3DProjectedBoundsAreSelectable()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag(),
                new SpatialBounds { Kind = SpatialBoundsKind.Box3D, LocalCenterYCm = 100 },
                new SpatialBox3D { HalfSizeXCm = 150, HalfSizeYCm = 100, HalfSizeZCm = 150 });

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            Click(system, globals, input, new Vector2(1700f, 1200f));

            AssertCommandSource(globals, local, entity);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_ClickWithoutGroundPoint_StillSelectsHoveredEntity()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag(),
                new SpatialBounds { Kind = SpatialBoundsKind.Footprint2D },
                CreateRectFootprint(0, 0, 300, 200));

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            SetConfirmSnapshot(globals, new Vector2(1700f, 1200f), pressedThisFrame: true, isDown: true);
            input.InjectAction("PointerPos", new Vector3(1700f, 1200f, 0f));
            input.Update();
            system.Update(0f);

            SetConfirmSnapshot(globals, new Vector2(1700f, 1200f), pressedThisFrame: false, isDown: false, releasedThisFrame: true);
            input.InjectAction("PointerPos", new Vector3(1700f, 1200f, 0f));
            input.Update();
            system.Update(0f);

            AssertCommandSource(globals, local, entity);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_ClickPressedAndReleasedInOneSnapshot_SelectsHoveredEntity()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag(),
                new SpatialBounds { Kind = SpatialBoundsKind.Footprint2D },
                CreateRectFootprint(0, 0, 300, 200));

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            SetConfirmSnapshot(globals, new Vector2(1700f, 1200f), pressedThisFrame: true, isDown: false, releasedThisFrame: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(1700, 1200));
            input.InjectAction("PointerPos", new Vector3(1700f, 1200f, 0f));
            input.Update();
            system.Update(0f);

            AssertCommandSource(globals, local, entity);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_AdditiveModifierAddsWithoutReplacing()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var second = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            Click(system, globals, input, new Vector2(1600f, 1200f));
            input.InjectAction(CommandSourceModifierActionIds.Additive, Vector3.One);
            Click(system, globals, input, new Vector2(2600f, 1600f));

            AssertCommandSource(globals, local, first, second);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_ToggleModifierRemovesExistingSelectionMember()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var second = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            CreateCommandSourceRuntime(world, globals);
            SeedCommandSource(world, globals, local, first, second);
            var system = CreateCommandSourceAcquisitionSystem(world, globals, local);

            input.InjectAction(CommandSourceModifierActionIds.Toggle, Vector3.One);
            Click(system, globals, input, new Vector2(1600f, 1200f));

            AssertCommandSource(globals, local, second);
        }

        [Test]
        public void CommandSourceAcquisitionSystem_AimConfirmRelease_DoesNotStealCommandSource()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var actor = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var enemy = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new CommandSourceSelectableTag());
            var bindings = new InteractionActionBindings();

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = bindings,
            };
            CreateCommandSourceRuntime(world, globals);
            SeedCommandSource(world, globals, local, actor);

            var selectionSystem = CreateCommandSourceAcquisitionSystem(world, globals, local);
            var mapping = new InputOrderMappingSystem(input, new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.AimCast,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        ArgsTemplate = new OrderArgsTemplate { I0 = 0 },
                        RequireTarget = false,
                        TargetType = OrderTargetType.Entity,
                        IsSkillMapping = true,
                    },
                },
            });
            mapping.SetInteractionActionBindings(bindings);

            mapping.SetLocalPlayer(actor, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 1001 : 0);
            mapping.SetCollectionPrimaryEntityProvider((string _, out Entity entity) =>
            {
                entity = actor;
                return true;
            });
            mapping.SetHoveredEntityProvider((out Entity entity) =>
            {
                entity = enemy;
                return true;
            });

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order order) =>
            {
                orders.Add(order);
                return OrderSubmitResult.Queued;
            });
            globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = mapping;

            input.InjectButtonPress("SkillQ");
            input.Update();
            selectionSystem.Update(0f);
            mapping.Update(0f);
            That(mapping.IsAiming, Is.True);

            SetConfirmSnapshot(globals, new Vector2(2600f, 1600f), pressedThisFrame: true, isDown: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(2600, 1600));
            input.InjectAction("PointerPos", new Vector3(2600f, 1600f, 0f));
            input.InjectButtonPress(InteractionActionBindings.DefaultConfirmActionId);
            input.Update();
            selectionSystem.Update(0f);
            mapping.Update(0f);

            SetConfirmSnapshot(globals, new Vector2(2600f, 1600f), pressedThisFrame: false, isDown: false, releasedThisFrame: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(2600, 1600));
            input.InjectAction("PointerPos", new Vector3(2600f, 1600f, 0f));
            input.Update();
            selectionSystem.Update(0f);
            mapping.Update(0f);

            AssertCommandSource(globals, local, actor);
            That(orders.Count, Is.EqualTo(1));
            That(orders[0].Actor, Is.EqualTo(actor));
            That(orders[0].Target, Is.EqualTo(enemy));
        }

        [Test]
        public void TabTargetCycleSystem_SkipsRuntimeDisabledCandidates()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create(
                new Team { Id = 1 },
                new VisualTransform { Position = Vector3.Zero });
            _ = world.Create(
                new Team { Id = 2 },
                new VisualTransform { Position = new Vector3(5f, 0f, 0f) },
                new CommandSourceSelectableTag(),
                CommandSourceSelectableState.Disabled);
            var enabledEnemy = world.Create(
                new Team { Id = 2 },
                new VisualTransform { Position = new Vector3(10f, 0f, 0f) },
                new CommandSourceSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };

            var system = new TabTargetCycleSystem(world, globals, searchRadiusCm: 3000);

            input.InjectButtonPress(TabTargetCycleSystem.TabTargetActionId);
            input.Update();
            system.Update(0f);

            That(globals.TryGetValue(CoreServiceKeys.TabTargetEntity.Name, out var targetObj), Is.True);
            That(targetObj, Is.EqualTo(enabledEnemy));
        }

        private static InputConfigRoot CreateInputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "SkillQ", Name = "SkillQ", Type = InputActionType.Button },
                    new() { Id = "Command", Name = "Command", Type = InputActionType.Button },
                    new() { Id = "Stop", Name = "Stop", Type = InputActionType.Button },
                    new() { Id = "Confirm", Name = "Confirm", Type = InputActionType.Button },
                    new() { Id = InteractionActionBindings.DefaultConfirmActionId, Name = "Command Source Acquire", Type = InputActionType.Button },
                    new() { Id = CommandSourceModifierActionIds.Additive, Name = CommandSourceModifierActionIds.Additive, Type = InputActionType.Button },
                    new() { Id = CommandSourceModifierActionIds.Toggle, Name = CommandSourceModifierActionIds.Toggle, Type = InputActionType.Button },
                    new() { Id = "TabTarget", Name = "TabTarget", Type = InputActionType.Button },
                    new() { Id = "TabTargetReverse", Name = "TabTargetReverse", Type = InputActionType.Button },
                    new() { Id = "PointerPos", Name = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = AuthoritativeGroundPointerHelper.ActionId, Name = AuthoritativeGroundPointerHelper.ActionId, Type = InputActionType.Axis3D },
                },
                Contexts = new List<InputContextDef>
                {
                    new() { Id = "Test", Name = "Test", Priority = 1 },
                },
            };
        }

        private static void Click(CommandSourceAcquisitionSystem system, Dictionary<string, object> globals, PlayerInputHandler input, Vector2 pointer)
        {
            SetConfirmSnapshot(globals, pointer, pressedThisFrame: true, isDown: true, releasedThisFrame: false);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2((int)pointer.X, (int)pointer.Y));
            input.InjectAction("PointerPos", new Vector3(pointer.X, pointer.Y, 0f));
            input.Update();
            system.Update(0f);

            SetConfirmSnapshot(globals, pointer, pressedThisFrame: false, isDown: false, releasedThisFrame: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2((int)pointer.X, (int)pointer.Y));
            input.InjectAction("PointerPos", new Vector3(pointer.X, pointer.Y, 0f));
            input.Update();
            system.Update(0f);
        }

        private static CommandSourceAcquisitionSystem CreateCommandSourceAcquisitionSystem(
            World world,
            Dictionary<string, object> globals,
            Entity owner)
        {
            return new CommandSourceAcquisitionSystem(
                world,
                globals,
                (out Entity resolvedOwner) =>
                {
                    resolvedOwner = owner;
                    return owner != Entity.Null && world.IsAlive(owner);
                });
        }

        private static void DragSelect(CommandSourceAcquisitionSystem system, Dictionary<string, object> globals, PlayerInputHandler input, Vector2 from, Vector2 to)
        {
            SetConfirmSnapshot(globals, from, pressedThisFrame: true, isDown: true, releasedThisFrame: false);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2((int)from.X, (int)from.Y));
            input.InjectAction("PointerPos", new Vector3(from.X, from.Y, 0f));
            input.Update();
            system.Update(0f);

            SetConfirmSnapshot(globals, to, pressedThisFrame: false, isDown: true, releasedThisFrame: false);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2((int)to.X, (int)to.Y));
            input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
            input.Update();
            system.Update(0f);

            SetConfirmSnapshot(globals, to, pressedThisFrame: false, isDown: false, releasedThisFrame: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2((int)to.X, (int)to.Y));
            input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
            input.Update();
            system.Update(0f);
        }

        private static void SetConfirmSnapshot(Dictionary<string, object> globals, Vector2 pointer, bool pressedThisFrame, bool isDown, bool releasedThisFrame = false)
        {
            string actionId = InteractionActionBindingsResolver.Require(globals, nameof(InteractionSelectionConvergenceTests)).ConfirmActionId;
            SetActionSnapshot(globals, actionId, pointer, pressedThisFrame, isDown, releasedThisFrame);
        }

        private static void SetActionSnapshot(Dictionary<string, object> globals, string actionId, Vector2 pointer, bool pressedThisFrame, bool isDown, bool releasedThisFrame = false)
        {
            var pointerButtons = globals.TryGetValue(CoreServiceKeys.AuthoritativePointerButtons.Name, out object? snapshotObj) &&
                                 snapshotObj is AuthoritativePointerButtonSnapshot snapshot
                ? snapshot
                : throw new InvalidOperationException("AuthoritativePointerButtons missing from globals.");
            pointerButtons.SetState(
                actionId,
                new PointerButtonState(
                    pointer,
                    pointer,
                    pointer,
                    pointer,
                    isDown: isDown,
                    pressedThisFrame: pressedThisFrame,
                    releasedThisFrame: releasedThisFrame,
                    hasPressPointer: pressedThisFrame,
                    hasReleasePointer: releasedThisFrame,
                    hasLastDownPointer: isDown || releasedThisFrame));
        }

        private static void SetAuthoritativeGroundPoint(PlayerInputHandler input, in WorldCmInt2 worldCm)
        {
            input.InjectAction(AuthoritativeGroundPointerHelper.ActionId, new Vector3(worldCm.X, 0f, worldCm.Y));
        }

        private static void AssertCommandSource(Dictionary<string, object> globals, Entity owner, params Entity[] expected)
        {
            var collections = (EntityCollectionStore)globals[CoreServiceKeys.EntityCollectionStore.Name];
            That(collections.TryGet(owner, EntityCollectionKeys.CommandSource, out EntityCollectionHandle handle), Is.True);
            That(collections.TryGetView(handle, out EntityCollectionView view), Is.True);
            That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.UiAcquisition));
            That(view.Role, Is.EqualTo(EntityCollectionRoleKind.CommandSource));
            That(view.PrimaryEntity, Is.EqualTo(expected.Length > 0 ? expected[0] : Entity.Null));
            That(view.Count, Is.EqualTo(expected.Length));

            Entity[] actual = new Entity[expected.Length];
            int written = collections.CopyEntities(handle, 0, actual);
            That(written, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                That(actual[i], Is.EqualTo(expected[i]));
            }
        }

        private static EntityCollectionStore CreateCommandSourceRuntime(World world, Dictionary<string, object> globals, string relationFilter = "All")
        {
            var config = new CommandSourceAcquisitionConfig
            {
                TargetFilter = new CommandSourceTargetFilterConfig { RelationFilter = relationFilter },
                Acquisition = new CommandSourceAcquisitionCollectionConfig
                {
                    CollectionKey = EntityCollectionKeys.UiCommandAcquisition,
                    Title = "Command acquisition",
                },
            };
            var collectionRegistry = new Ludots.Core.Registry.StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var collections = new EntityCollectionStore(collectionRegistry);
            globals[CoreServiceKeys.CommandSourceAcquisitionConfig.Name] = config;
            globals[CoreServiceKeys.EntityCollectionStore.Name] = collections;
            globals[CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionRegistry;
            return collections;
        }

        private static CommandSourceDomainHarness InstallCommandSourceDomainServices(
            World world,
            Dictionary<string, object> globals)
        {
            var types = new RelationshipTypeRegistry();
            int ownsTypeId = types.Register("Owns");
            int controlsTypeId = types.Register("Controls");
            int memberOfTypeId = types.Register("MemberOf");
            types.Register("Hostile", isSymmetric: true);
            types.Register("Friendly", isSymmetric: true);
            types.Register("Neutral", isSymmetric: true);
            var relationships = new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
            var ownership = new OwnershipResolver(relationships, ownsTypeId);
            var controlDomains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
            var stances = DomainStanceQuery.Create(relationships, memberOfTypeId, new DomainStanceConfig
            {
                StanceTypes = new List<string> { "Hostile", "Friendly", "Neutral" },
                SameDomainStance = "Friendly",
                SameTeamStance = "Friendly",
                DefaultStance = "Neutral",
            });
            globals[CoreServiceKeys.ControlDomainQuery.Name] = controlDomains;
            globals[CoreServiceKeys.DomainStanceQuery.Name] = stances;
            return new CommandSourceDomainHarness(relationships, memberOfTypeId);
        }

        private readonly record struct CommandSourceDomainHarness(
            RelationshipRuntime Relationships,
            int MemberOfTypeId);

        private static void SeedCommandSource(World world, Dictionary<string, object> globals, Entity owner, params Entity[] targets)
        {
            var collections = globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) &&
                              storeObj is EntityCollectionStore store
                ? store
                : throw new InvalidOperationException("EntityCollectionStore missing from globals.");
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.CommandSource,
                owner,
                targets.Length > 0 ? targets[0] : Entity.Null,
                "Command source",
                $"Seed | {targets.Length} actor(s)");
            collections.Replace(owner, descriptor, targets, owner);
        }

        private static WorldSizeSpec CreateWorldSizeSpec()
        {
            return new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100);
        }

        private static SpatialFootprint2D CreateRectFootprint(int centerXCm, int centerZCm, int widthCm, int depthCm)
        {
            int halfWidth = widthCm / 2;
            int halfDepth = depthCm / 2;
            var footprint = new SpatialFootprint2D();
            footprint.SetPolygonVertexCount(0, 4);
            footprint.SetVertex(0, 0, new WorldCmInt2(centerXCm - halfWidth, centerZCm - halfDepth));
            footprint.SetVertex(0, 1, new WorldCmInt2(centerXCm + halfWidth, centerZCm - halfDepth));
            footprint.SetVertex(0, 2, new WorldCmInt2(centerXCm + halfWidth, centerZCm + halfDepth));
            footprint.SetVertex(0, 3, new WorldCmInt2(centerXCm - halfWidth, centerZCm + halfDepth));
            return footprint;
        }

        private static IVisualHeightmap CreateFlatHeightmap()
        {
            return new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(-10_000, -10_000, 20_000, 20_000),
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[]
                    {
                        0, 0,
                        0, 0,
                    }));
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

        private sealed class ConstantScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(new Vector3(0f, 10f, 0f), new Vector3(0f, -1f, 0f));
            }
        }

        private sealed class AnchoredScreenRayProvider : IScreenRayProvider
        {
            private readonly Vector3 _origin;

            public AnchoredScreenRayProvider(Vector3 origin)
            {
                _origin = origin;
            }

            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(_origin, new Vector3(0f, -1f, 0f));
            }
        }

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f), -Vector3.UnitY);
            }
        }

        private sealed class WorldMappedScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
            }
        }

    }
}
