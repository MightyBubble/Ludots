using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Input.Orders
{
    public static class InputOrderActorAuthorization
    {
        public static bool IsAuthorized(
            World world,
            PlayerEntityLookup players,
            ControlDomainQuery controlDomains,
            Entity actor,
            int playerId)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (players == null) throw new ArgumentNullException(nameof(players));
            if (controlDomains == null) throw new ArgumentNullException(nameof(controlDomains));

            if (actor == Entity.Null || playerId <= 0 || !world.IsAlive(actor) ||
                !players.TryGet(playerId, out Entity controllerRep) ||
                !world.IsAlive(controllerRep))
            {
                return false;
            }

            return actor == controllerRep || controlDomains.IsControllableBy(controllerRep, actor);
        }
    }
}
