using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.UI.PanelActivation;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    [TestFixture]
    public sealed class PanelOrchestrationTests
    {
        private const string CommandSourceCountKey = "tests.ui.ctx.commandSource.count";

        // Script graph: visible = blackboard(commandSource.count) != 0.
        // E0 = LoadCaster; I1 = ReadBlackboardInt(E0, key); halt returns I1.
        private static PanelOrchestrationEntry CommandSourceGatedPanel(string panelType)
        {
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.ReadBlackboardInt, Dst = 1, A = 0, Imm = 0 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
            };
            string[] symbols = { CommandSourceCountKey };
            return new PanelOrchestrationEntry(panelType, program, symbols);
        }

        private static (World World, Entity Context, PanelOrchestrationRuntime Runtime, UiPanelActivationStore Store) Build()
        {
            var store = new UiPanelActivationStore();
            var runtime = new PanelOrchestrationRuntime(
                new[] { CommandSourceGatedPanel("panel.operations") },
                store);
            World world = World.Create();
            Entity context = world.Create();
            world.Add(context, new BlackboardIntBuffer());
            return (world, context, runtime, store);
        }

        [TearDown]
        public void TearDown()
        {
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void EvaluateAll_ContextSignalsDriveVisibility()
        {
            (World world, Entity context, PanelOrchestrationRuntime runtime, UiPanelActivationStore store) = Build();
            using (world)
            {
                int keyId = ConfigKeyRegistry.Register(CommandSourceCountKey);
                ref BlackboardIntBuffer blackboard = ref world.Get<BlackboardIntBuffer>(context);
                var api = new GasGraphRuntimeApi(world);

                blackboard.Set(keyId, 2);
                PanelActivationDiff showed = runtime.EvaluateAll(world, api, context);
                Assert.That(store.IsVisible("panel.operations"), Is.True);
                Assert.That(showed.Activated, Does.Contain("panel.operations"));

                blackboard.Set(keyId, 0);
                PanelActivationDiff hid = runtime.EvaluateAll(world, api, context);
                Assert.That(store.IsVisible("panel.operations"), Is.False);
                Assert.That(hid.Deactivated, Does.Contain("panel.operations"));
            }
        }

        [Test]
        public void RegionHost_ReconcilesLeasesWithStore()
        {
            (World world, Entity context, PanelOrchestrationRuntime runtime, UiPanelActivationStore store) = Build();
            using (world)
            {
                int keyId = ConfigKeyRegistry.Register(CommandSourceCountKey);
                ref BlackboardIntBuffer blackboard = ref world.Get<BlackboardIntBuffer>(context);
                var api = new GasGraphRuntimeApi(world);
                var host = new PanelRegionHost();

                blackboard.Set(keyId, 3);
                runtime.EvaluateAll(world, api, context);
                PanelActivationDiff first = host.Reconcile(store);
                Assert.That(first.Activated, Does.Contain("panel.operations"));
                Assert.That(host.Leased, Is.EquivalentTo(new[] { "panel.operations" }));

                PanelActivationDiff noop = host.Reconcile(store);
                Assert.That(noop.Activated, Is.Empty);
                Assert.That(noop.Deactivated, Is.Empty);

                blackboard.Set(keyId, 0);
                runtime.EvaluateAll(world, api, context);
                PanelActivationDiff released = host.Reconcile(store);
                Assert.That(released.Deactivated, Does.Contain("panel.operations"));
                Assert.That(host.Leased, Is.Empty);
            }
        }

        [Test]
        public void OrchestrationGraph_ReferencingNonConfigSymbol_FailsLoudly()
        {
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.HasTag, Dst = 1, A = 0, Imm = 0 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
            };

            Assert.That(
                () => new PanelOrchestrationRuntime(
                    new[] { new PanelOrchestrationEntry("panel.bad", program, new[] { "Some.Tag" }) },
                    new UiPanelActivationStore()),
                Throws.InvalidOperationException.With.Message.Contains("blackboard"));
        }

        [Test]
        public void WriteToken_CannotBeConstructedOutsideCore()
        {
            System.Reflection.ConstructorInfo[] ctors = typeof(PanelActivationWriteToken)
                .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(ctors, Has.Length.EqualTo(1));
            Assert.That(ctors[0].IsAssembly, Is.True,
                "Activation write token must stay Core-internal so panels/mods cannot mint it.");
        }

        [Test]
        public void EmptyContextEntity_FailsExplicitly()
        {
            (_, _, PanelOrchestrationRuntime runtime, _) = Build();
            using World world = World.Create();
            var api = new GasGraphRuntimeApi(world);

            Assert.That(
                () => runtime.EvaluateAll(world, api, Entity.Null),
                Throws.InvalidOperationException.With.Message.Contains("context entity"));
        }
    }
}
