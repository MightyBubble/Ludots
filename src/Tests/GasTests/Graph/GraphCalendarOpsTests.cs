using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
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

    [Test]
    public void LoadConfigKey_ResolvesRegisteredNameAndRejectsUnknown()
    {
        (_, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);
        int summer = ConfigKeyRegistry.GetId("summer");
        Assert.That(summer, Is.Not.EqualTo(ConfigKeyRegistry.InvalidId));

        GraphSliceResult known = CompilePatchExecute(api, ScriptOf(
            new GraphControlFlowNode { Id = "key", Op = nameof(GraphNodeOp.LoadConfigKey), Symbol = "summer" },
            new GraphControlFlowNode { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            new GraphControlFlowValueEdge("key", "value", "halt", "value")));
        Assert.That(known.ReturnInt, Is.EqualTo(summer));

        const string missing = "not-registered-phase-zz";
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            CompilePatchExecute(api, ScriptOf(
                new GraphControlFlowNode { Id = "key", Op = nameof(GraphNodeOp.LoadConfigKey), Symbol = missing },
                new GraphControlFlowNode { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
                new GraphControlFlowValueEdge("key", "value", "halt", "value"))))!;
        Assert.That(error.Message, Does.Contain(missing));
        Assert.That(ConfigKeyRegistry.GetId(missing), Is.EqualTo(ConfigKeyRegistry.InvalidId));
    }

    [Test]
    public void ReadCalendarCyclePhaseIndex_MatchesThePhaseTableSlot()
    {
        (CalendarRuntime runtime, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);
        int index = runtime.ReadCyclePhaseIndex(CalendarId, "season");
        Assert.That(index, Is.EqualTo(1));
        Assert.That(runtime.Project(CalendarId).Cycles.Single(cycle => cycle.CycleId == "season").PhaseIndex, Is.EqualTo(index));

        GraphSliceResult result = CompilePatchExecute(api, ScriptOf(
            new GraphControlFlowNode
            {
                Id = "slot",
                Op = nameof(GraphNodeOp.ReadCalendarCyclePhaseIndex),
                Calendar = CalendarId,
                Cycle = "season",
            },
            new GraphControlFlowNode { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            new GraphControlFlowValueEdge("slot", "value", "halt", "value")));
        Assert.That(result.ReturnInt, Is.EqualTo(index));

        var disabled = new CalendarRuntime(null, new CalendarDefinitionRegistry());
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            disabled.ReadCyclePhaseIndex(CalendarId, "season"))!;
        Assert.That(error.Message, Does.Contain("Calendar is not enabled"));
    }

    [Test]
    public void ReadCalendarDaysUntilPhase_CountsWholeDaysUntilThePhaseStarts()
    {
        (CalendarRuntime runtime, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);
        Assert.That(runtime.ReadDaysUntilPhase(CalendarId, "season", "summer"), Is.EqualTo(0));
        Assert.That(runtime.ReadDaysUntilPhase(CalendarId, "season", "autumn"), Is.EqualTo(90));
        Assert.That(runtime.ReadDaysUntilPhase(CalendarId, "season", "spring"), Is.EqualTo(270));

        var festival = new CalendarCycleDefinition(
            "festival",
            10,
            new[]
            {
                new CalendarPhaseDefinition("chunjie", "春节", 5),
                new CalendarPhaseDefinition("ordinary", "平", 4),
                new CalendarPhaseDefinition("duanwu", "端午", 1),
            });
        Assert.That(CalendarProjection.TryDaysUntilPhase(festival, 0, "duanwu", out int untilDuanwu), Is.True);
        Assert.That(untilDuanwu, Is.EqualTo(9));
        Assert.That(CalendarProjection.TryDaysUntilPhase(festival, 8, "duanwu", out int dayBefore), Is.True);
        Assert.That(dayBefore, Is.EqualTo(1));
        Assert.That(CalendarProjection.TryDaysUntilPhase(festival, 9, "duanwu", out int inside), Is.True);
        Assert.That(inside, Is.EqualTo(0));
        Assert.That(CalendarProjection.TryDaysUntilPhase(festival, 0, "chunjie", out int insideChunjie), Is.True);
        Assert.That(insideChunjie, Is.EqualTo(0));

        var months = new CalendarCycleDefinition(
            "month",
            30,
            new[]
            {
                new CalendarPhaseDefinition("month.03", "三月", 10),
                new CalendarPhaseDefinition("month.04", "四月", 10),
                new CalendarPhaseDefinition("month.05", "五月", 10),
            });
        Assert.That(CalendarProjection.TryDaysUntilPhase(months, 0, "month.04", 5, out int beforeFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(beforeFifth, Is.EqualTo(14));
        Assert.That(CalendarProjection.TryDaysUntilPhase(months, 12, "month.04", 5, out int insideBeforeFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(insideBeforeFifth, Is.EqualTo(2));
        Assert.That(CalendarProjection.TryDaysUntilPhase(months, 14, "month.04", 5, out int onFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(onFifth, Is.EqualTo(0));
        Assert.That(CalendarProjection.TryDaysUntilPhase(months, 19, "month.04", 5, out int afterFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(afterFifth, Is.EqualTo(25));
        Assert.That(CalendarProjection.TryDaysUntilPhase(months, 12, "month.04", 0, out int insideMonth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(insideMonth, Is.EqualTo(0));
        Assert.That(CalendarProjection.TryDaysUntilPhase(months, 12, "month.04", 11, out _), Is.EqualTo(CalendarDaysUntilStatus.DayExceedsPhase));

        InvalidOperationException missingPhase = Assert.Throws<InvalidOperationException>(() =>
            runtime.ReadDaysUntilPhase(CalendarId, "season", "month.04"))!;
        Assert.That(missingPhase.Message, Does.Contain("season"));
        Assert.That(missingPhase.Message, Does.Contain("month.04"));

        GraphSliceResult result = CompilePatchExecute(api, ScriptOf(
            new GraphControlFlowNode
            {
                Id = "until",
                Op = nameof(GraphNodeOp.ReadCalendarDaysUntilPhase),
                Calendar = CalendarId,
                Cycle = "season",
                Phase = "autumn",
            },
            new GraphControlFlowNode { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            new GraphControlFlowValueEdge("until", "value", "halt", "value")));
        Assert.That(result.ReturnInt, Is.EqualTo(90));

        GraphSliceResult fifth = CompilePatchExecute(api, ScriptOf(
            new GraphControlFlowNode
            {
                Id = "untilDay",
                Op = nameof(GraphNodeOp.ReadCalendarDaysUntilPhase),
                Calendar = CalendarId,
                Cycle = "season",
                Phase = "summer",
                Day = 5,
            },
            new GraphControlFlowNode { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            new GraphControlFlowValueEdge("untilDay", "value", "halt", "value")));
        Assert.That(fifth.ReturnInt, Is.EqualTo(4));

        InvalidOperationException tooLong = Assert.Throws<InvalidOperationException>(() =>
            runtime.ReadDaysUntilPhase(CalendarId, "season", "summer", 100))!;
        Assert.That(tooLong.Message, Does.Contain("summer"));
        Assert.That(tooLong.Message, Does.Contain("100"));

        var disabled = new CalendarRuntime(null, new CalendarDefinitionRegistry());
        InvalidOperationException disabledError = Assert.Throws<InvalidOperationException>(() =>
            disabled.ReadDaysUntilPhase(CalendarId, "season", "autumn"))!;
        Assert.That(disabledError.Message, Does.Contain("Calendar is not enabled"));
    }

    [Test]
    public void ReadCalendarDaysUntilPhase_JsonDayAndDuplicatePhaseNames()
    {
        (_, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);
        JsonSerializerOptions json = StrictJsonOptions.CreateCamelCase(includeFields: true);
        GraphControlFlowDocument authored = JsonSerializer.Deserialize<GraphControlFlowDocument>(
            """
            {
              "id": "Graph.Tests.CalendarDate.JsonDay",
              "kind": "Script",
              "entry": "untilDay",
              "nodes": [
                {
                  "id": "untilDay",
                  "op": "ReadCalendarDaysUntilPhase",
                  "calendar": "calendar.graph.test",
                  "cycle": "season",
                  "phase": "summer",
                  "day": 5
                },
                { "id": "halt", "op": "HaltReturnInt" }
              ],
              "controlEdges": [ { "from": "untilDay", "fromPort": "next", "to": "halt" } ],
              "valueEdges": [ { "from": "untilDay", "fromPort": "value", "to": "halt", "toPort": "value" } ]
            }
            """,
            json)!;
        Assert.That(authored.Nodes[0].Day, Is.EqualTo(5));
        Assert.That(CompilePatchExecute(api, authored).ReturnInt, Is.EqualTo(4));

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GraphControlFlowDocument>(
            """
            {
              "id": "Graph.Tests.CalendarDate.WrongDayField",
              "kind": "Script",
              "entry": "untilDay",
              "nodes": [
                {
                  "id": "untilDay",
                  "op": "ReadCalendarDaysUntilPhase",
                  "cycle": "season",
                  "phase": "summer",
                  "Day": 5
                },
                { "id": "halt", "op": "HaltReturnInt" }
              ],
              "controlEdges": [ { "from": "untilDay", "fromPort": "next", "to": "halt" } ],
              "valueEdges": [ { "from": "untilDay", "fromPort": "value", "to": "halt", "toPort": "value" } ]
            }
            """,
            json));

        var twice = new CalendarCycleDefinition(
            "festival",
            20,
            new[]
            {
                new CalendarPhaseDefinition("duanwu", "端午", 1),
                new CalendarPhaseDefinition("gap", "间", 9),
                new CalendarPhaseDefinition("duanwu", "端午", 1),
                new CalendarPhaseDefinition("rest", "余", 9),
            });
        Assert.That(CalendarProjection.TryDaysUntilPhase(twice, 0, "duanwu", out int onFirst), Is.True);
        Assert.That(onFirst, Is.EqualTo(0));
        Assert.That(CalendarProjection.TryDaysUntilPhase(twice, 1, "duanwu", out int towardSecond), Is.True);
        Assert.That(towardSecond, Is.EqualTo(9));
        Assert.That(CalendarProjection.TryDaysUntilPhase(twice, 11, "duanwu", out int wrapToFirst), Is.True);
        Assert.That(wrapToFirst, Is.EqualTo(9));

        var leap = new CalendarCycleDefinition(
            "month",
            28,
            new[]
            {
                new CalendarPhaseDefinition("month.03", "三月", 8),
                new CalendarPhaseDefinition("month.04", "短四月", 3),
                new CalendarPhaseDefinition("month.04", "四月", 10),
                new CalendarPhaseDefinition("month.05", "五月", 7),
            });
        Assert.That(CalendarProjection.TryDaysUntilPhase(leap, 9, "month.04", out int insideShort), Is.True);
        Assert.That(insideShort, Is.EqualTo(0));
        Assert.That(CalendarProjection.TryDaysUntilPhase(leap, 0, "month.04", 5, out int beforeLongFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(beforeLongFifth, Is.EqualTo(15));
        Assert.That(CalendarProjection.TryDaysUntilPhase(leap, 9, "month.04", 5, out int insideShortBeforeFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(insideShortBeforeFifth, Is.EqualTo(6));
        Assert.That(CalendarProjection.TryDaysUntilPhase(leap, 15, "month.04", 5, out int onLongFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(onLongFifth, Is.EqualTo(0));
        Assert.That(CalendarProjection.TryDaysUntilPhase(leap, 16, "month.04", 5, out int afterLongFifth), Is.EqualTo(CalendarDaysUntilStatus.Found));
        Assert.That(afterLongFifth, Is.EqualTo(27));
    }

    [Test]
    public void SubInt_SubtractsAndQueryRejectsIt()
    {
        (_, GasGraphRuntimeApi api) = CreateBoundApi(startDayIndex: 90);
        GraphSliceResult ahead = CompilePatchExecute(api, Subtract(360, 90));
        Assert.That(ahead.ReturnInt, Is.EqualTo(270));
        GraphSliceResult past = CompilePatchExecute(api, Subtract(10, 90));
        Assert.That(past.ReturnInt, Is.EqualTo(-80));

        GraphControlFlowCompileResult query = GraphControlFlowCompiler.Compile(
            new GraphControlFlowDocument
            {
                Id = "Graph.Tests.SubIntQuery",
                Kind = "Query",
                Entry = "sub",
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "sub", Op = nameof(GraphNodeOp.SubInt) },
                },
            },
            eventSchemas: null,
            enums: null);
        Assert.That(query.Diagnostics, Is.Not.Empty);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.SubInt), Is.False);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.SubInt), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.LoadConfigKey), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.ReadCalendarCyclePhaseIndex), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.ReadCalendarDaysUntilPhase), Is.True);
    }

    private static GraphControlFlowDocument Subtract(int left, int right)
    {
        return new GraphControlFlowDocument
        {
            Id = "Graph.Tests.SubInt",
            Kind = "Script",
            Entry = "left",
            Nodes = new List<GraphControlFlowNode>
            {
                new() { Id = "left", Op = nameof(GraphNodeOp.ConstInt), IntValue = left },
                new() { Id = "right", Op = nameof(GraphNodeOp.ConstInt), IntValue = right },
                new() { Id = "sub", Op = nameof(GraphNodeOp.SubInt) },
                new() { Id = "halt", Op = nameof(GraphNodeOp.HaltReturnInt) },
            },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new("left", "next", "right"),
                new("right", "next", "sub"),
                new("sub", "next", "halt"),
            },
            ValueEdges = new List<GraphControlFlowValueEdge>
            {
                new("left", "value", "sub", "a"),
                new("right", "value", "sub", "b"),
                new("sub", "value", "halt", "value"),
            },
        };
    }

    private static GraphControlFlowDocument ScriptOf(
        GraphControlFlowNode featured,
        GraphControlFlowNode halt,
        GraphControlFlowValueEdge value)
    {
        return new GraphControlFlowDocument
        {
            Id = "Graph.Tests.CalendarDate." + featured.Op,
            Kind = "Script",
            Entry = featured.Id,
            Nodes = new List<GraphControlFlowNode> { featured, halt },
            ControlEdges = new List<GraphControlFlowEdge>
            {
                new(featured.Id, "next", halt.Id),
            },
            ValueEdges = new List<GraphControlFlowValueEdge> { value },
        };
    }

    private static GraphSliceResult CompilePatchExecute(GasGraphRuntimeApi api, GraphControlFlowDocument document)
    {
        GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(document, eventSchemas: null, enums: null);
        Assert.That(compiled.Diagnostics, Is.Empty, string.Join(Environment.NewLine, compiled.Diagnostics));
        GraphProgramPackage package = compiled.Package!.Value;
        GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, new ThrowingSymbolResolver());
        GraphKindOperationPolicy.ValidateProgram(GraphKind.Script, package.Program, GasGraphOpHandlerTable.Instance);
        return Execute(api, package);
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
