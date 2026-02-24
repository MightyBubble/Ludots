using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    public class LifetimeConditionTests
    {
        private readonly TagOps _tagOps = new TagOps();

        [Test]
        public void TagSense_Effective_RespectsDisabledIf()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                world.Add(entity, new GameplayTagContainer());
                world.Add(entity, new TagCountContainer());

                int tagA = 1;
                int tagDisableA = 2;

                var ruleSetA = new TagRuleSet();
                unsafe
                {
                    ruleSetA.DisabledIfTags[0] = tagDisableA;
                    ruleSetA.DisabledIfCount = 1;
                }
                _tagOps.ClearRuleRegistry();
                _tagOps.RegisterTagRuleSet(tagA, ruleSetA);

                ref var tags = ref world.Get<GameplayTagContainer>(entity);
                ref var counts = ref world.Get<TagCountContainer>(entity);

                _tagOps.AddTag(ref tags, ref counts, tagDisableA);
                _tagOps.AddTag(ref tags, ref counts, tagA);

                That(_tagOps.HasTag(ref tags, tagA, TagSense.Present), Is.True);
                That(_tagOps.HasTag(ref tags, tagA, TagSense.Effective), Is.False);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectLifetime_ExpireCondition_TagPresent_ExpiresWhenMissing()
        {
            var world = World.Create();
            try
            {
                _tagOps.ClearRuleRegistry();

                var clock = new DiscreteClock();
                var clocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();
                var requests = new EffectRequestQueue();
                var lifetime = new EffectLifetimeSystem(world, clock, conditions, requests);

                int keepAliveTag = 3;
                var keepAlive = conditions.Register(new GasCondition(GasConditionKind.TagPresent, keepAliveTag, TagSense.Present));

                var source = world.Create();
                var target = world.Create();
                world.Add(target, new GameplayTagContainer());
                world.Add(target, new TagCountContainer());
                world.Add(target, new ActiveEffectContainer());

                ref var tags = ref world.Get<GameplayTagContainer>(target);
                ref var counts = ref world.Get<TagCountContainer>(target);
                _tagOps.AddTag(ref tags, ref counts, keepAliveTag);

                var effect = GameplayEffectFactory.CreateEffect(world, rootId: 1, source, target, durationTicks: 0, lifetimeKind: EffectLifetimeKind.Infinite, periodTicks: 0, targetContext: default, clockId: GasClockId.FixedFrame, expireCondition: keepAlive);
                ref var ge = ref world.Get<GameplayEffect>(effect);
                ge.State = EffectState.Committed;
                ref var container = ref world.Get<ActiveEffectContainer>(target);
                container.Add(effect);

                clocks.AdvanceFixedFrame();
                clocks.AdvanceStep();
                lifetime.Update(0.016f);
                That(world.IsAlive(effect), Is.True);

                _tagOps.RemoveTag(ref tags, ref counts, keepAliveTag);

                clocks.AdvanceFixedFrame();
                clocks.AdvanceStep();
                lifetime.Update(0.016f);
                That(world.IsAlive(effect), Is.False);
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
