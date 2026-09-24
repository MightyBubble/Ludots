using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class TriggerGraphRelationshipLinkTests
{
    [Test]
    public void TriggerGraph_EnsureThenAskThenRemove_WritesAndClearsTheLink()
    {
        using var world = World.Create();
        RelationshipHarness harness = RelationshipHarness.Create(world);
        Entity caster = world.Create();
        Entity target = world.Create();

        GraphInstruction[] ensured = CompileAndPatch(harness, "tests.trigger_graph.relationship_ensure", BondGraph("RelationshipEnsureLink", thenAsk: true));
        byte[] bools = Execute(world, harness.Api, caster, target, ensured);
        Assert.That(harness.Runtime.HasLink(caster, target, harness.SocialBondTypeId), Is.True);
        GraphInstruction ask = Single(ensured, GraphNodeOp.RelationshipHasLink);
        Assert.That(bools[ask.Dst], Is.EqualTo(1));

        GraphInstruction[] removed = CompileAndPatch(harness, "tests.trigger_graph.relationship_remove", BondGraph("RelationshipRemoveLink", thenAsk: false));
        Execute(world, harness.Api, caster, target, removed);
        Assert.That(harness.Runtime.HasLink(caster, target, harness.SocialBondTypeId), Is.False);
    }

    [Test]
    public void TriggerGraph_LinkWriteInsideEffectTransaction_FailsClosed()
    {
        using var world = World.Create();
        RelationshipHarness harness = RelationshipHarness.Create(world);
        Entity caster = world.Create();
        Entity target = world.Create();
        using var transaction = new EffectPhaseSideEffectTransaction(
            world,
            tagOps: null,
            effectRequests: null,
            spawnRequests: null,
            presentationEvents: null,
            attributeEntityCapacity: 2);
        transaction.Begin();
        harness.Api.BeginEffectSideEffectTransaction(transaction);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Execute(world, harness.Api, caster, target, RawEnsure(harness.SocialBondTypeId)))!;

        Assert.That(error.Message, Does.StartWith(EffectPhaseSideEffectTransaction.UnsupportedSideEffectError));
        Assert.That(harness.Runtime.HasLink(caster, target, harness.SocialBondTypeId), Is.False);
        harness.Api.EndEffectSideEffectTransaction(transaction);
        transaction.Rollback();
    }

    [Test]
    public void EffectPlan_StillRejectsEnsureLink()
    {
        const int templateId = 361;
        const int graphId = 3611;
        var templates = new EffectTemplateRegistry();
        var programs = new GraphProgramRegistry();
        programs.Register(graphId, RawEnsure(typeId: 1), GraphKind.Effect);
        EffectPhaseGraphBindings bindings = default;
        Assert.That(bindings.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, graphId), Is.True);
        templates.Register(templateId, new EffectTemplateData
        {
            LifetimeKind = EffectLifetimeKind.Instant,
            PhaseGraphBindings = bindings,
        });

        var builtins = new BuiltinHandlerRegistry();
        BuiltinHandlers.RegisterAll(builtins);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                new PresetTypeRegistry(),
                builtins,
                programs,
                GasGraphOpHandlerTable.Instance,
                "Test/effects.json"))!;

        Assert.That(error.Message, Does.StartWith(EffectExecutionPlanCompiler.UnsupportedOperationError));
        Assert.That(error.Message, Does.Contain(nameof(GraphNodeOp.RelationshipEnsureLink)));
    }

    [Test]
    public void Descriptor_OpensLinkReadWriteOnTriggerGraph_AndKeepsMetricWritesOnEffect()
    {
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.RelationshipEnsureLink), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.RelationshipEnsureLink), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.RelationshipRemoveLink), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.RelationshipHasLink), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.RelationshipGetMetric), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.RelationshipHasFlag), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.RelationshipEnsureLink), Is.False);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.RelationshipSetMetric), Is.False);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.RelationshipSetFlag), Is.False);

        var ensure = new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.RelationshipEnsureLink } };
        Assert.DoesNotThrow(() => GraphKindOperationPolicy.RequireAllowed(GraphKind.TriggerGraph, ensure, GasGraphOpHandlerTable.Instance));
        Assert.DoesNotThrow(() => GraphKindOperationPolicy.RequireAllowed(GraphKind.Script, ensure, GasGraphOpHandlerTable.Instance));
        Assert.Throws<InvalidOperationException>(() =>
            GraphKindOperationPolicy.RequireAllowed(GraphKind.Query, ensure, GasGraphOpHandlerTable.Instance));

        var setMetric = new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.RelationshipSetMetric } };
        Assert.Throws<InvalidOperationException>(() =>
            GraphKindOperationPolicy.RequireAllowed(GraphKind.TriggerGraph, setMetric, GasGraphOpHandlerTable.Instance));
    }

    [Test]
    public void TriggerGraph_SetMetric_IsNotAuthorable()
    {
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
            JsonNode.Parse("""
            {
              "kind": "TriggerGraph",
              "entries": [{ "label": "on_map_loaded", "event": "MapLoaded", "start": "caster" }],
              "nodes": [
                { "id": "caster", "op": "LoadCaster" },
                { "id": "target", "op": "LoadExplicitTarget" },
                { "id": "value", "op": "ConstInt", "intValue": 1 },
                { "id": "set", "op": "RelationshipSetMetric", "relationshipType": "SocialBond", "metric": "Loyalty" },
                { "id": "halt", "op": "HaltReturnInt" }
              ],
              "controlEdges": [
                { "from": "caster", "fromPort": "next", "to": "target" },
                { "from": "target", "fromPort": "next", "to": "value" },
                { "from": "value", "fromPort": "next", "to": "set" },
                { "from": "set", "fromPort": "next", "to": "halt" }
              ],
              "valueEdges": [
                { "from": "caster", "fromPort": "value", "to": "set", "toPort": "source" },
                { "from": "target", "fromPort": "value", "to": "set", "toPort": "target" },
                { "from": "value", "fromPort": "value", "to": "set", "toPort": "value" }
              ]
            }
            """)!.AsObject(),
            "tests.trigger_graph.relationship_set_metric",
            StrictJsonOptions.CreateCamelCase(includeFields: true));

        Assert.That(compiled.Succeeded, Is.False);
        Assert.That(FormatDiagnostics(compiled), Does.Contain("RelationshipSetMetric"));
    }

    private static JsonObject BondGraph(string op, bool thenAsk)
    {
        var nodes = new JsonArray
        {
            Node("caster", "LoadCaster"),
            Node("target", "LoadExplicitTarget"),
            Node("link", op, relationshipType: "SocialBond"),
        };
        var control = new JsonArray
        {
            Edge("caster", "target"),
            Edge("target", "link"),
        };
        var values = new JsonArray
        {
            ValueEdge("caster", "link", "source"),
            ValueEdge("target", "link", "target"),
        };
        if (thenAsk)
        {
            nodes.Add(Node("ask", "RelationshipHasLink", relationshipType: "SocialBond"));
            control.Add(Edge("link", "ask"));
            control.Add(Edge("ask", "halt"));
            values.Add(ValueEdge("caster", "ask", "source"));
            values.Add(ValueEdge("target", "ask", "target"));
        }
        else
        {
            control.Add(Edge("link", "halt"));
        }

        nodes.Add(Node("halt", "HaltReturnInt"));
        return new JsonObject
        {
            ["kind"] = "TriggerGraph",
            ["entries"] = new JsonArray
            {
                new JsonObject
                {
                    ["label"] = "on_map_loaded",
                    ["event"] = "MapLoaded",
                    ["start"] = "caster",
                },
            },
            ["nodes"] = nodes,
            ["controlEdges"] = control,
            ["valueEdges"] = values,
        };
    }

    private static JsonObject Node(string id, string op, string? relationshipType = null)
    {
        var node = new JsonObject
        {
            ["id"] = id,
            ["op"] = op,
        };
        if (relationshipType != null)
        {
            node["relationshipType"] = relationshipType;
        }

        return node;
    }

    private static JsonObject Edge(string from, string to)
        => new()
        {
            ["from"] = from,
            ["fromPort"] = "next",
            ["to"] = to,
        };

    private static JsonObject ValueEdge(string from, string to, string toPort)
        => new()
        {
            ["from"] = from,
            ["fromPort"] = "value",
            ["to"] = to,
            ["toPort"] = toPort,
        };

    private static GraphInstruction[] CompileAndPatch(RelationshipHarness harness, string graphId, JsonObject document)
    {
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
            document,
            graphId,
            StrictJsonOptions.CreateCamelCase(includeFields: true));
        Assert.That(compiled.Succeeded, Is.True, FormatDiagnostics(compiled));
        GraphInstruction[] program = compiled.Package!.Value.Program;
        GraphProgramSymbolPatcher.Patch(
            compiled.Package.Value.Symbols,
            program,
            new BondResolver(harness.SocialBondTypeId));
        GraphKindOperationPolicy.RequireAllowed(GraphKind.TriggerGraph, program, GasGraphOpHandlerTable.Instance);
        return program;
    }

    private static byte[] Execute(
        World world,
        GasGraphRuntimeApi api,
        Entity caster,
        Entity target,
        GraphInstruction[] program)
    {
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
        Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
        Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
        Span<int> callStack = stackalloc int[GraphVmLimits.MaxCallStackDepth];
        var cursor = new GraphExecutionCursor(0);
        GraphSliceResult result = GraphExecutor.ExecuteScriptSlice(
            world,
            caster,
            target,
            default,
            program,
            api,
            null,
            floats,
            ints,
            bools,
            entities,
            targets,
            callStack,
            ref cursor,
            budgetSteps: 64,
            GraphKind.TriggerGraph);
        Assert.That(result.Halted, Is.True, result.Status.ToString());
        var copy = new byte[bools.Length];
        bools.CopyTo(copy);
        return copy;
    }

    private static string FormatDiagnostics(GraphControlFlowCompileResult compiled)
    {
        var parts = new string[compiled.Diagnostics.Count];
        for (int i = 0; i < compiled.Diagnostics.Count; i++)
        {
            parts[i] = compiled.Diagnostics[i].Message;
        }

        return string.Join("; ", parts);
    }

    private static GraphInstruction[] RawEnsure(int typeId)
    {
        return
        [
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.RelationshipEnsureLink, A = 0, B = 1, Dst = (byte)typeId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
        ];
    }

    private static GraphInstruction Single(GraphInstruction[] program, GraphNodeOp op)
    {
        GraphInstruction found = default;
        int count = 0;
        for (int i = 0; i < program.Length; i++)
        {
            if (program[i].Op != (ushort)op)
            {
                continue;
            }

            found = program[i];
            count++;
        }

        Assert.That(count, Is.EqualTo(1));
        return found;
    }

    private sealed class RelationshipHarness
    {
        public GasGraphRuntimeApi Api = null!;
        public RelationshipRuntime Runtime = null!;
        public int SocialBondTypeId;

        public static RelationshipHarness Create(World world)
        {
            var types = new RelationshipTypeRegistry();
            var metrics = new RelationshipMetricRegistry();
            var flags = new RelationshipFlagRegistry();
            var runtime = new RelationshipRuntime(
                world,
                types,
                metrics,
                flags,
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(),
                new RelationshipReverseIndex(world));
            int socialBondTypeId = types.Register("SocialBond");
            var api = new GasGraphRuntimeApi(world, relationshipRuntime: runtime);
            return new RelationshipHarness
            {
                Api = api,
                Runtime = runtime,
                SocialBondTypeId = socialBondTypeId,
            };
        }
    }

    private sealed class BondResolver : IGraphSymbolResolver
    {
        private readonly int _typeId;
        public BondResolver(int typeId) => _typeId = typeId;
        public int ResolveTag(string name) => throw new NotSupportedException();
        public int ResolveAttribute(string name) => throw new NotSupportedException();
        public int ResolveEffectTemplate(string name) => throw new NotSupportedException();
        public int ResolveRelationshipType(string name) => _typeId;
        public int ResolveRelationshipMetric(string name) => throw new NotSupportedException();
        public int ResolveRelationshipFlag(string name) => throw new NotSupportedException();
        public int ResolveTargetDispatchPreset(string name) => throw new NotSupportedException();
        public int ResolveEntityTemplate(string name) => throw new NotSupportedException();
    }
}
