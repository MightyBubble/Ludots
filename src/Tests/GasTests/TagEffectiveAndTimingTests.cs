using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class TagEffectiveAndTimingTests
    {
        private readonly TagOps _tagOps = new TagOps();

        [Test]
        public void HasTag_UsesEffectiveSense_ForGraphRuntime()
        {
            var world = World.Create();
            try
            {
                int tagA = 1;
                int tagBlock = 2;

                var entity = world.Create(new GameplayTagContainer());
                ref var tags = ref world.Get<GameplayTagContainer>(entity);

                var ruleSet = new Ludots.Core.Gameplay.GAS.Components.TagRuleSet();
                unsafe
                {
                    ruleSet.DisabledIfTags[0] = tagBlock;
                    ruleSet.DisabledIfCount = 1;
                }
                _tagOps.RegisterTagRuleSet(tagA, ruleSet);

                var counts = new TagCountContainer();
                var dirty = new DirtyFlags();

                _tagOps.AddTag(ref tags, ref counts, tagA, ref dirty);
                That(_tagOps.HasTag(ref tags, tagA, TagSense.Present), Is.True);
                That(_tagOps.HasTag(ref tags, tagA, TagSense.Effective), Is.True);

                _tagOps.AddTag(ref tags, ref counts, tagBlock, ref dirty);
                That(_tagOps.HasTag(ref tags, tagA, TagSense.Present), Is.True);
                That(_tagOps.HasTag(ref tags, tagA, TagSense.Effective), Is.False);

                var api = new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: null, tagOps: _tagOps);
                That(api.HasTag(entity, tagA), Is.False);
            }
            finally
            {
                world.Dispose();
                _tagOps.ClearRuleRegistry();
            }
        }

        [Test]
        public void DeferredTrigger_TagCountBranch_ClearsDirtyFlags()
        {
            var world = World.Create();
            try
            {
                int tag = 3;
                var entity = world.Create(new TagCountContainer(), new DirtyFlags());
                ref var counts = ref world.Get<TagCountContainer>(entity);
                ref var dirty = ref world.Get<DirtyFlags>(entity);

                counts.AddCount(tag, 1);
                dirty.MarkTagDirty(tag);

                var queue = new DeferredTriggerQueue();
                var sys = new DeferredTriggerCollectionSystem(world, queue);
                sys.Update(dt: 0f);

                That(queue.TagCountTriggerCount, Is.EqualTo(1));
                That(world.Get<DirtyFlags>(entity).IsTagDirty(tag), Is.False);

                ref var counts2 = ref world.Get<TagCountContainer>(entity);
                ref var dirty2 = ref world.Get<DirtyFlags>(entity);
                counts2.AddCount(tag, 1);
                dirty2.MarkTagDirty(tag);
                sys.Update(dt: 0f);

                That(queue.TagCountTriggerCount, Is.EqualTo(2));
                That(world.Get<DirtyFlags>(entity).IsTagDirty(tag), Is.False);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void AbilityActivationBlockTags_UsesEffectiveSense()
        {
            var world = World.Create();
            try
            {
                int tagA = 10;
                int tagBlock = 11;

                var ruleSet = new Ludots.Core.Gameplay.GAS.Components.TagRuleSet();
                unsafe
                {
                    ruleSet.DisabledIfTags[0] = tagBlock;
                    ruleSet.DisabledIfCount = 1;
                }
                _tagOps.RegisterTagRuleSet(tagA, ruleSet);

                var abilityTemplate = world.Create();
                world.Add(abilityTemplate, new AbilityTemplate());
                world.Add(abilityTemplate, new AbilityExecSpec());
                world.Add(abilityTemplate, new AbilityActivationBlockTags());
                unsafe
                {
                    ref var blocks = ref world.Get<AbilityActivationBlockTags>(abilityTemplate);
                    blocks.RequiredAll.AddTag(tagA);
                }

                var defs = new AbilityDefinitionRegistry();
                defs.RegisterFromEntity(world, abilityTemplate, 6001);

                var caster = world.Create(new AbilityStateBuffer(), new GameplayTagContainer());
                ref var abilityState = ref world.Get<AbilityStateBuffer>(caster);
                abilityState.AddAbility(6001);

                ref var tags = ref world.Get<GameplayTagContainer>(caster);
                tags.AddTag(tagA);
                tags.AddTag(tagBlock);

                var sys = new AbilitySystem(world, effectRequests: null, abilityDefinitions: defs, tagOps: _tagOps);
                That(sys.TryActivateAbility(caster, slotIndex: 0), Is.False);
            }
            finally
            {
                world.Dispose();
                _tagOps.ClearRuleRegistry();
            }
        }

        [Test]
        public void EffectiveChangedBits_MarksDependentTags_WhenDisabledIfConditionToggles()
        {
            var world = World.Create();
            try
            {
                int tagA = 20;
                int tagBlock = 21;

                var ruleSet = new Ludots.Core.Gameplay.GAS.Components.TagRuleSet();
                unsafe
                {
                    ruleSet.DisabledIfTags[0] = tagBlock;
                    ruleSet.DisabledIfCount = 1;
                }
                _tagOps.RegisterTagRuleSet(tagA, ruleSet);

                var entity = world.Create(new GameplayTagContainer(), new DirtyFlags());
                ref var tags = ref world.Get<GameplayTagContainer>(entity);
                ref var dirty = ref world.Get<DirtyFlags>(entity);

                tags.AddTag(tagA);
                dirty.MarkTagDirty(tagA);

                var queue = new DeferredTriggerQueue();
                var collect = new DeferredTriggerCollectionSystem(world, queue, _tagOps);
                collect.Update(0f);

                That(world.Has<GameplayTagEffectiveChangedBits>(entity), Is.True);
                ref var changed = ref world.Get<GameplayTagEffectiveChangedBits>(entity);
                changed.Clear();

                ref var tags2 = ref world.Get<GameplayTagContainer>(entity);
                ref var dirty2 = ref world.Get<DirtyFlags>(entity);
                tags2.AddTag(tagBlock);
                dirty2.MarkTagDirty(tagBlock);
                collect.Update(0f);

                ref var changed2 = ref world.Get<GameplayTagEffectiveChangedBits>(entity);
                unsafe
                {
                    That(changed2.Bits[0] & (1UL << tagA), Is.Not.EqualTo(0UL));
                }

                var clear = new ClearPresentationFlagsSystem(world);
                clear.Update(0f);
                ref var changed3 = ref world.Get<GameplayTagEffectiveChangedBits>(entity);
                unsafe
                {
                    That(changed3.Bits[0] | changed3.Bits[1] | changed3.Bits[2] | changed3.Bits[3], Is.EqualTo(0UL));
                }
            }
            finally
            {
                world.Dispose();
                _tagOps.ClearRuleRegistry();
            }
        }
    }
}
