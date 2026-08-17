using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Systems
{
    /// <summary>
    /// Keeps each seat's possessed rep aligned with <see cref="PlayerEntityLookup"/>.
    /// Replaces LocalPlayerEntityResolverSystem (Epic #896) — no explicit-binding bypass.
    /// </summary>
    public sealed class SeatPossessionSyncSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;

        public SeatPossessionSyncSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out object? seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats ||
                seats.Count == 0)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.PlayerEntityLookup.Name, out object? lookupObj) ||
                lookupObj is not PlayerEntityLookup lookup)
            {
                return;
            }

            IReadOnlyList<string> ids = seats.SeatIds;
            for (int i = 0; i < ids.Count; i++)
            {
                ClientLocalSeat seat = seats.Require(ids[i]);
                if (seat.PossessedPlayerId <= 0)
                {
                    continue;
                }

                if (!lookup.TryGet(seat.PossessedPlayerId, out Entity rep) || !_world.IsAlive(rep))
                {
                    throw new System.InvalidOperationException(
                        $"Client local seat '{seat.SeatId}' possesses playerId {seat.PossessedPlayerId} but no live participant rep is bound.");
                }

                if (seat.PossessedRep != rep)
                {
                    seats.SetPossession(seat.SeatId, seat.PossessedPlayerId, rep);
                }
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
