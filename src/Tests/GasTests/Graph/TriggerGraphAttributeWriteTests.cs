using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class TriggerGraphAttributeWriteTests
{
    [Test]
    public void TriggerGraphModifyAttributeSet_WritesThroughAuthority()
    {
        int healthId = AttributeRegistry.Register("tests.trigger_graph.health");
        using var world = World.Create();
        Entity caster = world.Create(new AttributeBuffer(), new DirtyFlags());
        Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
        world.Get<AttributeBuffer>(target).SetBase(healthId, 100f);

        var api = new GasGraphRuntimeApi(
            world,
            tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
        var program = new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 2 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 42f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeSet, A = 2, B = 0, Imm = healthId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
        };

        ExecuteTriggerSlice(world, api, caster, target, program);

        Assert.That(world.Get<AttributeBuffer>(target).GetCurrent(healthId), Is.EqualTo(42f));
        Assert.That(world.Get<DirtyFlags>(target).IsAttributeDirty(healthId), Is.True);
        Assert.That(world.Has<GameplayAttributeChangedBits>(target), Is.True);
        Assert.That(world.Get<GameplayAttributeChangedBits>(target).IsSet(healthId), Is.True);
    }

    [Test]
    public void TriggerGraphModifyAttributeSet_UnknownEntityFailsClosed()
    {
        int healthId = AttributeRegistry.Register("tests.trigger_graph.dead_target");
        using var world = World.Create();
        Entity caster = world.Create(new AttributeBuffer(), new DirtyFlags());
        Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
        world.Get<AttributeBuffer>(target).SetBase(healthId, 100f);
        world.Destroy(target);
        var api = new GasGraphRuntimeApi(
            world,
            tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
        var program = new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 42f },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeSet, A = 1, B = 0, Imm = healthId },
            new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ExecuteTriggerSlice(world, api, caster, target, program));
        Assert.That(ex!.Message, Does.Contain("ModifyAttributeSetTargetDead"));
        Assert.That(ex.Message, Does.Contain("target entity"));
    }

    [Test]
    public void TriggerGraphModifyAttributeSet_UnknownAttributeFailsClosedByName()
    {
        const string attributeName = "tests.trigger_graph.unknown_attribute";
        var document = JsonNode.Parse($$"""
        {
          "kind": "TriggerGraph",
          "entries": [{ "label": "on_map_loaded", "event": "MapLoaded", "start": "value" }],
          "nodes": [
            { "id": "value", "op": "ConstFloat", "floatValue": 42 },
            { "id": "target", "op": "LoadExplicitTarget" },
            { "id": "write", "op": "ModifyAttributeSet", "attribute": "{{attributeName}}" },
            { "id": "halt", "op": "HaltReturnInt" }
          ],
          "controlEdges": [
            { "from": "value", "fromPort": "next", "to": "target" },
            { "from": "target", "fromPort": "next", "to": "write" },
            { "from": "write", "fromPort": "next", "to": "halt" }
          ],
          "valueEdges": [
            { "from": "value", "fromPort": "value", "to": "write", "toPort": "value" },
            { "from": "target", "fromPort": "value", "to": "write", "toPort": "target" }
          ]
        }
        """)!.AsObject();
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
            document,
            "tests.trigger_graph.unknown_attribute",
            StrictJsonOptions.CreateCamelCase(includeFields: true));
        Assert.That(compiled.Succeeded, Is.True);

        var program = compiled.Program;
        var ex = Assert.Throws<InvalidOperationException>(() => GraphProgramSymbolPatcher.Patch(
            compiled.Package!.Value.Symbols,
            program,
            new UnknownAttributeResolver(attributeName)));
        Assert.That(ex!.Message, Does.Contain(attributeName));
    }

    [Test]
    public void Descriptor_AllowsModifyAttributeSetForTriggerGraphExecution()
    {
        var program = new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeSet }
        };

        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.TriggerGraph, GraphNodeOp.ModifyAttributeSet), Is.True);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Script, GraphNodeOp.ModifyAttributeSet), Is.False);
        Assert.That(GraphOpDescriptorTable.IsAuthorable(GraphKind.Query, GraphNodeOp.ModifyAttributeSet), Is.False);
        Assert.DoesNotThrow(() => GraphKindOperationPolicy.RequireAllowed(
            GraphKind.TriggerGraph, program, GasGraphOpHandlerTable.Instance));
        Assert.Throws<InvalidOperationException>(() => GraphKindOperationPolicy.RequireAllowed(
            GraphKind.Script, program, GasGraphOpHandlerTable.Instance));
        Assert.DoesNotThrow(() => GraphKindOperationPolicy.RequireAllowed(
            GraphKind.Effect, program, GasGraphOpHandlerTable.Instance));
    }

    private static void ExecuteTriggerSlice(
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
            budgetSteps: 32,
            GraphKind.TriggerGraph);
        Assert.That(result.Halted, Is.True, result.Status.ToString());
    }

    private sealed class UnknownAttributeResolver : IGraphSymbolResolver
    {
        private readonly string _name;
        public UnknownAttributeResolver(string name) => _name = name;
        public int ResolveAttribute(string name) => throw new InvalidOperationException($"unknown attribute '{_name}'");
        public int ResolveTag(string name) => throw new NotSupportedException();
        public int ResolveEffectTemplate(string name) => throw new NotSupportedException();
        public int ResolveRelationshipType(string name) => throw new NotSupportedException();
        public int ResolveRelationshipMetric(string name) => throw new NotSupportedException();
        public int ResolveRelationshipFlag(string name) => throw new NotSupportedException();
        public int ResolveRelationshipReason(string name) => throw new NotSupportedException();
        public int ResolveTargetDispatchPreset(string name) => throw new NotSupportedException();
        public int ResolveEntityTemplate(string name) => throw new NotSupportedException();
    }
}
