using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class GraphTimeFlowOpsTests
{
    [Test]
    public void Read_CompilesAndReturnsTheLiveDomain()
    {
        using World world = World.Create();
        var timeFlow = new TimeFlowService();
        var api = new GasGraphRuntimeApi(world);
        api.BindTimeFlow(timeFlow);
        timeFlow.AcquireScaleToken(TimeFlowDomainIds.Gas, 500, owner: "test", reason: "half");

        GraphControlFlowCompileResult compiled = Compile(CreateReadDocument());
        GraphSliceResult result = Execute(api, compiled);

        Assert.That(result.Halted, Is.True);
        Assert.That(result.ReturnInt, Is.EqualTo(timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Gas)));
        Assert.That(result.ReturnInt, Is.EqualTo(500));
        Assert.That(api.ReadTimeFlowPaused(TimeFlowDomainIds.Simulation), Is.False);
    }

    [Test]
    public void Read_QueryGraphCompiles()
    {
        GraphControlFlowCompileResult compiled = Compile(new GraphControlFlowDocument
        {
            Id = "Graph.Tests.TimeFlowQuery",
            Kind = "Query",
            Entry = "paused",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "paused", Op = nameof(GraphNodeOp.ReadTimeFlowPaused), Domain = TimeFlowDomainIds.Simulation },
            },
        });

        Assert.That(compiled.Diagnostics, Is.Empty);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.ReadTimeFlowPaused), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.AcquireTimeFlowPause), Is.False);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.ReleaseTimeFlowToken), Is.True);
    }

    [Test]
    public void AcquirePause_ReleasesAndLeavesTheDomainRunning()
    {
        using World world = World.Create();
        var timeFlow = new TimeFlowService();
        var api = new GasGraphRuntimeApi(world);
        api.BindTimeFlow(timeFlow);

        GraphSliceResult result = Execute(api, Compile(CreatePauseDocument()));

        Assert.That(result.Halted, Is.True);
        Assert.That(result.ReturnInt, Is.GreaterThan(0));
        Assert.That(timeFlow.IsPaused(TimeFlowDomainIds.Simulation), Is.False);
        Assert.That(timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(TimeFlowService.DefaultScalePermille));
    }

    [Test]
    public void AcquireScale_ReportsTheStackedEffectiveScaleThenReleases()
    {
        using World world = World.Create();
        var timeFlow = new TimeFlowService();
        var api = new GasGraphRuntimeApi(world);
        api.BindTimeFlow(timeFlow);

        GraphSliceResult result = Execute(api, Compile(CreateScaleDocument()));

        Assert.That(result.Halted, Is.True);
        Assert.That(result.ReturnInt, Is.EqualTo(2000));
        Assert.That(timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation), Is.EqualTo(TimeFlowService.DefaultScalePermille));
    }

    [Test]
    public void Read_UnknownDomainFailsClosed()
    {
        using World world = World.Create();
        var api = new GasGraphRuntimeApi(world);
        api.BindTimeFlow(new TimeFlowService());
        var document = CreateReadDocument();
        document.Nodes[0].Domain = "simulation.missing";

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Execute(api, Compile(document)))!;

        Assert.That(error.Message, Does.Contain("simulation.missing"));
        Assert.That(error.Message, Does.Contain("not registered"));
    }

    [Test]
    public void AcquireScale_ZeroFailsClosed()
    {
        using World world = World.Create();
        var api = new GasGraphRuntimeApi(world);
        api.BindTimeFlow(new TimeFlowService());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Execute(api, Compile(CreateScaleDocument(scalePermille: 0))))!;

        Assert.That(error.Message, Does.Contain("pause token"));
    }

    [Test]
    public void Release_SecondTimeFailsClosed()
    {
        using World world = World.Create();
        var timeFlow = new TimeFlowService();
        var api = new GasGraphRuntimeApi(world);
        api.BindTimeFlow(timeFlow);
        int token = api.AcquireTimeFlowPause(TimeFlowDomainIds.Simulation, "graph", "node");
        api.ReleaseTimeFlowToken(token);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => api.ReleaseTimeFlowToken(token))!;

        Assert.That(error.Message, Does.Contain(token.ToString()));
        Assert.That(error.Message, Does.Contain("not active"));
    }

    [Test]
    public void Read_UnboundApiFailsClosed()
    {
        using World world = World.Create();
        var api = new GasGraphRuntimeApi(world);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            api.ReadTimeFlowPaused(TimeFlowDomainIds.Simulation))!;

        Assert.That(error.Message, Does.Contain("GAS.GRAPH.ERR.TimeFlowUnavailable"));
    }

    [Test]
    public void GasScale_IncludesParentWithoutASecondMultiplyOnThePolicyRead()
    {
        var timeFlow = new TimeFlowService();
        int simulation = timeFlow.AcquireScaleToken(TimeFlowDomainIds.Simulation, 2000, "test", "sim").Value;
        int gas = timeFlow.AcquireScaleToken(TimeFlowDomainIds.Gas, 2000, "test", "gas").Value;

        Assert.That(timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Gas), Is.EqualTo(4000));
        Assert.That(timeFlow.GetScalePermilleRelativeToParent(TimeFlowDomainIds.Gas), Is.EqualTo(2000));

        timeFlow.ReleaseToken(new TimeFlowToken(simulation));
        timeFlow.ReleaseToken(new TimeFlowToken(gas));
    }

    private static GraphControlFlowDocument CreateReadDocument()
    {
        return new GraphControlFlowDocument
        {
            Id = "Graph.Tests.TimeFlowRead",
            Kind = "Script",
            Entry = "scale",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "scale", Op = nameof(GraphNodeOp.ReadTimeFlowScalePermille), Domain = TimeFlowDomainIds.Gas },
                new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new("scale", "next", "halt"),
            },
            ValueEdges = new List<GraphControlFlowValueEdge>
            {
                new("scale", "value", "halt", "value"),
            },
        };
    }

    private static GraphControlFlowDocument CreatePauseDocument()
    {
        return new GraphControlFlowDocument
        {
            Id = "Graph.Tests.TimeFlowPause",
            Kind = "Script",
            Entry = "pause",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "pause", Op = nameof(GraphNodeOp.AcquireTimeFlowPause), Domain = TimeFlowDomainIds.Simulation },
                new() { Id = "release", Op = nameof(GraphNodeOp.ReleaseTimeFlowToken) },
                new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new("pause", "next", "release"),
                new("release", "next", "halt"),
            },
            ValueEdges = new List<GraphControlFlowValueEdge>
            {
                new("pause", "value", "release", "value"),
                new("pause", "value", "halt", "value"),
            },
        };
    }

    private static GraphControlFlowDocument CreateScaleDocument(int scalePermille = 2000)
    {
        return new GraphControlFlowDocument
        {
            Id = "Graph.Tests.TimeFlowScale",
            Kind = "Script",
            Entry = "two",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "two", Op = nameof(GraphNodeOp.ConstInt), IntValue = scalePermille },
                new() { Id = "acquire", Op = nameof(GraphNodeOp.AcquireTimeFlowScale), Domain = TimeFlowDomainIds.Simulation },
                new() { Id = "read", Op = nameof(GraphNodeOp.ReadTimeFlowScalePermille), Domain = TimeFlowDomainIds.Simulation },
                new() { Id = "release", Op = nameof(GraphNodeOp.ReleaseTimeFlowToken) },
                new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new("two", "next", "acquire"),
                new("acquire", "next", "read"),
                new("read", "next", "release"),
                new("release", "next", "halt"),
            },
            ValueEdges = new List<GraphControlFlowValueEdge>
            {
                new("two", "value", "acquire", "value"),
                new("acquire", "value", "release", "value"),
                new("read", "value", "halt", "value"),
            },
        };
    }

    private static GraphControlFlowCompileResult Compile(GraphControlFlowDocument document)
    {
        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(document, eventSchemas: null, enums: null);
        Assert.That(compiled.Diagnostics, Is.Empty, string.Join(Environment.NewLine, compiled.Diagnostics));
        GraphProgramSymbolPatcher.Patch(compiled.Package!.Value.Symbols, compiled.Package.Value.Program, new ThrowingSymbolResolver());
        return compiled;
    }

    private static int ResolveGraphId(string graphName)
    {
        int existing = GraphIdRegistry.GetId(graphName);
        if (existing != GraphIdRegistry.InvalidId)
        {
            return existing;
        }

        if (!GraphIdRegistry.IsFrozen)
        {
            return GraphIdRegistry.Register(graphName);
        }

        // 画廊先把图编号表冻住之后，测试图名不能再登记。令牌只要求拥有者名字非空，沿用一张已经登记的图。
        RegistryMapping[] mappings = GraphIdRegistry.SnapshotMappings();
        if (mappings.Length == 0)
        {
            throw new InvalidOperationException(
                "GraphId is frozen and empty, so this script has no document id to own a time token.");
        }

        return mappings[0].Id;
    }

    private static GraphSliceResult Execute(GasGraphRuntimeApi api, GraphControlFlowCompileResult compiled)
    {
        GraphProgramPackage package = compiled.Package!.Value;
        int graphId = ResolveGraphId(package.GraphName);
        var programs = new GraphProgramRegistry();
        programs.Register(graphId, package.Program, package.Kind, compiled.SourceMap, package.Symbols);
        using World world = World.Create();
        Entity caster = world.Create();
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var cursor = new GraphExecutionCursor();
        return GraphExecutor.ExecuteScriptSlice(
            world,
            caster,
            default,
            default,
            package.Program,
            api,
            programs,
            floats,
            ints,
            bools,
            entities,
            targets,
            callStack,
            ref cursor,
            budgetSteps: 32,
            graphId: graphId);
    }

    private sealed class ThrowingSymbolResolver : IGraphSymbolResolver
    {
        public int ResolveTag(string name) => throw new NotSupportedException();
        public int ResolveAttribute(string name) => throw new NotSupportedException();
        public int ResolveEffectTemplate(string name) => throw new NotSupportedException();
        public int ResolveRelationshipType(string name) => throw new NotSupportedException();
        public int ResolveRelationshipMetric(string name) => throw new NotSupportedException();
        public int ResolveRelationshipFlag(string name) => throw new NotSupportedException();
        public int ResolveTargetDispatchPreset(string name) => throw new NotSupportedException();
        public int ResolveEntityTemplate(string name) => throw new NotSupportedException();
        public int ResolveAbility(string name) => throw new NotSupportedException();
        public int ResolveOrderType(string name) => throw new NotSupportedException();
        public int ResolveTextToken(string name) => throw new NotSupportedException();
        public int ResolveGraphLookupTable(string name) => throw new NotSupportedException();
        public int ResolveGraphLookupField(string name) => throw new NotSupportedException();
        public int ResolveRngDistribution(string name) => throw new NotSupportedException();
        public int ResolveEqsQuery(string name) => throw new NotSupportedException();
    }
}
