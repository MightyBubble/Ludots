using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems;

internal static class AbilityActivationBlockTagEvaluator
{
    public static bool Passes(
        World world,
        Entity actor,
        TagOps tagOps,
        in AbilityActivationBlockTags blockTags)
        => Evaluate(world, actor, tagOps, in blockTags) == AbilityActivationRefusalReason.None;

    public static AbilityActivationRefusalReason Evaluate(
        World world,
        Entity actor,
        TagOps tagOps,
        in AbilityActivationBlockTags blockTags)
    {
        ref GameplayTagContainer actorTags = ref world.TryGetRef<GameplayTagContainer>(actor, out bool hasActorTags);
        if (!hasActorTags)
        {
            return blockTags.RequiredAll.IsEmpty
                ? AbilityActivationRefusalReason.None
                : AbilityActivationRefusalReason.RequiredActivationTagMissing;
        }

        if (!blockTags.RequiredAll.IsEmpty &&
            !tagOps.ContainsAll(ref actorTags, in blockTags.RequiredAll, TagSense.Effective))
        {
            return AbilityActivationRefusalReason.RequiredActivationTagMissing;
        }

        if (!blockTags.BlockedAny.IsEmpty &&
            tagOps.Intersects(ref actorTags, in blockTags.BlockedAny, TagSense.Effective))
        {
            return AbilityActivationRefusalReason.BlockedActivationTagPresent;
        }

        return AbilityActivationRefusalReason.None;
    }
}
