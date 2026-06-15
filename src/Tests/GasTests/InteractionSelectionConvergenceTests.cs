using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using CoreInputMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
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
        public void GasSelectionResponseSystem_UsesRegisteredRule_AndSharedInteractionBindings()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new AnchoredScreenRayProvider(new Vector3(1.5f, 10f, 2.5f)),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
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

            SetConfirmSnapshot(globals, new Vector2(150f, 250f), pressedThisFrame: true, isDown: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(150, 250));
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
        public void GasSelectionResponseSystem_UsesAuthoritativePointerButtonSnapshot_ForConfirmPointer()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var pointerButtons = new AuthoritativePointerButtonSnapshot();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = pointerButtons,
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.SelectionRequestQueue.Name] = new SelectionRequestQueue(),
                [CoreServiceKeys.SelectionResponseBuffer.Name] = new SelectionResponseBuffer(),
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

            var origin = world.Create(new Team { Id = 1 });
            var enemy = world.Create(WorldPositionCm.FromCm(1600, 1200), new Team { Id = 2 }, new SelectionSelectableTag());

            var rules = new SelectionRuleRegistry();
            rules.Register(77, new SelectionRule
            {
                Mode = SelectionRuleMode.SingleNearest,
                RelationshipFilter = RelationshipFilter.All,
                RadiusCm = 100,
                MaxCount = 1,
            });

            var system = new GasSelectionResponseSystem(world, globals, new StubSpatialQueryService(enemy), rules);
            var requests = (SelectionRequestQueue)globals[CoreServiceKeys.SelectionRequestQueue.Name];
            var responses = (SelectionResponseBuffer)globals[CoreServiceKeys.SelectionResponseBuffer.Name];
            requests.TryEnqueue(new SelectionRequest
            {
                RequestId = 42,
                RequestTagId = 77,
                Origin = origin,
            });

            pointerButtons.SetState(
                "Confirm",
                new PointerButtonState(
                    new Vector2(1600f, 1200f),
                    new Vector2(1600f, 1200f),
                    default,
                    new Vector2(1600f, 1200f),
                    isDown: true,
                    pressedThisFrame: true,
                    releasedThisFrame: false,
                    hasPressPointer: true,
                    hasReleasePointer: false,
                    hasLastDownPointer: true));

            SetAuthoritativeGroundPoint(input, new WorldCmInt2(1600, 1200));
            input.InjectAction("PointerPos", new Vector3(5200f, 4200f, 0f));
            input.Update();
            system.Update(0f);

            That(responses.TryConsume(42, out var response), Is.True);
            That(response.TryGetWorldPoint(out var worldPoint), Is.True);
            That(worldPoint, Is.EqualTo(new WorldCmInt2(1600, 1200)));
            That(response.Count, Is.EqualTo(1));
            That(response.GetEntity(0), Is.EqualTo(enemy));
        }

        [Test]
        public void GasInputResponseSystem_UsesSharedInteractionBindings()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var target = world.Create();
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
            CreateSelectionRuntime(world, globals);
            SeedLivePrimarySelection(world, globals, local, target);

            var system = new GasInputResponseSystem(world, globals);
            var requests = (InputRequestQueue)globals[CoreServiceKeys.AbilityInputRequestQueue.Name];
            var responses = (InputResponseBuffer)globals[CoreServiceKeys.InputResponseBuffer.Name];
            requests.TryEnqueue(new InputRequest { RequestId = 9, RequestTagId = 501 });

            SetConfirmSnapshot(globals, new Vector2(0f, 0f), pressedThisFrame: true, isDown: true);
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
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new ConstantScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
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

            SetConfirmSnapshot(globals, new Vector2(0f, 0f), pressedThisFrame: true, isDown: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(1, 1));
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
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new ConstantScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
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

            SetConfirmSnapshot(globals, new Vector2(0f, 0f), pressedThisFrame: true, isDown: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(1, 1));
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
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new ConstantScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
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

            SetConfirmSnapshot(globals, new Vector2(0f, 0f), pressedThisFrame: true, isDown: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(1, 1));
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
            var local = world.Create();
            var first = world.Create();
            var second = world.Create();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };
            SelectionRuntime selectionRuntime = CreateSelectionRuntime(world, globals);
            SeedLivePrimarySelection(world, globals, local, first, second);

            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 1001 : 0);
            mapping.SetSelectedContainerProvider((string setKey, out Entity container) =>
                selectionRuntime.TryCreateSnapshotLease(local, SelectionSetKeys.LivePrimary, SelectionSetKeys.CommandSnapshot, SelectionContainerKind.Snapshot, out _, out container));

            var orders = new List<Order>();
            mapping.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            input.InjectButtonPress("SkillQ");
            input.Update();
            mapping.Update(0f);

            That(orders.Count, Is.EqualTo(1));
            That(orders[0].Args.Selection.HasContainer, Is.True);
            Entity[] selected = new Entity[2];
            int copied = selectionRuntime.CopySelection(orders[0].Args.Selection.Container, selected);
            That(copied, Is.EqualTo(2));
            That(selected[0], Is.EqualTo(first));
            That(selected[1], Is.EqualTo(second));
        }

        [Test]
        public void InputOrderMapping_SelectionSnapshot_RemainsStable_AfterLiveSelectionChanges()
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
            var local = world.Create();
            var first = world.Create();
            var second = world.Create();
            var third = world.Create();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };
            SelectionRuntime selectionRuntime = CreateSelectionRuntime(world, globals);
            SeedLivePrimarySelection(world, globals, local, first, second);

            var mapping = new InputOrderMappingSystem(input, cfg);
            mapping.SetOrderTypeKeyResolver(key => key == "castAbility" ? 1001 : 0);
            mapping.SetSelectedContainerProvider((string setKey, out Entity container) =>
                selectionRuntime.TryCreateSnapshotLease(local, SelectionSetKeys.LivePrimary, SelectionSetKeys.CommandSnapshot, SelectionContainerKind.Snapshot, out _, out container));

            Order submitted = default;
            mapping.SetOrderSubmitHandler((in Order order) => submitted = order);

            input.InjectButtonPress("SkillQ");
            input.Update();
            mapping.Update(0f);

            SeedLivePrimarySelection(world, globals, local, third);

            That(submitted.Args.Selection.HasContainer, Is.True);
            Entity[] selected = new Entity[4];
            int copied = selectionRuntime.CopySelection(submitted.Args.Selection.Container, selected);
            That(copied, Is.EqualTo(2));
            That(selected[0], Is.EqualTo(first));
            That(selected[1], Is.EqualTo(second));
        }

        [Test]
        public void OrderSelectionLeaseCleanupSystem_ReclaimsSnapshotLease_WhenContainerLeavesAllOrders()
        {
            using var world = World.Create();
            var local = world.Create();
            var first = world.Create();
            var second = world.Create();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
            };
            SelectionRuntime selectionRuntime = CreateSelectionRuntime(world, globals);
            SeedLivePrimarySelection(world, globals, local, first, second);

            That(
                selectionRuntime.TryCreateSnapshotLease(
                    local,
                    SelectionSetKeys.LivePrimary,
                    SelectionSetKeys.CommandSnapshot,
                    SelectionContainerKind.Snapshot,
                    out Entity leaseOwner,
                    out Entity container),
                Is.True);

            var queue = new OrderQueue();
            var order = new Order
            {
                OrderTypeId = 1001,
                Actor = first,
                Args = new OrderArgs
                {
                    Selection = new OrderSelectionReference { Container = container }
                }
            };

            That(queue.TryEnqueue(in order), Is.True);

            var cleanup = new OrderSelectionLeaseCleanupSystem(world, queue);
            cleanup.Update(0f);
            That(world.IsAlive(leaseOwner), Is.True, "Lease owner should remain alive while an order references its snapshot container.");

            That(queue.TryDequeue(out _), Is.True);
            cleanup.Update(0f);
            That(world.IsAlive(leaseOwner), Is.False, "Lease owner should be reclaimed once no order references the snapshot container.");

            selectionRuntime.SweepDanglingState();
            That(world.IsAlive(container), Is.False, "Selection container should be removed after its lease owner is reclaimed and swept.");
        }

        [Test]
        public void InputOrderMapping_PositionCommand_FansOutAcrossLivePrimarySelection()
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
            mapping.SetSelectedEntityListProvider((string _, List<Entity> entities) =>
            {
                entities.Clear();
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
        public void InputOrderMapping_PositionMoveCommand_WithGroupFormation_AssignsOffsetTargetsAcrossLivePrimarySelection()
        {
            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var cfg = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                GroupMoveFormation = new GroupMoveFormationSettings
                {
                    Mode = GroupMoveFormationMode.Grid,
                    SpacingCm = 120
                },
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
            mapping.SetSelectedEntityListProvider((string _, List<Entity> entities) =>
            {
                entities.Clear();
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
            That(orders[0].Args.Spatial.WorldCm, Is.EqualTo(new Vector3(260f, 0f, 640f)));
            That(orders[1].Args.Spatial.WorldCm, Is.EqualTo(new Vector3(380f, 0f, 640f)));
        }

        [Test]
        public void InputOrderMapping_StopCommand_FansOutAcrossLivePrimarySelection()
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
            mapping.SetSelectedEntityListProvider((string _, List<Entity> entities) =>
            {
                entities.Clear();
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
        public void CurrentSelectionApplySystem_ClickAndScreenDrag_UpdateSelectionBuffer_SelectedTag_AndPrimaryEntity()
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
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.ScreenRayProvider.Name] = new WorldMappedScreenRayProvider(),
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.ScreenProjector.Name] = new WorldMappedScreenProjector(),
                [CoreServiceKeys.WorldSizeSpec.Name] = CreateWorldSizeSpec(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            var selectionRuntime = CreateSelectionRuntime(world, globals);

            var system = new CurrentSelectionApplySystem(world, globals);

            Click(system, globals, input, new Vector2(1600f, 1200f));

            AssertSelection(selectionRuntime, local, first);
            That(selectionRuntime.TryGetPrimary(local, SelectionSetKeys.LivePrimary, out var currentPrimary), Is.True);
            That(currentPrimary, Is.EqualTo(first));

            DragSelect(system, globals, input, new Vector2(1500f, 1100f), new Vector2(3500f, 2300f));

            AssertSelection(selectionRuntime, local, first, second, third);
            That(selectionRuntime.TryGetPrimary(local, SelectionSetKeys.LivePrimary, out currentPrimary), Is.True);
            That(currentPrimary, Is.EqualTo(first), "Primary selected entity should stay deterministic after box select.");
        }

        [Test]
        public void CurrentSelectionApplySystem_ClickEmptyGround_ClearsSelection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());

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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            SeedLivePrimarySelection(world, globals, local, first);

            var system = new CurrentSelectionApplySystem(world, globals);
            Click(system, globals, input, new Vector2(5200f, 4200f));

            That(selectionRuntime.GetSelectionCount(local, SelectionSetKeys.LivePrimary), Is.EqualTo(0));
            That(selectionRuntime.TryGetPrimary(local, SelectionSetKeys.LivePrimary, out _), Is.False);
        }

        [Test]
        public void CurrentSelectionApplySystem_CollectionOnlyAcquisition_DoesNotMutateFormalSelection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var selected = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            var acquired = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());

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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            selectionRuntime.Config.Acquisition.CommitToFormalSelection = false;
            SeedLivePrimarySelection(world, globals, local, selected);
            var collections = (EntityCollectionStore)globals[CoreServiceKeys.EntityCollectionStore.Name];
            var system = new CurrentSelectionApplySystem(world, globals);

            DragSelect(system, globals, input, new Vector2(2500f, 1500f), new Vector2(2700f, 1700f));

            AssertSelection(selectionRuntime, local, selected);
            That(collections.TryGet(local, EntityCollectionKeys.UiSelectionAcquisition, out EntityCollectionHandle handle), Is.True);
            That(collections.TryGetView(handle, out EntityCollectionView view), Is.True);
            That(view.SourceKind, Is.EqualTo(EntityCollectionSourceKind.UiAcquisition));
            That(view.Role, Is.EqualTo(EntityCollectionRoleKind.AcquisitionPreview));
            That(view.Count, Is.EqualTo(1));
            That(collections.TryGetEntityAt(handle, 0, out Entity row), Is.True);
            That(row, Is.EqualTo(acquired));
        }

        [Test]
        public void CurrentSelectionApplySystem_RuntimeDisabledEntity_IsNotSelectable()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            _ = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new SelectionSelectableTag(),
                SelectionSelectableState.Disabled);

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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var system = new CurrentSelectionApplySystem(world, globals);

            Click(system, globals, input, new Vector2(1600f, 1200f));

            That(selectionRuntime.GetSelectionCount(local, SelectionSetKeys.LivePrimary), Is.EqualTo(0));
            That(selectionRuntime.TryGetPrimary(local, SelectionSetKeys.LivePrimary, out _), Is.False);
        }

        [Test]
        public void CurrentSelectionApplySystem_TargetRelationFilter_GatesClickAcquireButKeepsHover()
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
                    new SelectionSelectableTag(),
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
                var selectionRuntime = CreateSelectionRuntime(world, globals, "Friendly");
                var system = new CurrentSelectionApplySystem(world, globals);

                Click(system, globals, input, new Vector2(1600f, 1200f));

                That(selectionRuntime.GetSelectionCount(local, SelectionSetKeys.LivePrimary), Is.EqualTo(0));
                That(globals.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out object? hovered), Is.True);
                That(hovered, Is.EqualTo(formation));

                world.Set(formation, new Team { Id = 1 });
                Click(system, globals, input, new Vector2(1600f, 1200f));

                AssertSelection(selectionRuntime, local, formation);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void CurrentSelectionApplySystem_TargetRelationFilter_FiltersBoxSelection()
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
                    new SelectionSelectableTag(),
                    new Team { Id = 1 });
                _ = world.Create(
                    WorldPositionCm.FromCm(2600, 1600),
                    new VisualTransform { Position = new Vector3(26f, 0f, 16f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new SelectionSelectableTag(),
                    new Team { Id = 2 });
                _ = world.Create(
                    WorldPositionCm.FromCm(3000, 1900),
                    new VisualTransform { Position = new Vector3(30f, 0f, 19f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new SelectionSelectableTag());
                var third = world.Create(
                    WorldPositionCm.FromCm(3400, 2200),
                    new VisualTransform { Position = new Vector3(34f, 0f, 22f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true },
                    new SelectionSelectableTag(),
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
                var selectionRuntime = CreateSelectionRuntime(world, globals, "Friendly");
                var system = new CurrentSelectionApplySystem(world, globals);

                DragSelect(system, globals, input, new Vector2(1500f, 1100f), new Vector2(3500f, 2300f));

                AssertSelection(selectionRuntime, local, first, third);
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void CurrentSelectionApplySystem_ClickUsesFootprintPolygonInsteadOfProjectedOriginOnly()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new SelectionSelectableTag(),
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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var system = new CurrentSelectionApplySystem(world, globals);

            Click(system, globals, input, new Vector2(1700f, 1200f));

            AssertSelection(selectionRuntime, local, entity);
        }

        [Test]
        public void CurrentSelectionApplySystem_DragSelectUsesFootprintIntersection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(3000, 1500),
                new VisualTransform { Position = new Vector3(30f, 0f, 15f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new SelectionSelectableTag(),
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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var system = new CurrentSelectionApplySystem(world, globals);

            DragSelect(system, globals, input, new Vector2(2500f, 1300f), new Vector2(2850f, 1700f));

            AssertSelection(selectionRuntime, local, entity);
        }

        [Test]
        public void CurrentSelectionApplySystem_Box3DProjectedBoundsAreSelectable()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new SelectionSelectableTag(),
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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var system = new CurrentSelectionApplySystem(world, globals);

            Click(system, globals, input, new Vector2(1700f, 1200f));

            AssertSelection(selectionRuntime, local, entity);
        }

        [Test]
        public void CurrentSelectionApplySystem_ClickWithoutGroundPoint_StillSelectsHoveredEntity()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(1600, 1200),
                new VisualTransform { Position = new Vector3(16f, 0f, 12f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new SelectionSelectableTag(),
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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var system = new CurrentSelectionApplySystem(world, globals);

            SetConfirmSnapshot(globals, new Vector2(1700f, 1200f), pressedThisFrame: true, isDown: true);
            input.InjectAction("PointerPos", new Vector3(1700f, 1200f, 0f));
            input.Update();
            system.Update(0f);

            SetConfirmSnapshot(globals, new Vector2(1700f, 1200f), pressedThisFrame: false, isDown: false, releasedThisFrame: true);
            input.InjectAction("PointerPos", new Vector3(1700f, 1200f, 0f));
            input.Update();
            system.Update(0f);

            AssertSelection(selectionRuntime, local, entity);
        }

        [Test]
        public void CurrentSelectionApplySystem_AdditiveModifierAddsWithoutReplacing()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            var second = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());

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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            var system = new CurrentSelectionApplySystem(world, globals);

            Click(system, globals, input, new Vector2(1600f, 1200f));
            input.InjectAction(SelectionModifierActionIds.Additive, Vector3.One);
            Click(system, globals, input, new Vector2(2600f, 1600f));

            AssertSelection(selectionRuntime, local, first, second);
        }

        [Test]
        public void CurrentSelectionApplySystem_ToggleModifierRemovesExistingSelectionMember()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var first = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            var second = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());

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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            SeedLivePrimarySelection(world, globals, local, first, second);
            var system = new CurrentSelectionApplySystem(world, globals);

            input.InjectAction(SelectionModifierActionIds.Toggle, Vector3.One);
            Click(system, globals, input, new Vector2(1600f, 1200f));

            AssertSelection(selectionRuntime, local, second);
        }

        [Test]
        public void CurrentSelectionApplySystem_AimConfirmRelease_DoesNotStealSelection()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var local = world.Create();
            var actor = world.Create(WorldPositionCm.FromCm(1600, 1200), new VisualTransform { Position = new Vector3(16f, 0f, 12f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());
            var enemy = world.Create(WorldPositionCm.FromCm(2600, 1600), new VisualTransform { Position = new Vector3(26f, 0f, 16f) }, new CullState { IsVisible = true }, new SelectionSelectableTag());

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
            var selectionRuntime = CreateSelectionRuntime(world, globals);
            SeedLivePrimarySelection(world, globals, local, actor);

            var selectionSystem = new CurrentSelectionApplySystem(world, globals);
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
            mapping.SetSelectedEntityProvider((string _, out Entity entity) =>
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
            mapping.Update(0f);
            That(mapping.IsAiming, Is.True);

            SetConfirmSnapshot(globals, new Vector2(2600f, 1600f), pressedThisFrame: true, isDown: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(2600, 1600));
            input.InjectAction("PointerPos", new Vector3(2600f, 1600f, 0f));
            input.InjectButtonPress("Select");
            input.Update();
            selectionSystem.Update(0f);
            mapping.Update(0f);

            SetConfirmSnapshot(globals, new Vector2(2600f, 1600f), pressedThisFrame: false, isDown: false, releasedThisFrame: true);
            SetAuthoritativeGroundPoint(input, new WorldCmInt2(2600, 1600));
            input.InjectAction("PointerPos", new Vector3(2600f, 1600f, 0f));
            input.Update();
            selectionSystem.Update(0f);
            mapping.Update(0f);

            AssertSelection(selectionRuntime, local, actor);
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
                    new() { Id = SelectionModifierActionIds.Additive, Name = SelectionModifierActionIds.Additive, Type = InputActionType.Button },
                    new() { Id = SelectionModifierActionIds.Toggle, Name = SelectionModifierActionIds.Toggle, Type = InputActionType.Button },
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

        private static void Click(CurrentSelectionApplySystem system, Dictionary<string, object> globals, PlayerInputHandler input, Vector2 pointer)
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

        private static void DragSelect(CurrentSelectionApplySystem system, Dictionary<string, object> globals, PlayerInputHandler input, Vector2 from, Vector2 to)
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

        private static void AssertSelection(SelectionRuntime selectionRuntime, Entity owner, params Entity[] expected)
        {
            That(selectionRuntime.GetSelectionCount(owner, SelectionSetKeys.LivePrimary), Is.EqualTo(expected.Length));
            Entity[] actual = new Entity[expected.Length];
            int written = selectionRuntime.CopySelection(owner, SelectionSetKeys.LivePrimary, actual);
            That(written, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                That(actual[i], Is.EqualTo(expected[i]));
            }
        }

        private static SelectionRuntime CreateSelectionRuntime(World world, Dictionary<string, object> globals, string relationFilter = "All")
        {
            var config = new SelectionRuntimeConfig
            {
                TargetFilter = new SelectionTargetFilterConfig { RelationFilter = relationFilter },
                Acquisition = new SelectionAcquisitionConfig { CommitToFormalSelection = true },
            };
            var registry = new Ludots.Core.Registry.StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var runtime = new SelectionRuntime(world, config, registry);
            var collectionRegistry = new Ludots.Core.Registry.StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var collections = new EntityCollectionStore(collectionRegistry);
            globals[CoreServiceKeys.SelectionRuntime.Name] = runtime;
            globals[CoreServiceKeys.SelectionConfig.Name] = config;
            globals[CoreServiceKeys.SelectionSetKeyRegistry.Name] = registry;
            globals[CoreServiceKeys.EntityCollectionStore.Name] = collections;
            globals[CoreServiceKeys.EntityCollectionKeyRegistry.Name] = collectionRegistry;
            return runtime;
        }

        private static void SeedLivePrimarySelection(World world, Dictionary<string, object> globals, Entity owner, params Entity[] targets)
        {
            var runtime = globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out object? runtimeObj) &&
                          runtimeObj is SelectionRuntime selection
                ? selection
                : throw new InvalidOperationException("SelectionRuntime missing from globals.");
            runtime.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, targets);
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
