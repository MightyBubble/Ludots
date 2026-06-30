using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Input.Orders
{
    public static class ActorOrderRoutingMatcher
    {
        public static bool TryResolveCandidate(
            World world,
            TagOps tagOps,
            Entity actor,
            IReadOnlyList<ActorOrderRoutingCandidate> candidates,
            out ActorOrderRoutingCandidate matchedCandidate)
        {
            matchedCandidate = null!;
            if (!world.IsAlive(actor) || candidates == null || candidates.Count == 0)
            {
                return false;
            }

            ActorOrderRoutingCandidate? best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                ActorOrderRoutingCandidate candidate = candidates[i];
                if (!TryMatch(world, tagOps, actor, candidate.Match))
                {
                    continue;
                }

                if (best == null || candidate.Priority > best.Priority)
                {
                    best = candidate;
                }
            }

            if (best == null || string.IsNullOrWhiteSpace(best.OrderTypeKey))
            {
                return false;
            }

            matchedCandidate = best;
            return true;
        }

        public static bool TryResolveOrderTypeKey(
            World world,
            TagOps tagOps,
            Entity actor,
            IReadOnlyList<ActorOrderRoutingCandidate> candidates,
            out string orderTypeKey)
        {
            orderTypeKey = string.Empty;
            if (!TryResolveCandidate(world, tagOps, actor, candidates, out ActorOrderRoutingCandidate matchedCandidate))
            {
                return false;
            }

            orderTypeKey = matchedCandidate.OrderTypeKey;
            return true;
        }

        public static bool TryMatch(
            World world,
            TagOps tagOps,
            Entity actor,
            ActorOrderRoutingMatch match)
        {
            if (!world.IsAlive(actor))
            {
                return false;
            }

            if (match == null)
            {
                return true;
            }

            if (match.RequiredAllTags is { Count: > 0 })
            {
                if (!world.Has<GameplayTagContainer>(actor))
                {
                    return false;
                }

                ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(actor);
                for (int i = 0; i < match.RequiredAllTags.Count; i++)
                {
                    string tagKey = match.RequiredAllTags[i];
                    int tagId = TagRegistry.GetId(tagKey);
                    if (tagId <= 0 || !tagOps.HasTag(ref tags, tagId, TagSense.Effective))
                    {
                        return false;
                    }
                }
            }

            if (match.BlockedAnyTags is { Count: > 0 } && world.Has<GameplayTagContainer>(actor))
            {
                ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(actor);
                for (int i = 0; i < match.BlockedAnyTags.Count; i++)
                {
                    string tagKey = match.BlockedAnyTags[i];
                    int tagId = TagRegistry.GetId(tagKey);
                    if (tagId > 0 && tagOps.HasTag(ref tags, tagId, TagSense.Effective))
                    {
                        return false;
                    }
                }
            }

            if (match.AbilitySlotIndex.HasValue)
            {
                int slotIndex = match.AbilitySlotIndex.Value;
                if (!world.Has<AbilityStateBuffer>(actor))
                {
                    return false;
                }

                ref AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(actor);
                bool hasForm = world.Has<AbilityFormSlotBuffer>(actor);
                AbilityFormSlotBuffer formSlots = hasForm ? world.Get<AbilityFormSlotBuffer>(actor) : default;
                bool hasGranted = world.Has<GrantedSlotBuffer>(actor);
                GrantedSlotBuffer grantedSlots = hasGranted ? world.Get<GrantedSlotBuffer>(actor) : default;
                AbilitySlotState slot = AbilitySlotResolver.Resolve(
                    in abilities,
                    in formSlots,
                    hasForm,
                    in grantedSlots,
                    hasGranted,
                    slotIndex);
                if (slot.AbilityId <= 0)
                {
                    return false;
                }

                string abilityKey = AbilityIdRegistry.GetName(slot.AbilityId);
                if (!string.IsNullOrWhiteSpace(match.AbilityIdKey) &&
                    !string.Equals(abilityKey, match.AbilityIdKey, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(match.AbilityIdKeySuffix) &&
                    abilityKey.IndexOf(match.AbilityIdKeySuffix, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
