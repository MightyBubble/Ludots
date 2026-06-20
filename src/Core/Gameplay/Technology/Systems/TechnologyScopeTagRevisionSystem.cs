using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Technology.Components;

namespace Ludots.Core.Gameplay.Technology.Systems
{
    public sealed class TechnologyScopeTagRevisionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription ScopeMemberChangedTagQuery = new QueryDescription()
            .WithAll<TechnologyScopeRefBuffer, GameplayTagEffectiveChangedBits>();

        private static readonly QueryDescription ScopeHostChangedTagQuery = new QueryDescription()
            .WithAll<TechnologyScopeMembershipRevision, GameplayTagEffectiveChangedBits>();

        public TechnologyScopeTagRevisionSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            var job = new BumpScopeRevisionJob
            {
                World = World
            };
            World.InlineEntityQuery<BumpScopeRevisionJob, TechnologyScopeRefBuffer, GameplayTagEffectiveChangedBits>(
                in ScopeMemberChangedTagQuery,
                ref job);

            var hostJob = new BumpHostRevisionJob();
            World.InlineEntityQuery<BumpHostRevisionJob, TechnologyScopeMembershipRevision, GameplayTagEffectiveChangedBits>(
                in ScopeHostChangedTagQuery,
                ref hostJob);
        }

        private struct BumpScopeRevisionJob : IForEachWithEntity<TechnologyScopeRefBuffer, GameplayTagEffectiveChangedBits>
        {
            public World World;

            public void Update(Entity entity, ref TechnologyScopeRefBuffer refs, ref GameplayTagEffectiveChangedBits changedBits)
            {
                if (!changedBits.IsAnyBitSet())
                {
                    return;
                }

                for (int i = 0; i < refs.Count; i++)
                {
                    Entity scopeHost;
                    unsafe
                    {
                        scopeHost = EntityUtil.Reconstruct(
                            refs.EntityIds[i],
                            refs.EntityWorldIds[i],
                            refs.EntityVersions[i]);
                    }

                    if (!World.IsAlive(scopeHost) ||
                        !World.Has<TechnologyScopeMembershipRevision>(scopeHost))
                    {
                        continue;
                    }

                    ref var revision = ref World.Get<TechnologyScopeMembershipRevision>(scopeHost);
                    revision.Revision++;
                }
            }
        }

        private struct BumpHostRevisionJob : IForEachWithEntity<TechnologyScopeMembershipRevision, GameplayTagEffectiveChangedBits>
        {
            public void Update(Entity entity, ref TechnologyScopeMembershipRevision revision, ref GameplayTagEffectiveChangedBits changedBits)
            {
                if (changedBits.IsAnyBitSet())
                {
                    revision.Revision++;
                }
            }
        }
    }
}
