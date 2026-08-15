using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Tests;
using Ludots.Tests.GAS.Production;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Integration.ProductionWiring
{
    [TestFixture]
    public sealed class GasProductionWiringIntegrationTests
    {
        [Test]
        public void GraphOutputs_WhenOwnerVersionRetires_ReclaimsSlotsAndInvalidatesHandles()
        {
            using World world = World.Create();
            var keys = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var values = new GraphOutputValueStore(keys, initialCapacity: 2);
            Entity retiredOwner = world.Create();
            GraphOutputValueHandle retiredHandle = values.SetInt(retiredOwner, "score", 7);
            values.SetInt(retiredOwner, "score", 8);
            Assert.That(values.TryGetView(retiredHandle, out GraphOutputValueView updated), Is.True);
            Assert.That(updated.IntValue, Is.EqualTo(8));

            var cleanup = new GraphOutputValueCleanupSystem(world, values);
            world.Destroy(retiredOwner);
            float dt = 0f;
            cleanup.Update(in dt);

            Assert.That(values.ActiveCount, Is.Zero);
            Assert.That(values.TryGetView(retiredHandle, out _), Is.False);

            Entity currentOwner = world.Create();
            GraphOutputValueHandle currentHandle = values.SetInt(currentOwner, "score", 11);
            Assert.That(values.ActiveCount, Is.EqualTo(1));
            Assert.That(values.TryGetView(currentHandle, out GraphOutputValueView current), Is.True);
            Assert.That(current.IntValue, Is.EqualTo(11));
            Assert.That(values.TryGetView(retiredHandle, out _), Is.False);
        }

        [Test]
        public void GraphOutputs_AfterHistoricalPeak_CleanupWorkReturnsToCurrentRetirements()
        {
            using World world = World.Create();
            var keys = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var values = new GraphOutputValueStore(keys, initialCapacity: 1_024);
            var cleanup = new GraphOutputValueCleanupSystem(world, values);
            var peakOwners = new Entity[512];

            for (int i = 0; i < peakOwners.Length; i++)
            {
                Entity owner = world.Create();
                peakOwners[i] = owner;
                values.SetInt(owner, "score", i);
                values.SetBool(owner, "ready", true);
            }

            for (int i = 0; i < peakOwners.Length; i++)
            {
                world.Destroy(peakOwners[i]);
            }

            float dt = 0f;
            cleanup.Update(in dt);
            Assert.That(cleanup.RetiredOwnersProcessedLastUpdate, Is.EqualTo(peakOwners.Length));
            Assert.That(cleanup.ReleasedLastUpdate, Is.EqualTo(peakOwners.Length * 2));
            Assert.That(values.ActiveCount, Is.Zero);

            Entity steadyOwner = world.Create();
            values.SetInt(steadyOwner, "score", 1);
            cleanup.Update(in dt);

            Assert.That(cleanup.RetiredOwnersProcessedLastUpdate, Is.Zero);
            Assert.That(cleanup.ReleasedLastUpdate, Is.Zero);
            Assert.That(values.ActiveCount, Is.EqualTo(1));
        }

        [Test]
        public void GraphOutputs_WhenOwnersRetireInBatch_CleanupVisitsEachOwnerAndOutputOnce()
        {
            using World world = World.Create();
            var keys = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var values = new GraphOutputValueStore(keys, initialCapacity: 128);
            var cleanup = new GraphOutputValueCleanupSystem(world, values);
            var owners = new Entity[32];

            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = world.Create();
                owners[i] = owner;
                values.SetInt(owner, "a", i);
                values.SetInt(owner, "b", i);
                values.SetInt(owner, "c", i);
                values.SetInt(owner, "d", i);
            }

            for (int i = 0; i < owners.Length; i++)
            {
                world.Destroy(owners[i]);
            }

            float dt = 0f;
            cleanup.Update(in dt);

            Assert.That(cleanup.RetiredOwnersProcessedLastUpdate, Is.EqualTo(owners.Length));
            Assert.That(cleanup.ReleasedLastUpdate, Is.EqualTo(owners.Length * 4));
            Assert.That(values.ActiveCount, Is.Zero);
        }

        [Test]
        public void GraphOutputs_DestroyAndCleanupHotPath_AllocatesZeroAfterWarmup()
        {
            using World world = World.Create();
            var keys = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            int scoreKey = keys.Register("score");
            var values = new GraphOutputValueStore(keys, initialCapacity: 16);
            var cleanup = new GraphOutputValueCleanupSystem(world, values);
            float dt = 0f;

            for (int i = 0; i < 32; i++)
            {
                Entity owner = world.Create();
                values.SetInt(owner, scoreKey, i);
                world.Destroy(owner);
                cleanup.Update(in dt);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1_000; i++)
            {
                Entity owner = world.Create();
                values.SetInt(owner, scoreKey, i);
                world.Destroy(owner);
                cleanup.Update(in dt);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.LessThanOrEqualTo(64));
            Assert.That(values.ActiveCount, Is.Zero);
        }

        [Test]
        public void GasBudgetReport_PublishesPerFrameStructuredBudgetAndAdmissionOverflowDiagnostics()
        {
            var budget = new GasBudget();
            budget.Reset();
            budget.ResponseQueueOverflowDropped = 3;
            budget.ActiveEffectContainerAttachDropped = 2;

            var admissions = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 1);
            var outcome = new OrderAdmissionOutcome(1, 1, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.Queued);
            var rejected = new OrderAdmissionOutcome(2, 1, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.RejectedQueueFull);
            var blackboardCapacityRejected = new OrderAdmissionOutcome(3, 1, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.RejectedBlackboardCapacity);
            var missingBlackboardRejected = new OrderAdmissionOutcome(4, 1, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.RejectedMissingBlackboard);
            Assert.That(admissions.TryWrite(in outcome), Is.True);
            Assert.That(admissions.TryWrite(in rejected), Is.False);
            Assert.That(admissions.TryWrite(in blackboardCapacityRejected), Is.False);
            Assert.That(admissions.TryWrite(in missingBlackboardRejected), Is.False);

            var diagnostics = new GasDiagnosticEventBuffer(capacity: 16);
            var report = new GasBudgetReportSystem(budget, diagnostics, admissions);
            float dt = 0f;
            report.Update(in dt);

            Assert.That(diagnostics.FrameIndex, Is.EqualTo(budget.FrameIndex));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.ResponseQueueOverflow, out GasDiagnosticEvent response), Is.True);
            Assert.That(response.System, Is.EqualTo(GasDiagnosticSystem.ResponseChain));
            Assert.That(response.Count, Is.EqualTo(3));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.ActiveEffectContainerAttachDropped, out GasDiagnosticEvent attach), Is.True);
            Assert.That(attach.Count, Is.EqualTo(2));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.OrderAdmissionResultOverflow, out GasDiagnosticEvent admission), Is.True);
            Assert.That(admission.System, Is.EqualTo(GasDiagnosticSystem.OrderAdmission));
            Assert.That(admission.Capacity, Is.EqualTo(1));
            Assert.That(admission.Count, Is.EqualTo(3));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.OrderAdmissionResultBacklog, out GasDiagnosticEvent backlog), Is.True);
            Assert.That(backlog.Capacity, Is.EqualTo(2));
            Assert.That(backlog.Count, Is.EqualTo(1));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.OrderAdmissionResultHighWatermark, out GasDiagnosticEvent highWatermark), Is.True);
            Assert.That(highWatermark.Capacity, Is.EqualTo(2));
            Assert.That(highWatermark.Count, Is.EqualTo(1));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.OrderRejectedQueueFull, out GasDiagnosticEvent rejectedQueue), Is.True);
            Assert.That(rejectedQueue.System, Is.EqualTo(GasDiagnosticSystem.OrderAdmission));
            Assert.That(rejectedQueue.Count, Is.EqualTo(1));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.OrderRejectedBlackboardCapacity, out GasDiagnosticEvent rejectedCapacity), Is.True);
            Assert.That(rejectedCapacity.System, Is.EqualTo(GasDiagnosticSystem.OrderAdmission));
            Assert.That(rejectedCapacity.Count, Is.EqualTo(1));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.OrderRejectedMissingBlackboard, out GasDiagnosticEvent rejectedMissing), Is.True);
            Assert.That(rejectedMissing.System, Is.EqualTo(GasDiagnosticSystem.OrderAdmission));
            Assert.That(rejectedMissing.Count, Is.EqualTo(1));
        }

        [Test]
        public void GasBudgetReport_PublishesEveryAdmissionCapacityRejectionFromFormalResultStorage()
        {
            var admissions = new OrderAdmissionResultBuffer(capacity: 1, rejectionCapacity: 2);
            var queue = new OrderQueue(64, admissions);
            var first = new Order { OrderTypeId = 2 };
            var second = new Order { OrderTypeId = 2 };
            var third = new Order { OrderTypeId = 2 };
            Assert.That(queue.SubmitAssigned(ref first), Is.EqualTo(OrderSubmitResult.Queued));
            Assert.That(queue.SubmitAssigned(ref second), Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            Assert.That(queue.SubmitAssigned(ref third), Is.EqualTo(OrderSubmitResult.RejectedAdmissionCapacity));
            var diagnostics = new GasDiagnosticEventBuffer(capacity: 8);
            var report = new GasBudgetReportSystem(new GasBudget(), diagnostics, admissions);
            float dt = 0f;

            report.Update(in dt);

            Assert.That(
                Find(diagnostics, GasDiagnosticMetric.OrderRejectedAdmissionCapacity, out GasDiagnosticEvent rejected),
                Is.True);
            Assert.That(rejected.Count, Is.EqualTo(2));
            Assert.That(
                Find(diagnostics, GasDiagnosticMetric.OrderAdmissionResultBacklog, out GasDiagnosticEvent backlog),
                Is.True);
            Assert.That(backlog.Capacity, Is.EqualTo(3));
            Assert.That(backlog.Count, Is.EqualTo(3));
        }

        [Test]
        public void CoreBootstrap_RegistersCompleteGraphAndDiagnosticProductionServices()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));

            Assert.That(engine.GetService(CoreServiceKeys.GasGraphRuntimeProductionServices), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.GasGraphRuntimeApi), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.GraphProgramRegistry), Is.Not.Null);
            GraphFunctionCatalog functions = engine.GetService(CoreServiceKeys.GraphFunctionCatalog);
            GraphActionCatalog actions = engine.GetService(CoreServiceKeys.GraphActionCatalog);
            Assert.That(functions, Is.Not.Null);
            Assert.That(actions, Is.Not.Null);
            Assert.That(functions.Require("demo.const.seven").GraphId, Is.GreaterThan(0));
            Assert.That(actions.Require("bt.patrol", GraphActionHost.BehaviorTree), Is.GreaterThan(0));
            Assert.That(actions.Require("level.phaseAdvance", GraphActionHost.Level), Is.GreaterThan(0));
            GraphOutputValueStore graphOutputValues = engine.GetService(CoreServiceKeys.GraphOutputValueStore);
            Assert.That(graphOutputValues, Is.Not.Null);
            Assert.That(
                graphOutputValues.Capacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.GraphOutputValueCapacity));
            Assert.That(engine.GetService(CoreServiceKeys.GasDiagnosticEventBuffer), Is.Not.Null);
            OrderAdmissionResultBuffer admissionResults = engine.GetService(CoreServiceKeys.OrderAdmissionResultBuffer);
            Assert.That(admissionResults, Is.Not.Null);
            Assert.That(
                admissionResults.Capacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.OrderAdmissionResultCapacity));
            Assert.That(
                admissionResults.RejectionCapacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.OrderAdmissionRejectionCapacity));
            Assert.That(
                engine.GetService(CoreServiceKeys.OrderQueue).Capacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.OrderQueueCapacity));
            Assert.That(
                engine.GetService(CoreServiceKeys.ChainOrderQueue).Capacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.ResponseChainOrderQueueCapacity));
            Assert.That(engine.GetService(CoreServiceKeys.OrderTerminalResultBuffer), Is.Not.Null);
            DirtyEntityQueue dirtyEntities = engine.GetService(CoreServiceKeys.DirtyEntityQueue);
            Assert.That(dirtyEntities, Is.Not.Null);
            Assert.That(dirtyEntities.Capacity, Is.EqualTo(16_384));
            ProjectileRuntimeSystem projectiles =
                CapabilityStandardShowcaseTestHarness.FindSystem<ProjectileRuntimeSystem>(
                    engine,
                    SystemGroup.EffectProcessing);
            Assert.That(
                projectiles.CollisionCandidateCapacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.ProjectileCollisionCandidateCapacity));
            Assert.That(
                projectiles.RuntimeEntityCapacity,
                Is.EqualTo(engine.MergedConfig.GasRuntimeCapacity.ProjectileRuntimeEntityCapacity));
        }

        [Test]
        public void CoreBootstrap_DerivedAttributeGraph_RunsThroughEngineOwnedGraphApi()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(
                    repoRoot,
                    new[] { "LudotsCoreMod", "DerivedAttributeGraphAcceptanceMod" }),
                Path.Combine(repoRoot, "assets"));

            GasGraphRuntimeApi graphApi = engine.GetService(CoreServiceKeys.GasGraphRuntimeApi);
            GraphProgramRegistry programs = engine.GetService(CoreServiceKeys.GraphProgramRegistry);
            Assert.That(graphApi, Is.Not.Null);
            Assert.That(programs, Is.Not.Null);

            int sourceAttributeId = AttributeRegistry.Register("tests.production-derived-graph.source");
            int doubledAttributeId = AttributeRegistry.Register("tests.production-derived-graph.doubled");
            int offsetAttributeId = AttributeRegistry.Register("tests.production-derived-graph.offset");
            Assert.That(sourceAttributeId, Is.LessThan(AttributeBuffer.MAX_ATTRS));
            Assert.That(doubledAttributeId, Is.LessThan(AttributeBuffer.MAX_ATTRS));
            Assert.That(offsetAttributeId, Is.LessThan(AttributeBuffer.MAX_ATTRS));
            int graphId = GraphIdRegistry.GetId("Tests.DerivedAttributeGraph.EngineOwned");
            Assert.That(graphId, Is.GreaterThan(0));
            Assert.That(programs.TryGetProgram(graphId, out _), Is.True);
            var attributes = new AttributeBuffer();
            attributes.SetBase(sourceAttributeId, 10f);
            var binding = new AttributeDerivedGraphBinding();
            binding.Add(graphId);
            Entity entity = engine.World.Create(
                attributes,
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags(),
                binding);

            var simulationLoop = engine.GetService(CoreServiceKeys.SimulationLoopController);
            engine.Start();
            simulationLoop.Step();
            Assert.DoesNotThrow(() =>
            {
                for (int frame = 0; frame < 16 && engine.World.Has<AttributeAggregateDirty>(entity); frame++)
                {
                    engine.Tick(1f / 60f);
                }
            });

            ref AttributeBuffer result = ref engine.World.Get<AttributeBuffer>(entity);
            Assert.That(result.GetCurrent(doubledAttributeId), Is.EqualTo(20f));
            Assert.That(result.GetCurrent(offsetAttributeId), Is.EqualTo(13f));
            Assert.That(engine.World.Has<AttributeAggregateDirty>(entity), Is.False);
        }

        [Test]
        public void CoreBootstrap_WiresFiniteGasWorkBudgetsIntoRuntimeState()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));

            AbilityExecRuntimeState ability = ReadSingle<AbilityExecRuntimeState>(engine.World);
            GasRuntimeState effects = ReadSingle<GasRuntimeState>(engine.World);
            Assert.That(ability.MaxWorkUnitsPerSlice, Is.EqualTo(4096));
            Assert.That(effects.EffectProcessingMaxWorkUnitsPerSlice, Is.EqualTo(4096));
            Assert.That(ability.MaxWorkUnitsPerSlice, Is.LessThan(int.MaxValue));
            Assert.That(effects.EffectProcessingMaxWorkUnitsPerSlice, Is.LessThan(int.MaxValue));
        }

        private static bool Find(
            GasDiagnosticEventBuffer diagnostics,
            GasDiagnosticMetric metric,
            out GasDiagnosticEvent value)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Metric == metric)
                {
                    value = diagnostics[i];
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static T ReadSingle<T>(World world) where T : struct
        {
            var query = new QueryDescription().WithAll<T>();
            T value = default;
            int count = 0;
            foreach (ref var chunk in world.Query(in query))
            {
                var values = chunk.GetSpan<T>();
                foreach (int i in chunk)
                {
                    value = values[i];
                    count++;
                }
            }

            Assert.That(count, Is.EqualTo(1), $"Expected exactly one {typeof(T).Name} runtime state.");
            return value;
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
