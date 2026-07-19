using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Unit tests for TimedTagExpirationSystem:
    /// - Tags expire at the correct tick
    /// - Expired tags are removed from container
    /// - Non-expired tags remain
    /// - DirtyFlags are set on expiration
    /// </summary>
    [TestFixture]
    public class TimedTagExpirationTests
    {
        [Test]
        public void TimedTag_ExpiresAtCorrectTick()
        {
            using var world = World.Create();

            int testTagId = 42;
            var clock = new DiscreteClock();

            var entity = world.Create(
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags()
            );

            // Add timed tag that expires at tick 5
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            ref var timed = ref world.Get<TimedTagBuffer>(entity);

            tags.AddTag(testTagId);
            counts.AddCount(testTagId, 1);
            timed.TryAdd(testTagId, 5, GasClockId.Step);

            var system = new TimedTagExpirationSystem(world, clock);

            // Advance to tick 3 — tag should still exist
            clock.Advance(ClockDomainId.Step, 3);
            system.Update(0f);
            That(world.Get<GameplayTagContainer>(entity).HasTag(testTagId), Is.True,
                "Tag should still exist at tick 3");

            // Advance to tick 5 — tag should expire
            clock.Advance(ClockDomainId.Step, 2);
            system.Update(0f);
            That(world.Get<GameplayTagContainer>(entity).HasTag(testTagId), Is.False,
                "Tag should be removed after expiration at tick 5");
        }

        [Test]
        public void TimedTag_NonExpired_Remains()
        {
            using var world = World.Create();

            int earlyTag = 10;
            int lateTag = 11;
            var clock = new DiscreteClock();

            var entity = world.Create(
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags()
            );

            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            ref var timed = ref world.Get<TimedTagBuffer>(entity);

            tags.AddTag(earlyTag);
            counts.AddCount(earlyTag, 1);
            timed.TryAdd(earlyTag, 2, GasClockId.Step);

            tags.AddTag(lateTag);
            counts.AddCount(lateTag, 1);
            timed.TryAdd(lateTag, 100, GasClockId.Step);

            var system = new TimedTagExpirationSystem(world, clock);

            // Advance past earlyTag expiry
            clock.Advance(ClockDomainId.Step, 3);
            system.Update(0f);

            That(world.Get<GameplayTagContainer>(entity).HasTag(earlyTag), Is.False,
                "Early tag should expire");
            That(world.Get<GameplayTagContainer>(entity).HasTag(lateTag), Is.True,
                "Late tag should remain");
        }

        [Test]
        public void TimedTag_DirtyFlagsSet_OnExpiration()
        {
            using var world = World.Create();

            int testTagId = 42;
            var clock = new DiscreteClock();

            var entity = world.Create(
                new GameplayTagContainer(),
                new TagCountContainer(),
                new TimedTagBuffer(),
                new DirtyFlags()
            );

            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            ref var timed = ref world.Get<TimedTagBuffer>(entity);

            tags.AddTag(testTagId);
            counts.AddCount(testTagId, 1);
            timed.TryAdd(testTagId, 1, GasClockId.Step);

            var system = new TimedTagExpirationSystem(world, clock);

            // Clear dirty flags first
            world.Get<DirtyFlags>(entity) = default;

            // Advance past expiry
            clock.Advance(ClockDomainId.Step, 2);
            system.Update(0f);

            ref var dirty = ref world.Get<DirtyFlags>(entity);
            That(dirty.IsTagDirty(testTagId), Is.True,
                "DirtyFlags should be set for expired tag");
        }
    }
}
