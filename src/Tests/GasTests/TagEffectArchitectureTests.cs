using System;
using System.IO;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Comprehensive tests for the Tag-Effect Architecture:
    ///   - EffectLifetimeKind precision (only 3 values)
    ///   - LifetimeFlags
    ///   - TagContribution + TagContributionFormula
    ///   - EffectGrantedTags component
    ///   - EffectTagContributionHelper (Grant/Revoke/Update)
    ///   - EffectStack with policies
    ///   - ExpireCondition config parsing
    ///   - GrantedTags config parsing
    ///   - Stack config parsing
    ///   - BuiltinHandlers.RegisterAll
    ///   - Integration: tag grant on apply, tag revoke on expire
    ///   - Integration: stack merge + tag update
    /// </summary>
    [TestFixture]
    public class TagEffectArchitectureTests
    {
        private readonly TagOps _tagOps = new TagOps();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        // ════════════════════════════════════════════════════════════════════
        //  1. EffectLifetimeKind — only 3 values remain
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectLifetimeKind_HasExactlyThreeValues()
        {
            var values = Enum.GetValues(typeof(EffectLifetimeKind));
            That(values.Length, Is.EqualTo(3));
            That(Enum.IsDefined(typeof(EffectLifetimeKind), (byte)0), Is.True); // Instant
            That(Enum.IsDefined(typeof(EffectLifetimeKind), (byte)1), Is.True); // After
            That(Enum.IsDefined(typeof(EffectLifetimeKind), (byte)2), Is.True); // Infinite
            That(Enum.IsDefined(typeof(EffectLifetimeKind), (byte)3), Is.False); // UntilTagRemoved removed
            That(Enum.IsDefined(typeof(EffectLifetimeKind), (byte)4), Is.False); // WhileTagPresent removed
        }

        [Test]
        public void LifetimeFlags_All_OnlyCoversThreeKinds()
        {
            var all = LifetimeFlags.All;
            That(all.Allows(EffectLifetimeKind.Instant), Is.True);
            That(all.Allows(EffectLifetimeKind.After), Is.True);
            That(all.Allows(EffectLifetimeKind.Infinite), Is.True);
            // Bit 3 and 4 should not be set
            That(((byte)all & 0b11000), Is.EqualTo(0));
        }

        // ════════════════════════════════════════════════════════════════════
        //  2. TagContribution + TagContributionFormula
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TagContribution_Fixed_ReturnsAmount()
        {
            var tc = new TagContribution { Formula = TagContributionFormula.Fixed, Amount = 10 };
            That(tc.Compute(1), Is.EqualTo(10));
            That(tc.Compute(5), Is.EqualTo(10)); // Fixed ignores stack count
            That(tc.Compute(0), Is.EqualTo(10));
        }

        [Test]
        public void TagContribution_Linear_ReturnsStackTimesAmount()
        {
            var tc = new TagContribution { Formula = TagContributionFormula.Linear, Amount = 6 };
            That(tc.Compute(5), Is.EqualTo(30));
            That(tc.Compute(10), Is.EqualTo(60));
            That(tc.Compute(0), Is.EqualTo(0));
        }

        [Test]
        public void TagContribution_LinearPlusBase_ReturnsBasePlusStackTimesAmount()
        {
            var tc = new TagContribution { Formula = TagContributionFormula.LinearPlusBase, Amount = 7, Base = 3 };
            That(tc.Compute(0), Is.EqualTo(3));
            That(tc.Compute(1), Is.EqualTo(10));
            That(tc.Compute(10), Is.EqualTo(73));
        }

        [Test]
        public void TagContribution_GraphProgram_ReturnsZero()
        {
            var tc = new TagContribution { Formula = TagContributionFormula.GraphProgram, Amount = 99 };
            That(tc.Compute(5), Is.EqualTo(0)); // Graph handled externally
        }

        // ════════════════════════════════════════════════════════════════════
        //  3. EffectGrantedTags component (inline storage)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectGrantedTags_AddAndGet_RoundTrips()
        {
            var tags = new EffectGrantedTags();
            tags.Add(new TagContribution { TagId = 100, Formula = TagContributionFormula.Linear, Amount = 6, Base = 0 });
            tags.Add(new TagContribution { TagId = 200, Formula = TagContributionFormula.Fixed, Amount = 1, Base = 0 });

            That(tags.Count, Is.EqualTo(2));
            var first = tags.Get(0);
            That(first.TagId, Is.EqualTo(100));
            That(first.Formula, Is.EqualTo(TagContributionFormula.Linear));
            That(first.Amount, Is.EqualTo(6));

            var second = tags.Get(1);
            That(second.TagId, Is.EqualTo(200));
            That(second.Formula, Is.EqualTo(TagContributionFormula.Fixed));
            That(second.Amount, Is.EqualTo(1));
        }

        [Test]
        public void EffectGrantedTags_MaxCapacity_DoesNotOverflow()
        {
            var tags = new EffectGrantedTags();
            for (int i = 0; i < EffectGrantedTags.MAX_GRANTS + 5; i++)
            {
                tags.Add(new TagContribution { TagId = i, Formula = TagContributionFormula.Fixed, Amount = 1 });
            }
            That(tags.Count, Is.EqualTo(EffectGrantedTags.MAX_GRANTS));
        }

        // ════════════════════════════════════════════════════════════════════
        //  4. EffectTagContributionHelper — Grant / Revoke / Update
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Helper_Grant_AddsTagCounts()
        {
            var tags = new EffectGrantedTags();
            tags.Add(new TagContribution { TagId = 10, Formula = TagContributionFormula.Linear, Amount = 6 });
            tags.Add(new TagContribution { TagId = 20, Formula = TagContributionFormula.Fixed, Amount = 1 });

            var container = new TagCountContainer();
            EffectTagContributionHelper.Grant(in tags, ref container, stackCount: 5);

            That(container.GetCount(10), Is.EqualTo(30)); // 5 * 6
            That(container.GetCount(20), Is.EqualTo(1));   // Fixed
        }

        [Test]
        public void Helper_Revoke_RemovesTagCounts()
        {
            var tags = new EffectGrantedTags();
            tags.Add(new TagContribution { TagId = 10, Formula = TagContributionFormula.Linear, Amount = 6 });

            var container = new TagCountContainer();
            EffectTagContributionHelper.Grant(in tags, ref container, stackCount: 5);
            That(container.GetCount(10), Is.EqualTo(30));

            EffectTagContributionHelper.Revoke(in tags, ref container, stackCount: 5);
            That(container.GetCount(10), Is.EqualTo(0));
        }

        [Test]
        public void Helper_Update_AdjustsTagCountsDelta()
        {
            var tags = new EffectGrantedTags();
            tags.Add(new TagContribution { TagId = 10, Formula = TagContributionFormula.Linear, Amount = 6 });

            var container = new TagCountContainer();
            EffectTagContributionHelper.Grant(in tags, ref container, stackCount: 3);
            That(container.GetCount(10), Is.EqualTo(18)); // 3 * 6

            // Stack 3 → 5
            EffectTagContributionHelper.Update(in tags, ref container, oldStackCount: 3, newStackCount: 5);
            That(container.GetCount(10), Is.EqualTo(30)); // 5 * 6 (delta +12)

            // Stack 5 → 2
            EffectTagContributionHelper.Update(in tags, ref container, oldStackCount: 5, newStackCount: 2);
            That(container.GetCount(10), Is.EqualTo(12)); // 2 * 6 (delta -18)
        }

        [Test]
        public unsafe void TagOps_AddTag_WhenTagCountContainerOverflows_IncrementsBudgetAndDoesNotMutate()
        {
            var budget = new GasBudget();
            var tagOps = new TagOps(new TagRuleRegistry(), budget);

            GameplayTagContainer tags = default;
            TagCountContainer counts = default;

            for (int tagId = 1; tagId <= TagCountContainer.CAPACITY; tagId++)
            {
                tagOps.AddTag(ref tags, ref counts, tagId);
            }

            int overflowTagId = TagCountContainer.CAPACITY + 1;
            var ex = Throws<InvalidOperationException>(() => tagOps.AddTag(ref tags, ref counts, overflowTagId));
            That(ex.Message, Is.EqualTo("GAS.TAG.ERR.TagCountOverflow"));
            That(budget.TagCountOverflowDropped, Is.EqualTo(1));

            That(tags.HasTag(overflowTagId), Is.False);
            That(counts.GetCount(overflowTagId), Is.EqualTo(0));
        }

        [Test]
        public void Helper_MultiTag_StackScenario()
        {
            // Scenario from plan: effectA Linear(6), effectB Linear(7)
            var tagsA = new EffectGrantedTags();
            tagsA.Add(new TagContribution { TagId = 1, Formula = TagContributionFormula.Linear, Amount = 6 });

            var tagsB = new EffectGrantedTags();
            tagsB.Add(new TagContribution { TagId = 1, Formula = TagContributionFormula.Linear, Amount = 7 });

            var container = new TagCountContainer();

            // 5 layers effectA: 30 tags
            EffectTagContributionHelper.Grant(in tagsA, ref container, stackCount: 5);
            That(container.GetCount(1), Is.EqualTo(30));

            // 10 layers effectB: +70 tags = 100 total
            EffectTagContributionHelper.Grant(in tagsB, ref container, stackCount: 10);
            That(container.GetCount(1), Is.EqualTo(100));

            // Revoke effectA (5 layers): 100 - 30 = 70
            EffectTagContributionHelper.Revoke(in tagsA, ref container, stackCount: 5);
            That(container.GetCount(1), Is.EqualTo(70));
        }

        // ════════════════════════════════════════════════════════════════════
        //  5. EffectStack with policies
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void EffectStack_TryAddStack_IncreasesCount()
        {
            var stack = new EffectStack { Count = 1, Limit = 5, Policy = StackPolicy.RefreshDuration };
            That(stack.TryAddStack(), Is.True);
            That(stack.Count, Is.EqualTo(2));
        }

        [Test]
        public void EffectStack_RejectNew_AtLimit()
        {
            var stack = new EffectStack { Count = 5, Limit = 5, Policy = StackPolicy.RefreshDuration, OverflowPolicy = StackOverflowPolicy.RejectNew };
            That(stack.TryAddStack(), Is.False);
            That(stack.Count, Is.EqualTo(5));
        }

        [Test]
        public void EffectStack_RemoveOldest_AtLimit()
        {
            var stack = new EffectStack { Count = 5, Limit = 5, Policy = StackPolicy.RefreshDuration, OverflowPolicy = StackOverflowPolicy.RemoveOldest };
            That(stack.TryAddStack(), Is.True);
            That(stack.Count, Is.EqualTo(5)); // count stays (removed one + added one)
        }

        [Test]
        public void EffectStack_NoLimit_AllowsUnlimited()
        {
            var stack = new EffectStack { Count = 999, Limit = 0, Policy = StackPolicy.KeepDuration };
            That(stack.TryAddStack(), Is.True);
            That(stack.Count, Is.EqualTo(1000));
        }

        // ════════════════════════════════════════════════════════════════════
        //  6. BuiltinHandlers.RegisterAll
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void BuiltinHandlers_RegisterAll_RegistersAllSeven()
        {
            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            That(registry.IsRegistered(BuiltinHandlerId.ApplyModifiers), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.ApplyForce), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.SpatialQuery), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.DispatchPayload), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.ReResolveAndDispatch), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.CreateProjectile), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.CreateUnit), Is.True);
        }

        [Test]
        public void BuiltinHandlers_ApplyModifiers_AppliesModifiersToTarget()
        {
            using var world = World.Create();
            var target = world.Create(new AttributeBuffer());
            var effect = world.Create();

            var ctx = new EffectContext { Source = effect, Target = target };
            var tpl = new EffectTemplateData();
            tpl.Modifiers = new EffectModifiers();
            // Use attrId 1 for testing
            tpl.Modifiers.Add(1, ModifierOp.Add, 42f);

            var mergedParams = new EffectConfigParams();
            BuiltinHandlers.HandleApplyModifiers(world, effect, ref ctx, in mergedParams, in tpl);

            ref var attrBuf = ref world.Get<AttributeBuffer>(target);
            That(attrBuf.GetCurrent(1), Is.EqualTo(42f));
        }

        [Test]
        public void BuiltinHandlers_ApplyForce_WritesForceToAttributes()
        {
            using var world = World.Create();
            int fxKey = EffectParamKeys.ForceXAttribute;
            int fyKey = EffectParamKeys.ForceYAttribute;
            That(fxKey, Is.Not.EqualTo(0), "EffectParamKeys must be initialized");
            That(fyKey, Is.Not.EqualTo(0), "EffectParamKeys must be initialized");

            var target = world.Create(new AttributeBuffer());
            var effect = world.Create();

            var ctx = new EffectContext { Source = effect, Target = target };
            var tpl = new EffectTemplateData { PresetAttribute0 = 5, PresetAttribute1 = 6 };

            var mergedParams = new EffectConfigParams();
            mergedParams.TryAddFloat(fxKey, 10f);
            mergedParams.TryAddFloat(fyKey, -3f);

            BuiltinHandlers.HandleApplyForce(world, effect, ref ctx, in mergedParams, in tpl);

            ref var attrBuf = ref world.Get<AttributeBuffer>(target);
            That(attrBuf.GetCurrent(5), Is.EqualTo(10f));
            That(attrBuf.GetCurrent(6), Is.EqualTo(-3f));
        }

        [Test]
        public void BuiltinHandlers_CreateProjectile_CreatesEntity()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var effect = world.Create();

            var ctx = new EffectContext { Source = caster, Target = target };
            var tpl = new EffectTemplateData();
            tpl.Projectile = new ProjectileDescriptor { Speed = 500, Range = 1000, ArcHeight = 0, ImpactEffectTemplateId = 42 };

            var mergedParams = new EffectConfigParams();
            BuiltinHandlers.HandleCreateProjectile(world, effect, ref ctx, in mergedParams, in tpl);

            // Verify a ProjectileState entity was created
            var query = new QueryDescription().WithAll<ProjectileState>();
            int count = 0;
            world.Query(in query, (ref ProjectileState ps) =>
            {
                count++;
                That(ps.Speed, Is.EqualTo(500));
                That(ps.ImpactEffectTemplateId, Is.EqualTo(42));
            });
            That(count, Is.EqualTo(1));
        }

        [Test]
        public void BuiltinHandlers_CreateUnit_CreatesEntities()
        {
            using var world = World.Create();
            var caster = world.Create();
            var effect = world.Create();

            var ctx = new EffectContext { Source = caster, Target = caster };
            var tpl = new EffectTemplateData();
            tpl.UnitCreation = new UnitCreationDescriptor { UnitTypeId = 7, Count = 3, OffsetRadius = 100, OnSpawnEffectTemplateId = 55 };

            var mergedParams = new EffectConfigParams();
            BuiltinHandlers.HandleCreateUnit(world, effect, ref ctx, in mergedParams, in tpl);

            var query = new QueryDescription().WithAll<SpawnedUnitState>();
            int count = 0;
            world.Query(in query, (ref SpawnedUnitState su) =>
            {
                count++;
                That(su.UnitTypeId, Is.EqualTo(7));
                That(su.OnSpawnEffectTemplateId, Is.EqualTo(55));
            });
            That(count, Is.EqualTo(3));
        }

        // ════════════════════════════════════════════════════════════════════
        //  7. ExpireCondition Config Parsing
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ExpireCondition_Loader_ParsesTagPresentCondition()
        {
            var conditions = new GasConditionRegistry();
            var templates = new EffectTemplateRegistry();
            var pipeline = CreateMinimalPipeline(
                @"{
                    ""id"": ""test_buff"",
                    ""tags"": [""Test.Buff""],
                    ""presetType"": ""None"",
                    ""lifetime"": ""After"",
                    ""duration"": { ""durationTicks"": 100 },
                    ""expireCondition"": { ""kind"": ""TagPresent"", ""tag"": ""Status.Shield"" }
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load("GAS/effects.json");

            int tplId = EffectTemplateIdRegistry.GetId("test_buff");
            That(tplId, Is.GreaterThan(0));
            That(templates.TryGetRef(tplId, out int idx), Is.True);
            ref readonly var tpl = ref templates.GetRef(idx);
            That(tpl.ExpireCondition.IsValid, Is.True);

            ref readonly var cond = ref conditions.Get(tpl.ExpireCondition);
            That(cond.Kind, Is.EqualTo(GasConditionKind.TagPresent));
        }

        // ════════════════════════════════════════════════════════════════════
        //  8. GrantedTags Config Parsing
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GrantedTags_Loader_ParsesLinearFormula()
        {
            var conditions = new GasConditionRegistry();
            var templates = new EffectTemplateRegistry();
            var pipeline = CreateMinimalPipeline(
                @"{
                    ""id"": ""test_slow"",
                    ""tags"": [""Test.Slow""],
                    ""presetType"": ""None"",
                    ""lifetime"": ""After"",
                    ""duration"": { ""durationTicks"": 60 },
                    ""grantedTags"": [
                        { ""tag"": ""Status.Slow"", ""formula"": ""Linear"", ""amount"": 6 },
                        { ""tag"": ""Status.Weak"", ""formula"": ""Fixed"", ""amount"": 1 }
                    ]
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load("GAS/effects.json");

            int tplId = EffectTemplateIdRegistry.GetId("test_slow");
            That(tplId, Is.GreaterThan(0));
            That(templates.TryGetRef(tplId, out int idx), Is.True);
            ref readonly var tpl = ref templates.GetRef(idx);
            That(tpl.GrantedTags.Count, Is.EqualTo(2));

            var first = tpl.GrantedTags.Get(0);
            That(first.Formula, Is.EqualTo(TagContributionFormula.Linear));
            That(first.Amount, Is.EqualTo(6));

            var second = tpl.GrantedTags.Get(1);
            That(second.Formula, Is.EqualTo(TagContributionFormula.Fixed));
            That(second.Amount, Is.EqualTo(1));
        }

        // ════════════════════════════════════════════════════════════════════
        //  9. Stack Config Parsing
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void StackConfig_Loader_ParsesRefreshDuration()
        {
            var conditions = new GasConditionRegistry();
            var templates = new EffectTemplateRegistry();
            var pipeline = CreateMinimalPipeline(
                @"{
                    ""id"": ""test_stackable"",
                    ""tags"": [""Test.Stackable""],
                    ""presetType"": ""None"",
                    ""lifetime"": ""After"",
                    ""duration"": { ""durationTicks"": 120 },
                    ""stack"": { ""limit"": 10, ""policy"": ""RefreshDuration"", ""overflowPolicy"": ""RejectNew"" }
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load("GAS/effects.json");

            int tplId = EffectTemplateIdRegistry.GetId("test_stackable");
            That(tplId, Is.GreaterThan(0));
            That(templates.TryGetRef(tplId, out int idx), Is.True);
            ref readonly var tpl = ref templates.GetRef(idx);
            That(tpl.HasStackPolicy, Is.True);
            That(tpl.StackPolicy, Is.EqualTo(StackPolicy.RefreshDuration));
            That(tpl.StackOverflowPolicy, Is.EqualTo(StackOverflowPolicy.RejectNew));
            That(tpl.StackLimit, Is.EqualTo(10));
        }

        // ════════════════════════════════════════════════════════════════════
        //  10. Integration: Tag Grant on Effect Apply + Revoke on Expire
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Integration_GrantedTags_GrantOnApply_RevokeOnExpire()
        {
            using var world = World.Create();

            // Create target entity with TagCountContainer
            var target = world.Create(new TagCountContainer());

            // Create a duration effect entity with EffectGrantedTags
            var grantedTags = new EffectGrantedTags();
            int slowTagId = 42;
            grantedTags.Add(new TagContribution { TagId = slowTagId, Formula = TagContributionFormula.Linear, Amount = 6 });

            var effectEntity = world.Create(
                new GameplayEffect { LifetimeKind = EffectLifetimeKind.After, TotalTicks = 100, RemainingTicks = 100 },
                new EffectContext { Source = default, Target = target },
                grantedTags
            );

            // Simulate Grant (OnApply)
            int stackCount = 1;
            ref readonly var gt = ref world.Get<EffectGrantedTags>(effectEntity);
            ref var tagCounts = ref world.Get<TagCountContainer>(target);
            EffectTagContributionHelper.Grant(in gt, ref tagCounts, stackCount);

            That(tagCounts.GetCount(slowTagId), Is.EqualTo(6));

            // Simulate Revoke (OnExpire)
            EffectTagContributionHelper.Revoke(in gt, ref tagCounts, stackCount);
            That(tagCounts.GetCount(slowTagId), Is.EqualTo(0));
        }

        [Test]
        public void Integration_StackChange_UpdatesTagCounts()
        {
            using var world = World.Create();
            var target = world.Create(new TagCountContainer());

            int slowTagId = 42;
            var grantedTags = new EffectGrantedTags();
            grantedTags.Add(new TagContribution { TagId = slowTagId, Formula = TagContributionFormula.Linear, Amount = 6 });

            var effectEntity = world.Create(
                new GameplayEffect { LifetimeKind = EffectLifetimeKind.After, TotalTicks = 100, RemainingTicks = 100 },
                new EffectContext { Source = default, Target = target },
                grantedTags,
                new EffectStack { Count = 3, Limit = 10, Policy = StackPolicy.RefreshDuration }
            );

            // Initial grant at stack 3
            ref readonly var gt = ref world.Get<EffectGrantedTags>(effectEntity);
            ref var tagCounts = ref world.Get<TagCountContainer>(target);
            EffectTagContributionHelper.Grant(in gt, ref tagCounts, stackCount: 3);
            That(tagCounts.GetCount(slowTagId), Is.EqualTo(18)); // 3 * 6

            // Stack 3 → 5
            EffectTagContributionHelper.Update(in gt, ref tagCounts, oldStackCount: 3, newStackCount: 5);
            That(tagCounts.GetCount(slowTagId), Is.EqualTo(30)); // 5 * 6

            // Revoke all at stack 5
            EffectTagContributionHelper.Revoke(in gt, ref tagCounts, stackCount: 5);
            That(tagCounts.GetCount(slowTagId), Is.EqualTo(0));
        }

        [Test]
        public void Integration_TwoEffects_SameTag_DifferentFormulas()
        {
            // Plan scenario: 5 layers effectA (Linear*6=30) + 10 layers effectB (Linear*7=70) = 100
            var container = new TagCountContainer();
            int tagId = 99;

            var tagsA = new EffectGrantedTags();
            tagsA.Add(new TagContribution { TagId = tagId, Formula = TagContributionFormula.Linear, Amount = 6 });

            var tagsB = new EffectGrantedTags();
            tagsB.Add(new TagContribution { TagId = tagId, Formula = TagContributionFormula.Linear, Amount = 7 });

            EffectTagContributionHelper.Grant(in tagsA, ref container, stackCount: 5);
            EffectTagContributionHelper.Grant(in tagsB, ref container, stackCount: 10);
            That(container.GetCount(tagId), Is.EqualTo(100));

            // effectA expires, revoke its 30
            EffectTagContributionHelper.Revoke(in tagsA, ref container, stackCount: 5);
            That(container.GetCount(tagId), Is.EqualTo(70));

            // effectB expires, revoke its 70
            EffectTagContributionHelper.Revoke(in tagsB, ref container, stackCount: 10);
            That(container.GetCount(tagId), Is.EqualTo(0));
        }

        // ════════════════════════════════════════════════════════════════════
        //  11. GasCondition Evaluator — tag presence/absence
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GasConditionEvaluator_TagPresent_ExpiresWhenTagRemoved()
        {
            using var world = World.Create();
            int tagId = 77;

            // Entity has the tag initially
            var tagContainer = new GameplayTagContainer();
            tagContainer.AddTag(tagId);
            var target = world.Create(tagContainer);

            var condition = new GasCondition(GasConditionKind.TagPresent, tagId, TagSense.Present);

            // Tag present → should NOT expire
            That(GasConditionEvaluator.ShouldExpire(world, target, in condition, _tagOps), Is.False);

            // Remove the tag
            ref var tc = ref world.Get<GameplayTagContainer>(target);
            tc.RemoveTag(tagId);

            // Tag absent → should expire
            That(GasConditionEvaluator.ShouldExpire(world, target, in condition, _tagOps), Is.True);
        }

        [Test]
        public void GasConditionEvaluator_TagAbsent_ExpiresWhenTagAppears()
        {
            using var world = World.Create();
            int tagId = 88;

            var target = world.Create(new GameplayTagContainer());

            var condition = new GasCondition(GasConditionKind.TagAbsent, tagId, TagSense.Present);

            // Tag absent → should NOT expire (condition wants tag to be absent)
            That(GasConditionEvaluator.ShouldExpire(world, target, in condition, _tagOps), Is.False);

            // Add the tag
            ref var tc = ref world.Get<GameplayTagContainer>(target);
            tc.AddTag(tagId);

            // Tag present → should expire (condition was "keep alive while tag absent")
            That(GasConditionEvaluator.ShouldExpire(world, target, in condition, _tagOps), Is.True);
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helper: create a minimal ConfigPipeline from a JSON effect string
        // ════════════════════════════════════════════════════════════════════

        private static ConfigPipeline CreateMinimalPipeline(string effectJson)
        {
            var json = "[" + effectJson + "]";
            var root = Path.Combine(Path.GetTempPath(), $"TagEffectTest_{Guid.NewGuid():N}");
            var gasDir = Path.Combine(root, "Configs", "GAS");
            Directory.CreateDirectory(gasDir);
            File.WriteAllText(Path.Combine(gasDir, "effects.json"), json);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }
    }
}
