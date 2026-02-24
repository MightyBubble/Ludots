using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public static class GasConditionEvaluator
    {
        public static bool ShouldExpire(World world, Entity target, in GasCondition condition, TagOps tagOps)
        {
            if (!world.IsAlive(target)) return true;
            ref var tags = ref world.TryGetRef<GameplayTagContainer>(target, out bool hasTags);
            if (!hasTags) return condition.Kind != GasConditionKind.TagPresent;

            bool present = tagOps.HasTag(ref tags, condition.TagId, condition.TagSense);
            bool satisfied = condition.Kind switch
            {
                GasConditionKind.TagPresent => present,
                GasConditionKind.TagAbsent => !present,
                _ => true
            };

            return !satisfied;
        }
    }
}
