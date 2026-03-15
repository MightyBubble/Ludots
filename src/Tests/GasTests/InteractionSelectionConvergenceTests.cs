using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
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
        public void GasSelectionResponseSystem_UsesRegisteredRule_AndSharedInteractionBindings()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new AnchoredScreenRayProvider(new Vector3(1.5f, 10f, 2.5f)),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.SelectionRequestQueue.Name] = new SelectionRequestQueue(),
                [CoreServiceKeys.SelectionResponseBuffer.Name] = new SelectionResponseBuffer(),
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

            var origin = world.Create(new Team { Id = 1 });
            var targetContext = world.Create();
            var enemy = world.Create(WorldPositionCm.FromCm(50, 0), new Team { Id = 2 }, new SelectionSelectableTag());
            _ = world.Create(WorldPositionCm.FromCm(40, 0), new Team { Id = 1 });

            var rules = new SelectionRuleRegistry();
            rules.Register(77, new SelectionRule
            {
                Mode = SelectionRuleMode.Radius,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = 200,
                MaxCount = 8,
            });

            var system = new GasSelectionResponseSystem(world, globals, new StubSpatialQueryService(enemy), rules);
            var requests = (SelectionRequestQueue)globals[CoreServiceKeys.SelectionRequestQueue.Name];
            var responses = (SelectionResponseBuffer)globals[CoreServiceKeys.SelectionResponseBuffer.Name];
            requests.TryEnqueue(new SelectionRequest
            {
                RequestId = 42,
                RequestTagId = 77,
                Origin = origin,
                TargetContext = targetContext,
            });

            input.InjectButtonPress("Confirm");
            input.Update();
            system.Update(0f);

            That(responses.TryConsume(42, out var response), Is.True);
            That(response.Count, Is.EqualTo(1));
            That(response.GetEntity(0), Is.EqualTo(enemy));
            That(response.TargetContext, Is.EqualTo(targetContext));
            That(response.TryGetWorldPoint(out var worldPoint), Is.True);
            That(worldPoint, Is.EqualTo(new WorldCmInt2(150, 250)));
        }

        [Test]
        public void GasInputResponseSystem_UsesSharedInteractionBindings()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var target = world.Create();
            var local = world.Create(new SelectionBuffer());
            SeedAmbientSelection(world, local, target);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AbilityInputRequestQueue.Name] = new InputRequestQueue(),
                [CoreServiceKeys.InputResponseBuffer.Name] = new InputResponseBuffer(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };
            CreateSelectionRuntime(world, globals);

            var system = new GasInputResponseSystem(world, globals);
            var requests = (InputRequestQueue)globals[CoreServiceKeys.AbilityInputRequestQueue.Name];
            var responses = (InputResponseBuffer)globals[CoreServiceKeys.InputResponseBuffer.Name];
            requests.TryEnqueue(new InputRequest { RequestId = 9, RequestTagId = 501 });

            input.InjectButtonPress("Confirm");
            input.Update();
            system.Update(0f);

            That(responses.TryConsume(9, out var response), Is.True);
            That(response.Target, Is.EqualTo(target));
            That(response.ResponseTagId, Is.EqualTo(501));
        }

        [Test]
        public void GasSelectionResponseSystem_FailsFast_WhenSelectionResponseBufferIsFull()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new ConstantScreenRayProvider(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.SelectionRequestQueue.Name] = new SelectionRequestQueue(),
                [CoreServiceKeys.SelectionResponseBuffer.Name] = new SelectionResponseBuffer(16),
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

            var origin = world.Create(new Team { Id = 1 });
            var enemy = world.Create(WorldPositionCm.FromCm(50, 0), new Team { Id = 2 });
            var rules = new SelectionRuleRegistry();
            rules.Register(77, new SelectionRule
            {
                Mode = SelectionRuleMode.SingleNearest,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = 200,
                MaxCount = 1,
            });

            var system = new GasSelectionResponseSystem(world, globals, new StubSpatialQueryService(enemy), rules);
            var requests = (SelectionRequestQueue)globals[CoreServiceKeys.SelectionRequestQueue.Name];
            var responses = (SelectionResponseBuffer)globals[CoreServiceKeys.SelectionResponseBuffer.Name];
            for (int i = 0; i < responses.Capacity; i++)
            {
                That(responses.TryAdd(new SelectionResponse { RequestId = 1000 + i }), Is.True);
            }

            requests.TryEnqueue(new SelectionRequest
            {
                RequestId = 42,
                RequestTagId = 77,
                Origin = origin,
            });

            input.InjectButtonPress("Confirm");
            input.Update();

            var ex = NUnit.Framework.Assert.Throws<InvalidOperationException>(() => system.Update(0f));
            That(ex?.Message, Does.Contain("buffer overflow"));
            That(requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void GasSelectionResponseSystem_SingleNearest_SkipsRuntimeDisabledCandidates()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new ConstantScreenRayProvider(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.SelectionRequestQueue.Name] = new SelectionRequestQueue(),
                [CoreServiceKeys.SelectionResponseBuffer.Name] = new SelectionResponseBuffer(),
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

            var origin = world.Create(new Team { Id = 1 });
            var disabledEnemy = world.Create(
                WorldPositionCm.FromCm(50, 0),
                new Team { Id = 2 },
                new SelectionSelectableTag(),
                SelectionSelectableState.Disabled);
            var enabledEnemy = world.Create(
                WorldPositionCm.FromCm(120, 0),
                new Team { Id = 2 },
                new SelectionSelectableTag());

            var rules = new SelectionRuleRegistry();
            rules.Register(77, new SelectionRule
            {
                Mode = SelectionRuleMode.SingleNearest,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = 300,
                MaxCount = 1,
            });

            var system = new GasSelectionResponseSystem(world, globals, new StubSpatialQueryService(disabledEnemy, enabledEnemy), rules);
            var requests = (SelectionRequestQueue)globals[CoreServiceKeys.SelectionRequestQueue.Name];
            var responses = (SelectionResponseBuffer)globals[CoreServiceKeys.SelectionResponseBuffer.Name];
            requests.TryEnqueue(new SelectionRequest
            {
                RequestId = 42,
                RequestTagId = 77,
                Origin = origin,
            });

            input.InjectButtonPress("Confirm");
            input.Update();
            system.Update(0f);

            That(responses.TryConsume(42, out var response), Is.True);
            That(response.Count, Is.EqualTo(1));
            That(response.GetEntity(0), Is.EqualTo(enabledEnemy));
        }

        [Test]
        public void GasSelectionResponseSystem_Radius_SkipsRuntimeDisabledCandidates()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new ConstantScreenRayProvider(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.SelectionRequestQueue.Name] = new SelectionRequestQueue(),
                [CoreServiceKeys.SelectionResponseBuffer.Name] = new SelectionResponseBuffer(),
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

            var origin = world.Create(new Team { Id = 1 });
            var disabledEnemy = world.Create(
                WorldPositionCm.FromCm(50, 0),
                new Team { Id = 2 },
                new SelectionSelectableTag(),
                SelectionSelectableState.Disabled);
            var enabledEnemy = world.Create(
                WorldPositionCm.FromCm(120, 0),
                new Team { Id = 2 },
                new SelectionSelectableTag());
            _ = world.Create(
                WorldPositionCm.FromCm(150, 0),
                new Team { Id = 2 });

            var rules = new SelectionRuleRegistry();
            rules.Register(77, new SelectionRule
            {
                Mode = SelectionRuleMode.Radius,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = 300,
                MaxCount = 8,
            });

            var system = new GasSelectionResponseSystem(world, globals, new StubSpatialQueryService(disabledEnemy, enabledEnemy), rules);
            var requests = (SelectionRequestQueue)globals[CoreServiceKeys.SelectionRequestQueue.Name];
            var responses = (SelectionResponseBuffer)globals[CoreServiceKeys.SelectionResponseBuffer.Name];
            requests.TryEnqueue(new SelectionRequest
            {
                RequestId = 42,
                RequestTagId = 77,
                Origin = origin,
            });

            input.InjectButtonPress("Confirm");
            input.Update();
            system.Update(0f);

            That(responses.TryConsume(42, out var response), Is.True);
            That(response.Count, Is.EqualTo(1));
            That(response.GetEntity(0), Is.EqualTo(enabledEnemy));
        }

        [Test]
        public void AbilityExecSystem_SelectionGate_PopulatesTargetContext_AndWorldPoint()
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
            spec.SetItem(0, ExecItemKind.SelectionGate, tick: 0, tagId: 77);
            spec.SetItem(1, ExecItemKind.EventGate, tick: 1, tagId: 999);
            var def = new AbilityDefinition { ExecSpec = spec };
            defs.Register(9001, in def);

            var selectionRequests = new SelectionRequestQueue();
            var selectionResponses = new SelectionResponseBuffer();
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                selectionRequests,
                selectionResponses,
                new EffectRequestQueue(),
                defs,
                castAbilityOrderTypeId: 100,
                orderTypeRegistry: new OrderTypeRegistry());

            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.True);
            That(selectionRequests.Count, Is.EqualTo(1));
            ref var waitingExec = ref world.Get<AbilityExecInstance>(actor);
            That(waitingExec.State, Is.EqualTo(AbilityExecRunState.GateWaiting));
            That(waitingExec.WaitRequestId, Is.EqualTo(7));

            var response = default(SelectionResponse);
            response.RequestId = 7;
            response.ResponseTagId = 77;
            response.TargetContext = targetContext;
            response.SetWorldPoint(new WorldCmInt2(300, 400));
            response.Count = 1;
            response.SetEntity(0, enemy);
            That(selectionResponses.TryAdd(response), Is.True);

            system.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref var exec = ref world.Get<AbilityExecInstance>(actor);
            That(exec.State, Is.EqualTo(AbilityExecRunState.Running));
            That(exec.Target, Is.EqualTo(enemy));
            That(exec.TargetContext, Is.EqualTo(targetContext));
            That(exec.MultiTargetCount, Is.EqualTo(1));
            That(exec.HasTargetPos, Is.EqualTo(1));
            That(exec.TargetPosCm.ToWorldCmInt2(), Is.EqualTo(new WorldCmInt2(300, 400)));
        }

        [Test]
        public void InputOrderMapping_EntitiesSelection_UsesSelectedEntitiesProvider()
        {
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "SkillQ",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "castAbility",
                        RequireSelection = true,
                        SelectionType = OrderSelectionType.Entities,
                        IsSkillMapping = false,
                    },
                },
            };

            using var world = World.Create();
            var first = world.Create();
            var second = world.Create();
            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 1001 : 0);
            mapping.SetSelectedEntitiesProvider((ref OrderEntitySelection entities) =>
            {
                entities = default;
                entities.Add(first);
                entities.Add(second);
                return true;
            });

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            input.InjectButtonPress("SkillQ");
            input.Update();
            mapping.Update(0f);

            That(orders.Count, Is.EqualTo(1));
            That(orders[0].Args.Entities.Count, Is.EqualTo(2));
            That(orders[0].Args.Entities.GetEntity(0), Is.EqualTo(first));
            That(orders[0].Args.Entities.GetEntity(1), Is.EqualTo(second));
        }

        [Test]
        public void InputOrderMapping_PositionCommand_FansOutAcrossAmbientSelection()
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
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "moveTo",
                        RequireSelection = true,
                        SelectionType = OrderSelectionType.Position,
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
            mapping.SetSelectedEntitiesProvider((ref OrderEntitySelection entities) =>
            {
                entities = default;
                entities.Add(first);
                entities.Add(second);
                return true;
            });

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order order) => orders.Add(order));

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
        public void InputOrderMapping_StopCommand_FansOutAcrossAmbientSelection()
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
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTypeKey = "stop",
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.None,
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
            mapping.SetSelectedEntitiesProvider((ref OrderEntitySelection entities) =>
            {
                entities = default;
                entities.Add(first);
                entities.Add(second);
                return true;
            });

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order order) => orders.Add(order));

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
        public void EntityClickSelectSystem_ClickAndScreenDrag_UpdateSelectionBuffer_SelectedTag_AndPrimaryEntity()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            var second = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            var third = world.Create(WorldPositionCm.FromCm(3400, 2200), new VisualTransform { Position = new Vector3(34f, 0f, 22f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var bridge = new SelectionBridgeProjectionSystem(world, globals, selectionRuntime);

            var system = new EntityClickSelectSystem(world, globals);

            Click(system, bridge, input, new Vector2(1600f, 1200f));

            AssertSelection(world, local, first);
            That(world.Has<SelectedTag>(first), Is.True);
            That(world.Has<SelectedTag>(second), Is.False);
            That(globals[CoreServiceKeys.SelectedEntity.Name], Is.EqualTo(first));

            DragSelect(system, bridge, input, new Vector2(1500f, 1100f), new Vector2(3500f, 2300f));

            ref var selection = ref world.Get<SelectionBuffer>(local);
            That(selection.Count, Is.EqualTo(3));
            That(selection.Contains(first), Is.True);
            That(selection.Contains(second), Is.True);
            That(selection.Contains(third), Is.True);
            That(world.Has<SelectedTag>(first), Is.True);
            That(world.Has<SelectedTag>(second), Is.True);
            That(world.Has<SelectedTag>(third), Is.True);
            That(globals[CoreServiceKeys.SelectedEntity.Name], Is.EqualTo(first), "Primary selected entity should stay deterministic after box select.");
        }

        [Test]
        public void EntityClickSelectSystem_ClickEmptyGround_ClearsSelection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create(new SelectionBuffer());
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            SeedAmbientSelection(world, local, first);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var bridge = new SelectionBridgeProjectionSystem(world, globals, selectionRuntime);
            bridge.Update(0f);

            var system = new EntityClickSelectSystem(world, globals);
            Click(system, bridge, input, new Vector2(5200f, 4200f));

            ref var cleared = ref world.Get<SelectionBuffer>(local);
            That(cleared.Count, Is.EqualTo(0));
            That(world.Has<SelectedTag>(first), Is.False);
            That(globals.ContainsKey(CoreServiceKeys.SelectedEntity.Name), Is.False);
        }

        [Test]
        public void EntityClickSelectSystem_RuntimeDisabledEntity_IsNotSelectable()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create(new SelectionBuffer());
            _ = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f) },
                new CullState { IsVisible = true },
                new SelectionSelectableTag(),
                SelectionSelectableState.Disabled);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var bridge = new SelectionBridgeProjectionSystem(world, globals, selectionRuntime);
            var system = new EntityClickSelectSystem(world, globals);

            Click(system, bridge, input, new Vector2(1600f, 1200f));

            ref var selection = ref world.Get<SelectionBuffer>(local);
            That(selection.Count, Is.EqualTo(0));
            That(globals.ContainsKey(CoreServiceKeys.SelectedEntity.Name), Is.False);
        }

        [Test]
        public void EntityClickSelectSystem_AimConfirmRelease_DoesNotStealSelection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create(new SelectionBuffer());
            var actor = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            var enemy = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            SeedAmbientSelection(world, local, actor);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var bridge = new SelectionBridgeProjectionSystem(world, globals, selectionRuntime);
            bridge.Update(0f);

            var selectionSystem = new EntityClickSelectSystem(world, globals);
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
                        RequireSelection = false,
                        SelectionType = OrderSelectionType.Entity,
                        IsSkillMapping = true,
                    },
                },
            });

            mapping.SetLocalPlayer(actor, 1);
            mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 1001 : 0);
            mapping.SetSelectedEntityProvider((out Entity entity) =>
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
            mapping.SetOrderSubmitHandler((in Order order) => orders.Add(order));
            globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = mapping;

            input.InjectButtonPress("SkillQ");
            input.Update();
            selectionSystem.Update(0f);
            bridge.Update(0f);
            mapping.Update(0f);
            That(mapping.IsAiming, Is.True);

            input.InjectAction("PointerPos", new Vector3(2600f, 1600f, 0f));
            input.InjectButtonPress("Select");
            input.Update();
            selectionSystem.Update(0f);
            bridge.Update(0f);
            mapping.Update(0f);

            input.InjectAction("PointerPos", new Vector3(2600f, 1600f, 0f));
            input.Update();
            selectionSystem.Update(0f);
            bridge.Update(0f);
            mapping.Update(0f);

            AssertSelection(world, local, actor);
            That(globals[CoreServiceKeys.SelectedEntity.Name], Is.EqualTo(actor));
            That(world.Has<SelectedTag>(actor), Is.True);
            That(world.Has<SelectedTag>(enemy), Is.False);
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
                new SelectionSelectableTag(),
                SelectionSelectableState.Disabled);
            var enabledEnemy = world.Create(
                new Team { Id = 2 },
                new VisualTransform { Position = new Vector3(10f, 0f, 0f) },
                new SelectionSelectableTag());

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
                    new() { Id = "Select", Name = "Select", Type = InputActionType.Button },
                    new() { Id = "TabTarget", Name = "TabTarget", Type = InputActionType.Button },
                    new() { Id = "TabTargetReverse", Name = "TabTargetReverse", Type = InputActionType.Button },
                    new() { Id = "PointerPos", Name = "PointerPos", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new() { Id = "Test", Name = "Test", Priority = 1 },
                },
            };
        }

        private static void Click(EntityClickSelectSystem system, SelectionBridgeProjectionSystem bridge, PlayerInputHandler input, Vector2 pointer)
        {
            input.InjectAction("PointerPos", new Vector3(pointer.X, pointer.Y, 0f));
            input.InjectButtonPress("Select");
            input.Update();
            system.Update(0f);
            bridge.Update(0f);

            input.InjectAction("PointerPos", new Vector3(pointer.X, pointer.Y, 0f));
            input.Update();
            system.Update(0f);
            bridge.Update(0f);
        }

        private static void DragSelect(EntityClickSelectSystem system, SelectionBridgeProjectionSystem bridge, PlayerInputHandler input, Vector2 from, Vector2 to)
        {
            input.InjectAction("PointerPos", new Vector3(from.X, from.Y, 0f));
            input.InjectButtonPress("Select");
            input.Update();
            system.Update(0f);
            bridge.Update(0f);

            input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
            input.InjectButtonPress("Select");
            input.Update();
            system.Update(0f);
            bridge.Update(0f);

            input.InjectAction("PointerPos", new Vector3(to.X, to.Y, 0f));
            input.Update();
            system.Update(0f);
            bridge.Update(0f);
        }

        private static void AssertSelection(World world, Entity owner, params Entity[] expected)
        {
            ref var selection = ref world.Get<SelectionBuffer>(owner);
            That(selection.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                That(selection.Get(i), Is.EqualTo(expected[i]));
            }
        }

        private static SelectionRuntime CreateSelectionRuntime(World world, Dictionary<string, object> globals)
        {
            var config = new SelectionRuntimeConfig();
            var registry = new Ludots.Core.Registry.StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var runtime = new SelectionRuntime(world, config, registry);
            globals[CoreServiceKeys.SelectionRuntime.Name] = runtime;
            globals[CoreServiceKeys.SelectionConfig.Name] = config;
            globals[CoreServiceKeys.SelectionSetKeyRegistry.Name] = registry;
            return runtime;
        }

        private static void SeedAmbientSelection(World world, Entity owner, Entity target)
        {
            if (!world.Has<SelectionBuffer>(owner))
            {
                world.Add(owner, default(SelectionBuffer));
            }

            ref var selection = ref world.Get<SelectionBuffer>(owner);
            selection.Clear();
            selection.Add(target);
            world.Set(owner, selection);
        }

        private static WorldSizeSpec CreateWorldSizeSpec()
        {
            return new WorldSizeSpec(new WorldAabbCm(-10_000, -10_000, 20_000, 20_000), 100);
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

        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity[] _results;

            public StubSpatialQueryService(params Entity[] results)
            {
                _results = results ?? Array.Empty<Entity>();
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);
            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);

            private SpatialQueryResult Write(Span<Entity> buffer)
            {
                if (buffer.Length == 0 || _results.Length == 0)
                {
                    return new SpatialQueryResult(0, _results.Length);
                }

                int count = Math.Min(buffer.Length, _results.Length);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = _results[i];
                }

                return new SpatialQueryResult(count, Math.Max(0, _results.Length - count));
            }
        }
    }
}
