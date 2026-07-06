using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// WASD-style axis intent → throttled move order kernel (RFC-0065 INT-6, DEC-15). Samples the
    /// configured Axis2D action from the authoritative input snapshot
    /// (<c>CoreServiceKeys.AuthoritativeInput</c>, fed by <c>PlayerInputHandler</c> through the
    /// accumulator) each simulation tick; while the axis is non-zero it submits one order per
    /// <see cref="AxisMoveConfig.ThrottleTicks"/> ticks to the <see cref="OrderQueue"/> targeting
    /// <c>current position + direction × stepDistanceCm</c>. Movement always goes through the order
    /// pipeline — this system never writes <see cref="WorldPositionCm"/> (iron law 1).
    /// <para>
    /// Actor selection (minimal surface): the resolved local player entity itself, when it carries
    /// <see cref="WorldPositionCm"/> (ARPG avatar). RTS multi-selection actors are wired later by
    /// cast dispatch — the control-plane anchor is the same local player rep either way.
    /// Disabled config (<c>enabled=false</c>, the shipped default) means zero work per tick.
    /// </para>
    /// </summary>
    public sealed class AxisMoveOrderSystem : ISystem<float>
    {
        private const float AxisDeadzoneSquared = 0.000001f;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly AxisMoveConfig _config;
        private readonly OrderQueue _orderQueue;
        private readonly int _orderTypeId;
        private int _cooldownTicks;

        public AxisMoveOrderSystem(
            World world,
            Dictionary<string, object> globals,
            AxisMoveConfig config,
            OrderQueue orderQueue,
            OrderTypeRegistry orderTypeRegistry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _orderQueue = orderQueue ?? throw new ArgumentNullException(nameof(orderQueue));
            if (orderTypeRegistry == null)
            {
                throw new ArgumentNullException(nameof(orderTypeRegistry));
            }

            AxisMoveConfigLoader.Validate(config, nameof(AxisMoveConfig));
            // Resolve the order type up front so a bad key fails at wiring, not on first key press.
            _orderTypeId = config.Enabled ? orderTypeRegistry.GetId(config.OrderTypeKey) : 0;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!_config.Enabled)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object inputObj) ||
                inputObj is not IInputActionReader input)
            {
                return;
            }

            Vector2 axis = input.ReadAction<Vector2>(_config.ActionId);
            if (axis.LengthSquared() <= AxisDeadzoneSquared)
            {
                // Released axis re-arms the throttle so a fresh press submits immediately.
                _cooldownTicks = 0;
                return;
            }

            if (_cooldownTicks > 0)
            {
                _cooldownTicks--;
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerId.Name, out object playerIdObj) ||
                playerIdObj is not int playerId ||
                playerId <= 0 ||
                !_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object localObj) ||
                localObj is not Entity local ||
                !_world.IsAlive(local) ||
                !_world.Has<WorldPositionCm>(local))
            {
                return;
            }

            Vector2 current = _world.Get<WorldPositionCm>(local).Value.ToVector2();
            Vector2 direction = Vector2.Normalize(axis);
            Vector2 target = current + (direction * _config.StepDistanceCm);

            var order = new Order
            {
                OrderTypeId = _orderTypeId,
                PlayerId = playerId,
                Actor = local,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = new Vector3(target.X, target.Y, 0f);

            if (_orderQueue.TryEnqueue(in order))
            {
                _cooldownTicks = _config.ThrottleTicks - 1;
            }
        }
    }
}
