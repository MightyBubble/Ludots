using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;

namespace RtsDemoMod.Systems
{
    public sealed class RtsLocalOrderSourceSystem : ISystem<float>
    {
        private const int MoveAbilitySlotIndex = 0;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly LocalOrderSourceHelper _helper;
        private readonly IModContext _ctx;
        private readonly List<Entity> _selectedActors = new(64);
        private InputOrderMappingSystem? _mapping;
        private bool _initialized;

        public RtsLocalOrderSourceSystem(World world, Dictionary<string, object> globals, OrderQueue orders, IModContext ctx)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            _ctx = ctx;
            _helper = new LocalOrderSourceHelper(world, globals, orders);
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            EnsureInitialized();
            if (_mapping == null)
            {
                return;
            }

            if (!SelectionRuntime.TryGetController(_world, _globals, out var controller))
            {
                return;
            }

            _mapping.SetLocalPlayer(controller, 1);
            _mapping.Update(dt);
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _mapping = _helper.TryCreateMapping(_ctx);
            if (_mapping == null)
            {
                return;
            }

            _mapping.ConfirmActionId = "Select";
            _mapping.CancelActionId = "Cancel";
            _mapping.SetOrderSubmitHandler(SubmitOrder);
        }

        private void SubmitOrder(in Order order)
        {
            if (SelectionRuntime.CollectSelected(_world, _globals, _selectedActors) <= 0)
            {
                if (_world.IsAlive(order.Actor))
                {
                    _orders.TryEnqueue(order);
                }
                return;
            }

            var selectionState = ResolveSelectionState();
            if (order.OrderTagId == _helper.StopOrderTagId)
            {
                for (int i = 0; i < _selectedActors.Count; i++)
                {
                    var actor = _selectedActors[i];
                    if (!_world.IsAlive(actor) || !_world.Has<OrderBuffer>(actor))
                    {
                        continue;
                    }

                    var perActor = order;
                    perActor.Actor = actor;
                    perActor.Target = default;
                    perActor.TargetContext = default;
                    perActor.Args.Entities = default;
                    _orders.TryEnqueue(perActor);
                }
                return;
            }

            bool isMoveOrder = order.OrderTagId == _helper.CastAbilityOrderTagId
                && order.Args.I0 == MoveAbilitySlotIndex
                && order.Args.Spatial.Kind == OrderSpatialKind.WorldCm;

            if (!isMoveOrder)
            {
                var primary = _selectedActors[0];
                if (_world.IsAlive(primary))
                {
                    var primaryOrder = order;
                    primaryOrder.Actor = primary;
                    primaryOrder.Args.Entities = default;
                    _orders.TryEnqueue(primaryOrder);
                }
                return;
            }

            var targetCm = new WorldCmInt2((int)order.Args.Spatial.WorldCm.X, (int)order.Args.Spatial.WorldCm.Z);
            selectionState.HasLastCommand = true;
            selectionState.LastCommandPointCm = targetCm;
            var assignments = RtsFormationPlanner.Plan(_world, _selectedActors, targetCm);
            for (int i = 0; i < assignments.Count; i++)
            {
                var assignment = assignments[i];
                if (!_world.IsAlive(assignment.Actor) || !_world.Has<OrderBuffer>(assignment.Actor))
                {
                    continue;
                }

                var perActor = order;
                perActor.Actor = assignment.Actor;
                perActor.Target = default;
                perActor.TargetContext = default;
                perActor.Args.Entities = default;
                perActor.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
                perActor.Args.Spatial.Mode = OrderCollectionMode.Single;
                perActor.Args.Spatial.WorldCm = new Vector3(assignment.TargetCm.X.ToFloat(), 0f, assignment.TargetCm.Y.ToFloat());
                _orders.TryEnqueue(perActor);
            }
        }

        private RtsSelectionState ResolveSelectionState()
        {
            if (_globals.TryGetValue(RtsDemoKeys.SelectionState, out var obj) && obj is RtsSelectionState state)
            {
                return state;
            }

            state = new RtsSelectionState();
            _globals[RtsDemoKeys.SelectionState] = state;
            return state;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}



