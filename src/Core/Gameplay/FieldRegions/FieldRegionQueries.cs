using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Fields;
using Ludots.Core.Map;

namespace Ludots.Core.Gameplay.FieldRegions
{
    public static class FieldRegionQueries
    {
        public static bool TryIsInFieldRegion(
            World world,
            MapSession session,
            Entity entity,
            string layerKey,
            string regionKey,
            out bool isIn)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (session == null) throw new ArgumentNullException(nameof(session));

            isIn = false;
            FieldSessionStore fields = session.Fields
                ?? throw new InvalidOperationException($"Map '{session.MapId.Value}' has no active field session.");
            if (string.IsNullOrWhiteSpace(layerKey) ||
                string.IsNullOrWhiteSpace(regionKey) ||
                !fields.TryGetByKey(layerKey, out FieldLayerData layerData) ||
                layerData is not DiscreteIdFieldLayerData layer)
            {
                return false;
            }

            int regionId = layer.Regions.GetId(regionKey);
            if (regionId <= 0 ||
                !world.IsAlive(entity) ||
                !world.Has<RegionMembershipCm>(entity))
            {
                return false;
            }

            RegionMembershipCm membership = world.Get<RegionMembershipCm>(entity);
            if (membership.Initialized == 0 || membership.LayerId != layer.LayerId.Value)
            {
                return false;
            }

            isIn = membership.RegionId == regionId;
            return true;
        }
    }
}
