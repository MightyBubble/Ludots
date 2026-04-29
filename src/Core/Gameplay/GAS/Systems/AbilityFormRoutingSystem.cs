using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// Resolves the active form route for each actor and writes stable form-layer slot overrides.
    /// Effective slot resolution remains centralized in <see cref="AbilitySlotResolver"/>.
    /// </summary>
    public sealed class AbilityFormRoutingSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription()
            .WithAll<AbilityStateBuffer, AbilityFormSetRef, AbilityFormSlotBuffer>();

        private readonly AbilityFormSetRegistry _formSets;
        private readonly TagOps _tagOps;

        public AbilityFormRoutingSystem(World world, AbilityFormSetRegistry formSets, TagOps tagOps = null)
            : base(world)
        {
            _formSets = formSets;
            _tagOps = tagOps ?? new TagOps();
        }

        public override void Update(in float dt)
        {
            var job = new RouteJob
            {
                World = World,
                FormSets = _formSets,
                TagOps = _tagOps,
            };
            World.InlineEntityQuery<RouteJob, AbilityStateBuffer, AbilityFormSetRef, AbilityFormSlotBuffer>(in Query, ref job);
        }

        private struct RouteJob : IForEachWithEntity<AbilityStateBuffer, AbilityFormSetRef, AbilityFormSlotBuffer>
        {
            public World World;
            public AbilityFormSetRegistry FormSets;
            public TagOps TagOps;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref AbilityStateBuffer abilities, ref AbilityFormSetRef formSetRef, ref AbilityFormSlotBuffer formSlots)
            {
                formSlots.ClearAll();

                if (formSetRef.FormSetId <= 0 || !FormSets.TryGet(formSetRef.FormSetId, out var formSet))
                {
                    return;
                }

                ref var tags = ref World.TryGetRef<GameplayTagContainer>(entity, out bool hasTags);

                int bestRouteIndex = -1;
                int bestPriority = int.MinValue;
                for (int routeIndex = 0; routeIndex < formSet.Routes.Count; routeIndex++)
                {
                    var route = formSet.Routes[routeIndex];
                    if (!MatchesRoute(TagOps, ref tags, hasTags, in route))
                    {
                        continue;
                    }

                    if (bestRouteIndex >= 0 && route.Priority <= bestPriority)
                    {
                        continue;
                    }

                    bestRouteIndex = routeIndex;
                    bestPriority = route.Priority;
                }

                if (bestRouteIndex < 0)
                {
                    return;
                }

                var selectedRoute = formSet.Routes[bestRouteIndex];
                for (int i = 0; i < selectedRoute.SlotOverrides.Count; i++)
                {
                    var slotOverride = selectedRoute.SlotOverrides[i];
                    if ((uint)slotOverride.SlotIndex >= (uint)abilities.Count)
                    {
                        continue;
                    }

                    formSlots.SetOverride(slotOverride.SlotIndex, slotOverride.AbilityId);
                }
            }
        }

        private static bool MatchesRoute(TagOps tagOps, ref GameplayTagContainer tags, bool hasTags, in AbilityFormRouteDefinition route)
        {
            var requiredAll = route.RequiredAll;
            if (!requiredAll.IsEmpty)
            {
                if (!hasTags || !tagOps.ContainsAll(ref tags, in requiredAll, TagSense.Effective))
                {
                    return false;
                }
            }

            var blockedAny = route.BlockedAny;
            if (hasTags && !blockedAny.IsEmpty && tagOps.Intersects(ref tags, in blockedAny, TagSense.Effective))
            {
                return false;
            }

            return true;
        }
    }
}
