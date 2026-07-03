using System;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Persistence
{
    public static class SaveEntityWorldIdNormalizer
    {
        public static void Normalize(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            NormalizeBlackboardEntityBuffer(world);
            NormalizeChildrenBuffer(world);
            NormalizeActiveEffectContainer(world);
            NormalizeAbilityStateBuffer(world);
            NormalizeTeamEntityRef(world);
        }

        private static void NormalizeBlackboardEntityBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<BlackboardEntityBuffer>();
            world.Query(in query, (ref BlackboardEntityBuffer refs) =>
            {
                unsafe
                {
                    for (int i = 0; i < refs.Count; i++)
                    {
                        refs.WorldIds[i] = worldId;
                    }
                }
            });
        }

        private static void NormalizeChildrenBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ChildrenBuffer>();
            world.Query(in query, (ref ChildrenBuffer children) =>
            {
                unsafe
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        children.ChildWorldIds[i] = worldId;
                    }
                }
            });
        }

        private static void NormalizeActiveEffectContainer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ActiveEffectContainer>();
            world.Query(in query, (ref ActiveEffectContainer activeEffects) =>
            {
                unsafe
                {
                    for (int i = 0; i < activeEffects.Count; i++)
                    {
                        activeEffects.WorldIds[i] = worldId;
                    }
                }
            });
        }

        private static void NormalizeAbilityStateBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<AbilityStateBuffer>();
            world.Query(in query, (ref AbilityStateBuffer abilities) =>
            {
                unsafe
                {
                    for (int i = 0; i < abilities.Count; i++)
                    {
                        if (abilities.TemplateIds[i] != 0 || abilities.TemplateVersions[i] != 0)
                        {
                            abilities.TemplateWorldIds[i] = worldId;
                        }
                    }
                }
            });
        }

        private static void NormalizeTeamEntityRef(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<TeamEntityRef>();
            world.Query(in query, (ref TeamEntityRef teamRef) =>
            {
                Entity value = teamRef.Value;
                if (value != Entity.Null)
                {
                    teamRef.Value = EntityUtil.Reconstruct(value.Id, worldId, value.Version);
                }
            });
        }
    }
}
