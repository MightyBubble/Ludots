using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Client;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// WASD-style axis intent to throttled move order kernel (RFC-0065 INT-6, DEC-15). The single
    /// source of truth for enablement and parameters is the active control scheme's
    /// <c>axisMove</c> declaration (<see cref="ControlSchemeRuntime.TryGetActiveAxisMove"/>). A
    /// scheme without the declaration means zero work per tick, and a hot switch takes effect on the
    /// next tick. While declared, the system samples the declared Axis2D action from the authoritative
    /// input snapshot and submits throttled <see cref="OrderQueue"/> move orders targeting
    /// <c>current position + direction * stepDistanceCm</c>. Movement always goes through the order
    /// pipeline; this system never writes <see cref="WorldPositionCm"/>.
    ///
    /// Multi-seat tables route per seat instead: each seat's channel carries its own scheme
    /// declaration and frozen axis snapshot, and orders submit with that seat's possessed rep —
    /// two seats' axis inputs drive their own reps without merging. The sole-seat path below is
    /// the global consumption chain and stays byte-identical for single-seat clients.
    /// </summary>
    public sealed class AxisMoveOrderSystem : ISystem<float>
    {
        private const float AxisDeadzoneSquared = 0.000001f;

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly ControlSchemeRuntime _schemes;
        private readonly OrderQueue _orderQueue;
        private readonly Dictionary<string, int> _throttleTicksBySeat = new(StringComparer.Ordinal);

        private uint _cachedSchemeRevision;
        private bool _hasBinding;
        private ControlSchemeAxisMoveBinding _binding;
        private int _throttleTicksRemaining;

        public AxisMoveOrderSystem(
            World world,
            Dictionary<string, object> globals,
            ControlSchemeRuntime schemes,
            OrderQueue orderQueue)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _schemes = schemes ?? throw new ArgumentNullException(nameof(schemes));
            _orderQueue = orderQueue ?? throw new ArgumentNullException(nameof(orderQueue));
            _cachedSchemeRevision = uint.MaxValue;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) &&
                seatsObj is ClientLocalSeatRegistry seats &&
                seats.Count > 1)
            {
                UpdatePerSeat(seats);
                return;
            }

            UpdateSoleSeat();
        }

        private void UpdatePerSeat(ClientLocalSeatRegistry seats)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatInputRuntime.Name, out object? runtimeObj) ||
                runtimeObj is not ClientLocalSeatInputRuntime seatInput)
            {
                return;
            }

            IReadOnlyList<string> ids = seats.SeatIds;
            for (int i = 0; i < ids.Count; i++)
            {
                ClientLocalSeat seat = seats.Require(ids[i]);
                if (!seat.HasPossession ||
                    !_world.IsAlive(seat.PossessedRep) ||
                    !_world.Has<WorldPositionCm>(seat.PossessedRep) ||
                    !seatInput.TryGetChannel(seat.SeatId, out ClientLocalSeatInputChannel channel) ||
                    !channel.TryGetActiveAxisMove(out ControlSchemeAxisMoveBinding binding))
                {
                    continue;
                }

                Vector2 axis = channel.Reader.ReadAction<Vector2>(binding.ActionId);
                if (axis.LengthSquared() <= AxisDeadzoneSquared)
                {
                    _throttleTicksBySeat[seat.SeatId] = 0;
                    continue;
                }

                if (_throttleTicksBySeat.TryGetValue(seat.SeatId, out int remaining) && remaining > 0)
                {
                    _throttleTicksBySeat[seat.SeatId] = remaining - 1;
                    continue;
                }

                Entity actor = seat.PossessedRep;
                Vector2 current = _world.Get<WorldPositionCm>(actor).Value.ToVector2();
                Vector2 direction = Vector2.Normalize(axis);
                Vector2 target = current + (direction * binding.StepDistanceCm);

                var order = new Order
                {
                    OrderTypeId = binding.OrderTypeId,
                    PlayerId = seat.PossessedPlayerId,
                    Actor = actor,
                    SubmitMode = OrderSubmitMode.Immediate,
                };
                order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
                order.Args.Spatial.Mode = OrderCollectionMode.Single;
                order.Args.Spatial.WorldCm = new Vector3(target.X, target.Y, 0f);

                if (_orderQueue.TryEnqueue(in order))
                {
                    _throttleTicksBySeat[seat.SeatId] = binding.ThrottleTicks - 1;
                }
            }
        }

        private void UpdateSoleSeat()
        {
            if (_cachedSchemeRevision != _schemes.Revision)
            {
                _cachedSchemeRevision = _schemes.Revision;
                _hasBinding = _schemes.TryGetActiveAxisMove(out _binding);
                _throttleTicksRemaining = 0;
            }

            if (!_hasBinding)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object inputObj) ||
                inputObj is not IInputActionReader input)
            {
                throw new InvalidOperationException(
                    $"AxisMoveOrderSystem has an active axis move declaration but the '{CoreServiceKeys.AuthoritativeInput.Name}' " +
                    "service is missing; register the authoritative input snapshot before the InputCollection group ticks.");
            }

            Vector2 axis = input.ReadAction<Vector2>(_binding.ActionId);
            if (axis.LengthSquared() <= AxisDeadzoneSquared)
            {
                _throttleTicksRemaining = 0;
                return;
            }

            if (_throttleTicksRemaining > 0)
            {
                _throttleTicksRemaining--;
                return;
            }

            ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(_globals);
            if (!seats.TryGetSoleSeat(out ClientLocalSeat soleSeat) ||
                !soleSeat.HasPossession ||
                !_world.IsAlive(soleSeat.PossessedRep) ||
                !_world.Has<WorldPositionCm>(soleSeat.PossessedRep))
            {
                return;
            }

            Entity local = soleSeat.PossessedRep;
            int playerId = soleSeat.PossessedPlayerId;

            Vector2 current = _world.Get<WorldPositionCm>(local).Value.ToVector2();
            Vector2 direction = Vector2.Normalize(axis);
            Vector2 target = current + (direction * _binding.StepDistanceCm);

            var order = new Order
            {
                OrderTypeId = _binding.OrderTypeId,
                PlayerId = playerId,
                Actor = local,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = new Vector3(target.X, target.Y, 0f);

            if (_orderQueue.TryEnqueue(in order))
            {
                _throttleTicksRemaining = _binding.ThrottleTicks - 1;
            }
        }
    }
}
