using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;

namespace ChampionSkillSandboxMod.Runtime
{
    internal static class ChampionSkillSandboxDuelistContextInspector
    {
        public static bool TryInspect(
            GameEngine engine,
            int duelistActionContextAbilityId,
            Span<ContextScoredCandidateProbe> probes,
            out Entity actor,
            out Entity hovered,
            out ContextGroupDefinition group,
            out int probeCount,
            out ContextScoredOrderResolution resolution)
        {
            actor = Entity.Null;
            hovered = Entity.Null;
            group = default;
            probeCount = 0;
            resolution = default;

            if (duelistActionContextAbilityId <= 0 ||
                engine.GetService(CoreServiceKeys.ActiveInputOrderMapping) is not InputOrderMappingSystem mappingSystem ||
                mappingSystem.GetMapping("ActionAttack") is not InputOrderMapping actionAttackMapping ||
                engine.GetService(CoreServiceKeys.ContextGroupRegistry) is not ContextGroupRegistry contextGroups ||
                engine.GetService(CoreServiceKeys.GraphProgramRegistry) is not GraphProgramRegistry graphPrograms ||
                engine.GetService(CoreServiceKeys.SpatialQueryService) is not ISpatialQueryService spatialQueries ||
                engine.GetService(CoreServiceKeys.SpatialCoordinateConverter) is not ISpatialCoordinateConverter spatialCoords)
            {
                return false;
            }

            actor = ResolveSelectedDuelist(engine.World, engine.GlobalContext, duelistActionContextAbilityId);
            if (actor == Entity.Null ||
                !TryResolveContextGroup(engine.World, actor, actionAttackMapping, contextGroups, out group))
            {
                return false;
            }

            hovered = ResolveHoveredEntity(engine);
            var graphApi = new GasGraphRuntimeApi(engine.World, spatialQueries, spatialCoords, eventBus: null, effectRequests: null);
            var resolver = new ContextScoredOrderResolver(engine.World, contextGroups, graphPrograms, spatialQueries, graphApi);
            return resolver.TryInspect(actor, actionAttackMapping, hovered, probes, out probeCount, out resolution);
        }

        private static Entity ResolveSelectedDuelist(World world, System.Collections.Generic.Dictionary<string, object> globals, int duelistActionContextAbilityId)
        {
            if (!SelectionContextRuntime.TryGetCurrentPrimary(world, globals, out Entity selected) ||
                selected == Entity.Null ||
                !world.IsAlive(selected) ||
                !world.Has<AbilityStateBuffer>(selected))
            {
                return Entity.Null;
            }

            ref readonly AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(selected);
            bool hasForm = world.Has<AbilityFormSlotBuffer>(selected);
            AbilityFormSlotBuffer formSlots = hasForm ? world.Get<AbilityFormSlotBuffer>(selected) : default;
            bool hasGranted = world.Has<GrantedSlotBuffer>(selected);
            GrantedSlotBuffer granted = hasGranted ? world.Get<GrantedSlotBuffer>(selected) : default;
            for (int slotIndex = 0; slotIndex < abilities.Count; slotIndex++)
            {
                var resolved = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in granted, hasGranted, slotIndex);
                if (resolved.AbilityId == duelistActionContextAbilityId)
                {
                    return selected;
                }
            }

            return Entity.Null;
        }

        private static Entity ResolveHoveredEntity(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out object? hoveredObj) &&
                   hoveredObj is Entity hovered &&
                   hovered != Entity.Null &&
                   engine.World.IsAlive(hovered)
                ? hovered
                : Entity.Null;
        }

        private static bool TryResolveContextGroup(
            World world,
            Entity actor,
            InputOrderMapping mapping,
            ContextGroupRegistry contextGroups,
            out ContextGroupDefinition group)
        {
            group = default;
            if (mapping.ArgsTemplate.I0 is null || !world.Has<AbilityStateBuffer>(actor))
            {
                return false;
            }

            int rootSlotIndex = mapping.ArgsTemplate.I0.Value;
            ref readonly AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(actor);
            bool hasForm = world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasGranted = world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? world.Get<GrantedSlotBuffer>(actor) : default;
            var resolved = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in granted, hasGranted, rootSlotIndex);
            return resolved.AbilityId > 0 && contextGroups.TryGetByRootAbility(resolved.AbilityId, out group);
        }
    }
}
