using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Gameplay.Relationships
{
    public static class RelationshipTeamBootstrapper
    {
        public static Entity EnsureTeamEntity(World world, TeamEntityLookup lookup, int teamId, string name)
        {
            if (lookup.TryGet(teamId, out Entity existing) && world.IsAlive(existing))
            {
                return existing;
            }

            var entity = world.Create(
                new TeamIdentity { TeamId = teamId },
                new Team { Id = teamId },
                new Name { Value = name },
                new GameplayTagContainer(),
                new TagCountContainer(),
                default(AttributeBuffer),
                new DirtyFlags(),
                new ActiveEffectContainer());
            lookup.Register(teamId, entity);
            return entity;
        }
    }
}
