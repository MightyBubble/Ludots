using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Providers.FixtureProviders;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class GraphOfferTaskTests
{
    [Test]
    public void OfferTask_CompilesPreservesSymbolAndCreatesScopedTask()
    {
        using World world = World.Create();
        Entity scope = world.Create();
        TaskRuntimeService tasks = CreateTaskRuntime(world, "task.graph.offer", "Graph task");
        var api = new GasGraphRuntimeApi(world);
        api.BindTaskRuntimeService(tasks);

        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
            CreateDocument("task.graph.offer"),
            eventSchemas: null,
            enums: null);
        Assert.That(compiled.Diagnostics, Is.Empty, string.Join(Environment.NewLine, compiled.Diagnostics));
        GraphProgramPackage package = compiled.Package!.Value;
        GraphInstruction offer = package.Program.Single(i => i.Op == (ushort)GraphNodeOp.OfferTask);
        Assert.That(package.Symbols[offer.Imm], Is.EqualTo("task.graph.offer"));

        GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, new NoOpSymbolResolver());
        offer = package.Program.Single(i => i.Op == (ushort)GraphNodeOp.OfferTask);
        Assert.That(package.Symbols[offer.Imm], Is.EqualTo("task.graph.offer"));

        Execute(world, api, package, scope);

        TaskView view = tasks.CaptureViews().Single();
        Assert.That(view.ScopeHost, Is.EqualTo(scope));
        Assert.That(world.Get<Name>(view.Entity).Value, Is.EqualTo("Graph task"));
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.OfferTask), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.OfferTask), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.OfferTask), Is.False);
    }

    [Test]
    public void OfferTask_UnknownTaskFailsLoudWithTaskId()
    {
        using World world = World.Create();
        Entity scope = world.Create();
        var api = new GasGraphRuntimeApi(world);
        api.BindTaskRuntimeService(new TaskRuntimeService(
            world,
            new TaskDefinitionRegistry(),
            CreateProviders(),
            new TaskPresentationBuffer()));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => api.OfferTask("task.missing", scope))!;

        Assert.That(error.Message, Does.Contain("task.missing"));
    }

    [Test]
    public void OfferTask_DeadScopeFailsLoud()
    {
        using World world = World.Create();
        Entity scope = world.Create();
        world.Destroy(scope);
        var api = new GasGraphRuntimeApi(world);
        api.BindTaskRuntimeService(CreateTaskRuntime(world, "task.graph.scope", "Scoped task"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => api.OfferTask("task.graph.scope", scope))!;

        Assert.That(error.Message, Does.Contain("OfferTaskScopeInvalid"));
    }

    private static GraphControlFlowDocument CreateDocument(string taskId)
    {
        return new GraphControlFlowDocument
        {
            Id = "Graph.Tests.OfferTask",
            Kind = "Script",
            Entry = "scope",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "scope", Op = "LoadExplicitTarget" },
                new() { Id = "offer", Op = "OfferTask", TaskId = taskId },
                new() { Id = "halt", Op = "HaltReturnInt" },
            },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new("scope", "next", "offer"),
                new("offer", "next", "halt"),
            },
            ValueEdges = new List<GraphControlFlowValueEdge>
            {
                new("scope", "value", "offer", "source"),
            },
        };
    }

    private static void Execute(
        World world,
        GasGraphRuntimeApi api,
        GraphProgramPackage package,
        Entity scope)
    {
        const int graphId = 91;
        var programs = new GraphProgramRegistry();
        programs.Register(
            graphId,
            package.Program,
            GraphKind.Script,
            GraphInstructionSourceMap.Empty,
            package.Symbols);
        Entity caster = world.Create();
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var cursor = new GraphExecutionCursor();

        GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
            world,
            caster,
            scope,
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

        Assert.That(result.Halted, Is.True);
    }

    private static TaskRuntimeService CreateTaskRuntime(World world, string id, string displayName)
    {
        var definitions = new TaskDefinitionRegistry();
        definitions.Register(id, new TaskDefinition
        {
            DisplayName = displayName,
            StartPolicy = TaskStartPolicy.Automatic,
            Objectives =
            {
                new TaskObjectiveDefinition
                {
                    Id = "stay",
                    Kind = TaskObjectiveKind.Signal,
                    SignalKey = "task.graph.stay",
                },
            },
        });
        return new TaskRuntimeService(
            world,
            definitions,
            CreateProviders(),
            new TaskPresentationBuffer());
    }

    private static ProviderServices CreateProviders()
    {
        var services = new ProviderServices(allowTestDomainOverride: true);
        FixtureProviderInstaller.InstallMinimal(services);
        return services;
    }

    private sealed class NoOpSymbolResolver : IGraphSymbolResolver
    {
        public int ResolveTag(string name) => throw new NotSupportedException();
        public int ResolveAttribute(string name) => throw new NotSupportedException();
        public int ResolveEffectTemplate(string name) => throw new NotSupportedException();
        public int ResolveRelationshipType(string name) => throw new NotSupportedException();
        public int ResolveRelationshipMetric(string name) => throw new NotSupportedException();
        public int ResolveRelationshipFlag(string name) => throw new NotSupportedException();
        public int ResolveRelationshipReason(string name) => throw new NotSupportedException();
        public int ResolveTargetDispatchPreset(string name) => throw new NotSupportedException();
        public int ResolveEntityTemplate(string name) => throw new NotSupportedException();
    }
}
