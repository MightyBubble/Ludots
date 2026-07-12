using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GasTests
{
    [TestFixture]
    public sealed class GasProductionWiringTests
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

            world.Destroy(retiredOwner);
            var cleanup = new GraphOutputValueCleanupSystem(world, values);
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
        public void GasBudgetReport_PublishesPerFrameStructuredBudgetAndAdmissionOverflowDiagnostics()
        {
            var budget = new GasBudget();
            budget.Reset();
            budget.ResponseQueueOverflowDropped = 3;
            budget.ActiveEffectContainerAttachDropped = 2;

            var admissions = new OrderAdmissionResultBuffer(capacity: 1);
            var outcome = new OrderAdmissionOutcome(1, 1, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.Queued);
            var rejected = new OrderAdmissionOutcome(2, 1, OrderAdmissionStage.GlobalIntake, OrderSubmitResult.RejectedQueueFull);
            Assert.That(admissions.TryWrite(in outcome), Is.True);
            Assert.That(admissions.TryWrite(in rejected), Is.False);

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
            Assert.That(admission.Count, Is.EqualTo(1));
            Assert.That(Find(diagnostics, GasDiagnosticMetric.OrderRejectedQueueFull, out GasDiagnosticEvent rejectedQueue), Is.True);
            Assert.That(rejectedQueue.System, Is.EqualTo(GasDiagnosticSystem.OrderAdmission));
            Assert.That(rejectedQueue.Count, Is.EqualTo(1));
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
            Assert.That(engine.GetService(CoreServiceKeys.GraphOutputValueStore), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.GasDiagnosticEventBuffer), Is.Not.Null);
            Assert.That(engine.GetService(CoreServiceKeys.OrderAdmissionResultBuffer), Is.Not.Null);
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
