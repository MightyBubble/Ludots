using System;
using System.IO;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;
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
        public void TagContribution_GraphProgram_ThrowsUntilEvaluatorIsWired()
        {
            var tc = new TagContribution { Formula = TagContributionFormula.GraphProgram, Amount = 99 };
            var ex = Throws<System.InvalidOperationException>(() => tc.Compute(5));
            That(ex!.Message, Does.Contain("GraphProgram"));
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
        public void BuiltinHandlers_RegisterAll_RegistersAllTen()
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
            That(registry.IsRegistered(BuiltinHandlerId.ApplyDisplacement), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.ApplyRelation), Is.True);
            That(registry.IsRegistered(BuiltinHandlerId.ExecuteExchange), Is.True);
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
            int attributeId = AttributeRegistry.Register($"Test.Builtin.ApplyModifiers.{Guid.NewGuid():N}");
            tpl.Modifiers.Add(attributeId, ModifierOp.Add, 42f);

            var mergedParams = new EffectConfigParams();
            BuiltinHandlers.HandleApplyModifiers(world, effect, ref ctx, in mergedParams, in tpl);

            ref var attrBuf = ref world.Get<AttributeBuffer>(target);
            That(attrBuf.GetCurrent(attributeId), Is.EqualTo(42f));
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
            const int forceXAttrId = AttributeBuffer.MAX_ATTRS - 2;
            const int forceYAttrId = AttributeBuffer.MAX_ATTRS - 1;

            var ctx = new EffectContext { Source = effect, Target = target };
            var tpl = new EffectTemplateData { PresetAttribute0 = forceXAttrId, PresetAttribute1 = forceYAttrId };

            var mergedParams = new EffectConfigParams();
            mergedParams.TryAddFloat(fxKey, 10f);
            mergedParams.TryAddFloat(fyKey, -3f);

            BuiltinHandlers.HandleApplyForce(world, effect, ref ctx, in mergedParams, in tpl);

            ref var attrBuf = ref world.Get<AttributeBuffer>(target);
            That(attrBuf.GetCurrent(forceXAttrId), Is.EqualTo(10f));
            That(attrBuf.GetCurrent(forceYAttrId), Is.EqualTo(-3f));
        }

        [Test]
        public void BuiltinHandlers_ApplyRelation_SetParentLinksAndSnapsSubjectToParentPosition()
        {
            using var world = World.Create();
            var parent = world.Create(
                WorldPositionCm.FromCm(1200, 800),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(1100, 700) });
            var child = world.Create(
                WorldPositionCm.FromCm(10, 20),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(5, 15) });
            var effect = world.Create();

            var ctx = new EffectContext { Source = child, Target = parent };
            var tpl = new EffectTemplateData
            {
                Relation = new RelationDescriptor
                {
                    Operation = RelationOperation.SetParent,
                    Subject = RelationEntitySlot.Source,
                    Parent = RelationEntitySlot.Target,
                    SnapSubjectToParentPosition = true
                }
            };

            var mergedParams = new EffectConfigParams();
            BuiltinHandlers.HandleApplyRelation(world, effect, ref ctx, in mergedParams, in tpl);

            That(world.Has<ChildOf>(child), Is.True);
            That(world.Get<ChildOf>(child).Parent, Is.EqualTo(parent));
            That(world.Has<ChildrenBuffer>(parent), Is.True);
            That(world.Get<ChildrenBuffer>(parent).Count, Is.EqualTo(1));
            That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(1200, 800)));
            That(world.Get<PreviousWorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(1100, 700)));
        }

        [Test]
        public void BuiltinHandlers_ApplyRelation_RemoveParentDetachesChild()
        {
            using var world = World.Create();
            var parent = world.Create();
            var child = world.Create();
            var effect = world.Create();

            RelationOps.SetParent(world, child, parent);
            That(world.Has<ChildOf>(child), Is.True);

            var ctx = new EffectContext { Source = child };
            var tpl = new EffectTemplateData
            {
                Relation = new RelationDescriptor
                {
                    Operation = RelationOperation.RemoveParent,
                    Subject = RelationEntitySlot.Source
                }
            };

            var mergedParams = new EffectConfigParams();
            BuiltinHandlers.HandleApplyRelation(world, effect, ref ctx, in mergedParams, in tpl);

            That(world.Has<ChildOf>(child), Is.False);
            That(world.Has<ChildrenBuffer>(parent), Is.True);
            That(world.Get<ChildrenBuffer>(parent).Count, Is.EqualTo(0));
        }

        [Test]
        public void BuiltinHandlers_CreateProjectile_EnqueuesAssemblySpawnRequest()
        {
            using var world = World.Create();
            var caster = world.Create(WorldPositionCm.FromCm(1200, 800));
            var target = world.Create();
            var effect = world.Create();
            var queue = new RuntimeEntitySpawnQueue(capacity: 4);
            var runtime = new BuiltinHandlerExecutionContext
            {
                SpawnRequests = queue,
            };
            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var ctx = new EffectContext { Source = caster, Target = target };
            var tpl = new EffectTemplateData();
            tpl.Projectile = new ProjectileDescriptor { Speed = 500, Range = 1000, ArcHeight = 0, ImpactEffectTemplateId = 42 };

            var mergedParams = new EffectConfigParams();
            registry.Invoke(BuiltinHandlerId.CreateProjectile, world, effect, ref ctx, in mergedParams, in tpl, runtime);

            That(queue.TryDequeue(out var request), Is.True);
            That(request.Kind, Is.EqualTo(RuntimeEntitySpawnKind.Assembly));
            That(request.HasProjectileState, Is.EqualTo(1));
            That(request.HasWorldPosition, Is.EqualTo(1));
            That(request.Projectile.Speed, Is.EqualTo(Fix64.FromInt(500)));
            That(request.Projectile.ImpactEffectTemplateId, Is.EqualTo(42));
            That(request.WorldPositionCm, Is.EqualTo(Fix64Vec2.FromInt(1200, 800)));
        }

        [Test]
        public void BuiltinHandlers_CreateProjectile_PreservesTargetPointAndLaunchOriginInQueuedRequest()
        {
            using var world = World.Create();
            var caster = world.Create(WorldPositionCm.FromCm(300, 500));
            var effect = world.Create();
            var queue = new RuntimeEntitySpawnQueue(capacity: 4);
            var runtime = new BuiltinHandlerExecutionContext
            {
                SpawnRequests = queue,
            };
            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var ctx = new EffectContext { Source = caster, Target = Entity.Null };
            var tpl = new EffectTemplateData();
            tpl.Projectile = new ProjectileDescriptor { Speed = 600, Range = 1200, ArcHeight = 0, ImpactEffectTemplateId = 0 };

            var mergedParams = new EffectConfigParams();
            mergedParams.TryAddFloat(EffectParamKeys.TargetPosX, 950f);
            mergedParams.TryAddFloat(EffectParamKeys.TargetPosY, 640f);

            registry.Invoke(BuiltinHandlerId.CreateProjectile, world, effect, ref ctx, in mergedParams, in tpl, runtime);

            That(queue.TryDequeue(out var request), Is.True);
            That(request.Projectile.HasLaunchOrigin, Is.EqualTo(1));
            That(request.Projectile.LaunchOriginCm.X.ToFloat(), Is.EqualTo(300f).Within(0.01f));
            That(request.Projectile.LaunchOriginCm.Y.ToFloat(), Is.EqualTo(500f).Within(0.01f));
            That(request.Projectile.HasTargetPoint, Is.EqualTo(1));
            That(request.Projectile.TargetPointCm.X.ToFloat(), Is.EqualTo(950f).Within(0.01f));
            That(request.Projectile.TargetPointCm.Y.ToFloat(), Is.EqualTo(640f).Within(0.01f));
        }

        [Test]
        public void ProjectileRuntimeSystem_TargetPointImpact_PublishesCallerParams()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var caster = world.Create(WorldPositionCm.FromCm(100, 200));
            var projectile = world.Create(
                new ProjectileState
                {
                    Speed = Fix64.FromInt(400),
                    Range = 1200,
                    ArcHeight = 0,
                    ImpactEffectTemplateId = 77,
                    Source = caster,
                    Target = Entity.Null,
                    LaunchOriginCm = Fix64Vec2.FromFloat(100f, 200f),
                    HasLaunchOrigin = 1,
                    TargetPointCm = Fix64Vec2.FromFloat(160f, 240f),
                    HasTargetPoint = 1,
                },
                WorldPositionCm.FromCm(100, 200),
                new PreviousWorldPositionCm { Value = WorldPositionCm.FromCm(100, 200).Value });

            using var system = new ProjectileRuntimeSystem(world, requests, spatialQueries: null);
            system.Update(0.2f);

            That(world.IsAlive(projectile), Is.False, "Projectile should despawn after reaching the preserved target point.");
            That(requests.Count, Is.EqualTo(1));

            var request = requests[0];
            That(request.TemplateId, Is.EqualTo(77));
            That(request.HasCallerParams, Is.True);
            That(request.CallerParams.TryGetFloat(EffectParamKeys.TargetOriginX, out float originX), Is.True);
            That(request.CallerParams.TryGetFloat(EffectParamKeys.TargetOriginY, out float originY), Is.True);
            That(request.CallerParams.TryGetFloat(EffectParamKeys.TargetPosX, out float targetX), Is.True);
            That(request.CallerParams.TryGetFloat(EffectParamKeys.TargetPosY, out float targetY), Is.True);
            That(originX, Is.EqualTo(100f).Within(0.01f));
            That(originY, Is.EqualTo(200f).Within(0.01f));
            That(targetX, Is.EqualTo(160f).Within(0.01f));
            That(targetY, Is.EqualTo(240f).Within(0.01f));
        }

        [Test]
        public void ProjectileRuntimeSystem_DestroyOnFirstHit_PublishesHitEffectAndDespawns()
        {
            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            try
            {
                using var world = World.Create();
                var requests = new EffectRequestQueue();
                var caster = world.Create(
                    WorldPositionCm.FromCm(0, 0),
                    new Team { Id = 1 });
                var hostile = world.Create(
                    WorldPositionCm.FromCm(260, 0),
                    new Team { Id = 2 });
                var bystander = world.Create(
                    WorldPositionCm.FromCm(520, 0),
                    new Team { Id = 2 });
                var projectile = world.Create(
                    new ProjectileState
                    {
                        Speed = Fix64.FromInt(1200),
                        Range = 900,
                        HitEffectTemplateId = 88,
                        PresentationEffectTemplateId = 88,
                        TravelMode = ProjectileTravelMode.Direction,
                        ImpactPolicy = ProjectileImpactPolicy.DestroyOnFirstHit,
                        CollisionHalfWidthCm = 60,
                        CollisionRelationFilter = RelationshipFilter.Hostile,
                        CollisionExcludeSource = 1,
                        MaxHitCount = 1,
                        Source = caster,
                        LaunchOriginCm = Fix64Vec2.Zero,
                        HasLaunchOrigin = 1,
                        Direction = Fix64Vec2.UnitX,
                        HasDirection = 1,
                    },
                    WorldPositionCm.FromCm(0, 0),
                    new PreviousWorldPositionCm { Value = WorldPositionCm.FromCm(0, 0).Value });

                using var system = new ProjectileRuntimeSystem(world, requests, new TestLineQueryService(hostile, bystander));
                system.Update(0.3f);

                That(world.IsAlive(projectile), Is.False);
                That(requests.Count, Is.EqualTo(1));
                That(requests[0].TemplateId, Is.EqualTo(88));
                That(requests[0].Target, Is.EqualTo(hostile));
            }
            finally
            {
                TeamManager.Clear();
            }
        }

        [Test]
        public void BuiltinHandlers_CreateUnit_EnqueuesRuntimeSpawnRequests()
        {
            using var world = World.Create();
            var caster = world.Create(WorldPositionCm.FromCm(1200, 3400));
            var effect = world.Create();
            var queue = new RuntimeEntitySpawnQueue(capacity: 8);
            var runtime = new BuiltinHandlerExecutionContext
            {
                SpawnRequests = queue,
            };
            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var ctx = new EffectContext { Source = caster, Target = caster };
            var tpl = new EffectTemplateData();
            tpl.UnitCreation = new UnitCreationDescriptor { UnitTypeId = 7, Count = 3, OffsetRadius = 100, OnSpawnEffectTemplateId = 55 };

            var mergedParams = new EffectConfigParams();
            registry.Invoke(BuiltinHandlerId.CreateUnit, world, effect, ref ctx, in mergedParams, in tpl, runtime);

            int count = 0;
            while (queue.TryDequeue(out var request))
            {
                count++;
                That(request.Kind, Is.EqualTo(RuntimeEntitySpawnKind.UnitType));
                That(request.UnitTypeId, Is.EqualTo(7));
                That(request.OnSpawnEffectTemplateId, Is.EqualTo(55));
                That(request.CopySourceTeam, Is.EqualTo(1));
                That(request.WorldPositionCm, Is.Not.EqualTo(Fix64Vec2.Zero));
            }
            That(count, Is.EqualTo(3));
        }

        [Test]
        public void BuiltinHandlers_CreateUnit_WithTemplate_EnqueuesTemplateSpawnRequests()
        {
            using var world = World.Create();
            var caster = world.Create(WorldPositionCm.FromCm(900, 1500));
            var effect = world.Create();
            var queue = new RuntimeEntitySpawnQueue(capacity: 8);
            var runtime = new BuiltinHandlerExecutionContext
            {
                SpawnRequests = queue,
            };
            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            var ctx = new EffectContext { Source = caster, Target = caster };
            var tpl = new EffectTemplateData();
            tpl.UnitCreation = new UnitCreationDescriptor
            {
                TemplateId = "test_manifest_wall",
                UseTemplateSpawn = true,
                Count = 2,
                OffsetRadius = 60,
                OnSpawnEffectTemplateId = 91,
                CopySourcePlayerOwner = true,
                LinkSourceAsParent = true,
            };

            var mergedParams = new EffectConfigParams();
            registry.Invoke(BuiltinHandlerId.CreateUnit, world, effect, ref ctx, in mergedParams, in tpl, runtime);

            int count = 0;
            while (queue.TryDequeue(out var request))
            {
                count++;
                That(request.Kind, Is.EqualTo(RuntimeEntitySpawnKind.Template));
                That(request.TemplateId, Is.EqualTo("test_manifest_wall"));
                That(request.OnSpawnEffectTemplateId, Is.EqualTo(91));
                That(request.CopySourceTeam, Is.EqualTo(1));
                That(request.CopySourcePlayerOwner, Is.EqualTo(1));
                That(request.LinkSourceAsParent, Is.EqualTo(1));
                That(request.WorldPositionCm, Is.Not.EqualTo(Fix64Vec2.Zero));
            }

            That(count, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeEntitySpawnSystem_SpawnUnitType_CreatesEntityAndPublishesOnSpawnEffect()
        {
            UnitTypeRegistry.Clear();
            int unitTypeId = UnitTypeRegistry.Register("TestWolf");

            using var world = World.Create();
            var source = world.Create(
                new Team { Id = 7 },
                new MapEntity { MapId = new Ludots.Core.Map.MapId("runtime_spawn_test") });
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var effects = new EffectRequestQueue();
            var templates = new DataRegistry<EntityTemplate>(CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }"));
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                stableIds,
                effects);

            That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.UnitType,
                Source = source,
                WorldPositionCm = Fix64Vec2.FromInt(420, 840),
                UnitTypeId = unitTypeId,
                OnSpawnEffectTemplateId = 123,
                CopySourceTeam = 1,
            }), Is.True);

            system.Update(0f);

            Entity spawned = Entity.Null;
            int spawnCount = 0;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm, PreviousWorldPositionCm, VisualTransform, CullState, AttributeBuffer>();
            world.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm position, ref PreviousWorldPositionCm previous, ref VisualTransform transform, ref CullState cull, ref AttributeBuffer buffer) =>
            {
                if (!string.Equals(name.Value, "Unit:TestWolf", StringComparison.Ordinal))
                {
                    return;
                }

                spawnCount++;
                spawned = entity;
                That(position.Value, Is.EqualTo(Fix64Vec2.FromInt(420, 840)));
                That(previous.Value, Is.EqualTo(Fix64Vec2.FromInt(420, 840)));
                That(transform.Scale, Is.EqualTo(System.Numerics.Vector3.One));
                That(cull.IsVisible, Is.False);
                That(cull.LOD, Is.EqualTo(Ludots.Core.Presentation.Components.LODLevel.Low));
            });

            That(spawnCount, Is.EqualTo(1));
            That(spawned, Is.Not.EqualTo(Entity.Null));
            That(world.Has<Team>(spawned), Is.True);
            That(world.Get<Team>(spawned).Id, Is.EqualTo(7));
            That(world.Has<MapEntity>(spawned), Is.True);
            That(world.Get<MapEntity>(spawned).MapId.Value, Is.EqualTo("runtime_spawn_test"));
            That(world.Has<Ludots.Core.Presentation.Components.PresentationStableId>(spawned), Is.True);
            That(world.Get<Ludots.Core.Presentation.Components.PresentationStableId>(spawned).Value, Is.GreaterThan(0));

            That(effects.Count, Is.EqualTo(1));
            That(effects[0].Source, Is.EqualTo(source));
            That(effects[0].Target, Is.EqualTo(spawned));
            That(effects[0].TemplateId, Is.EqualTo(123));

            UnitTypeRegistry.Clear();
        }

        [Test]
        public void RuntimeEntitySpawnSystem_SpawnTemplate_EmitsExplicitReceipt()
        {
            string templateJson = @"[
              {
                ""id"": ""test_receipt_template"",
                ""components"": {
                  ""Name"": { ""Value"": ""Template:Receipt"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""FacingDirection"": { ""AngleRad"": 0.0 },
                  ""AttributeBuffer"": { ""base"": {} },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }", templateJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            using var world = World.Create();
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 4);
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                stableIds,
                receipts: receipts);

            That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "test_receipt_template",
                WorldPositionCm = Fix64Vec2.FromInt(17, 23),
                HasWorldPosition = 1,
                ReceiptChannelId = 11,
                ReceiptId = 701,
                EmitReceipt = 1,
            }), Is.True);

            system.Update(0f);

            That(receipts.TryDequeue(out RuntimeEntitySpawnReceipt receipt), Is.True);
            That(receipt.ReceiptChannelId, Is.EqualTo(11));
            That(receipt.ReceiptId, Is.EqualTo(701));
            That(receipt.Kind, Is.EqualTo(RuntimeEntitySpawnKind.Template));
            That(receipt.TemplateId, Is.EqualTo("test_receipt_template"));
            That(world.IsAlive(receipt.Entity), Is.True);
            That(world.Get<WorldPositionCm>(receipt.Entity).Value, Is.EqualTo(Fix64Vec2.FromInt(17, 23)));
            That(receipts.Count, Is.EqualTo(0));
        }

        [Test]
        public void RuntimeEntitySpawnReceiptQueue_ChannelOperations_DoNotConsumeOtherChannels()
        {
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 4);

            That(receipts.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = 5,
                ReceiptId = 1,
                Kind = RuntimeEntitySpawnKind.Template,
                Entity = Entity.Null,
                TemplateId = "a",
            }), Is.True);
            That(receipts.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = 9,
                ReceiptId = 2,
                Kind = RuntimeEntitySpawnKind.Template,
                Entity = Entity.Null,
                TemplateId = "b",
            }), Is.True);
            That(receipts.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = 5,
                ReceiptId = 3,
                Kind = RuntimeEntitySpawnKind.Template,
                Entity = Entity.Null,
                TemplateId = "c",
            }), Is.True);

            That(receipts.Count, Is.EqualTo(3));
            That(receipts.CountForChannel(5), Is.EqualTo(2));
            That(receipts.CountForChannel(9), Is.EqualTo(1));

            That(receipts.TryDequeueForChannel(5, out RuntimeEntitySpawnReceipt first), Is.True);
            That(first.ReceiptId, Is.EqualTo(1));
            That(receipts.Count, Is.EqualTo(2));
            That(receipts.CountForChannel(5), Is.EqualTo(1));
            That(receipts.CountForChannel(9), Is.EqualTo(1));

            That(receipts.TryDequeueForChannel(5, out RuntimeEntitySpawnReceipt second), Is.True);
            That(second.ReceiptId, Is.EqualTo(3));
            That(receipts.TryDequeueForChannel(5, out _), Is.False);
            That(receipts.Count, Is.EqualTo(1));

            That(receipts.TryDequeue(out RuntimeEntitySpawnReceipt remaining), Is.True);
            That(remaining.ReceiptChannelId, Is.EqualTo(9));
            That(remaining.ReceiptId, Is.EqualTo(2));
            That(receipts.Count, Is.EqualTo(0));
        }

        [Test]
        public void RuntimeEntitySpawnSystem_SpawnAssembly_CreatesProjectileEntity()
        {
            using var world = World.Create();
            var source = world.Create(new MapEntity { MapId = new Ludots.Core.Map.MapId("assembly_spawn_test") });
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var effects = new EffectRequestQueue();
            var templates = new DataRegistry<EntityTemplate>(CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }"));
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                stableIds,
                effects);

            That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Assembly,
                Source = source,
                WorldPositionCm = Fix64Vec2.FromInt(150, 275),
                HasWorldPosition = 1,
                Projectile = new ProjectileState
                {
                    Speed = Fix64.FromInt(333),
                    Range = 900,
                    ImpactEffectTemplateId = 12,
                    Source = source,
                },
                HasProjectileState = 1,
            }), Is.True);

            system.Update(0f);

            int count = 0;
            var query = new QueryDescription().WithAll<ProjectileState, WorldPositionCm, PreviousWorldPositionCm, MapEntity, Ludots.Core.Presentation.Components.PresentationStableId>();
            world.Query(in query, (ref ProjectileState projectile, ref WorldPositionCm position, ref PreviousWorldPositionCm previous, ref MapEntity map, ref Ludots.Core.Presentation.Components.PresentationStableId stableId) =>
            {
                count++;
                That(projectile.Speed, Is.EqualTo(Fix64.FromInt(333)));
                That(position.Value, Is.EqualTo(Fix64Vec2.FromInt(150, 275)));
                That(previous.Value, Is.EqualTo(Fix64Vec2.FromInt(150, 275)));
                That(map.MapId.Value, Is.EqualTo("assembly_spawn_test"));
                That(stableId.Value, Is.GreaterThan(0));
            });

            That(count, Is.EqualTo(1));
        }

        [Test]
        public void RuntimeEntitySpawnSystem_SpawnTemplate_CopiesOwnerAndParentWhenRequested()
        {
            string templateJson = @"[
              {
                ""id"": ""test_manifest_wall"",
                ""components"": {
                  ""Name"": { ""Value"": ""Template:Wall"" },
                  ""GameplayTagContainer"": {}
                }
              }
            ]";

            var pipeline = CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }", templateJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            using var world = World.Create();
            var source = world.Create(
                new Team { Id = 5 },
                new PlayerOwner { PlayerId = 12 },
                new MapEntity { MapId = new Ludots.Core.Map.MapId("template_spawn_test") });
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                stableIds);

            That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                Source = source,
                TemplateId = "test_manifest_wall",
                WorldPositionCm = Fix64Vec2.FromInt(1110, 2220),
                HasWorldPosition = 1,
                CopySourceTeam = 1,
                CopySourcePlayerOwner = 1,
                LinkSourceAsParent = 1,
            }), Is.True);

            system.Update(0f);

            int count = 0;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm, PlayerOwner, ChildOf, Team, MapEntity>();
            world.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm position, ref PlayerOwner owner, ref ChildOf parent, ref Team team, ref MapEntity map) =>
            {
                if (!string.Equals(name.Value, "Template:Wall", StringComparison.Ordinal))
                {
                    return;
                }

                count++;
                That(position.Value, Is.EqualTo(Fix64Vec2.FromInt(1110, 2220)));
                That(owner.PlayerId, Is.EqualTo(12));
                That(parent.Parent, Is.EqualTo(source));
                That(team.Id, Is.EqualTo(5));
                That(map.MapId.Value, Is.EqualTo("template_spawn_test"));
                That(world.Has<GameplayTagContainer>(entity), Is.True);
                That(world.Has<Ludots.Core.Presentation.Components.PresentationStableId>(entity), Is.True);
                That(world.Get<Ludots.Core.Presentation.Components.PresentationStableId>(entity).Value, Is.GreaterThan(0));
            });

            That(count, Is.EqualTo(1));
            That(world.Has<ChildrenBuffer>(source), Is.True);
            That(world.Get<ChildrenBuffer>(source).Count, Is.EqualTo(1));
        }

        [Test]
        public void RuntimeEntitySpawnSystem_BatchTemplate_AppliesAttributeCurrentAndPreseedsSnapshot()
        {
            int durabilityId = AttributeRegistry.Register("Test.Batch.Durability");
            string templateJson = @"[
              {
                ""id"": ""test_attr_current_batch"",
                ""components"": {
                  ""Name"": { ""Value"": ""Template:AttrCurrent"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""FacingDirection"": { ""AngleRad"": 0.0 },
                  ""AttributeBuffer"": {
                    ""base"": { ""Test.Batch.Durability"": 100 },
                    ""current"": { ""Test.Batch.Durability"": 72 }
                  },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }", templateJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            using var world = World.Create();
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 4);
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                stableIds,
                receipts: receipts);

            for (int i = 0; i < 2; i++)
            {
                That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "test_attr_current_batch",
                    WorldPositionCm = Fix64Vec2.FromInt(i * 100, 0),
                    HasWorldPosition = 1,
                    ReceiptChannelId = 12,
                    ReceiptId = 800 + i,
                    EmitReceipt = 1,
                }), Is.True);
            }

            system.Update(0f);

            Entity first = Entity.Null;
            int count = 0;
            var query = new QueryDescription().WithAll<Name, AttributeBuffer, AttributeLastSnapshot>();
            world.Query(in query, (Entity entity, ref Name name, ref AttributeBuffer attributes, ref AttributeLastSnapshot snapshot) =>
            {
                if (!string.Equals(name.Value, "Template:AttrCurrent", StringComparison.Ordinal))
                {
                    return;
                }

                count++;
                if (first == Entity.Null)
                {
                    first = entity;
                }

                That(attributes.GetBase(durabilityId), Is.EqualTo(100f));
                That(attributes.GetCurrent(durabilityId), Is.EqualTo(72f));
                unsafe { That(snapshot.Values[durabilityId], Is.EqualTo(72f)); }
            });

            That(count, Is.EqualTo(2));
            That(first, Is.Not.EqualTo(Entity.Null));
            That(receipts.TryDequeueForChannel(12, out RuntimeEntitySpawnReceipt firstReceipt), Is.True);
            That(firstReceipt.ReceiptId, Is.EqualTo(800));
            That(firstReceipt.TemplateId, Is.EqualTo("test_attr_current_batch"));
            That(world.IsAlive(firstReceipt.Entity), Is.True);
            That(receipts.TryDequeueForChannel(12, out RuntimeEntitySpawnReceipt secondReceipt), Is.True);
            That(secondReceipt.ReceiptId, Is.EqualTo(801));
            That(secondReceipt.TemplateId, Is.EqualTo("test_attr_current_batch"));
            That(world.IsAlive(secondReceipt.Entity), Is.True);
            That(receipts.Count, Is.EqualTo(0));

            var queue = new DeferredTriggerQueue();
            using var deferred = new DeferredTriggerCollectionSystem(world, queue);
            AttributeMutationOps.AddCurrent(world, first, durabilityId, -2f);
            deferred.Update(0.016f);

            That(queue.AttributeTriggerCount, Is.EqualTo(1));
            var trigger = queue.GetAttributeTrigger(0);
            That(trigger.AttributeId, Is.EqualTo(durabilityId));
            That(trigger.OldValue, Is.EqualTo(72f));
            That(trigger.NewValue, Is.EqualTo(70f));
        }

        [Test]
        public void RuntimeEntitySpawnSystem_BatchTemplate_RejectsFieldCaseAliases()
        {
            string templateJson = @"[
              {
                ""id"": ""test_batch_bad_case"",
                ""components"": {
                  ""Name"": { ""value"": ""Template:BadCase"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""FacingDirection"": { ""AngleRad"": 0.0 },
                  ""AttributeBuffer"": { ""base"": {} },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }", templateJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            using var world = World.Create();
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(world, requests, templates, templateKeys, stableIds);

            for (int i = 0; i < 2; i++)
            {
                That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "test_batch_bad_case",
                    WorldPositionCm = Fix64Vec2.FromInt(i * 100, 0),
                    HasWorldPosition = 1,
                }), Is.True);
            }

            InvalidOperationException ex = Throws<InvalidOperationException>(() => system.Update(0f))!;
            That(ex.Message, Does.Contain("unsupported property 'value'"));
        }

        [Test]
        public void RuntimeEntitySpawnSystem_BatchTemplate_StaticTransformMatchesComponentRegistryMarkers()
        {
            string templateJson = @"[
              {
                ""id"": ""test_static_batch"",
                ""components"": {
                  ""Name"": { ""Value"": ""Template:StaticBatch"" },
                  ""WorldPositionCm"": { ""Value"": { ""X"": 0, ""Y"": 0 } },
                  ""FacingDirection"": { ""AngleRad"": 0.0 },
                  ""PresentationStaticTransform"": {},
                  ""AttributeBuffer"": { ""base"": {} },
                  ""GameplayTagContainer"": {},
                  ""TagCountContainer"": {}
                }
              }
            ]";

            var pipeline = CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }", templateJson);
            var templates = new DataRegistry<EntityTemplate>(pipeline);
            templates.Load("Entities/templates.json", ConfigCatalogLoader.Load(pipeline));

            using var world = World.Create();
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(world, requests, templates, templateKeys, stableIds);

            for (int i = 0; i < 2; i++)
            {
                That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "test_static_batch",
                    WorldPositionCm = Fix64Vec2.FromInt(i * 100, 0),
                    HasWorldPosition = 1,
                }), Is.True);
            }

            system.Update(0f);

            int count = 0;
            var query = new QueryDescription().WithAll<Name, PresentationStaticTransform, PresentationStaticVisualPending, PresentationStaticCullPending>();
            world.Query(in query, (Entity entity, ref Name name, ref PresentationStaticTransform transform, ref PresentationStaticVisualPending visualPending, ref PresentationStaticCullPending cullPending) =>
            {
                if (!string.Equals(name.Value, "Template:StaticBatch", StringComparison.Ordinal))
                {
                    return;
                }

                count++;
            });

            That(count, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeEntitySpawnSystem_SpawnUnitType_CopiesPlayerOwnerAndLinksParentWhenRequested()
        {
            UnitTypeRegistry.Clear();
            int unitTypeId = UnitTypeRegistry.Register("TestSummon");

            using var world = World.Create();
            var source = world.Create(
                new Team { Id = 3 },
                new PlayerOwner { PlayerId = 9 },
                new MapEntity { MapId = new Ludots.Core.Map.MapId("summon_spawn_test") });
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var templates = new DataRegistry<EntityTemplate>(CreateMinimalPipeline(@"{ ""id"": ""noop"", ""presetType"": ""None"" }"));
            var templateKeys = new EntityTemplateKeyRegistry();
            var stableIds = new Ludots.Core.Presentation.PresentationStableIdAllocator();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                stableIds);

            That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.UnitType,
                Source = source,
                WorldPositionCm = Fix64Vec2.FromInt(800, 900),
                UnitTypeId = unitTypeId,
                CopySourceTeam = 1,
                CopySourcePlayerOwner = 1,
                LinkSourceAsParent = 1,
            }), Is.True);

            system.Update(0f);

            int count = 0;
            var query = new QueryDescription().WithAll<Name, PlayerOwner, ChildOf>();
            world.Query(in query, (Entity entity, ref Name name, ref PlayerOwner owner, ref ChildOf parent) =>
            {
                if (!string.Equals(name.Value, "Unit:TestSummon", StringComparison.Ordinal))
                {
                    return;
                }

                count++;
                That(owner.PlayerId, Is.EqualTo(9));
                That(parent.Parent, Is.EqualTo(source));
                That(world.Has<Team>(entity), Is.True);
                That(world.Get<Team>(entity).Id, Is.EqualTo(3));
            });

            That(count, Is.EqualTo(1));
            That(world.Has<ChildrenBuffer>(source), Is.True);
            That(world.Get<ChildrenBuffer>(source).Count, Is.EqualTo(1));

            UnitTypeRegistry.Clear();
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
                    ""duration"": { ""durationTicks"": 100, ""periodTicks"": 0, ""clockId"": ""FixedFrame"" },
                    ""participatesInResponse"": true,
                    ""expireCondition"": { ""kind"": ""TagPresent"", ""tag"": ""Status.Shield"", ""sense"": ""Effective"" }
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json");

            int tplId = EffectTemplateIdRegistry.GetId("test_buff");
            That(tplId, Is.GreaterThan(0));
            That(templates.TryGetRef(tplId, out int idx), Is.True);
            ref readonly var tpl = ref templates.GetRef(idx);
            That(tpl.ExpireCondition.IsValid, Is.True);

            ref readonly var cond = ref conditions.Get(tpl.ExpireCondition);
            That(cond.Kind, Is.EqualTo(GasConditionKind.TagPresent));
        }

        [Test]
        public void Relation_Loader_ParsesRelationDescriptor()
        {
            var conditions = new GasConditionRegistry();
            var templates = new EffectTemplateRegistry();
            var pipeline = CreateMinimalPipeline(
                @"{
                    ""id"": ""test_attach"",
                    ""presetType"": ""Relation"",
                    ""lifetime"": ""Instant"",
                    ""participatesInResponse"": true,
                    ""relation"": {
                        ""operation"": ""SetParent"",
                        ""subject"": ""Source"",
                        ""parent"": ""TargetContext"",
                        ""snapSubjectToParentPosition"": true
                    }
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json");

            int tplId = EffectTemplateIdRegistry.GetId("test_attach");
            That(tplId, Is.GreaterThan(0));
            That(templates.TryGetRef(tplId, out int idx), Is.True);
            ref readonly var tpl = ref templates.GetRef(idx);
            That(tpl.PresetType, Is.EqualTo(EffectPresetType.Relation));
            That(tpl.Relation.Operation, Is.EqualTo(RelationOperation.SetParent));
            That(tpl.Relation.Subject, Is.EqualTo(RelationEntitySlot.Source));
            That(tpl.Relation.Parent, Is.EqualTo(RelationEntitySlot.TargetContext));
            That(tpl.Relation.SnapSubjectToParentPosition, Is.True);
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
                    ""duration"": { ""durationTicks"": 60, ""periodTicks"": 0, ""clockId"": ""FixedFrame"" },
                    ""participatesInResponse"": true,
                    ""grantedTags"": [
                        { ""tag"": ""Status.Slow"", ""formula"": ""Linear"", ""amount"": 6 },
                        { ""tag"": ""Status.Weak"", ""formula"": ""Fixed"", ""amount"": 1 }
                    ]
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json");

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

        [Test]
        public void GrantedTags_Loader_AllowsBaseOnlyForLinearPlusBase()
        {
            var conditions = new GasConditionRegistry();
            var templates = new EffectTemplateRegistry();
            var pipeline = CreateMinimalPipeline(
                @"{
                    ""id"": ""test_tags_without_base"",
                    ""tags"": [""Test.Tags""],
                    ""presetType"": ""None"",
                    ""lifetime"": ""After"",
                    ""duration"": { ""durationTicks"": 60, ""periodTicks"": 0, ""clockId"": ""FixedFrame"" },
                    ""participatesInResponse"": true,
                    ""grantedTags"": [
                        { ""tag"": ""Status.Slow"", ""formula"": ""Linear"", ""amount"": 6 },
                        { ""tag"": ""Status.Weak"", ""formula"": ""Fixed"", ""amount"": 1 }
                    ]
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json");

            int tplId = EffectTemplateIdRegistry.GetId("test_tags_without_base");
            That(tplId, Is.GreaterThan(0));
            That(templates.TryGetRef(tplId, out int idx), Is.True);
            ref readonly var tpl = ref templates.GetRef(idx);
            That(tpl.GrantedTags.Count, Is.EqualTo(2));
            That(tpl.GrantedTags.Get(0).Base, Is.EqualTo(0));
            That(tpl.GrantedTags.Get(1).Base, Is.EqualTo(0));
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
                    ""duration"": { ""durationTicks"": 120, ""periodTicks"": 0, ""clockId"": ""FixedFrame"" },
                    ""participatesInResponse"": true,
                    ""stack"": { ""limit"": 10, ""policy"": ""RefreshDuration"", ""overflowPolicy"": ""RejectNew"" }
                }");

            var loader = new EffectTemplateLoader(pipeline, templates, conditions);
            loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json");

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

        private sealed class TestLineQueryService : ISpatialQueryService
        {
            private readonly Entity[] _hits;

            public TestLineQueryService(params Entity[] hits)
            {
                _hits = hits;
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRange(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRing(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => default;

            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
            {
                int count = Math.Min(buffer.Length, _hits.Length);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = _hits[i];
                }

                return new SpatialQueryResult(count, 0);
            }
        }

        private static ConfigPipeline CreateMinimalPipeline(string effectJson, string templatesJson = null)
        {
            var json = "[" + effectJson + "]";
            var root = Path.Combine(Path.GetTempPath(), $"TagEffectTest_{Guid.NewGuid():N}");
            var gasDir = Path.Combine(root, "Configs", "GAS");
            Directory.CreateDirectory(gasDir);
            File.WriteAllText(Path.Combine(gasDir, "effects.json"), json);
            File.WriteAllText(
                Path.Combine(root, "Configs", "config_catalog.json"),
                @"[
  { ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Entities/templates.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");

            if (!string.IsNullOrWhiteSpace(templatesJson))
            {
                string entityDir = Path.Combine(root, "Entities");
                Directory.CreateDirectory(entityDir);
                File.WriteAllText(Path.Combine(entityDir, "templates.json"), templatesJson);
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }
    }
}
