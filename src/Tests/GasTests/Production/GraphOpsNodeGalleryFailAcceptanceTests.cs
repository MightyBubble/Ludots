using System;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[TestFixture]
[NonParallelizable]
[Category("ci-gate")]
public sealed class GraphOpsNodeGalleryFailAcceptanceTests
{
    [Test]
    public void Fail_ThrowsGasGraphErrWithGraphId()
    {
        GraphControlFlowCompileResult query = GraphControlFlowCompiler.Compile(FailDoc("Query"));
        Assert.That(query.Diagnostics, Is.Empty, string.Join("\n", query.Diagnostics.Select(d => d.Message)));

        GraphControlFlowDocument doc = FailDoc("Effect");
        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc);
        Assert.That(compiled.Diagnostics, Is.Empty, string.Join("\n", compiled.Diagnostics.Select(d => d.Message)));
        GraphProgramPackage package = compiled.Package!.Value;
        GraphInstruction fail = package.Program.Single(instruction => instruction.Op == (ushort)GraphNodeOp.Fail);
        Assert.That(fail.Op, Is.EqualTo((ushort)GraphNodeOp.Fail));

        GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, PassthroughSymbolResolver.Instance);
        int graphId = ConfigKeyRegistry.Register(doc.Id);
        using World world = World.Create();
        var api = new GasGraphRuntimeApi(world);
        var frame = GraphFrame.Bind(
            GraphKind.Effect,
            GraphEntityPreset.None,
            world,
            world.Create(),
            world.Create(),
            default,
            api,
            programs: null,
            new float[GraphVmLimits.MaxFloatRegisters],
            new int[GraphVmLimits.MaxIntRegisters],
            new byte[GraphVmLimits.MaxBoolRegisters],
            new Entity[GraphVmLimits.MaxEntityRegisters],
            new Entity[GraphVmLimits.MaxTargets],
            new int[GraphVmLimits.MaxIntIds],
            new int[GraphVmLimits.MaxCallStackDepth]);
        frame.GraphId = graphId;

        InvalidOperationException? error = null;
        try
        {
            GraphExecutor.Execute(ref frame, package.Program, programAlreadyValidated: false);
        }
        catch (InvalidOperationException ex)
        {
            error = ex;
        }

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR"));
        Assert.That(error.Message, Does.Contain("UnmatchedCastMode"));
        Assert.That(error.Message, Does.Contain(doc.Id));
    }

    private static GraphControlFlowDocument FailDoc(string kind)
    {
        return new GraphControlFlowDocument
        {
            Id = "tests.graph.fail." + kind,
            Kind = kind,
            Entry = "boom",
            Nodes =
            {
                new GraphControlFlowNode { Id = "boom", Op = "Fail", Message = "UnmatchedCastMode" },
            },
        };
    }

    private sealed class PassthroughSymbolResolver : IGraphSymbolResolver
    {
        public static readonly PassthroughSymbolResolver Instance = new();

        public int ResolveTag(string name) => TagRegistry.Register(name);
        public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
        public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
        public int ResolveRelationshipType(string name) => 1;
        public int ResolveRelationshipMetric(string name) => 1;
        public int ResolveRelationshipFlag(string name) => 1;
        public int ResolveTargetDispatchPreset(string name) => 1;
        public int ResolveEntityTemplate(string name) => 1;
    }
}
