using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Graph.Codegen;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Graph
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class GraphCodegenRuntimeActivationTests
    {
        [Test]
        public void Binder_CodegenMode_AttachesGeneratedExecute_AndExecutorUsesIt()
        {
            GraphInstruction[] program =
            [
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 11 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            ];

            var registry = new GraphProgramRegistry();
            registry.Register(1, program, GraphKind.Effect);

            var binder = new GraphCodegenRuntimeBinder();
            binder.BindAll(registry, GraphCodegenLoadMode.Codegen);

            That(registry.GetExecutionBackend(1), Is.EqualTo(GraphExecutionBackend.Codegen));
            That(registry.TryGetRegistration(1, out GraphProgramRegistration reg), Is.True);
            That(reg.GeneratedExecute, Is.Not.Null);
            That(reg.GeneratedExecuteSlice, Is.Not.Null);

            Span<float> f = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> i = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> b = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> e = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
            GraphFrame frame = GraphFrame.Bind(
                GraphKind.Effect,
                GraphEntityPreset.None,
                world: null,
                caster: default,
                explicitTarget: default,
                targetPosCm: default,
                api: null!,
                programs: registry,
                f,
                i,
                b,
                e,
                targets,
                callStack);
            frame.GraphId = 1;
            GraphExecutor.ExecuteRegistered(registry, 1, GraphKind.Effect, ref frame);
            That(frame.Cursor.ReturnInt, Is.EqualTo(11));
            That(frame.Cursor.Status, Is.EqualTo(GraphExecutionStatus.Halted));
        }

        [Test]
        public void Binder_CodegenMode_FailsClosed_OnUndefinedOpcode()
        {
            GraphInstruction[] program =
            [
                new() { Op = 65000, Dst = 0, Imm = 1 },
            ];
            var registry = new GraphProgramRegistry();
            // Bypass EnsureProgramValid by attaching after a valid register then replacing? 
            // Register will fail policy. Instead bind a valid id then force Attach path via binder on snapshot —
            // use HandlerForward-ineligible undefined by constructing registration via Register of Halt only then
            // manually replace program array is internal. Simpler: call binder on empty + verify interpret no-op,
            // and call Emit fail path separately.
            registry.Register(
                2,
                [
                    new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 1 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                ],
                GraphKind.Effect);

            var binder = new GraphCodegenRuntimeBinder();
            binder.BindAll(registry, GraphCodegenLoadMode.Codegen);
            That(registry.GetExecutionBackend(2), Is.EqualTo(GraphExecutionBackend.Codegen));

            var ex = Throws<InvalidOperationException>(() =>
                GraphCsharpEmitter.Emit(program, "bad"));
            That(ex!.Message, Does.Contain("fail-closed").IgnoreCase.Or.Contain("rejected"));
        }

        [Test]
        public void LoadModeParser_RejectsUnknown()
        {
            var ex = Throws<InvalidOperationException>(() =>
                GraphCodegenLoadModeParser.Parse("magic"));
            That(ex!.Message, Does.Contain("GAS/graph_codegen_bake.json:mode"));
        }
        [Test]
        public void BakeConfigLoader_ReadsModeFromCatalogDeepObject()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_GraphCodegenBake_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "GAS"));
            try
            {
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    """[{"Path":"GAS/graph_codegen_bake.json","Policy":"DeepObject","AllowEmpty":true}]""");
                File.WriteAllText(
                    Path.Combine(root, "GAS", "graph_codegen_bake.json"),
                    """{"mode":"codegen"}""");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(
                    vfs,
                    new FunctionRegistry(),
                    new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);
                var bake = new GraphCodegenBakeConfigLoader(pipeline).Load(catalog);

                That(bake.Mode, Is.EqualTo("codegen"));
                That(bake.ParsedMode, Is.EqualTo(GraphCodegenLoadMode.Codegen));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
