using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class TimedTagExpirationSystem : BaseSystem<World, float>
    {
        private readonly IClock _clock;
        private readonly TagOps _tagOps;

        private static readonly QueryDescription _withDirtyQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, TagCountContainer, TimedTagBuffer, DirtyFlags>();

        private static readonly QueryDescription _withoutDirtyQuery = new QueryDescription()
            .WithAll<GameplayTagContainer, TagCountContainer, TimedTagBuffer>()
            .WithNone<DirtyFlags>();

        public TimedTagExpirationSystem(World world, IClock clock, TagOps tagOps) : base(world)
        {
            _clock = clock;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
        }

        public override void Update(in float dt)
        {
            var withDirty = new WithDirtyJob { World = World, Clock = _clock, TagOps = _tagOps };
            World.InlineEntityQuery<WithDirtyJob, GameplayTagContainer, TagCountContainer, TimedTagBuffer, DirtyFlags>(in _withDirtyQuery, ref withDirty);

            var withoutDirty = new WithoutDirtyJob();
            World.InlineEntityQuery<WithoutDirtyJob, GameplayTagContainer, TagCountContainer, TimedTagBuffer>(in _withoutDirtyQuery, ref withoutDirty);
        }

        private struct WithDirtyJob : IForEachWithEntity<GameplayTagContainer, TagCountContainer, TimedTagBuffer, DirtyFlags>
        {
            public World World;
            public IClock Clock;
            public TagOps TagOps;

            public void Update(Entity entity, ref GameplayTagContainer tags, ref TagCountContainer counts, ref TimedTagBuffer timed, ref DirtyFlags dirtyFlags)
            {
                for (int i = timed.Count - 1; i >= 0; i--)
                {
                    int tagId;
                    unsafe
                    {
                        fixed (int* ids = timed.TagIds) tagId = ids[i];
                    }
                    int expireAt;
                    unsafe
                    {
                        fixed (int* exp = timed.ExpireAt) expireAt = exp[i];
                    }
                    GasClockId clockId;
                    unsafe
                    {
                        fixed (byte* clocks = timed.ClockIds) clockId = (GasClockId)clocks[i];
                    }

                    int now = GasClockRuntime.Now(World, Clock, clockId, entity, "Timed tag expiration");
                    if (now < expireAt) continue;

                    TagOps.RemoveTag(World, entity, tagId);
                    timed.RemoveAtSwapBack(i);
                }
            }
        }

        private struct WithoutDirtyJob : IForEachWithEntity<GameplayTagContainer, TagCountContainer, TimedTagBuffer>
        {
            public void Update(Entity entity, ref GameplayTagContainer tags, ref TagCountContainer counts, ref TimedTagBuffer timed)
            {
                throw new InvalidOperationException(
                    $"{TagOps.MissingDirtyFlagsError}: entity={entity.Id}, system=TimedTagExpirationSystem.");
            }
        }
    }
}
