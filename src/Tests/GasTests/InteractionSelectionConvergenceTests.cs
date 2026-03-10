using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
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
                [CoreServiceKeys.SelectionRequestQueue.Name] = new SelectionRequestQueue(),
                [CoreServiceKeys.SelectionResponseBuffer.Name] = new SelectionResponseBuffer(),
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

            var origin = world.Create(new Team { Id = 1 });
            var targetContext = world.Create();
            var enemy = world.Create(WorldPositionCm.FromCm(50, 0), new Team { Id = 2 });
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
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = input,
                [CoreServiceKeys.AuthoritativeInput.Name] = input,
                [CoreServiceKeys.AbilityInputRequestQueue.Name] = new InputRequestQueue(),
                [CoreServiceKeys.InputResponseBuffer.Name] = new InputResponseBuffer(),
                [CoreServiceKeys.SelectedEntity.Name] = target,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

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

        private static InputConfigRoot CreateInputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "SkillQ", Name = "SkillQ", Type = InputActionType.Button },
                    new() { Id = "Confirm", Name = "Confirm", Type = InputActionType.Button },
                    new() { Id = "Select", Name = "Select", Type = InputActionType.Button },
                    new() { Id = "PointerPos", Name = "PointerPos", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new() { Id = "Test", Name = "Test", Priority = 1 },
                },
            };
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

        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity _priority;

            public StubSpatialQueryService(Entity priority)
            {
                _priority = priority;
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
                if (buffer.Length == 0)
                {
                    return new SpatialQueryResult(0, 1);
                }

                buffer[0] = _priority;
                return new SpatialQueryResult(1, 0);
            }
        }
    }
}

