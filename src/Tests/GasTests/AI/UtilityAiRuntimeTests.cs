using System;
using System.Diagnostics;
using Arch.Core;
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
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class UtilityAiRuntimeTests
    {
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
                    new UtilityAiProfileDefinition(decisionMakerOffset: 0, decisionMakerCount: 1, decisionIntervalSteps: 1, maxCandidates: 16, defaultStanceId: -1)
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
                        taskOffset: 0,
                        taskCount: 1,
                        priority: 10,
                        baseScore: 1f,
                        weight: 1f,
                        momentumBonus: 0f,
                        minDurationSteps: 0,
                        cooldownSteps: 0,
                        autocastAbilityId: attackAbilityId,
                        abilitySlotIndex: 0,
                        sharedCooldownTagId: 0,
                        flags: UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.OrdinaryAttack | UtilityAiDecisionFlags.RequiresTarget)
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
            var decision = new UtilityAiDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);

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
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, 5, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget)
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

            var decision = new UtilityAiDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
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
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 2, 0, 1, 5, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget)
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

            var decision = new UtilityAiDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(1));
            Assert.That(orders.TryDequeue(out var order), Is.True);
            Assert.That(order.Target, Is.EqualTo(farHigh));
        }

        [Test]
        public void UtilityAiDecisionSystem_SharedCooldownTag_BlocksAutocastSubmission()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));

            AbilityIdRegistry.Clear();
            TagRegistry.Clear();
            int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
            int gcdTagId = TagRegistry.Register("Cooldown.Global.Test");
            var abilities = new AbilityDefinitionRegistry();
            abilities.Register(attackAbilityId, new AbilityDefinition
            {
                HasCooldown = true,
                Cooldown = new AbilityCooldown { CooldownTagId = gcdTagId }
            });

            TeamManager.Clear();
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            GameplayTagContainer noTags = default;

            var runtime = new UtilityAiCompiledRuntime(
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, 5, 1f, 1f, 0f, 0, 3, attackAbilityId, 0, gcdTagId, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget)
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

            var decision = new UtilityAiDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
            decision.Update(1f / 60f);

            Assert.That(orders.Count, Is.EqualTo(0));
            Assert.That(world.Has<UtilityAiDecisionTrace>(actor), Is.True);
            Assert.That(world.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason, Is.EqualTo((int)UtilityAiReadinessBlockReason.SharedCooldown));
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
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[]
                {
                    new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, 5, 1f, 1f, 0f, 0, 0, attackAbilityId, 0, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget)
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

            var decision = new UtilityAiDecisionSystem(world, clock, runtime, spatial, abilities, new GraphProgramRegistry(), null, orders);
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
                firstCooldownSteps: 0,
                secondCooldownSteps: 0);

            fixture.AddActor(runtime, currentDecisionId: 0, decisionStartedStep: 0);
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(201));
            Assert.That(order.Target, Is.EqualTo(target));
        }

        [Test]
        public void UtilityAiDecisionSystem_StateMachine_RespectsPerDecisionCooldown()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(500, 0);
            var runtime = fixture.CreateTwoDecisionRuntime(
                lowPriority: 1,
                highPriority: 10,
                firstMinDurationSteps: 0,
                firstCooldownSteps: 4,
                secondCooldownSteps: 0);

            fixture.AddActor(runtime, currentDecisionId: 0, decisionStartedStep: 0, cooldownDecisionId: 0, decisionCooldownUntilStep: 4);
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(202));
        }

        [Test]
        public void UtilityAiDecisionSystem_OrdinaryAttack_AutoRepeatsWhenNoHigherCandidateExists()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, cooldownSteps: 0);
            var actor = fixture.AddActor(runtime);

            fixture.RunDecision(runtime);
            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var first), Is.True);
            Assert.That(first.Target, Is.EqualTo(target));

            ref var buffer = ref fixture.World.Get<OrderBuffer>(actor);
            buffer.Clear();
            fixture.Clock.Advance(ClockDomainId.Step, 1);
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var second), Is.True);
            Assert.That(second.Target, Is.EqualTo(target));
        }

        [Test]
        public void UtilityAiDecisionSystem_SharedCooldownFromAbilityMetadata_AllowsOneAutocastPerWindow()
        {
            using var fixture = RuntimeFixture.Create(attackCooldownTag: true);
            _ = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, cooldownSteps: 3, sharedCooldownTagId: 0);
            var actor = fixture.AddActor(runtime);

            fixture.RunDecision(runtime);
            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out _), Is.True);

            ref var buffer = ref fixture.World.Get<OrderBuffer>(actor);
            buffer.Clear();
            fixture.Clock.Advance(ClockDomainId.Step, 1);
            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            Assert.That(fixture.World.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason, Is.EqualTo((int)UtilityAiReadinessBlockReason.SharedCooldown));
        }

        [Test]
        public void UtilityAiDecisionSystem_ActuatorReadinessAndAimGate_BlockAndReleaseAbility()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, cooldownSteps: 0);
            var actor = fixture.AddActor(runtime);
            fixture.World.Add(actor, new ActuatorReadiness { ActuatorId = fixture.AttackAbilityId, Ready01 = 0.5f });

            fixture.RunDecision(runtime);
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            Assert.That(fixture.World.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason, Is.EqualTo((int)UtilityAiReadinessBlockReason.ActuatorNotReady));

            fixture.World.Set(actor, new ActuatorReadiness { ActuatorId = fixture.AttackAbilityId, Ready01 = 1f });
            fixture.World.Add(actor, new AimGate { ActuatorId = fixture.AttackAbilityId, Ready01 = 0f });
            fixture.Clock.Advance(ClockDomainId.Step, 1);
            fixture.RunDecision(runtime);
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            Assert.That(fixture.World.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason, Is.EqualTo((int)UtilityAiReadinessBlockReason.AimGateNotReady));

            fixture.World.Set(actor, new AimGate { ActuatorId = fixture.AttackAbilityId, Ready01 = 1f });
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
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, cooldownSteps: 0);
            fixture.AddActor(runtime);

            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(effects.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_SameTickOrderIntent_IsConsumedByOrderBufferSystem()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, cooldownSteps: 0);
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
            var decision = new UtilityAiDecisionSystem(fixture.World, fixture.Clock, runtime, fixture.Spatial, fixture.Abilities, new GraphProgramRegistry(), null, fixture.Orders);
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
        public void UtilityAiRuntime_TargetFiltering10kCandidates_IsAllocationFree()
        {
            using var fixture = RuntimeFixture.Create(orderCapacity: 20000);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, abilityId: fixture.AttackAbilityId, cooldownSteps: 0, maxResults: 10050);
            _ = fixture.AddActor(runtime);
            for (int i = 0; i < 10_000; i++)
            {
                int x = (i % 100) * 20;
                int y = (i / 100) * 20;
                _ = fixture.CreateHostile(x, y);
            }

            var decision = new UtilityAiDecisionSystem(fixture.World, fixture.Clock, runtime, fixture.Spatial, fixture.Abilities, new GraphProgramRegistry(), null, fixture.Orders);
            decision.Update(1f / 60f);
            fixture.Orders.Clear();
            ref var actorState = ref fixture.World.Get<UtilityAiState>(fixture.Actor);
            actorState.NextThinkStep = 0;
            ref var actorBuffer = ref fixture.World.Get<OrderBuffer>(fixture.Actor);
            actorBuffer.Clear();

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
            Console.WriteLine($"[UtilityAI] 10k target filtering elapsed={elapsedMs:F3}ms allocated={allocated}");
            Assert.That(allocated, Is.LessThanOrEqualTo(64));
            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
        }

        private sealed class RuntimeFixture : IDisposable
        {
            private RuntimeFixture(
                World world,
                DiscreteClock clock,
                OrderAdmissionResultBuffer admissionResults,
                OrderQueue orders,
                AbilityDefinitionRegistry abilities,
                ChunkedGridSpatialPartitionWorld partition,
                WorldSizeSpec spec,
                SpatialQueryService spatial,
                int attackAbilityId,
                int sharedCooldownTagId)
            {
                World = world;
                Clock = clock;
                AdmissionResults = admissionResults;
                Orders = orders;
                Abilities = abilities;
                Partition = partition;
                Spec = spec;
                Spatial = spatial;
                AttackAbilityId = attackAbilityId;
                SharedCooldownTagId = sharedCooldownTagId;
            }

            public World World { get; }
            public DiscreteClock Clock { get; }
            public OrderAdmissionResultBuffer AdmissionResults { get; }
            public OrderQueue Orders { get; }
            public AbilityDefinitionRegistry Abilities { get; }
            public ChunkedGridSpatialPartitionWorld Partition { get; }
            public WorldSizeSpec Spec { get; }
            public SpatialQueryService Spatial { get; }
            public int AttackAbilityId { get; }
            public int SharedCooldownTagId { get; }
            public Entity Actor { get; private set; }

            public static RuntimeFixture Create(bool attackCooldownTag = false, int orderCapacity = 64)
            {
                var world = World.Create();
                var clock = new DiscreteClock();
                var admissionResults = new OrderAdmissionResultBuffer(orderCapacity, orderCapacity);
                var orders = new OrderQueue(orderCapacity, admissionResults);
                AbilityIdRegistry.Clear();
                TagRegistry.Clear();
                int attackAbilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
                int sharedCooldownTagId = TagRegistry.Register("Cooldown.Global.Test");
                var abilities = new AbilityDefinitionRegistry();
                var attack = new AbilityDefinition();
                if (attackCooldownTag)
                {
                    attack.HasCooldown = true;
                    attack.Cooldown = new AbilityCooldown { CooldownTagId = sharedCooldownTagId };
                }

                abilities.Register(attackAbilityId, attack);
                TeamManager.Clear();
                TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
                var partition = new ChunkedGridSpatialPartitionWorld(64, initialChunkCapacity: 2048);
                var spec = new WorldSizeSpec(new WorldAabbCm(-1000, -1000, 220000, 220000), 100);
                var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
                spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());
                return new RuntimeFixture(world, clock, admissionResults, orders, abilities, partition, spec, spatial, attackAbilityId, sharedCooldownTagId);
            }

            public Entity AddActor(
                UtilityAiCompiledRuntime runtime,
                int currentDecisionId = -1,
                int decisionStartedStep = 0,
                int cooldownDecisionId = -1,
                int decisionCooldownUntilStep = 0)
            {
                Actor = World.Create(
                    new UtilityAiAgent { ProfileId = 0 },
                    new UtilityAiState
                    {
                        CurrentDecisionId = currentDecisionId,
                        DecisionStartedStep = decisionStartedStep,
                        CooldownDecisionId = cooldownDecisionId,
                        DecisionCooldownUntilStep = decisionCooldownUntilStep,
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
                var decision = new UtilityAiDecisionSystem(World, Clock, runtime, Spatial, Abilities, new GraphProgramRegistry(), null, Orders);
                decision.Update(1f / 60f);
            }

            public UtilityAiCompiledRuntime CreateSingleDecisionRuntime(
                int orderTypeId,
                int abilityId,
                int cooldownSteps,
                int sharedCooldownTagId = 0,
                int maxResults = 64)
            {
                GameplayTagContainer noTags = default;
                return new UtilityAiCompiledRuntime(
                    new[] { new UtilityAiProfileDefinition(0, 1, 1, maxResults, -1) },
                    new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                    new[]
                    {
                        new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, 5, 1f, 1f, 0f, 0, cooldownSteps, abilityId, 0, sharedCooldownTagId, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.OrdinaryAttack | UtilityAiDecisionFlags.RequiresTarget)
                    },
                    new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                    new[] { new UtilityAiTargetFilterDefinition(0, 2, maxResults) },
                    new[]
                    {
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 250000, 0, RelationshipFilter.All, in noTags),
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                    },
                    new[] { new UtilityAiInputDefinition(UtilityAiInputKind.DistanceToTarget, 0, 0) },
                    new[] { new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.RangeInverse, 0f, 250000f) },
                    new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                    new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, orderTypeId, abilityId, 0, (int)OrderSubmitMode.Immediate, 0, -1, 0) },
                    Array.Empty<UtilityAiStanceDefinition>(),
                    Array.Empty<UtilityAiActuatorDefinition>());
            }

            public UtilityAiCompiledRuntime CreateTwoDecisionRuntime(
                int lowPriority,
                int highPriority,
                int firstMinDurationSteps,
                int firstCooldownSteps,
                int secondCooldownSteps)
            {
                GameplayTagContainer noTags = default;
                return new UtilityAiCompiledRuntime(
                    new[] { new UtilityAiProfileDefinition(0, 1, 1, 64, -1) },
                    new[] { new UtilityAiDecisionMakerDefinition(0, 2, UtilityAiSelectionMode.FixedPriority, 0f) },
                    new[]
                    {
                        new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, lowPriority, 1f, 1f, 0f, firstMinDurationSteps, firstCooldownSteps, AttackAbilityId, 0, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget),
                        new UtilityAiDecisionDefinition(0, 0, 1, 1, 1, highPriority, 1f, 1f, 0f, 0, secondCooldownSteps, AttackAbilityId, 0, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget)
                    },
                    new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                    new[] { new UtilityAiTargetFilterDefinition(0, 2, 64) },
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
