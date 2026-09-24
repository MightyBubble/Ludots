using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Systems;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class UtilityAiRuntimeTests
    {
        private static UtilityAiDecisionSystem CreateDecisionSystem(
            World world,
            IClock clock,
            UtilityAiCompiledRuntime runtime,
            ISpatialQueryService spatial,
            AbilityDefinitionRegistry abilities,
            GraphProgramRegistry? graphs,
            IGraphRuntimeApi? graphApi,
            OrderQueue orders,
            ProgressionRequirementEvaluator? progressionRequirements = null)
        {
            var eligibility = new AbilityActivationEligibilityQuery(
                world,
                abilities,
                new TagOps(
                    new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                    new TagRuleRegistry()),
                graphs,
                graphApi,
                progressionRequirements,
                actuatorGate: new UtilityAiAbilityActuatorGate(world, runtime));
            return new UtilityAiDecisionSystem(
                world,
                clock,
                runtime,
                spatial,
                abilities,
                graphs,
                graphApi,
                orders,
                eligibility);
        }

        [Test]
        public void UtilityAiDecisionSystem_SubmitsAttackOrder_ForNearestHostile()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig { Key = "attackTarget", OrderTypeId = 102 });

            AbilityIdRegistry.Clear();
            int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(attackAbilityId, new AbilityDefinition());

            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            GameplayTagContainer noTags = default;

            var runtime = new UtilityAiCompiledRuntime(
                new[]
                {
                    new UtilityAiProfileDefinition(decisionMakerOffset: 0, decisionMakerCount: 1, decisionIntervalSteps: 1, maxCandidates: 16, maxGraphScoreInstructions: 4096, defaultStanceId: -1)
                },
                new[]
                {
                    new UtilityAiDecisionMakerDefinition(decisionOffset: 0, decisionCount: 1, UtilityAiSelectionMode.FixedPriority, switchMargin: 0f)
                },
                new[]
                {
                    new UtilityAiDecisionDefinition(
                        targetFilterId: 0,
                        considerationOffset: 0,
                        considerationCount: 1,
                        taskIndex: 0,
                        priority: 10,
                        baseScore: 1f,
                        weight: 1f,
                        momentumBonus: 0f,
                        minDurationSteps: 0,
                        decisionRepeatDelaySteps: 0,
                        abilityId: attackAbilityId,
                        abilitySlotIndex: 0,
                        keepRunningUntilFinished: false)
                },
                new[]
                {
                    new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply)
                },
                new[]
                {
                    new UtilityAiTargetFilterDefinition(opOffset: 0, opCount: 2, maxResults: 16)
                },
                new[]
                {
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 1200, 0, RelationshipFilter.All, in noTags),
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                },
                new[]
                {
                    new UtilityAiInputDefinition(UtilityAiInputKind.DistanceToTarget, 0, 0)
                },
                new[]
                {
                    new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.RangeInverse, 0f, 1200f)
                },
                new[]
                {
                    new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f)
                },
                new[]
                {
                    new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 102, attackAbilityId, 0, (int)OrderSubmitMode.Immediate, 0, -1, 0)
                },
                Array.Empty<UtilityAiStanceDefinition>(),
                Array.Empty<UtilityAiActuatorDefinition>());

            var partition = new ChunkedGridSpatialPartitionWorld(64);
            var spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());

            var actor = world.Create(
                new UtilityAiAgent { ProfileId = 0 },
                new UtilityAiState { CurrentDecisionId = -1, NextThinkStep = 0 },
                new UtilityAiDecisionTrace(),
                new UtilityAiCombatMemory(),
                new OrderBuffer { ActiveIndex = -1 },
                new AbilityStateBuffer(),
                new Team { Id = 1 },
                WorldPositionCm.FromCm(0, 0));
            ref var abilityBuffer = ref world.Get<AbilityStateBuffer>(actor);
            abilityBuffer.AddAbility(attackAbilityId);
            partition.Add(actor, 0, 0);

            var nearEnemy = world.Create(new Team { Id = 2 }, WorldPositionCm.FromCm(400, 0), new OrderBuffer { ActiveIndex = -1 });
            var farEnemy = world.Create(new Team { Id = 2 }, WorldPositionCm.FromCm(900, 0), new OrderBuffer { ActiveIndex = -1 });
            partition.Add(nearEnemy, 4, 0);
            partition.Add(farEnemy, 9, 0);

            var schedule = new UtilityAiThinkScheduleSystem(world, clock, runtime);
            var decision = CreateDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);

            schedule.Update(1f / 60f);
            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(102));
            Assert.That(order.Target, Is.EqualTo(nearEnemy));
            Assert.That(order.Args.I0, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_FixedPrioritySelectsOnlyHostileCandidates()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));

            AbilityIdRegistry.Clear();
            int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(attackAbilityId, new AbilityDefinition());

            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Friendly);
            GameplayTagContainer noTags = default;

            var runtime = new UtilityAiCompiledRuntime(
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, 4096, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 1, 0, 5, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, false)
                },
                new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                new[] { new UtilityAiTargetFilterDefinition(0, 2, 16) },
                new[]
                {
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 1500, 0, RelationshipFilter.All, in noTags),
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                },
                new[] { new UtilityAiInputDefinition(UtilityAiInputKind.Constant, 1, 0) },
                new[] { new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.Identity, 0f, 1f) },
                new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 102, attackAbilityId, 0, 0, 0, -1, 0) },
                Array.Empty<UtilityAiStanceDefinition>(),
                Array.Empty<UtilityAiActuatorDefinition>());

            var partition = new ChunkedGridSpatialPartitionWorld(64);
            var spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());

            var actor = world.Create(
                new UtilityAiAgent { ProfileId = 0 },
                new UtilityAiState { CurrentDecisionId = -1, NextThinkStep = 0 },
                new UtilityAiDecisionTrace(),
                new UtilityAiCombatMemory(),
                new OrderBuffer { ActiveIndex = -1 },
                new AbilityStateBuffer(),
                new Team { Id = 1 },
                WorldPositionCm.FromCm(0, 0));
            ref var actorAbilities = ref world.Get<AbilityStateBuffer>(actor);
            actorAbilities.AddAbility(attackAbilityId);
            partition.Add(actor, 0, 0);

            var friendly = world.Create(new Team { Id = 3 }, WorldPositionCm.FromCm(200, 0), new OrderBuffer { ActiveIndex = -1 });
            var hostile = world.Create(new Team { Id = 2 }, WorldPositionCm.FromCm(800, 0), new OrderBuffer { ActiveIndex = -1 });
            partition.Add(friendly, 2, 0);
            partition.Add(hostile, 8, 0);

            var decision = CreateDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryDequeue(out var order), Is.True);
            Assert.That(order.Target, Is.EqualTo(hostile));
        }

        [Test]
        public void UtilityAiDecisionSystem_PriorityBucketThenDistance_SelectsHigherPriorityTarget()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));

            AbilityIdRegistry.Clear();
            int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(attackAbilityId, new AbilityDefinition());

            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            GameplayTagContainer noTags = default;

            var runtime = new UtilityAiCompiledRuntime(
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, 4096, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 2, 0, 5, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, false)
                },
                new[]
                {
                    new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.PriorityBucket),
                    new UtilityAiConsiderationDefinition(1, 1, 1, 1f, UtilityAiAggregateMode.WeightedSum)
                },
                new[] { new UtilityAiTargetFilterDefinition(0, 2, 16) },
                new[]
                {
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 1500, 0, RelationshipFilter.All, in noTags),
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                },
                new[]
                {
                    new UtilityAiInputDefinition(UtilityAiInputKind.TargetPriorityBucket, 0, 0),
                    new UtilityAiInputDefinition(UtilityAiInputKind.DistanceToTarget, 0, 0)
                },
                new[]
                {
                    new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.Identity, 0f, 1f),
                    new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.RangeInverse, 0f, 1500f)
                },
                new[]
                {
                    new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f),
                    new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f)
                },
                new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 102, attackAbilityId, 0, 0, 0, -1, 0) },
                Array.Empty<UtilityAiStanceDefinition>(),
                Array.Empty<UtilityAiActuatorDefinition>());

            var partition = new ChunkedGridSpatialPartitionWorld(64);
            var spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());

            var actor = world.Create(
                new UtilityAiAgent { ProfileId = 0 },
                new UtilityAiState { CurrentDecisionId = -1, NextThinkStep = 0 },
                new UtilityAiCombatMemory(),
                new OrderBuffer { ActiveIndex = -1 },
                new AbilityStateBuffer(),
                new Team { Id = 1 },
                WorldPositionCm.FromCm(0, 0));
            ref var actorAbilities = ref world.Get<AbilityStateBuffer>(actor);
            actorAbilities.AddAbility(attackAbilityId);
            partition.Add(actor, 0, 0);

            var nearLow = world.Create(
                new Team { Id = 2 },
                new UtilityAiTargetPriority { Bucket = 1 },
                WorldPositionCm.FromCm(200, 0),
                new OrderBuffer { ActiveIndex = -1 });
            var farHigh = world.Create(
                new Team { Id = 2 },
                new UtilityAiTargetPriority { Bucket = 5 },
                WorldPositionCm.FromCm(900, 0),
                new OrderBuffer { ActiveIndex = -1 });
            partition.Add(nearLow, 2, 0);
            partition.Add(farHigh, 9, 0);

            var decision = CreateDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryDequeue(out var order), Is.True);
            Assert.That(order.Target, Is.EqualTo(farHigh));
        }

        [Test]
        public void UtilityAiDecisionSystem_ActivationBlockTag_BlocksAutocastSubmission()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));

            AbilityIdRegistry.Clear();
            TagRegistry.Clear();
            int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
            int gcdTagId = TagRegistry.Register("Cooldown.Global.Test");
            var blockTags = new AbilityActivationBlockTags();
            blockTags.BlockedAny.AddTag(gcdTagId);
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(attackAbilityId, new AbilityDefinition
            {
                HasActivationBlockTags = true,
                ActivationBlockTags = blockTags
            });

            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            GameplayTagContainer noTags = default;

            var runtime = new UtilityAiCompiledRuntime(
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, 4096, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 1, 0, 5, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, false)
                },
                new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                new[] { new UtilityAiTargetFilterDefinition(0, 2, 16) },
                new[]
                {
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 1500, 0, RelationshipFilter.All, in noTags),
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                },
                new[] { new UtilityAiInputDefinition(UtilityAiInputKind.Constant, 1, 0) },
                new[] { new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.Identity, 0f, 1f) },
                new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 102, attackAbilityId, 0, 0, 0, -1, 0) },
                Array.Empty<UtilityAiStanceDefinition>(),
                Array.Empty<UtilityAiActuatorDefinition>());

            var partition = new ChunkedGridSpatialPartitionWorld(64);
            var spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());

            var actorTags = new GameplayTagContainer();
            actorTags.AddTag(gcdTagId);
            var actor = world.Create(
                new UtilityAiAgent { ProfileId = 0 },
                new UtilityAiState { CurrentDecisionId = -1, NextThinkStep = 0 },
                new UtilityAiDecisionTrace(),
                new UtilityAiCombatMemory(),
                new OrderBuffer { ActiveIndex = -1 },
                new AbilityStateBuffer(),
                new Team { Id = 1 },
                actorTags,
                WorldPositionCm.FromCm(0, 0));
            ref var actorAbilities = ref world.Get<AbilityStateBuffer>(actor);
            actorAbilities.AddAbility(attackAbilityId);
            partition.Add(actor, 0, 0);

            var hostile = world.Create(new Team { Id = 2 }, WorldPositionCm.FromCm(500, 0), new OrderBuffer { ActiveIndex = -1 });
            partition.Add(hostile, 5, 0);

            var decision = CreateDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(0));
            Assert.That(world.Has<UtilityAiDecisionTrace>(actor), Is.True);
            Assert.That(world.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason, Is.EqualTo((int)UtilityAiReadinessBlockReason.ActivationBlockTags));
        }

        [Test]
        public void UtilityAiDecisionSystem_DoesNotSubmit_WhenOrderBufferAlreadyBusy()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));

            AbilityIdRegistry.Clear();
            int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(attackAbilityId, new AbilityDefinition());

            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            GameplayTagContainer noTags = default;

            var runtime = new UtilityAiCompiledRuntime(
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, 4096, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 1, 0, 5, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, false)
                },
                new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                new[] { new UtilityAiTargetFilterDefinition(0, 2, 16) },
                new[]
                {
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 1500, 0, RelationshipFilter.All, in noTags),
                    new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                },
                new[] { new UtilityAiInputDefinition(UtilityAiInputKind.Constant, 1, 0) },
                new[] { new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.Identity, 0f, 1f) },
                new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 102, attackAbilityId, 0, 0, 0, -1, 0) },
                Array.Empty<UtilityAiStanceDefinition>(),
                Array.Empty<UtilityAiActuatorDefinition>());

            var partition = new ChunkedGridSpatialPartitionWorld(64);
            var spec = new WorldSizeSpec(new WorldAabbCm(-5000, -5000, 10000, 10000), 100);
            var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
            spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());

            var actor = world.Create(
                new UtilityAiAgent { ProfileId = 0 },
                new UtilityAiState { CurrentDecisionId = -1, NextThinkStep = 0 },
                new UtilityAiCombatMemory(),
                new OrderBuffer
                {
                    ActiveIndex = 0,
                    ActiveOrder = new QueuedOrder { Order = new Order { OrderTypeId = 101 } }
                },
                new AbilityStateBuffer(),
                new Team { Id = 1 },
                WorldPositionCm.FromCm(0, 0));
            ref var actorAbilities = ref world.Get<AbilityStateBuffer>(actor);
            actorAbilities.AddAbility(attackAbilityId);
            partition.Add(actor, 0, 0);

            var hostile = world.Create(new Team { Id = 2 }, WorldPositionCm.FromCm(500, 0), new OrderBuffer { ActiveIndex = -1 });
            partition.Add(hostile, 5, 0);

            var decision = CreateDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(0));
            Assert.That(world.Has<UtilityAiDecisionTrace>(actor), Is.False);
        }

        [Test]
        public void UtilityAiDecisionSystem_StateMachine_RespectsCurrentDecisionMinDuration()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(500, 0);
            var runtime = fixture.CreateTwoDecisionRuntime(
                lowPriority: 1,
                highPriority: 10,
                firstMinDurationSteps: 5,
                firstDecisionRepeatDelaySteps: 0,
                secondDecisionRepeatDelaySteps: 0);

            fixture.AddActor(runtime, currentDecisionId: 0, decisionStartedStep: 0);
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(201));
            Assert.That(order.Target, Is.EqualTo(target));
        }

        [Test]
        public void UtilityAiDecisionSystem_StateMachine_RespectsDecisionRepeatDelay()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(500, 0);
            var runtime = fixture.CreateTwoDecisionRuntime(
                lowPriority: 1,
                highPriority: 10,
                firstMinDurationSteps: 0,
                firstDecisionRepeatDelaySteps: 4,
                secondDecisionRepeatDelaySteps: 0);

            fixture.AddActor(runtime, currentDecisionId: 0, decisionStartedStep: 0, repeatDelayDecisionId: 0, decisionRepeatDelayUntilStep: 4);
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(202));
        }

        [Test]
        public void UtilityAiDecisionSystem_ZeroRepeatDelay_AllowsDecisionAgainOnNextThink()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, decisionRepeatDelaySteps: 0);
            var actor = fixture.AddActor(runtime);
            OrderTypeRegistry orderTypes = CreateUtilityOrderTypes(fixture.TerminalResults);
            var orderBuffer = CreateOrderBufferSystem(fixture, orderTypes);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            orderBuffer.Update(0f);
            results.Update(0f);

            ref var firstBuffer = ref fixture.World.Get<OrderBuffer>(actor);
            Assert.That(firstBuffer.ActiveOrder.Order.Target, Is.EqualTo(target));
            Assert.That(OrderSubmitter.NotifyOrderComplete(fixture.World, actor, orderTypes), Is.True);
            fixture.AdmissionResults.EndLogicStep();
            fixture.TerminalResults.Clear();
            fixture.Clock.Advance(ClockDomainId.Step, 1);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryPeek(out var second), Is.True);
            Assert.That(second.Target, Is.EqualTo(target));
            orderBuffer.Update(0f);
            results.Update(0f);
            fixture.AdmissionResults.EndLogicStep();
        }

        [Test]
        public void UtilityAiDecisionSystem_ActuatorReadinessAndAimGate_BlockAndReleaseAbility()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                includeActuator: true);
            int actuatorId = runtime.Actuators[0].Id;
            var actor = fixture.AddActor(runtime);
            fixture.World.Add(actor, new ActuatorReadiness { ActuatorId = actuatorId, Ready01 = 0.5f });

            fixture.RunDecision(runtime);
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            Assert.That(fixture.World.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason, Is.EqualTo((int)UtilityAiReadinessBlockReason.ActuatorNotReady));

            fixture.World.Set(actor, new ActuatorReadiness { ActuatorId = actuatorId, Ready01 = 1f });
            fixture.World.Add(actor, new AimGate { ActuatorId = actuatorId, Ready01 = 0f });
            fixture.Clock.Advance(ClockDomainId.Step, 1);
            fixture.RunDecision(runtime);
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            Assert.That(fixture.World.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason, Is.EqualTo((int)UtilityAiReadinessBlockReason.AimGateNotReady));

            fixture.World.Set(actor, new AimGate { ActuatorId = actuatorId, Ready01 = 1f });
            fixture.Clock.Advance(ClockDomainId.Step, 1);
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.Target, Is.EqualTo(target));
        }

        [Test]
        public void UtilityAiDecisionSystem_ExecutionOnlySubmitsOrder_DoesNotPublishEffectRequest()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            var effects = new EffectRequestQueue();
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, decisionRepeatDelaySteps: 0);
            fixture.AddActor(runtime);

            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(effects.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_InvalidTaskKind_ThrowsBeforeOrderSubmission()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                taskKind: (UtilityAiTaskKind)byte.MaxValue);
            Entity actor = fixture.AddActor(runtime);

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.RunDecision(runtime));

            Assert.That(ex!.Message, Does.Contain("UTILITY.TASK.ERR.UnsupportedKind"));
            Assert.That(ex.Message, Does.Contain("kind=255"));
            Assert.That(fixture.Orders.Count, Is.Zero);
            Assert.That(fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId, Is.Zero);
        }

        [Test]
        public void UtilityAiDecisionSystem_FormOverrideSubmitsTheSameEffectiveSlotAbilityGasWillExecute()
        {
            using var fixture = RuntimeFixture.Create();
            const int formAbilityId = 9917;
            fixture.Abilities.Register(formAbilityId, new AbilityDefinition());
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0);
            Entity actor = fixture.AddActor(runtime);
            var formSlots = new AbilityFormSlotBuffer();
            formSlots.SetOverride(0, formAbilityId);
            fixture.World.Add(actor, formSlots);
            _ = fixture.CreateHostile(300, 0);
            var decision = CreateDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.Spatial,
                fixture.Abilities,
                new GraphProgramRegistry(),
                graphApi: null,
                orders: fixture.Orders);

            decision.Update(1f / 60f);

            Assert.That(fixture.Orders.TryDequeue(out Order order), Is.True);
            Assert.That(order.Args.I0, Is.Zero);
            Assert.That(
                fixture.World.Get<UtilityAiDecisionTrace>(actor).LastSubmittedAbilityId,
                Is.EqualTo(formAbilityId));
        }

        [Test]
        public void UtilityAiDecisionSystem_ProgressionRefusalDoesNotSubmitAnOrder()
        {
            using var fixture = RuntimeFixture.Create();
            const int progressionId = 121;
            const int requirementId = 122;
            GameplayTagContainer noRequiredTags = default;
            var requirement = new ProgressionRequirementDefinition(
                requirementId,
                new[]
                {
                    new ProgressionRequirementNode(
                        ProgressionRequirementNodeKind.ProgressionCompleted,
                        ScopeKey.Self,
                        RoleSlot.ScopeHost,
                        firstChild: 0,
                        childCount: 0,
                        progressionId,
                        requiredCount: 1,
                        graphProgramId: 0,
                        in noRequiredTags)
                },
                Array.Empty<int>());
            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(requirementId, requirement);
            var progression = new ProgressionRequirementEvaluator(
                fixture.World,
                requirements,
                new ScopeKeyRegistry(),
                tagOps: new TagOps(
                    new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                    new TagRuleRegistry()));
            fixture.Abilities.Register(fixture.AttackAbilityId, new AbilityDefinition
            {
                HasUseProgressionRequirement = true,
                UseProgressionRequirementId = requirementId
            });
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0);
            Entity actor = fixture.AddActor(runtime);
            Entity target = fixture.CreateHostile(300, 0);
            fixture.World.Add(target, new ProgressionStateBuffer());
            var decision = CreateDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.Spatial,
                fixture.Abilities,
                new GraphProgramRegistry(),
                graphApi: null,
                orders: fixture.Orders,
                progressionRequirements: progression);

            decision.Update(1f / 60f);

            Assert.That(fixture.Orders.Count, Is.Zero);
            Assert.That(
                fixture.World.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason,
                Is.EqualTo((int)UtilityAiReadinessBlockReason.ProgressionRequirement));

            Assert.That(progression.TryComplete(target, progressionId), Is.True);
            fixture.Clock.Advance(ClockDomainId.Step, 1);
            decision.Update(1f / 60f);
            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void UtilityAiDecisionSystem_SameTickOrderIntent_IsConsumedByOrderBufferSystem()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, decisionRepeatDelaySteps: 0);
            var actor = fixture.AddActor(runtime);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "attackTarget",
                OrderTypeId = 102,
                Priority = 100,
                BufferWindowMs = 0,
                PendingBufferWindowMs = 0,
                SameTypePolicy = SameTypePolicy.Replace,
                QueueFullPolicy = QueueFullPolicy.DropOldest,
                MaxQueueSize = 1,
                QueuedModeMaxSize = 1,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                EntityBlackboardKey = -1,
                SpatialBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });

            var spatialUpdate = new SpatialPartitionUpdateSystem(fixture.World, fixture.Partition, fixture.Spec);
            var decision = CreateDecisionSystem(fixture.World, fixture.Clock, runtime, fixture.Spatial, fixture.Abilities, new GraphProgramRegistry(), null, fixture.Orders);
            var orderBuffer = new OrderBufferSystem(
                fixture.World,
                fixture.Clock,
                orderTypes,
                new OrderRuleRegistry(),
                fixture.AdmissionResults,
                fixture.Orders,
                stepRateHz: 30);

            fixture.AdmissionResults.BeginLogicStep();
            spatialUpdate.Update(1f / 60f);
            decision.Update(1f / 60f);
            orderBuffer.Update(1f / 60f);

            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            ref var buffer = ref fixture.World.Get<OrderBuffer>(actor);
            Assert.That(buffer.HasActive, Is.True);
            Assert.That(buffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(102));
            Assert.That(buffer.ActiveOrder.Order.Target, Is.EqualTo(target));
        }

        [Test]
        public void UtilityAiOrderResultSystem_GlobalAcceptedThenEntityRejected_RecordsTypedFailureWithoutRepeatDelay()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 5);
            Entity actor = fixture.AddActor(runtime);
            OrderTypeRegistry orderTypes = CreateUtilityOrderTypes(
                fixture.TerminalResults,
                requireEntityBlackboard: true);
            var orderBuffer = CreateOrderBufferSystem(fixture, orderTypes);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);

            UtilityAiState submitted = fixture.World.Get<UtilityAiState>(actor);
            int orderId = submitted.LastSubmittedOrderId;
            Assert.Multiple(() =>
            {
                Assert.That(orderId, Is.GreaterThan(0));
                Assert.That(submitted.CurrentTaskStatus, Is.EqualTo(UtilityAiTaskRunStatus.Submitted));
                Assert.That(submitted.RepeatDelayDecisionId, Is.EqualTo(-1));
                Assert.That(
                    fixture.AdmissionResults.TryGet(
                        orderId,
                        OrderAdmissionStage.GlobalIntake,
                        out OrderAdmissionOutcome global),
                    Is.True);
                Assert.That(global.Result, Is.EqualTo(OrderSubmitResult.Queued));
            });

            orderBuffer.Update(0f);
            results.Update(0f);
            fixture.AdmissionResults.EndLogicStep();

            UtilityAiState failed = fixture.World.Get<UtilityAiState>(actor);
            UtilityAiDecisionTrace trace = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(failed.LastSubmittedOrderId, Is.EqualTo(orderId));
                Assert.That(failed.CurrentTaskStatus, Is.EqualTo(UtilityAiTaskRunStatus.Failed));
                Assert.That(failed.CurrentTaskFailureReason, Is.EqualTo(OrderFailureReason.SubmissionMissingBlackboard));
                Assert.That(failed.CurrentDecisionId, Is.EqualTo(-1));
                Assert.That(failed.RepeatDelayDecisionId, Is.EqualTo(-1));
                Assert.That(failed.DecisionRepeatDelayUntilStep, Is.Zero);
                Assert.That(trace.LastSubmittedOrderId, Is.EqualTo(orderId));
                Assert.That(trace.LastTaskStatus, Is.EqualTo((int)UtilityAiTaskRunStatus.Failed));
                Assert.That(trace.LastTaskFailureReason, Is.EqualTo((int)OrderFailureReason.SubmissionMissingBlackboard));
                Assert.That(
                    fixture.AdmissionResults.TryGet(
                        orderId,
                        OrderAdmissionStage.EntityIntake,
                        out OrderAdmissionOutcome entityAdmission),
                    Is.True);
                Assert.That(entityAdmission.Result, Is.EqualTo(OrderSubmitResult.RejectedMissingBlackboard));
            });
        }

        [Test]
        public void UtilityAiOrderResultSystem_PlainSubmitOrder_CompletesSubmissionAsAdmittedNotExecutionCompleted()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 4);
            Entity actor = fixture.AddActor(runtime);
            OrderTypeRegistry orderTypes = CreateUtilityOrderTypes(fixture.TerminalResults);
            var orderBuffer = CreateOrderBufferSystem(fixture, orderTypes);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            int orderId = fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId;
            orderBuffer.Update(0f);
            results.Update(0f);
            fixture.AdmissionResults.EndLogicStep();

            UtilityAiState state = fixture.World.Get<UtilityAiState>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(orderId, Is.GreaterThan(0));
                Assert.That(state.LastSubmittedOrderId, Is.EqualTo(orderId));
                Assert.That(state.CurrentTaskStatus, Is.EqualTo(UtilityAiTaskRunStatus.Admitted));
                Assert.That(state.CurrentTaskStatus, Is.Not.EqualTo(UtilityAiTaskRunStatus.Completed));
                Assert.That(state.CurrentTaskFailureReason, Is.EqualTo(OrderFailureReason.None));
                Assert.That(state.RepeatDelayDecisionId, Is.EqualTo(0));
                Assert.That(state.DecisionRepeatDelayUntilStep, Is.EqualTo(4));
                Assert.That(fixture.TerminalResults.Count, Is.Zero);
                Assert.That(fixture.World.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(orderId));
            });
        }

        [Test]
        public void UtilityAiOrderResultSystem_EntityPending_RemainsPendingUntilLaterAdmission()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 4);
            Entity actor = fixture.AddActor(runtime);
            OrderTypeRegistry orderTypes = CreateUtilityOrderTypes(
                fixture.TerminalResults,
                sameTypePolicy: SameTypePolicy.Ignore,
                pendingBufferWindowMs: 1000);
            var orderBuffer = CreateOrderBufferSystem(fixture, orderTypes);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            int orderId = fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId;
            ref OrderBuffer actorOrders = ref fixture.World.Get<OrderBuffer>(actor);
            var blocking = new Order
            {
                OrderId = 777,
                OrderTypeId = 102,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate
            };
            actorOrders.SetActiveDirect(in blocking, priority: 100);

            orderBuffer.Update(0f);
            results.Update(0f);
            fixture.AdmissionResults.EndLogicStep();

            UtilityAiState state = fixture.World.Get<UtilityAiState>(actor);
            OrderBuffer observedOrders = fixture.World.Get<OrderBuffer>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(state.CurrentTaskStatus, Is.EqualTo(UtilityAiTaskRunStatus.Pending));
                Assert.That(state.RepeatDelayDecisionId, Is.EqualTo(-1));
                Assert.That(observedOrders.HasPending, Is.True);
                Assert.That(observedOrders.PendingOrder.Order.OrderId, Is.EqualTo(orderId));
                Assert.That(
                    fixture.AdmissionResults.TryGet(
                        orderId,
                        OrderAdmissionStage.EntityIntake,
                        out OrderAdmissionOutcome pending),
                    Is.True);
                Assert.That(pending.Result, Is.EqualTo(OrderSubmitResult.Pending));
            });

            fixture.AdmissionResults.BeginLogicStep();
            orderBuffer.NotifyOrderComplete(actor);
            results.Update(0f);
            fixture.AdmissionResults.EndEntityIntake();
            fixture.AdmissionResults.EndLogicStep();

            state = fixture.World.Get<UtilityAiState>(actor);
            observedOrders = fixture.World.Get<OrderBuffer>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(state.CurrentTaskStatus, Is.EqualTo(UtilityAiTaskRunStatus.Admitted));
                Assert.That(state.CurrentTaskFailureReason, Is.EqualTo(OrderFailureReason.None));
                Assert.That(state.RepeatDelayDecisionId, Is.EqualTo(0));
                Assert.That(state.DecisionRepeatDelayUntilStep, Is.EqualTo(4));
                Assert.That(observedOrders.HasPending, Is.False);
                Assert.That(observedOrders.ActiveOrder.Order.OrderId, Is.EqualTo(orderId));
            });
        }

        [TestCase(
            OrderTerminalState.Completed,
            OrderFailureReason.None,
            UtilityAiTaskRunStatus.Completed,
            0)]
        [TestCase(
            OrderTerminalState.Failed,
            OrderFailureReason.PreconditionFailed,
            UtilityAiTaskRunStatus.Failed,
            -1)]
        [TestCase(
            OrderTerminalState.Cancelled,
            OrderFailureReason.Interrupted,
            UtilityAiTaskRunStatus.Cancelled,
            -1)]
        public void UtilityAiOrderResultSystem_KeepRunning_WaitsForTypedTerminalOutcome(
            OrderTerminalState terminalState,
            OrderFailureReason failureReason,
            UtilityAiTaskRunStatus expectedStatus,
            int expectedDecisionId)
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 3,
                keepRunningUntilFinished: true);
            Entity actor = fixture.AddActor(runtime);
            OrderTypeRegistry orderTypes = CreateUtilityOrderTypes(fixture.TerminalResults);
            var orderBuffer = CreateOrderBufferSystem(fixture, orderTypes);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            int orderId = fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId;
            orderBuffer.Update(0f);
            results.Update(0f);

            Assert.Multiple(() =>
            {
                Assert.That(
                    fixture.World.Get<UtilityAiState>(actor).CurrentTaskStatus,
                    Is.EqualTo(UtilityAiTaskRunStatus.Admitted));
                Assert.That(
                    fixture.World.Get<UtilityAiState>(actor).RepeatDelayDecisionId,
                    Is.EqualTo(-1));
            });

            var terminal = new OrderTerminalOutcome(
                orderId,
                102,
                terminalState,
                failureReason,
                actor);
            orderTypes.PublishTerminalResult(in terminal);
            results.Update(0f);
            fixture.AdmissionResults.EndLogicStep();

            UtilityAiState state = fixture.World.Get<UtilityAiState>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(state.CurrentTaskStatus, Is.EqualTo(expectedStatus));
                Assert.That(state.CurrentTaskFailureReason, Is.EqualTo(failureReason));
                Assert.That(state.CurrentDecisionId, Is.EqualTo(expectedDecisionId));
                Assert.That(
                    state.RepeatDelayDecisionId,
                    Is.EqualTo(terminalState == OrderTerminalState.Completed ? 0 : -1));
                Assert.That(
                    state.DecisionRepeatDelayUntilStep,
                    Is.EqualTo(terminalState == OrderTerminalState.Completed ? 3 : 0));
            });
        }

        [Test]
        public void UtilityAiDecisionSystem_GlobalAdmissionCapacity_RecordsAssignedIdAndTypedFailure()
        {
            using var fixture = RuntimeFixture.Create(orderCapacity: 1);
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 6);
            Entity actor = fixture.AddActor(runtime);

            fixture.AdmissionResults.BeginLogicStep();
            var occupying = new Order
            {
                Actor = actor,
                OrderTypeId = 102,
                SubmitMode = OrderSubmitMode.Immediate
            };
            Assert.That(
                fixture.Orders.SubmitAssigned(ref occupying),
                Is.EqualTo(OrderSubmitResult.Queued));

            fixture.RunDecision(runtime);
            fixture.AdmissionResults.EndEntityIntake();
            fixture.AdmissionResults.EndLogicStep();

            UtilityAiState state = fixture.World.Get<UtilityAiState>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(state.LastSubmittedOrderId, Is.GreaterThan(occupying.OrderId));
                Assert.That(state.CurrentTaskStatus, Is.EqualTo(UtilityAiTaskRunStatus.Failed));
                Assert.That(state.CurrentTaskFailureReason, Is.EqualTo(OrderFailureReason.SubmissionAdmissionCapacity));
                Assert.That(state.CurrentDecisionId, Is.EqualTo(-1));
                Assert.That(state.RepeatDelayDecisionId, Is.EqualTo(-1));
                Assert.That(
                    fixture.AdmissionResults.TryGet(
                        state.LastSubmittedOrderId,
                        OrderAdmissionStage.GlobalIntake,
                        out OrderAdmissionOutcome rejection),
                    Is.True);
                Assert.That(rejection.Result, Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            });
        }

        [Test]
        public void UtilityAiOrderResultSystem_MissingEntityAdmission_FailsExplicitly()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0);
            Entity actor = fixture.AddActor(runtime);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            int orderId = fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId;
            Assert.That(fixture.Orders.TryDequeue(out _), Is.True);

            var ex = Assert.Throws<InvalidOperationException>(() => results.Update(0f));
            Assert.That(ex!.Message, Does.Contain(UtilityAiOrderResultSystem.MissingEntityAdmissionError));
            Assert.That(ex.Message, Does.Contain($"orderId={orderId}"));

            fixture.AdmissionResults.EndEntityIntake();
            fixture.AdmissionResults.EndLogicStep();
        }

        [Test]
        public void UtilityAiOrderResultSystem_TerminalCapacityFailure_RemainsExplicitAndRetryable()
        {
            using var fixture = RuntimeFixture.Create(terminalCapacity: 1);
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                keepRunningUntilFinished: true);
            Entity actor = fixture.AddActor(runtime);
            OrderTypeRegistry orderTypes = CreateUtilityOrderTypes(fixture.TerminalResults);
            var orderBuffer = CreateOrderBufferSystem(fixture, orderTypes);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            orderBuffer.Update(0f);
            results.Update(0f);

            var occupying = new OrderTerminalOutcome(
                999,
                102,
                OrderTerminalState.Completed,
                OrderFailureReason.None,
                Entity.Null);
            orderTypes.PublishTerminalResult(in occupying);

            var ex = Assert.Throws<InvalidOperationException>(
                () => OrderSubmitter.NotifyOrderComplete(fixture.World, actor, orderTypes));
            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Does.Contain("ORDER.TERMINAL.ERR.ResultCapacityExceeded"));
                Assert.That(fixture.World.Get<OrderBuffer>(actor).HasActive, Is.True);
                Assert.That(
                    fixture.World.Get<UtilityAiState>(actor).CurrentTaskStatus,
                    Is.EqualTo(UtilityAiTaskRunStatus.Admitted));
            });

            fixture.AdmissionResults.EndLogicStep();
        }

        [Test]
        public void UtilityAiOrderResultSystem_LostKeepRunningTerminal_FailsExplicitly()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                keepRunningUntilFinished: true);
            Entity actor = fixture.AddActor(runtime);
            OrderTypeRegistry orderTypes = CreateUtilityOrderTypes(fixture.TerminalResults);
            var orderBuffer = CreateOrderBufferSystem(fixture, orderTypes);
            var results = new UtilityAiOrderResultSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.AdmissionResults,
                fixture.TerminalResults);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            int orderId = fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId;
            orderBuffer.Update(0f);
            results.Update(0f);
            fixture.World.Get<OrderBuffer>(actor).ClearActive();

            var ex = Assert.Throws<InvalidOperationException>(() => results.Update(0f));
            Assert.That(ex!.Message, Does.Contain(UtilityAiOrderResultSystem.MissingTerminalResultError));
            Assert.That(ex.Message, Does.Contain($"orderId={orderId}"));

            fixture.AdmissionResults.EndLogicStep();
        }

        [TestCase(true, false, "GraphProgramRegistry")]
        [TestCase(false, true, "IGraphRuntimeApi")]
        public void UtilityAiDecisionSystem_GraphScoreMissingService_FailsDuringAssembly(
            bool omitGraphRegistry,
            bool omitGraphApi,
            string expectedService)
        {
            using var fixture = RuntimeFixture.Create();
            const int graphId = 716;
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 0.75f }
            };
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                graphScoreProgram: program,
                graphId: graphId);
            var graphs = new GraphProgramRegistry();
            graphs.Register(graphId, program, GraphKind.Score);
            var graphApi = new GasGraphRuntimeApi(fixture.World);

            var ex = Assert.Throws<InvalidOperationException>(() => CreateDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.Spatial,
                fixture.Abilities,
                omitGraphRegistry ? null : graphs,
                omitGraphApi ? null : graphApi,
                fixture.Orders));

            Assert.That(ex!.Message, Does.Contain("GraphScore"));
            Assert.That(ex.Message, Does.Contain(expectedService));
            Assert.That(ex.Message, Does.Contain("assembly"));
        }

        [Test]
        public void UtilityAiRuntime_GraphScoreExecution_IsAllocationFree()
        {
            using var fixture = RuntimeFixture.Create();
            const int graphId = 717;
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 0.75f }
            };
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                graphScoreProgram: program,
                graphId: graphId);
            Entity actor = fixture.AddActor(runtime);
            Entity target = fixture.CreateHostile(300, 0);
            var graphApi = new GasGraphRuntimeApi(fixture.World);
            UtilityAiGraphScoreProgramDefinition frozenProgram = runtime.GraphScorePrograms[0];

            var warmupBudget = new GraphInstructionBudget(16);
            _ = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecutePrevalidatedScore(
                fixture.World,
                actor,
                target,
                default,
                frozenProgram.Program,
                graphApi,
                ref warmupBudget,
                out _);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();

            var measuredBudget = new GraphInstructionBudget(16);
            GraphExecutionStatus status = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecutePrevalidatedScore(
                fixture.World,
                actor,
                target,
                default,
                frozenProgram.Program,
                graphApi,
                ref measuredBudget,
                out float score);

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(status, Is.EqualTo(GraphExecutionStatus.Completed));
            Assert.That(measuredBudget.Consumed, Is.EqualTo(1));
            Assert.That(score, Is.EqualTo(0.75f));
        }

        [Test]
        public void UtilityAiDecisionSystem_MaxCandidatesIsTotalAcrossDecisionsAndFilters_ExhaustionFailsClosed()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            _ = fixture.CreateHostile(600, 0);
            UtilityAiCompiledRuntime runtime = fixture.CreateTwoDecisionRuntime(
                lowPriority: 1,
                highPriority: 10,
                firstMinDurationSteps: 0,
                firstDecisionRepeatDelaySteps: 0,
                secondDecisionRepeatDelaySteps: 0,
                maxCandidates: 3);
            Entity actor = fixture.AddActor(runtime);

            fixture.RunDecision(runtime);

            UtilityAiDecisionTrace first = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Orders.Count, Is.EqualTo(0));
                Assert.That(first.ThinkOutcome, Is.EqualTo((int)UtilityAiThinkOutcome.CandidateBudgetExhausted));
                Assert.That(first.CandidateCount, Is.EqualTo(3));
                Assert.That(first.CandidateLimit, Is.EqualTo(3));
                Assert.That(first.BestDecisionId, Is.EqualTo(-1));
                Assert.That(first.LastSubmittedOrderId, Is.EqualTo(0));
            });

            ref UtilityAiState state = ref fixture.World.Get<UtilityAiState>(actor);
            state.NextThinkStep = 0;
            fixture.RunDecision(runtime);

            UtilityAiDecisionTrace second = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(second.ThinkOutcome, Is.EqualTo(first.ThinkOutcome));
                Assert.That(second.CandidateCount, Is.EqualTo(first.CandidateCount));
                Assert.That(second.CandidateLimit, Is.EqualTo(first.CandidateLimit));
                Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void UtilityAiDecisionSystem_MaxCandidates_AllRawTargetsRejectedByFilter_ExhaustsBeforeInspectingNextTarget()
        {
            using var fixture = RuntimeFixture.Create();
            for (int i = 0; i < 4; i++)
            {
                Entity friendly = fixture.CreateHostile(300 + i * 100, 0);
                fixture.World.Get<Team>(friendly).Id = 1;
            }

            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                maxResults: 8,
                maxCandidates: 3);
            Entity actor = fixture.AddActor(runtime);

            fixture.RunDecision(runtime);

            UtilityAiDecisionTrace trace = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Orders.Count, Is.EqualTo(0));
                Assert.That(trace.ThinkOutcome, Is.EqualTo((int)UtilityAiThinkOutcome.CandidateBudgetExhausted));
                Assert.That(trace.CandidateCount, Is.EqualTo(3));
                Assert.That(trace.CandidateLimit, Is.EqualTo(3));
                Assert.That(trace.LastFilterRejectReason, Is.EqualTo((int)UtilityAiFilterRejectReason.Relationship));
                Assert.That(trace.BestDecisionId, Is.EqualTo(-1));
            });
        }

        [Test]
        public void UtilityAiDecisionSystem_GraphScoreTotalBudget_ExactActualInstructionBoundarySucceeds()
        {
            using var fixture = RuntimeFixture.Create();
            const int graphId = 7181;
            GraphInstruction[] program = CreateJumpingScoreProgram();
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                maxResults: 16,
                maxCandidates: 2,
                maxGraphScoreInstructions: 6,
                graphScoreProgram: program,
                graphId: graphId);
            Entity actor = fixture.AddActor(runtime);
            _ = fixture.CreateHostile(300, 0);
            _ = fixture.CreateHostile(600, 0);
            var graphs = new GraphProgramRegistry();
            graphs.Register(graphId, program, GraphKind.Score);
            var graphApi = new GasGraphRuntimeApi(fixture.World);
            var decision = CreateDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.Spatial,
                fixture.Abilities,
                graphs,
                graphApi,
                fixture.Orders);

            decision.Update(1f / 60f);

            UtilityAiDecisionTrace trace = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Orders.Count, Is.EqualTo(1));
                Assert.That(trace.ThinkOutcome, Is.EqualTo((int)UtilityAiThinkOutcome.CandidateSelected));
                Assert.That(trace.CandidateCount, Is.EqualTo(2));
                Assert.That(trace.GraphScoreInstructionCount, Is.EqualTo(6));
                Assert.That(trace.GraphScoreInstructionLimit, Is.EqualTo(6));
            });
        }

        [Test]
        public void UtilityAiDecisionSystem_GraphScoreTotalBudget_AcrossShortExecutionsExhaustsAndFailsClosed()
        {
            using var fixture = RuntimeFixture.Create();
            const int graphId = 7182;
            GraphInstruction[] program = CreateJumpingScoreProgram();
            UtilityAiCompiledRuntime runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                maxResults: 16,
                maxCandidates: 2,
                maxGraphScoreInstructions: 5,
                graphScoreProgram: program,
                graphId: graphId);
            Entity actor = fixture.AddActor(runtime);
            _ = fixture.CreateHostile(300, 0);
            _ = fixture.CreateHostile(600, 0);
            var graphs = new GraphProgramRegistry();
            graphs.Register(graphId, program, GraphKind.Score);
            var graphApi = new GasGraphRuntimeApi(fixture.World);
            var decision = CreateDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                fixture.Spatial,
                fixture.Abilities,
                graphs,
                graphApi,
                fixture.Orders);

            decision.Update(1f / 60f);

            UtilityAiDecisionTrace trace = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Orders.Count, Is.EqualTo(0));
                Assert.That(trace.ThinkOutcome, Is.EqualTo((int)UtilityAiThinkOutcome.GraphScoreInstructionBudgetExhausted));
                Assert.That(trace.CandidateCount, Is.EqualTo(2));
                Assert.That(trace.GraphScoreInstructionCount, Is.EqualTo(5));
                Assert.That(trace.GraphScoreInstructionLimit, Is.EqualTo(5));
                Assert.That(trace.BestDecisionId, Is.EqualTo(-1));
                Assert.That(trace.LastSubmittedOrderId, Is.EqualTo(0));
            });
        }

        [Test]
        public void GraphExecutor_SharedInstructionBudget_BackwardLoopCountsRepeatedInstructionsBeforePerExecutionFuse()
        {
            using var fixture = RuntimeFixture.Create();
            Entity actor = fixture.AddActor(fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0));
            Entity target = fixture.CreateHostile(300, 0);
            var graphApi = new GasGraphRuntimeApi(fixture.World);
            var budget = new GraphInstructionBudget(7);
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = -1 }
            };

            GraphExecutionStatus status = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor.ExecuteScore(
                fixture.World,
                actor,
                target,
                default,
                program,
                graphApi,
                GraphKind.Score,
                ref budget,
                out float score);

            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo(GraphExecutionStatus.InstructionBudgetExhausted));
                Assert.That(budget.Consumed, Is.EqualTo(7));
                Assert.That(budget.Consumed, Is.LessThan(GraphVmLimits.MaxInstructionsPerExecution));
                Assert.That(score, Is.EqualTo(0f));
            });
        }

        [Test]
        public void UtilityAiDecisionSystem_RuntimeReload_RebuildsTargetScratchCapacity()
        {
            using var fixture = RuntimeFixture.Create();
            UtilityAiCompiledRuntime smallRuntime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                maxResults: 1,
                maxCandidates: 1);
            UtilityAiCompiledRuntime rebuiltRuntime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                maxResults: 4,
                maxCandidates: 4);
            Entity actor = fixture.AddActor(smallRuntime);
            _ = fixture.CreateHostile(300, 0);
            var runtimeSource = new UtilityAiRuntimeSource(smallRuntime);
            var graphs = new GraphProgramRegistry();
            var eligibility = new AbilityActivationEligibilityQuery(
                fixture.World,
                fixture.Abilities,
                new TagOps(
                    new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                    new TagRuleRegistry()),
                graphs,
                graphApi: null,
                progressionRequirements: null,
                actuatorGate: new UtilityAiAbilityActuatorGate(fixture.World, runtimeSource));
            var decision = new UtilityAiDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtimeSource,
                fixture.Spatial,
                fixture.Abilities,
                graphs,
                graphApi: null,
                fixture.Orders,
                eligibility);

            decision.Update(1f / 60f);

            UtilityAiDecisionTrace beforeReload = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(
                    beforeReload.ThinkOutcome,
                    Is.EqualTo((int)UtilityAiThinkOutcome.TargetScratchCapacityExhausted));
                Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            });

            runtimeSource.Update(rebuiltRuntime);
            fixture.World.Get<UtilityAiState>(actor).NextThinkStep = 0;
            decision.Update(1f / 60f);

            UtilityAiDecisionTrace afterReload = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.Multiple(() =>
            {
                Assert.That(afterReload.ThinkOutcome, Is.EqualTo((int)UtilityAiThinkOutcome.CandidateSelected));
                Assert.That(afterReload.CandidateCount, Is.EqualTo(1));
                Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            });
        }

        private static GraphInstruction[] CreateJumpingScoreProgram()
        {
            return new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 0.75f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 0.25f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 }
            };
        }

        [Test]
        public void UtilityAiRuntime_10kEntities_UsesDeterministicBoundedCandidatesAndZeroAllocation()
        {
            using var fixture = RuntimeFixture.Create(orderCapacity: 20000);
            var runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                abilityId: fixture.AttackAbilityId,
                decisionRepeatDelaySteps: 0,
                maxResults: 10050,
                maxCandidates: 32);
            Entity actor = fixture.AddActor(runtime);
            for (int i = 0; i < 10_000; i++)
            {
                int x = (i % 100) * 20;
                int y = (i / 100) * 20;
                _ = fixture.CreateHostile(x, y);
            }

            var decision = CreateDecisionSystem(fixture.World, fixture.Clock, runtime, fixture.Spatial, fixture.Abilities, new GraphProgramRegistry(), null, fixture.Orders);
            decision.Update(1f / 60f);
            UtilityAiDecisionTrace first = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            ref var actorState = ref fixture.World.Get<UtilityAiState>(actor);
            actorState.NextThinkStep = 0;
            decision.Update(1f / 60f);
            UtilityAiDecisionTrace second = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            actorState.NextThinkStep = 0;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();

            decision.Update(1f / 60f);

            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            double elapsedMs = (stop - start) * 1000d / Stopwatch.Frequency;
            UtilityAiDecisionTrace measured = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Console.WriteLine(
                $"[UtilityAI] 10k bounded think elapsed={elapsedMs:F3}ms allocated={allocated} candidates={measured.CandidateCount}/{measured.CandidateLimit}");
            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.EqualTo(0));
                Assert.That(fixture.Orders.Count, Is.EqualTo(0));
                Assert.That(measured.ThinkOutcome, Is.EqualTo((int)UtilityAiThinkOutcome.CandidateBudgetExhausted));
                Assert.That(measured.CandidateCount, Is.EqualTo(32));
                Assert.That(measured.CandidateLimit, Is.EqualTo(32));
                Assert.That(second.ThinkOutcome, Is.EqualTo(first.ThinkOutcome));
                Assert.That(second.CandidateCount, Is.EqualTo(first.CandidateCount));
                Assert.That(measured.ThinkOutcome, Is.EqualTo(second.ThinkOutcome));
                Assert.That(measured.CandidateCount, Is.EqualTo(second.CandidateCount));
            });
        }

        private static OrderTypeRegistry CreateUtilityOrderTypes(
            OrderTerminalResultBuffer terminalResults,
            bool requireEntityBlackboard = false,
            SameTypePolicy sameTypePolicy = SameTypePolicy.Replace,
            int pendingBufferWindowMs = 0)
        {
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "attackTarget",
                OrderTypeId = 102,
                Priority = 100,
                BufferWindowMs = 0,
                PendingBufferWindowMs = pendingBufferWindowMs,
                SameTypePolicy = sameTypePolicy,
                QueueFullPolicy = QueueFullPolicy.RejectNew,
                MaxQueueSize = 1,
                QueuedModeMaxSize = 1,
                AllowQueuedMode = true,
                ClearQueueOnActivate = true,
                EntityBlackboardKey = requireEntityBlackboard ? 1 : -1,
                SpatialBlackboardKey = -1,
                IntArg0BlackboardKey = -1
            });
            return orderTypes;
        }

        private static OrderBufferSystem CreateOrderBufferSystem(
            RuntimeFixture fixture,
            OrderTypeRegistry orderTypes)
        {
            return new OrderBufferSystem(
                fixture.World,
                fixture.Clock,
                orderTypes,
                new OrderRuleRegistry(),
                fixture.AdmissionResults,
                fixture.Orders,
                stepRateHz: 30);
        }

        private sealed class RuntimeFixture : IDisposable
        {
            private RuntimeFixture(
                World world,
                DiscreteClock clock,
                OrderAdmissionResultBuffer admissionResults,
                OrderQueue orders,
                OrderTerminalResultBuffer terminalResults,
                AbilityDefinitionRegistry abilities,
                ChunkedGridSpatialPartitionWorld partition,
                WorldSizeSpec spec,
                SpatialQueryService spatial,
                int attackAbilityId)
            {
                World = world;
                Clock = clock;
                AdmissionResults = admissionResults;
                Orders = orders;
                TerminalResults = terminalResults;
                Abilities = abilities;
                Partition = partition;
                Spec = spec;
                Spatial = spatial;
                AttackAbilityId = attackAbilityId;
            }

            public World World { get; }
            public DiscreteClock Clock { get; }
            public OrderAdmissionResultBuffer AdmissionResults { get; }
            public OrderQueue Orders { get; }
            public OrderTerminalResultBuffer TerminalResults { get; }
            public AbilityDefinitionRegistry Abilities { get; }
            public ChunkedGridSpatialPartitionWorld Partition { get; }
            public WorldSizeSpec Spec { get; }
            public SpatialQueryService Spatial { get; }
            public int AttackAbilityId { get; }
            public Entity Actor { get; private set; }

            public static RuntimeFixture Create(
                int orderCapacity = 64,
                int terminalCapacity = OrderTerminalResultBuffer.DefaultCapacity)
            {
                var world = World.Create();
                var clock = new DiscreteClock();
                var admissionResults = new OrderAdmissionResultBuffer(orderCapacity, orderCapacity);
                var orders = new OrderQueue(orderCapacity, admissionResults);
                var terminalResults = new OrderTerminalResultBuffer(terminalCapacity);
                AbilityIdRegistry.Clear();
                TagRegistry.Clear();
                int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
                var abilities = new AbilityDefinitionRegistry();
                abilities.Register(attackAbilityId, new AbilityDefinition());
                TeamManager.Clear();
                TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
                var partition = new ChunkedGridSpatialPartitionWorld(64, initialChunkCapacity: 2048);
                var spec = new WorldSizeSpec(new WorldAabbCm(-1000, -1000, 220000, 220000), 100);
                var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
                spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());
                return new RuntimeFixture(
                    world,
                    clock,
                    admissionResults,
                    orders,
                    terminalResults,
                    abilities,
                    partition,
                    spec,
                    spatial,
                    attackAbilityId);
            }

            public Entity AddActor(
                UtilityAiCompiledRuntime runtime,
                int currentDecisionId = -1,
                int decisionStartedStep = 0,
                int repeatDelayDecisionId = -1,
                int decisionRepeatDelayUntilStep = 0)
            {
                Actor = World.Create(
                    new UtilityAiAgent { ProfileId = 0 },
                    new UtilityAiState
                    {
                        CurrentDecisionId = currentDecisionId,
                        DecisionStartedStep = decisionStartedStep,
                        RepeatDelayDecisionId = repeatDelayDecisionId,
                        DecisionRepeatDelayUntilStep = decisionRepeatDelayUntilStep,
                        NextThinkStep = 0
                    },
                    new UtilityAiDecisionTrace(),
                    new UtilityAiCombatMemory(),
                    new OrderBuffer { ActiveIndex = -1 },
                    new AbilityStateBuffer(),
                    new Team { Id = 1 },
                    WorldPositionCm.FromCm(0, 0));
                ref var abilityBuffer = ref World.Get<AbilityStateBuffer>(Actor);
                abilityBuffer.AddAbility(AttackAbilityId);
                Partition.Add(Actor, 0, 0);
                return Actor;
            }

            public Entity CreateHostile(int x, int y)
            {
                var target = World.Create(
                    new Team { Id = 2 },
                    WorldPositionCm.FromCm(x, y),
                    new OrderBuffer { ActiveIndex = -1 });
                Partition.Add(target, x / Spec.GridCellSizeCm, y / Spec.GridCellSizeCm);
                return target;
            }

            public void RunDecision(UtilityAiCompiledRuntime runtime)
            {
                var decision = CreateDecisionSystem(World, Clock, runtime, Spatial, Abilities, new GraphProgramRegistry(), null, Orders);
                decision.Update(1f / 60f);
            }

            public UtilityAiCompiledRuntime CreateSingleDecisionRuntime(
                int orderTypeId,
                int abilityId,
                int decisionRepeatDelaySteps,
                int maxResults = 64,
                int maxCandidates = -1,
                int maxGraphScoreInstructions = 4096,
                GraphInstruction[]? graphScoreProgram = null,
                int graphId = 0,
                bool keepRunningUntilFinished = false,
                bool includeActuator = false,
                UtilityAiTaskKind taskKind = UtilityAiTaskKind.SubmitOrder)
            {
                GameplayTagContainer noTags = default;
                bool useGraphScore = graphScoreProgram != null;
                UtilityAiGraphScoreProgramDefinition[] graphScorePrograms = useGraphScore
                    ? new[]
                    {
                        new UtilityAiGraphScoreProgramDefinition(
                            graphId,
                            graphScoreProgram,
                            "UtilityAiRuntimeTests.CreateSingleDecisionRuntime")
                    }
                    : Array.Empty<UtilityAiGraphScoreProgramDefinition>();
                int candidateLimit = maxCandidates > 0 ? maxCandidates : maxResults;
                return new UtilityAiCompiledRuntime(
                    new[] { new UtilityAiProfileDefinition(0, 1, 1, candidateLimit, maxGraphScoreInstructions, -1) },
                    new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                    new[]
                    {
                        new UtilityAiDecisionDefinition(0, 0, 1, 0, 5, 1f, 1f, 0f, 0, decisionRepeatDelaySteps, abilityId, 0, keepRunningUntilFinished)
                    },
                    new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                    new[] { new UtilityAiTargetFilterDefinition(0, 2, maxResults) },
                    new[]
                    {
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 250000, 0, RelationshipFilter.All, in noTags),
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                    },
                    new[]
                    {
                        new UtilityAiInputDefinition(
                            useGraphScore ? UtilityAiInputKind.GraphScore : UtilityAiInputKind.DistanceToTarget,
                            0,
                            useGraphScore ? graphId : 0)
                    },
                    new[]
                    {
                        new UtilityAiNormalizationDefinition(
                            useGraphScore ? UtilityAiNormalizationKind.Identity : UtilityAiNormalizationKind.RangeInverse,
                            0f,
                            useGraphScore ? 1f : 250000f)
                    },
                    new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                    new[] { new UtilityAiTaskDefinition(taskKind, orderTypeId, abilityId, 0, (int)OrderSubmitMode.Immediate, 0, -1, 0) },
                    Array.Empty<UtilityAiStanceDefinition>(),
                    includeActuator
                        ? new[] { new UtilityAiActuatorDefinition(0, abilityId, readinessInputId: -1, aimGateInputId: -1) }
                        : Array.Empty<UtilityAiActuatorDefinition>(),
                    graphScorePrograms: graphScorePrograms);
            }

            public UtilityAiCompiledRuntime CreateTwoDecisionRuntime(
                int lowPriority,
                int highPriority,
                int firstMinDurationSteps,
                int firstDecisionRepeatDelaySteps,
                int secondDecisionRepeatDelaySteps,
                int maxCandidates = 64)
            {
                GameplayTagContainer noTags = default;
                return new UtilityAiCompiledRuntime(
                    new[] { new UtilityAiProfileDefinition(0, 1, 1, maxCandidates, 4096, -1) },
                    new[] { new UtilityAiDecisionMakerDefinition(0, 2, UtilityAiSelectionMode.FixedPriority, 0f) },
                    new[]
                    {
                        new UtilityAiDecisionDefinition(0, 0, 1, 0, lowPriority, 1f, 1f, 0f, firstMinDurationSteps, firstDecisionRepeatDelaySteps, AttackAbilityId, 0, false),
                        new UtilityAiDecisionDefinition(1, 0, 1, 1, highPriority, 1f, 1f, 0f, 0, secondDecisionRepeatDelaySteps, AttackAbilityId, 0, false)
                    },
                    new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                    new[]
                    {
                        new UtilityAiTargetFilterDefinition(0, 2, 64),
                        new UtilityAiTargetFilterDefinition(0, 2, 64)
                    },
                    new[]
                    {
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 250000, 0, RelationshipFilter.All, in noTags),
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                    },
                    new[] { new UtilityAiInputDefinition(UtilityAiInputKind.Constant, 1, 0) },
                    new[] { new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.Identity, 0f, 1f) },
                    new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                    new[]
                    {
                        new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 201, AttackAbilityId, 0, (int)OrderSubmitMode.Immediate, 0, -1, 0),
                        new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, 202, AttackAbilityId, 0, (int)OrderSubmitMode.Immediate, 0, -1, 0)
                    },
                    Array.Empty<UtilityAiStanceDefinition>(),
                    Array.Empty<UtilityAiActuatorDefinition>());
            }

            public void Dispose()
            {
                World.Destroy(World);
            }
        }
    }
}
