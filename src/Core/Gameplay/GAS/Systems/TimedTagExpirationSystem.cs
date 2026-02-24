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

        private readonly CommandBuffer _commandBuffer = new CommandBuffer();

        public TimedTagExpirationSystem(World world, IClock clock, TagOps tagOps = null) : base(world)
        {
            _clock = clock;
            _tagOps = tagOps ?? new TagOps();
        }

        public override void Update(in float dt)
        {
            var withDirty = new WithDirtyJob { Clock = _clock, TagOps = _tagOps };
            World.InlineEntityQuery<WithDirtyJob, GameplayTagContainer, TagCountContainer, TimedTagBuffer, DirtyFlags>(in _withDirtyQuery, ref withDirty);

            var withoutDirty = new WithoutDirtyJob { Clock = _clock, CommandBuffer = _commandBuffer, TagOps = _tagOps };
            World.InlineEntityQuery<WithoutDirtyJob, GameplayTagContainer, TagCountContainer, TimedTagBuffer>(in _withoutDirtyQuery, ref withoutDirty);

            _commandBuffer.Playback(World, dispose: true);
        }

        private struct WithDirtyJob : IForEachWithEntity<GameplayTagContainer, TagCountContainer, TimedTagBuffer, DirtyFlags>
        {
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

                    int now = Clock.Now(clockId.ToDomainId());
                    if (now < expireAt) continue;

                    TagOps.RemoveTag(ref tags, ref counts, tagId, ref dirtyFlags);
                    timed.RemoveAtSwapBack(i);
                }
            }
        }

        private struct WithoutDirtyJob : IForEachWithEntity<GameplayTagContainer, TagCountContainer, TimedTagBuffer>
        {
            public IClock Clock;
            public CommandBuffer CommandBuffer;
            public TagOps TagOps;

            public void Update(Entity entity, ref GameplayTagContainer tags, ref TagCountContainer counts, ref TimedTagBuffer timed)
            {
                DirtyFlags dirtyFlags = default;
                bool anyDirty = false;

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

                    int now = Clock.Now(clockId.ToDomainId());
                    if (now < expireAt) continue;

                    TagOps.RemoveTag(ref tags, ref counts, tagId);
                    dirtyFlags.MarkTagDirty(tagId);
                    anyDirty = true;
                    timed.RemoveAtSwapBack(i);
                }

                if (anyDirty)
                {
                    CommandBuffer.Add(entity, dirtyFlags);
                }
            }
        }
    }
}
