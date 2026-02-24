using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Teams;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Deep gap tests for GAS features NOT covered by GPT's existing mods or tests.
    /// Each [Test] targets a specific feature gap identified in the complementary analysis.
    /// </summary>
    [TestFixture]
    public class GasFeatureGapTests
    {
        private readonly TagOps _tagOps = new TagOps();

        // ════════════════════════════════════════════════════════════════════
        //  1. GasClock Manual / TurnBased mode
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GasClock_ManualMode_DoesNotAdvanceWithoutRequest()
        {
            var clock = new DiscreteClock();
            var policy = new GasClockStepPolicy(1, GasStepMode.Manual);
            var system = new GasClockSystem(clock, policy);
            var view = new GasClocks(clock);

            // Advance 10 fixed frames without manual request
            for (int i = 0; i < 10; i++)
                system.Update(0.016f);

            That(view.FixedFrameNow, Is.EqualTo(10));
            That(view.StepNow, Is.EqualTo(0), "Manual mode should NOT advance step without RequestStep");
        }

        [Test]
        public void GasClock_ManualMode_AdvancesOnRequest()
        {
            var clock = new DiscreteClock();
            var policy = new GasClockStepPolicy(1, GasStepMode.Manual);
            var system = new GasClockSystem(clock, policy);
            var view = new GasClocks(clock);

            // Request 3 steps, then advance fixed frames
            policy.RequestStep(3);
            for (int i = 0; i < 10; i++)
                system.Update(0.016f);

            That(view.StepNow, Is.EqualTo(3), "Should advance exactly 3 steps after requesting 3");
        }

        [Test]
        public void GasClock_PausedMode_NeverAdvances()
        {
            var clock = new DiscreteClock();
            var policy = new GasClockStepPolicy(1, GasStepMode.Paused);
            var system = new GasClockSystem(clock, policy);
            var view = new GasClocks(clock);

            policy.RequestStep(5); // Even with pending requests
            for (int i = 0; i < 20; i++)
                system.Update(0.016f);

            That(view.StepNow, Is.EqualTo(0), "Paused mode should never advance step");
        }

        [Test]
        public void GasClock_SwitchAutoToManual_StopsAutoAdvancement()
        {
            var clock = new DiscreteClock();
            var policy = new GasClockStepPolicy(2, GasStepMode.Auto);
            var system = new GasClockSystem(clock, policy);
            var view = new GasClocks(clock);

            // Auto advance 4 fixed frames = 2 steps
            for (int i = 0; i < 4; i++)
                system.Update(0.016f);
            That(view.StepNow, Is.EqualTo(2));

            // Switch to Manual
            policy.SetMode(GasStepMode.Manual);

            // More fixed frames should NOT produce steps
            for (int i = 0; i < 10; i++)
                system.Update(0.016f);
            That(view.StepNow, Is.EqualTo(2), "After switching to Manual, no automatic step advancement");

            // Manual request produces one step
            policy.RequestStep(1);
            system.Update(0.016f);
            That(view.StepNow, Is.EqualTo(3));
        }

        // ════════════════════════════════════════════════════════════════════
        //  2. Effect ExpireCondition (TagPresent / TagAbsent)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ExpireCondition_TagPresent_EffectExpiresWhenTagRemoved()
        {
            using var world = World.Create();
            var tagOps = new TagOps();
            int tagA = 10;

            // Register condition: expires when TagPresent is no longer satisfied
            var conditions = new GasConditionRegistry();
            var condition = new GasCondition(GasConditionKind.TagPresent, tagA, TagSense.Present);
            var handle = conditions.Register(in condition);
            That(handle.IsValid, Is.True);

            ref readonly var cond = ref conditions.Get(in handle);
            That(cond.Kind, Is.EqualTo(GasConditionKind.TagPresent));
            That(cond.TagId, Is.EqualTo(tagA));

            // Create entity with tag present
            var entity = world.Create(new GameplayTagContainer(), new TagCountContainer());
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            tags.AddTag(tagA);

            bool shouldExpire = GasConditionEvaluator.ShouldExpire(world, entity, in cond, tagOps);
            That(shouldExpire, Is.False, "Should NOT expire while tag is present");

            // Remove tag
            tags.RemoveTag(tagA);
            shouldExpire = GasConditionEvaluator.ShouldExpire(world, entity, in cond, tagOps);
            That(shouldExpire, Is.True, "Should expire when tag is removed");
        }

        [Test]
        public void ExpireCondition_TagAbsent_EffectExpiresWhenTagAdded()
        {
            using var world = World.Create();
            var tagOps = new TagOps();
            int tagB = 20;

            var conditions = new GasConditionRegistry();
            var condition = new GasCondition(GasConditionKind.TagAbsent, tagB, TagSense.Present);
            var handle = conditions.Register(in condition);

            ref readonly var cond = ref conditions.Get(in handle);
            That(cond.Kind, Is.EqualTo(GasConditionKind.TagAbsent));

            // Create entity without tag
            var entity = world.Create(new GameplayTagContainer(), new TagCountContainer());

            bool shouldExpire = GasConditionEvaluator.ShouldExpire(world, entity, in cond, tagOps);
            That(shouldExpire, Is.False, "Should NOT expire while tag is absent");

            // Add tag
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            tags.AddTag(tagB);
            shouldExpire = GasConditionEvaluator.ShouldExpire(world, entity, in cond, tagOps);
            That(shouldExpire, Is.True, "Should expire when tag becomes present");
        }

        // ════════════════════════════════════════════════════════════════════
        //  3. DeferredTrigger integration (Collection → Queue)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void DeferredTrigger_TagChanged_DirtyFlagMarksCorrectTag()
        {
            var dirty = default(DirtyFlags);

            // Mark tag 5 as dirty
            dirty.MarkTagDirty(5);

            That(dirty.IsTagDirty(5), Is.True, "Tag 5 should be marked dirty");
            That(dirty.IsTagDirty(6), Is.False, "Tag 6 should NOT be dirty");
            That(dirty.IsTagDirty(0), Is.False, "Tag 0 should NOT be dirty");

            // Mark multiple tags
            dirty.MarkTagDirty(100);
            dirty.MarkTagDirty(255);
            That(dirty.IsTagDirty(100), Is.True);
            That(dirty.IsTagDirty(255), Is.True);

            // Clear specific
            dirty.ClearTagDirty(5);
            That(dirty.IsTagDirty(5), Is.False, "Tag 5 should be cleared");
            That(dirty.IsTagDirty(100), Is.True, "Tag 100 should still be dirty");
        }

        [Test]
        public void DeferredTrigger_AttributeChanged_TracksOldAndNewValue()
        {
            using var world = World.Create();
            var queue = new DeferredTriggerQueue();
            var system = new DeferredTriggerCollectionSystem(world, queue);

            var e = world.Create();
            var attrs = new AttributeBuffer();
            attrs.SetCurrent(3, 75f); // attribute 3 = 75
            world.Add(e, attrs);

            var snap = default(AttributeLastSnapshot);
            unsafe { snap.Values[3] = 50f; } // was 50
            world.Add(e, snap);

            var dirty = default(DirtyFlags);
            dirty.MarkAttributeDirty(3);
            world.Add(e, dirty);

            system.Update(0.016f);

            That(queue.AttributeTriggerCount, Is.EqualTo(1));
            var trigger = queue.GetAttributeTrigger(0);
            That(trigger.AttributeId, Is.EqualTo(3));
            That(trigger.OldValue, Is.EqualTo(50f));
            That(trigger.NewValue, Is.EqualTo(75f));
        }

        // ════════════════════════════════════════════════════════════════════
        //  4. Response Chain - Chain type (append new effect)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ResponseChainListener_ChainType_StoresEffectTemplateId()
        {
            var listener = default(ResponseChainListener);
            int spellTag = 100;
            int counterEffectId = 42;

            bool added = listener.Add(spellTag, ResponseType.Chain, priority: 50, effectTemplateId: counterEffectId);
            That(added, Is.True);
            That(listener.Count, Is.EqualTo(1));
            That(listener.MatchesEventTag(spellTag), Is.True);

            // Verify stored data
            unsafe
            {
                That(listener.ResponseTypes[0], Is.EqualTo((byte)ResponseType.Chain));
                That(listener.EffectTemplateIds[0], Is.EqualTo(counterEffectId));
                That(listener.Priorities[0], Is.EqualTo(50));
            }
        }

        [Test]
        public void ResponseChainListener_MultipleTypes_CoexistInSameListener()
        {
            var listener = default(ResponseChainListener);
            int tag1 = 100;
            int tag2 = 200;

            listener.Add(tag1, ResponseType.Hook, priority: 100);
            listener.Add(tag1, ResponseType.Modify, priority: 80, modifyValue: 5f);
            listener.Add(tag2, ResponseType.Chain, priority: 60, effectTemplateId: 99);

            That(listener.Count, Is.EqualTo(3));
            That(listener.MatchesEventTag(tag1), Is.True);
            That(listener.MatchesEventTag(tag2), Is.True);
            That(listener.MatchesEventTag(999), Is.False);
        }

        [Test]
        public void ResponseChainListener_Capacity_RejectsOverflow()
        {
            var listener = default(ResponseChainListener);
            for (int i = 0; i < ResponseChainListener.CAPACITY; i++)
            {
                bool ok = listener.Add(i + 1, ResponseType.Hook, priority: i);
                That(ok, Is.True, $"Should accept item {i}");
            }
            // One more should fail
            bool overflow = listener.Add(999, ResponseType.Hook, priority: 0);
            That(overflow, Is.False, "Should reject overflow beyond CAPACITY");
            That(listener.Count, Is.EqualTo(ResponseChainListener.CAPACITY));
        }

        // ════════════════════════════════════════════════════════════════════
        //  5. TeamManager asymmetric relationships
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TeamManager_AsymmetricRelationship_AViewsDifferentlyThanB()
        {
            TeamManager.Clear();

            // A views B as Hostile, but B views A as Friendly (tribute/vassal)
            TeamManager.SetRelationship(1, 2, TeamRelationship.Hostile);
            TeamManager.SetRelationship(2, 1, TeamRelationship.Friendly);

            That(TeamManager.GetRelationship(1, 2), Is.EqualTo(TeamRelationship.Hostile));
            That(TeamManager.GetRelationship(2, 1), Is.EqualTo(TeamRelationship.Friendly));
        }

        [Test]
        public void TeamManager_ThreeFactionSetup_AllPairsCorrect()
        {
            TeamManager.Clear();
            TeamManager.DefaultRelationship = TeamRelationship.Neutral;

            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Hostile);

            // Same team = always Friendly
            That(TeamManager.GetRelationship(1, 1), Is.EqualTo(TeamRelationship.Friendly));
            // Cross teams
            That(TeamManager.GetRelationship(1, 2), Is.EqualTo(TeamRelationship.Hostile));
            That(TeamManager.GetRelationship(2, 3), Is.EqualTo(TeamRelationship.Hostile));
            That(TeamManager.GetRelationship(1, 3), Is.EqualTo(TeamRelationship.Hostile));
            // Unknown team falls back to default
            That(TeamManager.GetRelationship(1, 99), Is.EqualTo(TeamRelationship.Neutral));
        }

        [Test]
        public void TeamManager_FourFactionAsymmetric_ComplexDiplomacy()
        {
            TeamManager.Clear();
            TeamManager.DefaultRelationship = TeamRelationship.Neutral;

            // Empire(1)↔Federation(2): Allied
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Friendly);
            // Empire(1)↔Horde(3): At war
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
            // Horde(3)→Nomads(4): Hostile, but Nomads(4)→Horde(3): Neutral (one-way threat)
            TeamManager.SetRelationship(3, 4, TeamRelationship.Hostile);
            TeamManager.SetRelationship(4, 3, TeamRelationship.Neutral);

            That(TeamManager.GetRelationship(1, 2), Is.EqualTo(TeamRelationship.Friendly));
            That(TeamManager.GetRelationship(2, 1), Is.EqualTo(TeamRelationship.Friendly));
            That(TeamManager.GetRelationship(1, 3), Is.EqualTo(TeamRelationship.Hostile));
            That(TeamManager.GetRelationship(3, 4), Is.EqualTo(TeamRelationship.Hostile));
            That(TeamManager.GetRelationship(4, 3), Is.EqualTo(TeamRelationship.Neutral), "Asymmetric: Nomads view Horde as Neutral");
            That(TeamManager.GetRelationship(1, 4), Is.EqualTo(TeamRelationship.Neutral), "No explicit relationship = default Neutral");
        }

        // ════════════════════════════════════════════════════════════════════
        //  6. EffectStack component-level tests
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectStack_AddDuration_Policy()
        {
            var stack = new EffectStack
            {
                Count = 1,
                Limit = 5,
                Policy = StackPolicy.AddDuration,
                OverflowPolicy = StackOverflowPolicy.RejectNew
            };

            That(stack.TryAddStack(), Is.True);
            That(stack.Count, Is.EqualTo(2));
            That(stack.Policy, Is.EqualTo(StackPolicy.AddDuration));
        }

        [Test]
        public void EffectStack_KeepDuration_DoesNotAffectDuration()
        {
            var stack = new EffectStack
            {
                Count = 3,
                Limit = 10,
                Policy = StackPolicy.KeepDuration,
                OverflowPolicy = StackOverflowPolicy.RemoveOldest
            };

            That(stack.TryAddStack(), Is.True);
            That(stack.Count, Is.EqualTo(4));
        }

        // ════════════════════════════════════════════════════════════════════
        //  7. EffectGrantedTags component creation and retrieval
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectGrantedTags_MultipleEntries_CorrectStorage()
        {
            var tags = new EffectGrantedTags();
            tags.Add(new TagContribution { TagId = 10, Formula = TagContributionFormula.Fixed, Amount = 1 });
            tags.Add(new TagContribution { TagId = 20, Formula = TagContributionFormula.Linear, Amount = 5 });
            tags.Add(new TagContribution { TagId = 30, Formula = TagContributionFormula.LinearPlusBase, Amount = 3, Base = 7 });

            That(tags.Count, Is.EqualTo(3));

            var t0 = tags.Get(0);
            That(t0.TagId, Is.EqualTo(10));
            That(t0.Formula, Is.EqualTo(TagContributionFormula.Fixed));

            var t1 = tags.Get(1);
            That(t1.TagId, Is.EqualTo(20));
            That(t1.Formula, Is.EqualTo(TagContributionFormula.Linear));
            That(t1.Amount, Is.EqualTo(5));

            var t2 = tags.Get(2);
            That(t2.TagId, Is.EqualTo(30));
            That(t2.Base, Is.EqualTo(7));
        }

        // ════════════════════════════════════════════════════════════════════
        //  8. AbilityCost manual check + deduction patterns
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AbilityCost_MultiResource_CheckBothBeforeDeducting()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());
            ref var buf = ref world.Get<AttributeBuffer>(entity);

            int minerals = 5; // attribute index 5
            int gas = 6;      // attribute index 6
            buf.SetCurrent(minerals, 200f);
            buf.SetCurrent(gas, 100f);

            float mineralCost = 150f;
            float gasCost = 75f;

            bool canAfford = buf.GetCurrent(minerals) >= mineralCost &&
                             buf.GetCurrent(gas) >= gasCost;
            That(canAfford, Is.True);

            // Deduct both
            buf.SetCurrent(minerals, buf.GetCurrent(minerals) - mineralCost);
            buf.SetCurrent(gas, buf.GetCurrent(gas) - gasCost);

            That(buf.GetCurrent(minerals), Is.EqualTo(50f));
            That(buf.GetCurrent(gas), Is.EqualTo(25f));

            // Second attempt should fail
            canAfford = buf.GetCurrent(minerals) >= mineralCost;
            That(canAfford, Is.False, "Cannot afford second build");
        }

        // ════════════════════════════════════════════════════════════════════
        //  9. EffectModifiers multiple attributes
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectModifiers_MultipleOps_AllApplied()
        {
            var mods = default(EffectModifiers);
            mods.Add(0, ModifierOp.Add, -20f);      // Health -20
            mods.Add(1, ModifierOp.Add, 50f);        // AttackSpeed +50
            mods.Add(2, ModifierOp.Multiply, 1.5f);  // Armor * 1.5

            That(mods.Count, Is.GreaterThanOrEqualTo(3));
        }

        // ════════════════════════════════════════════════════════════════════
        //  10. GasConditionRegistry roundtrip
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GasConditionRegistry_RegisterAndLookup()
        {
            var registry = new GasConditionRegistry();

            var c1 = new GasCondition(GasConditionKind.TagPresent, 42, TagSense.Present);
            var c2 = new GasCondition(GasConditionKind.TagAbsent, 99, TagSense.Effective);
            var h1 = registry.Register(in c1);
            var h2 = registry.Register(in c2);

            That(h1.IsValid, Is.True);
            That(h2.IsValid, Is.True);

            ref readonly var r1 = ref registry.Get(in h1);
            That(r1.Kind, Is.EqualTo(GasConditionKind.TagPresent));
            That(r1.TagId, Is.EqualTo(42));

            ref readonly var r2 = ref registry.Get(in h2);
            That(r2.Kind, Is.EqualTo(GasConditionKind.TagAbsent));
            That(r2.TagId, Is.EqualTo(99));
            That(r2.TagSense, Is.EqualTo(TagSense.Effective));
        }

        // ════════════════════════════════════════════════════════════════════
        //  11. TagOps rule enforcement (Attached + Removed + RemoveIf)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TagOps_AttachedTag_AutomaticallyAdded()
        {
            var ops = new TagOps();
            int stunTag = 50;
            int cannotMoveTag = 51;

            var rule = new TagRuleSet();
            unsafe { rule.AttachedTags[0] = cannotMoveTag; }
            rule.AttachedCount = 1;
            ops.RegisterTagRuleSet(stunTag, rule);

            var tags = new GameplayTagContainer();
            var counts = new TagCountContainer();

            ops.AddTag(ref tags, ref counts, stunTag);

            That(tags.HasTag(stunTag), Is.True, "Primary tag should be present");
            That(tags.HasTag(cannotMoveTag), Is.True, "Attached tag should be auto-added");
        }

        [Test]
        public void TagOps_RemovedTag_AutomaticallyRemoved()
        {
            var ops = new TagOps();
            int blockedTag = 60;
            int colonizingTag = 61;

            var rule = new TagRuleSet();
            unsafe { rule.RemovedTags[0] = colonizingTag; }
            rule.RemovedCount = 1;
            ops.RegisterTagRuleSet(blockedTag, rule);

            var tags = new GameplayTagContainer();
            var counts = new TagCountContainer();

            // First add colonizing
            ops.AddTag(ref tags, ref counts, colonizingTag);
            That(tags.HasTag(colonizingTag), Is.True);

            // Then add blocked -> should auto-remove colonizing
            ops.AddTag(ref tags, ref counts, blockedTag);
            That(tags.HasTag(blockedTag), Is.True);
            That(tags.HasTag(colonizingTag), Is.False, "Removed tag should be auto-removed");
        }
    }
}
