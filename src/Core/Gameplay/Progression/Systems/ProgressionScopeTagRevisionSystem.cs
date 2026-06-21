using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Progression.Components;

namespace Ludots.Core.Gameplay.Progression.Systems
{
    public sealed class ProgressionScopeTagRevisionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription ScopeMemberChangedTagQuery = new QueryDescription()
            .WithAll<ProgressionScopeRefBuffer, GameplayTagEffectiveChangedBits>();

        private static readonly QueryDescription ScopeHostChangedTagQuery = new QueryDescription()
            .WithAll<ProgressionScopeMembershipRevision, GameplayTagEffectiveChangedBits>();

        public ProgressionScopeTagRevisionSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            var job = new BumpScopeRevisionJob
            {
                World = World
            };
            World.InlineEntityQuery<BumpScopeRevisionJob, ProgressionScopeRefBuffer, GameplayTagEffectiveChangedBits>(
                in ScopeMemberChangedTagQuery,
                ref job);

            var hostJob = new BumpHostRevisionJob();
            World.InlineEntityQuery<BumpHostRevisionJob, ProgressionScopeMembershipRevision, GameplayTagEffectiveChangedBits>(
                in ScopeHostChangedTagQuery,
                ref hostJob);
        }

        private struct BumpScopeRevisionJob : IForEachWithEntity<ProgressionScopeRefBuffer, GameplayTagEffectiveChangedBits>
        {
            public World World;

            public void Update(Entity entity, ref ProgressionScopeRefBuffer refs, ref GameplayTagEffectiveChangedBits changedBits)
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
                        !World.Has<ProgressionScopeMembershipRevision>(scopeHost))
                    {
                        continue;
                    }

                    ref var revision = ref World.Get<ProgressionScopeMembershipRevision>(scopeHost);
                    revision.Revision++;
                }
            }
        }

        private struct BumpHostRevisionJob : IForEachWithEntity<ProgressionScopeMembershipRevision, GameplayTagEffectiveChangedBits>
        {
            public void Update(Entity entity, ref ProgressionScopeMembershipRevision revision, ref GameplayTagEffectiveChangedBits changedBits)
            {
                if (changedBits.IsAnyBitSet())
                {
                    revision.Revision++;
                }
            }
        }
    }
}
