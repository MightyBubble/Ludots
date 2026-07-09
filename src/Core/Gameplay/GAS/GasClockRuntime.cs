using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public static class GasClockRuntime
    {
        public static int Now(World world, IClock clock, GasClockId clockId, Entity localClockEntity, string context)
        {
            if (clockId != GasClockId.EntityLocal)
            {
                return clock.Now(clockId.ToDomainId());
            }

            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsAlive(localClockEntity))
            {
                throw new InvalidOperationException($"{context} requires an alive EntityLocal clock entity.");
            }

            if (!world.Has<EntityLocalClock>(localClockEntity))
            {
                throw new InvalidOperationException($"{context} requires EntityLocalClock on the clock entity.");
            }

            return world.Get<EntityLocalClock>(localClockEntity).LocalStep;
        }
    }
}
