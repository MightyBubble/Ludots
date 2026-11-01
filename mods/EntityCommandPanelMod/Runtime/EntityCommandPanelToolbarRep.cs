using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace EntityCommandPanelMod.Runtime
{
    internal static class EntityCommandPanelToolbarRep
    {
        public static Entity Require(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException("Toolbar rep requires ClientLocalSeatRegistry.");
            Entity found = Entity.Null;
            var seatIdsPossessing = new List<string>();
            IReadOnlyList<string> seatIds = seats.SeatIds;
            for (int i = 0; i < seatIds.Count; i++)
            {
                ClientLocalSeat seat = seats.Require(seatIds[i]);
                if (!seat.HasPossession)
                {
                    continue;
                }

                found = seat.PossessedRep;
                seatIdsPossessing.Add(seat.SeatId);
            }

            if (seatIdsPossessing.Count == 1)
            {
                if (!engine.World.IsAlive(found))
                {
                    throw new InvalidOperationException($"Toolbar rep on seat '{seatIdsPossessing[0]}' is not alive.");
                }

                return found;
            }

            if (seatIdsPossessing.Count == 0)
            {
                throw new InvalidOperationException("Toolbar rep requires one possessed local seat.");
            }

            throw new InvalidOperationException(
                $"Toolbar rep is ambiguous across seats {string.Join(", ", seatIdsPossessing)}.");
        }
    }
}
