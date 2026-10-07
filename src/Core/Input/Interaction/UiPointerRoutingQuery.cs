using System;
using Arch.Core;
using Ludots.Core.Client;

namespace Ludots.Core.Input.Interaction
{

    /// <summary>
    /// Read-only projection answering one host question: "does this seat's input context
    /// route the device pointer to UI right now?" The answer reads entity state (active
    /// interaction context chain on the seat's rep) plus the static profile definitions —
    /// never view state. This is the structural replacement path for the retired-in-waiting
    /// UiCaptured service flag: input routing is decided by which context is active, and a
    /// UI context claims the pointer by declaring <c>pointerRouting: "ui"</c> on its
    /// profile. The newest active context that declares routing wins; contexts that
    /// declare nothing defer to their ancestors, mirroring the LIFO arbitration the order
    /// drain uses for activeCollectionKey.
    /// </summary>
    public static class UiPointerRoutingQuery
    {
        public static bool RoutesPointerToUi(
            World world,
            ClientLocalSeatRegistry seats,
            InteractionContextProfileRegistry profiles,
            string seatId)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(seats);
            ArgumentNullException.ThrowIfNull(profiles);
            if (string.IsNullOrWhiteSpace(seatId))
            {
                return false;
            }

            ClientLocalSeat seat = seats.Require(seatId);
            if (!seat.HasPossession || !world.IsAlive(seat.PossessedRep))
            {
                return false;
            }

            Entity rep = seat.PossessedRep;
            Span<int> chain = stackalloc int[InteractionContextInstances.Capacity + 1];
            int count = InteractionContextInstanceRuntime.CopyActiveContextIdsNewestFirst(world, rep, chain);
            for (int i = 0; i < count; i++)
            {
                if (TryResolveRouting(profiles, chain[i], out bool routesToUi))
                {
                    return routesToUi;
                }
            }

            return false;
        }

        private static bool TryResolveRouting(InteractionContextProfileRegistry profiles, int contextId, out bool routesToUi)
        {
            routesToUi = false;
            if (!profiles.TryGetDefinition(contextId, out InteractionContextProfileDefinition definition))
            {
                return false;
            }

            string routing = definition.PointerRouting ?? string.Empty;
            if (string.IsNullOrWhiteSpace(routing))
            {
                return false;
            }

            routesToUi = string.Equals(routing.Trim(), "ui", StringComparison.Ordinal);
            return true;
        }
    }
}
