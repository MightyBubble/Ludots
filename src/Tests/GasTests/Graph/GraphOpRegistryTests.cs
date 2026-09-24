using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GraphOpRegistryTests
    {
        [Test]
        public void Register_DuplicateNameWithDifferentOpcode_Throws()
        {
            var registry = new GraphOpRegistry();
            registry.Register("ConstFloat", (ushort)GraphNodeOp.ConstFloat);

            Throws<InvalidOperationException>(() => registry.Register("ConstFloat", (ushort)GraphNodeOp.ConstInt));
        }

        [Test]
        public void Register_AfterFreeze_Throws()
        {
            var registry = new GraphOpRegistry();
            registry.Register("ConstFloat", (ushort)GraphNodeOp.ConstFloat);
            registry.Freeze();

            Throws<InvalidOperationException>(() => registry.Register("ConstInt", (ushort)GraphNodeOp.ConstInt));
        }

        [Test]
        public void GraphCompiler_UsesInjectedRegistryAlias()
        {
            var registry = GasGraphOpRegistry.CreateMutableDefault();
            registry.Register("literal.float", (ushort)GraphNodeOp.ConstFloat);
            registry.Freeze();

            var cfg = new GraphConfig
            {
                Id = "Test.Graph.Alias",
                Kind = "Effect",
                Entry = "value",
                Nodes = new List<GraphNodeConfig>
                {
                    new GraphNodeConfig { Id = "value", Op = "literal.float", FloatValue = 3.5f }
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg, registry);

            That(pkg.HasValue, Is.True);
            for (int i = 0; i < diags.Count; i++)
            {
                That(diags[i].Severity, Is.Not.EqualTo(GraphDiagnosticSeverity.Error), diags[i].Message);
            }

            That(pkg!.Value.Program.Length, Is.EqualTo(1));
            That(pkg.Value.Program[0].Op, Is.EqualTo((ushort)GraphNodeOp.ConstFloat));
            That(pkg.Value.Program[0].ImmF, Is.EqualTo(3.5f));
        }

        [Test]
        public void CustomOp_CompilesAndExecutesThroughRegisteredHandler()
        {
            ushort customOp = (ushort)((ushort)GraphNodeOp.KnowledgeHasProjection + 1);
            var registry = GasGraphOpRegistry.CreateMutableDefault();
            registry.Register(GasGraphOpDescriptor.CreateUnary(
                "tests.addImmediateFloat",
                customOp,
                GraphValueType.Float,
                GraphValueType.Float));
            registry.Freeze();

            var handlers = GasGraphOpHandlerTable.CreateMutableDefault();
            handlers.Register(customOp, HandleAddImmediateFloat);
            handlers.Freeze();

            var cfg = new GraphConfig
            {
                Id = "Test.Graph.CustomOp",
                Kind = "Effect",
                Entry = "base",
                Nodes = new List<GraphNodeConfig>
                {
                    new GraphNodeConfig { Id = "base", Op = "ConstFloat", FloatValue = 3.5f, Next = "sum" },
                    new GraphNodeConfig
                    {
                        Id = "sum",
                        Op = "tests.addImmediateFloat",
                        Inputs = new List<string> { "base" },
                        FloatValue = 2.25f
                    }
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg, registry);

            That(pkg.HasValue, Is.True);
            for (int i = 0; i < diags.Count; i++)
            {
                That(diags[i].Severity, Is.Not.EqualTo(GraphDiagnosticSeverity.Error), diags[i].Message);
            }

            GraphInstruction[] program = pkg!.Value.Program;
            That(program.Length, Is.EqualTo(2));
            That(program[1].Op, Is.EqualTo(customOp));
            That(program[1].A, Is.EqualTo(program[0].Dst));
            That(program[1].ImmF, Is.EqualTo(2.25f));

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var state = new GraphExecutionState
            {
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = new GraphTargetList(targets)
            };

            GasGraphOpHandlerTable.Execute(ref state, program, handlers);

            That(floats[program[1].Dst], Is.EqualTo(5.75f));
            Throws<InvalidOperationException>(() => handlers.Register((ushort)(customOp + 1), HandleAddImmediateFloat));
        }

        [Test]
        public void CustomOp_WithoutHandler_FailsAtExecution()
        {
            ushort customOp = (ushort)((ushort)GraphNodeOp.KnowledgeHasProjection + 2);
            var registry = GasGraphOpRegistry.CreateMutableDefault();
            registry.Register(GasGraphOpDescriptor.Create("tests.missingHandler", customOp));
            registry.Freeze();

            var cfg = new GraphConfig
            {
                Id = "Test.Graph.CustomOpMissingHandler",
                Kind = "Effect",
                Entry = "custom",
                Nodes = new List<GraphNodeConfig>
                {
                    new GraphNodeConfig { Id = "custom", Op = "tests.missingHandler" }
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg, registry);

            That(pkg.HasValue, Is.True);
            for (int i = 0; i < diags.Count; i++)
            {
                That(diags[i].Severity, Is.Not.EqualTo(GraphDiagnosticSeverity.Error), diags[i].Message);
            }

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var state = new GraphExecutionState
            {
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = new GraphTargetList(targets)
            };

            var handlers = GasGraphOpHandlerTable.CreateMutableDefault();
            handlers.Freeze();

            try
            {
                GasGraphOpHandlerTable.Execute(ref state, pkg!.Value.Program, handlers);
                Fail("Executing a custom graph op without a registered handler should fail.");
            }
            catch (InvalidOperationException)
            {
            }
        }

        [Test]
        public void CustomOp_ResolvesButDoesNotParseAsBuiltinEnum()
        {
            ushort customOp = (ushort)((ushort)GraphNodeOp.KnowledgeHasProjection + 3);
            var registry = GasGraphOpRegistry.CreateMutableDefault();
            registry.Register(GasGraphOpDescriptor.Create("tests.rawCustom", customOp));
            registry.Freeze();

            That(GraphNodeOpParser.TryResolve("tests.rawCustom", registry, out GraphOpDescriptor descriptor, out GraphNodeOp builtin), Is.True);
            That(descriptor.OpCode, Is.EqualTo(customOp));
            That(builtin, Is.EqualTo(GraphNodeOp.None));
            That(GraphNodeOpParser.TryParse("tests.rawCustom", registry, out _), Is.False);
        }

        [Test]
        public void GraphProgramConfigLoader_UsesModContextRegisteredAlias()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphOpRegistryTests", Guid.NewGuid().ToString("N"));
            try
            {
                GraphIdRegistry.Clear();

                var opRegistry = GasGraphOpRegistry.CreateMutableDefault();
                var opHandlers = GasGraphOpHandlerTable.CreateMutableDefault();
                var vfs = new VirtualFileSystem();
                var modContext = new ModContext(
                    "TestGraphAliasMod",
                    vfs,
                    new FunctionRegistry(),
                    new TriggerManager(),
                    new SystemFactoryRegistry(),
                    new TriggerDecoratorRegistry(),
                    opRegistry,
                    opHandlers);

                That(ReferenceEquals(modContext.GraphOpRegistry, opRegistry), Is.True);
                That(ReferenceEquals(modContext.GasGraphOpHandlers, opHandlers), Is.True);
                modContext.GraphOpRegistry.Register("literal.float", (ushort)GraphNodeOp.ConstFloat);

                string coreRoot = Path.Combine(tempRoot, "Core");
                string graphDir = Path.Combine(coreRoot, "Configs", "GAS");
                Directory.CreateDirectory(graphDir);
                File.WriteAllText(Path.Combine(graphDir, "graphs.json"), """
[
  {
    "id": "Test.Graph.ModAlias",
    "entry": "value",
    "nodes": [
      { "id": "value", "op": "literal.float", "floatValue": 2.25 }
    ]
  }
]
""");

                vfs.Mount("Core", coreRoot);
                var pipeline = new ConfigPipeline(
                    vfs,
                    new ModLoader(vfs, new FunctionRegistry(), new TriggerManager(), graphOpRegistry: opRegistry));
                var catalog = new ConfigCatalog();
                catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));
                var programs = new GraphProgramRegistry();
                var loader = new GraphProgramConfigLoader(
                    pipeline,
                    programs,
                    new TestGraphSymbolResolver(),
                    opRegistry: opRegistry);

                opRegistry.Freeze();
                var packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
                loader.PatchAndRegister(packages);

                int graphId = GraphIdRegistry.GetId("Test.Graph.ModAlias");
                That(graphId, Is.GreaterThan(0));
                That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True);
                That(program.Length, Is.EqualTo(1));
                That(program[0].Op, Is.EqualTo((ushort)GraphNodeOp.ConstFloat));
                That(program[0].ImmF, Is.EqualTo(2.25f));
                Throws<InvalidOperationException>(() => opRegistry.Register("late.literal.float", (ushort)GraphNodeOp.ConstFloat));
            }
            finally
            {
                GraphIdRegistry.Clear();
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        private sealed class TestGraphSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => 0;
            public int ResolveAttribute(string name) => 0;
            public int ResolveEffectTemplate(string name) => 0;
            public int ResolveRelationshipType(string name) => 0;
            public int ResolveRelationshipMetric(string name) => 0;
            public int ResolveRelationshipFlag(string name) => 0;
            public int ResolveRelationshipReason(string name) => 0;
            public int ResolveTargetDispatchPreset(string name) => 0;
            public int ResolveEntityTemplate(string name) => 0;
        }

        private static void HandleAddImmediateFloat(ref GraphExecutionState state, in GraphInstruction ins, ref int pc)
        {
            state.F[ins.Dst] = state.F[ins.A] + ins.ImmF;
        }
    }
}
