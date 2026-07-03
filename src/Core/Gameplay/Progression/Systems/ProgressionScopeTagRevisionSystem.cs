using Arch.Core;
using Arch.System;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Progression.Systems
{
    public sealed class ProgressionScopeTagRevisionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription ScopeMemberChangedTagQuery = new QueryDescription()
            .WithAll<ScopeRefBuffer, GameplayTagEffectiveChangedBits>();

        private static readonly QueryDescription ScopeHostChangedTagQuery = new QueryDescription()
            .WithAll<ScopeMembershipRevision, GameplayTagEffectiveChangedBits>();

        public ProgressionScopeTagRevisionSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            var job = new BumpScopeRevisionJob
            {
                World = World
            };
            World.InlineEntityQuery<BumpScopeRevisionJob, ScopeRefBuffer, GameplayTagEffectiveChangedBits>(
                in ScopeMemberChangedTagQuery,
                ref job);

            var hostJob = new BumpHostRevisionJob();
            World.InlineEntityQuery<BumpHostRevisionJob, ScopeMembershipRevision, GameplayTagEffectiveChangedBits>(
                in ScopeHostChangedTagQuery,
                ref hostJob);
        }

        private struct BumpScopeRevisionJob : IForEachWithEntity<ScopeRefBuffer, GameplayTagEffectiveChangedBits>
        {
            public World World;

            public void Update(Entity entity, ref ScopeRefBuffer refs, ref GameplayTagEffectiveChangedBits changedBits)
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
                        !World.Has<ScopeMembershipRevision>(scopeHost))
                    {
                        continue;
                    }

                    ref var revision = ref World.Get<ScopeMembershipRevision>(scopeHost);
                    revision.Revision++;
                }
            }
        }

        private struct BumpHostRevisionJob : IForEachWithEntity<ScopeMembershipRevision, GameplayTagEffectiveChangedBits>
        {
            public void Update(Entity entity, ref ScopeMembershipRevision revision, ref GameplayTagEffectiveChangedBits changedBits)
            {
                if (changedBits.IsAnyBitSet())
                {
                    revision.Revision++;
                }
            }
        }
    }
}
