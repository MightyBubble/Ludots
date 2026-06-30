using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Systems
{
    public sealed class RtsProducerRallyOrderSystem : ISystem<float>
    {
        public const string SuppressDefaultCommandKey = "RtsDemoMod.SuppressDefaultCommand";

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly OrderQueue _orders;
        private readonly OrderTypeRegistry _orderTypes;
        private int _setRallyPointOrderTypeId;

        public RtsProducerRallyOrderSystem(
            World world,
            Dictionary<string, object> globals,
            OrderQueue orders,
            OrderTypeRegistry orderTypes)
        {
            _world = world;
            _globals = globals;
            _orders = orders;
            _orderTypes = orderTypes ?? throw new ArgumentNullException(nameof(orderTypes));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            _globals[SuppressDefaultCommandKey] = false;
            if (!_orderTypes.TryGetId("setRallyPoint", out _setRallyPointOrderTypeId) || _setRallyPointOrderTypeId <= 0)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return;
            }

            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(_globals, nameof(RtsProducerRallyOrderSystem));
            if (!input.PressedThisFrame(bindings.CommandActionId))
            {
                return;
            }

            if (!SelectionContextRuntime.TryGetCurrentPrimary(_world, _globals, out Entity selected) ||
                !IsProducer(selected))
            {
                return;
            }

            if (!AuthoritativeGroundPointerHelper.TryRead(input, out WorldCmInt2 groundPoint))
            {
                return;
            }

            var order = new Order
            {
                OrderTypeId = _setRallyPointOrderTypeId,
                PlayerId = ResolvePlayerId(),
                Actor = selected,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(groundPoint.X, 0f, groundPoint.Y),
                    },
                },
            };

            if (_orders.TryEnqueue(in order))
            {
                _globals[SuppressDefaultCommandKey] = true;
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool IsProducer(Entity entity)
        {
            if (!_world.IsAlive(entity) || !_world.Has<AbilityStateBuffer>(entity))
            {
                return false;
            }

            ref AbilityStateBuffer abilities = ref _world.Get<AbilityStateBuffer>(entity);
            if (abilities.Count <= 2)
            {
                return false;
            }

            int abilityId = abilities.Get(2).AbilityId;
            if (abilityId <= 0)
            {
                return false;
            }

            string abilityKey = AbilityIdRegistry.GetName(abilityId);
            return abilityKey.Contains(".Train", StringComparison.Ordinal);
        }

        private int ResolvePlayerId()
        {
            return _globals.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object? value) &&
                   value is int playerId &&
                   playerId > 0
                ? playerId
                : 1;
        }
    }
}
