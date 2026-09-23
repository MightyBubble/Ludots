using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.Calendar;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class GraphCalendarOpsTests
{
    private const string CalendarId = "calendar.graph.test";

    [Test]
    public void Read_CompilesPatchesAndReturnsTheLiveDay()
    {
        (CalendarRuntime runtime, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);
        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(CreateReadDocument(), eventSchemas: null, enums: null);
        Assert.That(compiled.Diagnostics, Is.Empty, string.Join(Environment.NewLine, compiled.Diagnostics));
        GraphProgramPackage package = compiled.Package!.Value;

        GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, new ThrowingSymbolResolver());
        GraphKindOperationPolicy.ValidateProgram(GraphKind.Script, package.Program, GasGraphOpHandlerTable.Instance);

        GraphInstruction activeYear = package.Program.Single(i => i.Op == (ushort)GraphNodeOp.ReadCalendarYear && i.Imm == 0);
        GraphInstruction namedYear = package.Program.Single(i =>
            i.Op == (ushort)GraphNodeOp.ReadCalendarYear && i.Imm == ConfigKeyRegistry.GetId(CalendarId));
        GraphInstruction cycle = package.Program.Single(i => i.Op == (ushort)GraphNodeOp.ReadCalendarCycleDay);
        Assert.That(activeYear.Flags, Is.EqualTo(0));
        Assert.That(namedYear.Flags, Is.EqualTo(0));
        Assert.That(cycle.B, Is.EqualTo(0));
        Assert.That(cycle.C, Is.EqualTo(0));
        Assert.That(cycle.Flags, Is.EqualTo(0));
        Assert.That(CalendarOpEncoding.UnpackCycle(cycle.Imm), Is.EqualTo(ConfigKeyRegistry.GetId("season")));
        Assert.That(CalendarOpEncoding.UnpackCalendar(cycle.Imm), Is.EqualTo(ConfigKeyRegistry.GetId(CalendarId)));

        GraphSliceResult result = Execute(api, package);
        Assert.That(result.Halted, Is.True);
        Assert.That(result.ReturnInt, Is.EqualTo(runtime.DayIndex));
        Assert.That(result.ReturnInt, Is.EqualTo(90));
        Assert.That(api.ReadCalendarYear(0), Is.EqualTo(1));
        Assert.That(api.ReadCalendarCycleDay(cycle.Imm), Is.EqualTo(1));
    }

    [Test]
    public void Read_QueryGraphCompiles()
    {
        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(
            new GraphControlFlowDocument
            {
                Id = "Graph.Tests.CalendarQuery",
                Kind = "Query",
                Entry = "day",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "day", Op = nameof(GraphNodeOp.ReadCalendarDayIndex) },
                },
            },
            eventSchemas: null,
            enums: null);

        Assert.That(compiled.Diagnostics, Is.Empty, string.Join(Environment.NewLine, compiled.Diagnostics));
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.ReadCalendarDayIndex), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.SetCalendarDayIndex), Is.False);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.ApplyCalendarStart), Is.True);
    }

    [Test]
    public void Read_DisabledCalendarFailsClosedExceptTheEnabledCheck()
    {
        using World world = World.Create();
        var api = new GasGraphRuntimeApi(world);
        api.BindCalendarRuntime(new CalendarRuntime(null, new CalendarDefinitionRegistry()));

        Assert.That(api.ReadCalendarEnabled(), Is.False);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => api.ReadCalendarDayIndex())!;
        Assert.That(error.Message, Does.Contain("Calendar is not enabled"));
        Assert.That(error.Message, Does.Contain("Calendar/world.json"));
    }

    [Test]
    public void Read_UnboundApiFailsClosed()
    {
        using World world = World.Create();
        var api = new GasGraphRuntimeApi(world);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => api.ReadCalendarEnabled())!;
        Assert.That(error.Message, Does.Contain("GAS.GRAPH.ERR.CalendarRuntimeUnavailable"));
    }

    [Test]
    public void Write_SetDayMovesForwardAndRewindFails()
    {
        (CalendarRuntime runtime, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);

        api.SetCalendarDayIndex(91);

        Assert.That(runtime.DayIndex, Is.EqualTo(91));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => api.SetCalendarDayIndex(90))!;
        Assert.That(error.Message, Does.Contain("cannot move backward"));
        Assert.That(runtime.DayIndex, Is.EqualTo(91));
    }

    [Test]
    public void Write_ApplyDifferentValuesAfterCommitFails()
    {
        (_, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);

        api.ApplyCalendarStart(1, 0);
        Assert.That(api.ReadCalendarDayIndex(), Is.EqualTo(1));
        Assert.That(api.ReadCalendarTicksIntoDay(), Is.EqualTo(0));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => api.ApplyCalendarStart(2, 0))!;
        Assert.That(error.Message, Does.Contain("already committed"));
        Assert.That(error.Message, Does.Contain("Requested dayIndex=2"));
        api.ApplyCalendarStart(1, 0);
        Assert.That(api.ReadCalendarDayIndex(), Is.EqualTo(1));
    }

    private static GraphControlFlowDocument CreateReadDocument()
    {
        return new GraphControlFlowDocument
        {
            Id = "Graph.Tests.CalendarRead",
            Kind = "Script",
            Entry = "activeYear",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "activeYear", Op = nameof(GraphNodeOp.ReadCalendarYear) },
                new() { Id = "day", Op = nameof(GraphNodeOp.ReadCalendarDayIndex) },
                new() { Id = "namedYear", Op = nameof(GraphNodeOp.ReadCalendarYear), Calendar = CalendarId },
                new() { Id = "cycle", Op = nameof(GraphNodeOp.ReadCalendarCycleDay), Calendar = CalendarId, Cycle = "season" },
                new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new("activeYear", "next", "day"),
                new("day", "next", "namedYear"),
                new("namedYear", "next", "cycle"),
                new("cycle", "next", "halt"),
            },
            ValueEdges = new List<GraphControlFlowValueEdge>
            {
                new("day", "value", "halt", "value"),
            },
        };
    }

    private static (CalendarRuntime Runtime, GasGraphRuntimeApi Api) CreateBoundApi(int startDayIndex)
    {
        JsonArray calendars = (JsonArray)JsonNode.Parse(
            """
            [
              {
                "id": "calendar.graph.test",
                "yearLengthDays": 360,
                "eras": [ { "id": "era.founding", "label": "立国", "startDayIndex": 0 } ],
                "cycles": [
                  {
                    "id": "season",
                    "lengthDays": 360,
                    "phases": [
                      { "id": "spring", "label": "春", "lengthDays": 90 },
                      { "id": "summer", "label": "夏", "lengthDays": 90 },
                      { "id": "autumn", "label": "秋", "lengthDays": 90 },
                      { "id": "winter", "label": "冬", "lengthDays": 90 }
                    ]
                  }
                ]
              }
            ]
            """)!;
        var registry = new CalendarDefinitionRegistry();
        foreach (CalendarDefinition calendar in CalendarConfigLoader.ParseCalendars(calendars, "test.calendars"))
        {
            registry.Register(calendar);
        }

        JsonObject worldJson = (JsonObject)JsonNode.Parse(
            $$"""
            {
              "tickSource": "Step",
              "ticksPerDay": 20,
              "startDayIndex": {{startDayIndex}},
              "activeCalendarId": "calendar.graph.test",
              "dayPhases": [
                { "id": "dawn", "label": "晓", "startPermille": 0 },
                { "id": "day", "label": "昼", "startPermille": 250 },
                { "id": "dusk", "label": "暮", "startPermille": 750 },
                { "id": "night", "label": "夜", "startPermille": 875 }
              ]
            }
            """)!;
        var runtime = new CalendarRuntime(CalendarConfigLoader.ParseWorld(worldJson, "test.world"), registry);
        World world = World.Create();
        var api = new GasGraphRuntimeApi(world);
        api.BindCalendarRuntime(runtime);
        return (runtime, api);
    }

    private static GraphSliceResult Execute(GasGraphRuntimeApi api, GraphProgramPackage package)
    {
        const int graphId = 91;
        var programs = new GraphProgramRegistry();
        programs.Register(graphId, package.Program, GraphKind.Script, GraphInstructionSourceMap.Empty, package.Symbols);
        World world = World.Create();
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
    }
}
