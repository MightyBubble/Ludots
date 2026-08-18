using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.UI.PanelActivation;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// ShowPanel/HidePanel graph ops (#1014 contract five): any graph — Script,
    /// level blueprint, effect — fires these ops and the panel responds. The UI
    /// never decides when; it records the decision.
    /// </summary>
    [TestFixture]
    public sealed class PanelShowHideOpTests
    {
        private const string PanelTypeKey = "tests.panel.entity_attributes";

        [SetUp]
        public void SetUp()
        {
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
        }

        private static (World World, Entity Caster, GraphProgramRegistry Programs, UiPanelActivationStore Store, GasGraphRuntimeApi Api) Build()
        {
            World world = World.Create();
            Entity caster = world.Create();

            var store = new UiPanelActivationStore();
            var api = new GasGraphRuntimeApi(world);
            api.BindPanelActivation(new PanelActivationApi(store));

            var programs = new GraphProgramRegistry();
            return (world, caster, programs, store, api);
        }

        /// <summary>Script graph: ShowPanel(entity_attributes) → HaltReturnInt(1).</summary>
        private static GraphInstruction[] ShowPanelProgram(int panelKeySymbolIndex) => new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.ShowPanel, Imm = panelKeySymbolIndex },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        };

        private static GraphInstruction[] HidePanelProgram(int panelKeySymbolIndex) => new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.HidePanel, Imm = panelKeySymbolIndex },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
        };

        [Test]
        public void ShowPanelOp_MakesPanelVisible()
        {
            (World world, Entity caster, GraphProgramRegistry programs, UiPanelActivationStore store, GasGraphRuntimeApi api) = Build();
            using (world)
            {
                int graphId = GraphIdRegistry.Register("tests.script.show_panel");
                programs.Register(graphId, ShowPanelProgram(0), GraphKind.Script);

                int[] ints = new int[GraphVmLimits.MaxIntRegisters];
                byte[] bools = new byte[GraphVmLimits.MaxBoolRegisters];
                int[] callStack = new int[GraphVmLimits.MaxCallStackDepth];
                var cursor = new GraphExecutionCursor();
                GraphInstruction[] program = programs.RequireProgramArray(graphId, GraphKind.Script, "panel test");

                // Patch the panel type symbol
                GraphProgramSymbolPatcher.Patch(
                    new[] { PanelTypeKey },
                    program,
                    new ThrowingResolver());

                GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                    programs, program, ints, bools, callStack, ref cursor, 64, world, caster, caster, api);

                Assert.That(store.IsVisible(PanelTypeKey), Is.True,
                    "ShowPanel op executed → panel must be visible");
            }
        }

        [Test]
        public void HidePanelOp_HidesPreviouslyShownPanel()
        {
            (World world, Entity caster, GraphProgramRegistry programs, UiPanelActivationStore store, GasGraphRuntimeApi api) = Build();
            using (world)
            {
                int showId = GraphIdRegistry.Register("tests.script.show_panel");
                programs.Register(showId, ShowPanelProgram(0), GraphKind.Script);
                int hideId = GraphIdRegistry.Register("tests.script.hide_panel");
                programs.Register(hideId, HidePanelProgram(0), GraphKind.Script);

                int[] ints = new int[GraphVmLimits.MaxIntRegisters];
                byte[] bools = new byte[GraphVmLimits.MaxBoolRegisters];
                int[] callStack = new int[GraphVmLimits.MaxCallStackDepth];

                GraphInstruction[] show = programs.RequireProgramArray(showId, GraphKind.Script, "show");
                GraphProgramSymbolPatcher.Patch(new[] { PanelTypeKey }, show, new ThrowingResolver());
                var cursor = new GraphExecutionCursor();
                GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                    programs, show, ints, bools, callStack, ref cursor, 64, world, caster, caster, api);
                Assert.That(store.IsVisible(PanelTypeKey), Is.True);

                GraphInstruction[] hide = programs.RequireProgramArray(hideId, GraphKind.Script, "hide");
                GraphProgramSymbolPatcher.Patch(new[] { PanelTypeKey }, hide, new ThrowingResolver());
                cursor = new GraphExecutionCursor();
                GraphExecutor.ExecuteResolvedRegisteredScriptSlice(
                    programs, hide, ints, bools, callStack, ref cursor, 64, world, caster, caster, api);
                Assert.That(store.IsVisible(PanelTypeKey), Is.False,
                    "HidePanel op after ShowPanel → panel must be hidden");
            }
        }

        [Test]
        public void DirectApiCall_ShowHideWithoutGraph()
        {
            (World world, Entity caster, _, UiPanelActivationStore store, GasGraphRuntimeApi api) = Build();
            using (world)
            {
                api.ShowPanel(ConfigKeyRegistry.Register(PanelTypeKey));
                Assert.That(store.IsVisible(PanelTypeKey), Is.True);

                api.HidePanel(ConfigKeyRegistry.Register(PanelTypeKey));
                Assert.That(store.IsVisible(PanelTypeKey), Is.False);
            }
        }

        [Test]
        public void RegionHost_ReconcilesLeaseDiff()
        {
            (World world, Entity caster, _, UiPanelActivationStore store, GasGraphRuntimeApi api) = Build();
            using (world)
            {
                var host = new PanelRegionHost();
                api.ShowPanel(ConfigKeyRegistry.Register(PanelTypeKey));
                var (activated, _) = host.Reconcile(store);
                Assert.That(activated, Does.Contain(PanelTypeKey));

                // Idempotent: reconcile again, no change
                var (none, noneOff) = host.Reconcile(store);
                Assert.That(none, Is.Empty);
                Assert.That(noneOff, Is.Empty);

                api.HidePanel(ConfigKeyRegistry.Register(PanelTypeKey));
                var (_, deactivated) = host.Reconcile(store);
                Assert.That(deactivated, Does.Contain(PanelTypeKey));
            }
        }

        private sealed class ThrowingResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw new InvalidOperationException($"Panel ops must not reference tags; got '{name}'.");
            public int ResolveAttribute(string name) => throw new InvalidOperationException($"Panel ops must not reference attributes; got '{name}'.");
            public int ResolveEffectTemplate(string name) => throw new InvalidOperationException($"Panel ops must not reference effect templates; got '{name}'.");
            public int ResolveRelationshipType(string name) => throw new InvalidOperationException($"Panel ops must not reference relationship types; got '{name}'.");
            public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException($"Panel ops must not reference relationship metrics; got '{name}'.");
            public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException($"Panel ops must not reference relationship flags; got '{name}'.");
            public int ResolveRelationshipReason(string name) => throw new InvalidOperationException($"Panel ops must not reference relationship reasons; got '{name}'.");
            public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException($"Panel ops must not reference dispatch presets; got '{name}'.");
            public int ResolveEntityTemplate(string name) => throw new InvalidOperationException($"Panel ops must not reference entity templates; got '{name}'.");
        }
    }
}
