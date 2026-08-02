using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Systems;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map.Hex;
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
            using var fixture = RuntimeFixture.Create();
            var nearEnemy = fixture.CreateHostile(400, 0);
            _ = fixture.CreateHostile(900, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102);
            fixture.AddActor();

            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(102));
            Assert.That(order.Target, Is.EqualTo(nearEnemy));
            Assert.That(order.Args.I0, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_FixedPrioritySelectsOnlyHostileCandidates()
        {
            using var fixture = RuntimeFixture.Create();
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Friendly);
            _ = fixture.CreateTarget(teamId: 3, x: 200, y: 0);
            var hostile = fixture.CreateHostile(800, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, inputKind: UtilityAiInputKind.Constant);
            fixture.AddActor();

            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.Target, Is.EqualTo(hostile));
        }

        [Test]
        public void UtilityAiDecisionSystem_PriorityBucketThenDistance_SelectsHigherPriorityTarget()
        {
            using var fixture = RuntimeFixture.Create();
            GameplayTagContainer noTags = default;
            var nearLow = fixture.CreateHostile(200, 0, new UtilityAiTargetPriority { Bucket = 1 });
            var farHigh = fixture.CreateHostile(900, 0, new UtilityAiTargetPriority { Bucket = 5 });
            var runtime = new UtilityAiCompiledRuntime(
                new[] { new UtilityAiProfileDefinition(0, 1, 1, 16, 64, -1) },
                new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                new[] { new UtilityAiDecisionDefinition(0, 0, 2, 0, 1, 5, 1f, 1f, 0f, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget) },
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
                new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, AiOrderPayloadKind.CastAbility, 102, 0, (int)OrderSubmitMode.Immediate, 0) },
                Array.Empty<UtilityAiStanceDefinition>(),
                Array.Empty<UtilityAiActuatorDefinition>());
            fixture.AddActor();

            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.Target, Is.EqualTo(farHigh));
            Assert.That(order.Target, Is.Not.EqualTo(nearLow));
        }

        [Test]
        public void UtilityAiDecisionSystem_DoesNotPreBlockOnSourceLockoutTag_SubmitsOrderForGasPath()
        {
            using var fixture = RuntimeFixture.Create();
            int lockoutTagId = TagRegistry.Register("Cooldown.Global.Test");
            GameplayTagContainer actorTags = default;
            actorTags.AddTag(lockoutTagId);
            _ = fixture.CreateHostile(500, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, inputKind: UtilityAiInputKind.Constant);
            fixture.AddActor(actorTags: actorTags);

            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(102));
        }

        [Test]
        public void UtilityAiDecisionSystem_StateMachine_RespectsCurrentDecisionMinimumDuration()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(500, 0);
            var runtime = fixture.CreateTwoDecisionRuntime(
                lowPriority: 1,
                highPriority: 10,
                firstMinDurationSteps: 5);
            fixture.AddActor(currentDecisionId: 0, decisionStartedStep: 0);

            fixture.RunDecision(runtime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.OrderTypeId, Is.EqualTo(201));
            Assert.That(order.Target, Is.EqualTo(target));
        }

        [Test]
        public void UtilityAiDecisionSystem_OrderReceiptRejectedByEntityIntake_BlocksTaskAndClearsSubmittedOrder()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, inputKind: UtilityAiInputKind.Constant);
            var actor = fixture.AddActor(withOrderBuffer: false);
            var orderTypes = fixture.CreateOrderTypes(orderTypeId: 102);
            var orderBuffer = new OrderBufferSystem(
                fixture.World,
                fixture.Clock,
                orderTypes,
                new OrderRuleRegistry(),
                fixture.AdmissionResults,
                fixture.Orders,
                stepRateHz: 30);

            fixture.RunDecision(runtime);

            int submittedOrderId = fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId;
            Assert.That(submittedOrderId, Is.GreaterThan(0));
            Assert.That(fixture.Orders.Count, Is.EqualTo(1));

            fixture.AdmissionResults.BeginLogicStep();
            orderBuffer.Update(1f / 60f);
            fixture.Clock.Advance(ClockDomainId.Step, 1);
            fixture.RunDecision(runtime);

            ref var state = ref fixture.World.Get<UtilityAiState>(actor);
            Assert.That(state.LastSubmittedOrderId, Is.EqualTo(0));
            Assert.That(state.CurrentTaskStatus, Is.EqualTo((byte)UtilityAiTaskRunStatus.Blocked));
            Assert.That(fixture.World.Get<UtilityAiDecisionTrace>(actor).LastTaskStatus, Is.EqualTo((int)UtilityAiTaskRunStatus.Blocked));
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_CandidateBudgetStopsThinkLoopWithoutSilentZeroScore()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(100, 0);
            _ = fixture.CreateHostile(200, 0);
            _ = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                inputKind: UtilityAiInputKind.Constant,
                maxResults: 3,
                maxCandidates: 1);
            fixture.AddActor();

            fixture.RunDecision(runtime);

            var trace = fixture.World.Get<UtilityAiDecisionTrace>(fixture.Actor);
            Assert.That(trace.CandidateCount, Is.EqualTo(0));
            Assert.That(trace.LastFilterRejectReason, Is.EqualTo((int)UtilityAiFilterRejectReason.CandidateBudgetExhausted));
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_CandidateBudgetBoundsTargetDiscoveryWork()
        {
            using var fixture = RuntimeFixture.Create();
            for (int i = 0; i < 10; i++)
            {
                _ = fixture.CreateHostile(100 + i * 20, 0);
            }

            var runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                inputKind: UtilityAiInputKind.Constant,
                maxResults: 10_000,
                maxCandidates: 1);
            fixture.AddActor();
            var boundedSpatial = new RecordingSpatialQueryService(fixture.Spatial);
            var decision = new UtilityAiDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                boundedSpatial,
                graphScorer: null,
                fixture.Orders,
                fixture.TerminalResults);

            decision.Update(1f / 60f);

            var trace = fixture.World.Get<UtilityAiDecisionTrace>(fixture.Actor);
            Assert.That(boundedSpatial.LastRadiusBufferLength, Is.EqualTo(1));
            Assert.That(trace.CandidateCount, Is.EqualTo(0));
            Assert.That(trace.LastFilterRejectReason, Is.EqualTo((int)UtilityAiFilterRejectReason.CandidateBudgetExhausted));
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_TooManyCandidates_WaitsForCompleteEvaluationBeforeActing()
        {
            using var fixture = RuntimeFixture.Create();
            var nearest = fixture.CreateHostile(100, 0);
            _ = fixture.CreateHostile(200, 0);
            _ = fixture.CreateHostile(300, 0);
            var exhaustedRuntime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                inputKind: UtilityAiInputKind.Constant,
                maxResults: 3,
                maxCandidates: 1);
            fixture.AddActor();

            fixture.RunDecision(exhaustedRuntime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
            var exhaustedTrace = fixture.World.Get<UtilityAiDecisionTrace>(fixture.Actor);
            Assert.That(exhaustedTrace.LastFilterRejectReason, Is.EqualTo((int)UtilityAiFilterRejectReason.CandidateBudgetExhausted));

            fixture.Clock.Advance(ClockDomainId.Step, 1);
            var completeRuntime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                inputKind: UtilityAiInputKind.Constant,
                maxResults: 4,
                maxCandidates: 4);

            fixture.RunDecision(completeRuntime);

            Assert.That(fixture.Orders.Count, Is.EqualTo(1));
            Assert.That(fixture.Orders.TryDequeue(out var order), Is.True);
            Assert.That(order.Target, Is.EqualTo(nearest));
        }

        [Test]
        public void UtilityAiDecisionSystem_TruncatedSpatialCandidateScan_DoesNotSubmitPartialOrder()
        {
            using var fixture = RuntimeFixture.Create();
            var firstVisible = fixture.CreateHostile(100, 0);
            _ = fixture.CreateHostile(200, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(
                orderTypeId: 102,
                inputKind: UtilityAiInputKind.Constant,
                maxResults: 8,
                maxCandidates: 8);
            fixture.AddActor();
            var decision = new UtilityAiDecisionSystem(
                fixture.World,
                fixture.Clock,
                runtime,
                new TruncatedSpatialQueryService(firstVisible),
                graphScorer: null,
                fixture.Orders,
                fixture.TerminalResults);

            decision.Update(1f / 60f);

            var trace = fixture.World.Get<UtilityAiDecisionTrace>(fixture.Actor);
            Assert.That(trace.CandidateCount, Is.EqualTo(0));
            Assert.That(trace.LastFilterRejectReason, Is.EqualTo((int)UtilityAiFilterRejectReason.CandidateBudgetExhausted));
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_ScoreGraphBudgetExhaustionIsObservable()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(100, 0);
            const int graphId = 3001;
            fixture.Graphs.Register(
                graphId,
                new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f } },
                GraphKind.Score);
            var graphScorer = CompiledGraphScoreRuntime.Compile(fixture.World, fixture.GraphApi, fixture.Graphs);
            var runtime = fixture.CreateGraphScoreRuntime(
                orderTypeId: 102,
                graphId: graphId,
                maxCandidates: 2,
                graphConsiderationCount: 2,
                graphScoreInstructionBudget: 1);
            fixture.AddActor();

            fixture.RunDecision(runtime, graphScorer);

            var trace = fixture.World.Get<UtilityAiDecisionTrace>(fixture.Actor);
            Assert.That(trace.CandidateCount, Is.EqualTo(1));
            Assert.That(trace.LastFilterRejectReason, Is.EqualTo((int)UtilityAiFilterRejectReason.ScoreGraphBudgetExhausted));
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_SameTickOrderIntent_IsConsumedByOrderBufferSystem()
        {
            using var fixture = RuntimeFixture.Create();
            var target = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102);
            var actor = fixture.AddActor();
            var orderTypes = fixture.CreateOrderTypes(orderTypeId: 102);
            var spatialUpdate = new SpatialPartitionUpdateSystem(fixture.World, fixture.Partition, fixture.Spec);
            var decision = fixture.CreateDecisionSystem(runtime);
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
        public void UtilityAiDecisionSystem_ReadsOrderTerminalOutcomeAfterSeveralFramesAndReleasesLedgerSlot()
        {
            using var fixture = RuntimeFixture.Create();
            _ = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102);
            var actor = fixture.AddActor();
            var orderTypes = fixture.CreateOrderTypes(orderTypeId: 102);
            var orderBuffer = new OrderBufferSystem(
                fixture.World,
                fixture.Clock,
                orderTypes,
                new OrderRuleRegistry(),
                fixture.AdmissionResults,
                fixture.Orders,
                stepRateHz: 30);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            int orderId = fixture.World.Get<UtilityAiState>(actor).LastSubmittedOrderId;
            Assert.That(orderId, Is.GreaterThan(0));
            orderBuffer.Update(1f / 60f);

            Assert.That(OrderSubmitter.NotifyOrderComplete(fixture.World, actor, orderTypes), Is.True);
            for (int i = 0; i < 3; i++)
            {
                fixture.TerminalResults.Clear();
                fixture.Clock.Advance(ClockDomainId.Step, 1);
            }

            fixture.RunDecision(runtime);

            var state = fixture.World.Get<UtilityAiState>(actor);
            var trace = fixture.World.Get<UtilityAiDecisionTrace>(actor);
            Assert.That(state.LastSubmittedOrderId, Is.EqualTo(0));
            Assert.That(state.CurrentTaskStatus, Is.EqualTo((byte)UtilityAiTaskRunStatus.Complete));
            Assert.That(trace.LastTaskStatus, Is.EqualTo((int)UtilityAiTaskRunStatus.Complete));
            Assert.That(fixture.TerminalResults.LedgerCount, Is.EqualTo(0));
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiDecisionSystem_MultipleAgentsWaitOnIndependentTerminalOutcomesWithoutLeakingLedgerSlots()
        {
            using var fixture = RuntimeFixture.Create(orderCapacity: 8);
            _ = fixture.CreateHostile(300, 0);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102);
            var first = fixture.AddActor();
            var second = fixture.AddActor();
            var orderTypes = fixture.CreateOrderTypes(orderTypeId: 102);
            var orderBuffer = new OrderBufferSystem(
                fixture.World,
                fixture.Clock,
                orderTypes,
                new OrderRuleRegistry(),
                fixture.AdmissionResults,
                fixture.Orders,
                stepRateHz: 30);

            fixture.AdmissionResults.BeginLogicStep();
            fixture.RunDecision(runtime);
            Assert.That(fixture.World.Get<UtilityAiState>(first).LastSubmittedOrderId, Is.GreaterThan(0));
            Assert.That(fixture.World.Get<UtilityAiState>(second).LastSubmittedOrderId, Is.GreaterThan(0));
            orderBuffer.Update(1f / 60f);

            Assert.That(OrderSubmitter.NotifyOrderComplete(fixture.World, first, orderTypes), Is.True);
            Assert.That(OrderSubmitter.NotifyOrderComplete(fixture.World, second, orderTypes), Is.True);
            Assert.That(fixture.TerminalResults.LedgerCount, Is.EqualTo(2));
            fixture.TerminalResults.Clear();
            fixture.Clock.Advance(ClockDomainId.Step, 1);

            fixture.RunDecision(runtime);

            Assert.That(fixture.World.Get<UtilityAiState>(first).CurrentTaskStatus, Is.EqualTo((byte)UtilityAiTaskRunStatus.Complete));
            Assert.That(fixture.World.Get<UtilityAiState>(second).CurrentTaskStatus, Is.EqualTo((byte)UtilityAiTaskRunStatus.Complete));
            Assert.That(fixture.TerminalResults.LedgerCount, Is.EqualTo(0));
            Assert.That(fixture.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void UtilityAiRuntime_TargetFiltering10kCandidates_IsAllocationFree()
        {
            using var fixture = RuntimeFixture.Create(orderCapacity: 20000);
            var runtime = fixture.CreateSingleDecisionRuntime(orderTypeId: 102, maxResults: 10050, maxCandidates: 10050);
            _ = fixture.AddActor();
            for (int i = 0; i < 10_000; i++)
            {
                int x = (i % 100) * 20;
                int y = (i / 100) * 20;
                _ = fixture.CreateHostile(x, y);
            }

            var decision = fixture.CreateDecisionSystem(runtime);
            decision.Update(1f / 60f);
            fixture.Orders.Clear();
            ref var actorState = ref fixture.World.Get<UtilityAiState>(fixture.Actor);
            actorState.LastSubmittedOrderId = 0;
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
                OrderTerminalResultBuffer terminalResults,
                OrderQueue orders,
                ChunkedGridSpatialPartitionWorld partition,
                WorldSizeSpec spec,
                SpatialQueryService spatial,
                GraphProgramRegistry graphs,
                StubGraphApi graphApi)
            {
                World = world;
                Clock = clock;
                AdmissionResults = admissionResults;
                TerminalResults = terminalResults;
                Orders = orders;
                Partition = partition;
                Spec = spec;
                Spatial = spatial;
                Graphs = graphs;
                GraphApi = graphApi;
            }

            public World World { get; }
            public DiscreteClock Clock { get; }
            public OrderAdmissionResultBuffer AdmissionResults { get; }
            public OrderTerminalResultBuffer TerminalResults { get; }
            public OrderQueue Orders { get; }
            public ChunkedGridSpatialPartitionWorld Partition { get; }
            public WorldSizeSpec Spec { get; }
            public SpatialQueryService Spatial { get; }
            public GraphProgramRegistry Graphs { get; }
            public StubGraphApi GraphApi { get; }
            public Entity Actor { get; private set; }

            public static RuntimeFixture Create(int orderCapacity = 64)
            {
                var world = World.Create();
                var clock = new DiscreteClock();
                var admissionResults = new OrderAdmissionResultBuffer(orderCapacity, orderCapacity);
                var terminalResults = new OrderTerminalResultBuffer(orderCapacity);
                var orders = new OrderQueue(orderCapacity, admissionResults);
                TagRegistry.Clear();
                TeamManager.Clear();
                TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
                var partition = new ChunkedGridSpatialPartitionWorld(64, initialChunkCapacity: 2048);
                var spec = new WorldSizeSpec(new WorldAabbCm(-1000, -1000, 220000, 220000), 100);
                var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
                spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());
                var graphs = new GraphProgramRegistry();
                var graphApi = new StubGraphApi(world);
                return new RuntimeFixture(world, clock, admissionResults, terminalResults, orders, partition, spec, spatial, graphs, graphApi);
            }

            public Entity AddActor(
                int currentDecisionId = -1,
                int decisionStartedStep = 0,
                bool withOrderBuffer = true,
                GameplayTagContainer actorTags = default)
            {
                Actor = World.Create(
                    new UtilityAiAgent { ProfileId = 0 },
                    new UtilityAiState
                    {
                        CurrentDecisionId = currentDecisionId,
                        DecisionStartedStep = decisionStartedStep,
                        NextThinkStep = 0
                    },
                    new UtilityAiDecisionTrace(),
                    new UtilityAiCombatMemory(),
                    new Team { Id = 1 },
                    actorTags,
                    WorldPositionCm.FromCm(0, 0));
                if (withOrderBuffer)
                {
                    World.Add(Actor, new OrderBuffer { ActiveIndex = -1 });
                }

                Partition.Add(Actor, 0, 0);
                return Actor;
            }

            public Entity CreateHostile(int x, int y)
                => CreateTarget(2, x, y);

            public Entity CreateHostile<T>(int x, int y, T component)
                where T : struct
            {
                var target = World.Create(
                    new Team { Id = 2 },
                    WorldPositionCm.FromCm(x, y),
                    component,
                    new OrderBuffer { ActiveIndex = -1 });
                Partition.Add(target, x / Spec.GridCellSizeCm, y / Spec.GridCellSizeCm);
                return target;
            }

            public Entity CreateTarget(int teamId, int x, int y)
            {
                var target = World.Create(
                    new Team { Id = teamId },
                    WorldPositionCm.FromCm(x, y),
                    new OrderBuffer { ActiveIndex = -1 });
                Partition.Add(target, x / Spec.GridCellSizeCm, y / Spec.GridCellSizeCm);
                return target;
            }

            public UtilityAiDecisionSystem CreateDecisionSystem(
                UtilityAiCompiledRuntime runtime,
                IReadOnlyGraphScorer? graphScorer = null)
                => new(World, Clock, runtime, Spatial, graphScorer, Orders, TerminalResults);

            public void RunDecision(
                UtilityAiCompiledRuntime runtime,
                IReadOnlyGraphScorer? graphScorer = null)
            {
                var decision = CreateDecisionSystem(runtime, graphScorer);
                decision.Update(1f / 60f);
            }

            public OrderTypeRegistry CreateOrderTypes(int orderTypeId)
            {
                var orderTypes = new OrderTypeRegistry(TerminalResults);
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "attackTarget",
                    OrderTypeId = orderTypeId,
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
                });
                return orderTypes;
            }

            public UtilityAiCompiledRuntime CreateSingleDecisionRuntime(
                int orderTypeId,
                int abilitySlotIndex = 0,
                UtilityAiInputKind inputKind = UtilityAiInputKind.DistanceToTarget,
                int maxResults = 64,
                int maxCandidates = 64)
            {
                GameplayTagContainer noTags = default;
                return new UtilityAiCompiledRuntime(
                    new[] { new UtilityAiProfileDefinition(0, 1, 1, maxCandidates, 64, -1) },
                    new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                    new[] { new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, 5, 1f, 1f, 0f, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.OrdinaryAttack | UtilityAiDecisionFlags.RequiresTarget) },
                    new[] { new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.Multiply) },
                    new[] { new UtilityAiTargetFilterDefinition(0, 2, maxResults) },
                    new[]
                    {
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 250000, 0, RelationshipFilter.All, in noTags),
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                    },
                    new[] { new UtilityAiInputDefinition(inputKind, inputKind == UtilityAiInputKind.Constant ? 1 : 0, 0) },
                    new[] { new UtilityAiNormalizationDefinition(inputKind == UtilityAiInputKind.Constant ? UtilityAiNormalizationKind.Identity : UtilityAiNormalizationKind.RangeInverse, 0f, 250000f) },
                    new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                    new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, AiOrderPayloadKind.CastAbility, orderTypeId, abilitySlotIndex, (int)OrderSubmitMode.Immediate, 0) },
                    Array.Empty<UtilityAiStanceDefinition>(),
                    Array.Empty<UtilityAiActuatorDefinition>());
            }

            public UtilityAiCompiledRuntime CreateTwoDecisionRuntime(
                int lowPriority,
                int highPriority,
                int firstMinDurationSteps)
            {
                GameplayTagContainer noTags = default;
                return new UtilityAiCompiledRuntime(
                    new[] { new UtilityAiProfileDefinition(0, 1, 1, 64, 64, -1) },
                    new[] { new UtilityAiDecisionMakerDefinition(0, 2, UtilityAiSelectionMode.FixedPriority, 0f) },
                    new[]
                    {
                        new UtilityAiDecisionDefinition(0, 0, 1, 0, 1, lowPriority, 1f, 1f, 0f, firstMinDurationSteps, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget),
                        new UtilityAiDecisionDefinition(0, 0, 1, 1, 1, highPriority, 1f, 1f, 0f, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget)
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
                        new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, AiOrderPayloadKind.CastAbility, 201, 0, (int)OrderSubmitMode.Immediate, 0),
                        new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, AiOrderPayloadKind.CastAbility, 202, 0, (int)OrderSubmitMode.Immediate, 0)
                    },
                    Array.Empty<UtilityAiStanceDefinition>(),
                    Array.Empty<UtilityAiActuatorDefinition>());
            }

            public UtilityAiCompiledRuntime CreateGraphScoreRuntime(
                int orderTypeId,
                int graphId,
                int maxCandidates,
                int graphConsiderationCount,
                int graphScoreInstructionBudget = 64)
            {
                GameplayTagContainer noTags = default;
                var considerations = new UtilityAiConsiderationDefinition[graphConsiderationCount];
                for (int i = 0; i < considerations.Length; i++)
                {
                    considerations[i] = new UtilityAiConsiderationDefinition(0, 0, 0, 1f, UtilityAiAggregateMode.WeightedSum);
                }

                return new UtilityAiCompiledRuntime(
                    new[] { new UtilityAiProfileDefinition(0, 1, 1, maxCandidates, graphScoreInstructionBudget, -1) },
                    new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
                    new[] { new UtilityAiDecisionDefinition(0, 0, considerations.Length, 0, 1, 5, 1f, 1f, 0f, 0, UtilityAiDecisionFlags.Autocast | UtilityAiDecisionFlags.RequiresTarget) },
                    considerations,
                    new[] { new UtilityAiTargetFilterDefinition(0, 2, 16) },
                    new[]
                    {
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.SpatialRadius, 250000, 0, RelationshipFilter.All, in noTags),
                        new UtilityAiTargetFilterOpDefinition(UtilityAiTargetFilterOpKind.Relationship, 0, 0, RelationshipFilter.Hostile, in noTags)
                    },
                    new[] { new UtilityAiInputDefinition(UtilityAiInputKind.GraphScore, 0, graphId) },
                    new[] { new UtilityAiNormalizationDefinition(UtilityAiNormalizationKind.Identity, 0f, 1f) },
                    new[] { new UtilityAiCurveDefinition(UtilityAiCurveKind.Linear, 1f) },
                    new[] { new UtilityAiTaskDefinition(UtilityAiTaskKind.SubmitOrder, AiOrderPayloadKind.CastAbility, orderTypeId, 0, (int)OrderSubmitMode.Immediate, 0) },
                    Array.Empty<UtilityAiStanceDefinition>(),
                    Array.Empty<UtilityAiActuatorDefinition>());
            }

            public void Dispose()
            {
                World.Destroy(World);
            }
        }

        private sealed class StubGraphApi : IGraphRuntimeApi
        {
            private readonly World _world;

            public StubGraphApi(World world)
            {
                _world = world;
            }

            public bool TryGetGridPos(Entity entity, out IntVector2 gridPos)
            {
                if (_world.TryGet(entity, out WorldPositionCm position))
                {
                    var worldCm = position.Value.ToWorldCmInt2();
                    gridPos = new IntVector2(worldCm.X, worldCm.Y);
                    return true;
                }

                gridPos = default;
                return false;
            }

            public bool HasTag(Entity entity, int tagId)
                => _world.TryGet(entity, out GameplayTagContainer tags) && tags.HasTag(tagId);

            public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
            {
                if (_world.TryGet(entity, out AttributeBuffer buffer))
                {
                    value = buffer.GetCurrent(attributeId);
                    return true;
                }

                value = 0f;
                return false;
            }

            public SpatialQueryResult QueryRadius(IntVector2 center, float radius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryCone(IntVector2 origin, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(IntVector2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryLine(IntVector2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRange(IntVector2 center, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRing(IntVector2 center, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexNeighbors(IntVector2 center, Span<Entity> buffer) => default;
            public int GetTeamId(Entity entity) => 0;
            public uint GetEntityLayerCategory(Entity entity) => 0;
            public int GetRelationship(int teamA, int teamB) => 0;
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId) { }
            public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args) { }
            public void RemoveEffectTemplate(Entity target, int templateId) { }
            public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta) { }
            public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value) { }
            public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude) { }
            public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value) { value = 0f; return false; }
            public bool TryReadBlackboardInt(Entity entity, int keyId, out int value) { value = 0; return false; }
            public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value) { value = default; return false; }
            public void WriteBlackboardFloat(Entity entity, int keyId, float value) { }
            public void WriteBlackboardInt(Entity entity, int keyId, int value) { }
            public void WriteBlackboardEntity(Entity entity, int keyId, Entity value) { }
            public bool TryLoadConfigFloat(int keyId, out float value) { value = 0f; return false; }
            public bool TryLoadConfigInt(int keyId, out int value) { value = 0; return false; }
        }

        private sealed class TruncatedSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity _firstVisible;

            public TruncatedSpatialQueryService(Entity firstVisible)
            {
                _firstVisible = firstVisible;
            }

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer)
            {
                buffer[0] = _firstVisible;
                return new SpatialQueryResult(count: 1, dropped: 1);
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer) => default;
        }

        private sealed class RecordingSpatialQueryService : ISpatialQueryService
        {
            private readonly ISpatialQueryService _inner;

            public RecordingSpatialQueryService(ISpatialQueryService inner)
            {
                _inner = inner;
            }

            public int LastRadiusBufferLength { get; private set; }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
                => _inner.QueryAabb(in bounds, buffer);

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer)
            {
                LastRadiusBufferLength = buffer.Length;
                return _inner.QueryRadius(center, radiusCm, buffer);
            }

            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer)
                => _inner.QueryCone(origin, directionDeg, halfAngleDeg, rangeCm, buffer);

            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer)
                => _inner.QueryRectangle(center, halfWidthCm, halfHeightCm, rotationDeg, buffer);

            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
                => _inner.QueryLine(origin, directionDeg, lengthCm, halfWidthCm, buffer);

            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer)
                => _inner.QueryHexRange(center, hexRadius, buffer);

            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer)
                => _inner.QueryHexRing(center, hexRadius, buffer);
        }
    }
}
