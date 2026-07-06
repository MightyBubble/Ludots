using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map;

namespace Ludots.Core.Gameplay.Spawning
{
    public static class RuntimeEntityMapOwnershipSupport
    {
        public static void TryCopyMapEntityFromSource(World world, Entity source, Entity entity)
        {
            if (!world.IsAlive(source) || !world.IsAlive(entity) || !world.Has<MapEntity>(source))
            {
                return;
            }

            var mapEntity = world.Get<MapEntity>(source);
            if (world.Has<MapEntity>(entity))
            {
                world.Set(entity, mapEntity);
            }
            else
            {
                world.Add(entity, mapEntity);
            }
        }
    }
}
