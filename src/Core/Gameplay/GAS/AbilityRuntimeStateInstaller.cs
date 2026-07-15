using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public static class AbilityRuntimeStateInstaller
    {
        public static void EnsureForAbilities(
            World world,
            Entity entity,
            AbilityDefinitionRegistry definitions,
            ReadOnlySpan<int> abilityIds,
            string authoringContext)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(definitions);

            AbilityStateRequirements requirements = default;
            for (int i = 0; i < abilityIds.Length; i++)
            {
                requirements.Include(ResolveRequirements(
                    definitions,
                    abilityIds[i],
                    $"{authoringContext} ability {i}"));
            }

            EnsureRequiredState(world, entity, in requirements);
        }

        public static void EnsureForAuthoredAbilities(
            World world,
            Entity entity,
            AbilityDefinitionRegistry definitions,
            AbilityFormSetRegistry? formSets,
            string authoringContext)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(definitions);

            if (!world.IsAlive(entity) || !world.Has<AbilityStateBuffer>(entity))
            {
                return;
            }

            ref AbilityStateBuffer abilities = ref world.Get<AbilityStateBuffer>(entity);
            AbilityStateRequirements requirements = default;
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilitySlotState slot = abilities.Get(i);
                requirements.Include(ResolveRequirements(
                    definitions,
                    slot.AbilityId,
                    $"{authoringContext} ability slot {i}"));
            }

            if (world.Has<AbilityFormSetRef>(entity))
            {
                AbilityFormSetRegistry registry = formSets ?? throw new InvalidOperationException(
                    $"{authoringContext} requires AbilityFormSetRegistry to assemble form ability runtime state.");
                int formSetId = world.Get<AbilityFormSetRef>(entity).FormSetId;
                if (!registry.TryGet(formSetId, out AbilityFormSetDefinition formSet))
                {
                    throw new InvalidOperationException(
                        $"{authoringContext} references unknown ability form set id '{formSetId}'.");
                }

                for (int routeIndex = 0; routeIndex < formSet.Routes.Count; routeIndex++)
                {
                    var overrides = formSet.Routes[routeIndex].SlotOverrides;
                    for (int overrideIndex = 0; overrideIndex < overrides.Count; overrideIndex++)
                    {
                        AbilityFormSlotOverride slotOverride = overrides[overrideIndex];
                        requirements.Include(ResolveRequirements(
                            definitions,
                            slotOverride.AbilityId,
                            $"{authoringContext} ability form set {formSetId} route {routeIndex} override {overrideIndex}"));
                    }
                }
            }

            EnsureRequiredState(world, entity, in requirements);
        }

        private static AbilityStateRequirements ResolveRequirements(
            AbilityDefinitionRegistry definitions,
            int abilityId,
            string authoringContext)
        {
            if (abilityId <= 0 || !definitions.TryGet(abilityId, out AbilityDefinition definition))
            {
                throw new InvalidOperationException(
                    $"{authoringContext} references unknown ability id '{abilityId}'.");
            }

            AbilityStateRequirements requirements = default;
            requirements.Include(in definition.ExecSpec);
            if (definition.HasToggleSpec)
            {
                requirements.RequiresTagState |= definition.ToggleSpec.ToggleTagId > 0;
                requirements.Include(in definition.ToggleSpec.DeactivateExecSpec);
            }

            return requirements;
        }

        private static void EnsureRequiredState(
            World world,
            Entity entity,
            in AbilityStateRequirements requirements)
        {
            if (!requirements.RequiresTagState)
            {
                return;
            }

            TagStateInstaller.EnsureInstalled(world, entity);
            if (requirements.RequiresTimedTagState && !world.Has<TimedTagBuffer>(entity))
            {
                world.Add(entity, new TimedTagBuffer());
            }
        }

        private struct AbilityStateRequirements
        {
            public bool RequiresTagState;
            public bool RequiresTimedTagState;

            public void Include(in AbilityStateRequirements other)
            {
                RequiresTagState |= other.RequiresTagState;
                RequiresTimedTagState |= other.RequiresTimedTagState;
            }

            public void Include(in AbilityExecSpec spec)
            {
                for (int i = 0; i < spec.ItemCount; i++)
                {
                    ExecItemKind kind = spec.GetKind(i);
                    if (kind == ExecItemKind.TagClip || kind == ExecItemKind.TagClipTarget)
                    {
                        RequiresTagState = true;
                        RequiresTimedTagState |= spec.GetDurationTicks(i) > 0;
                    }
                    else if (kind == ExecItemKind.TagSignal || kind == ExecItemKind.TagSignalTarget)
                    {
                        RequiresTagState = true;
                    }
                }
            }
        }
    }
}
