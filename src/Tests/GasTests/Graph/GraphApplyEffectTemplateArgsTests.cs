using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using Ludots.Core.Mathematics;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GraphApplyEffectTemplateArgsTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        [Test]
        public void GraphControlFlowCompiler_ApplyEffectTemplate_WithTwoFloatArgs_EncodesFlagsAndRegs()
        {
            var cfg = new GraphControlFlowDocument
            {
                Id = "Test.ApplyEffectTemplate.Args",
                Kind = "Effect",
                Entry = "t1",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "t1", Op = "LoadExplicitTarget" },
                    new GraphControlFlowNode { Id = "fx", Op = "ConstFloat", FloatValue = 12.5f },
                    new GraphControlFlowNode { Id = "fy", Op = "ConstFloat", FloatValue = -7.0f },
                    new GraphControlFlowNode { Id = "a1", Op = "ApplyEffectTemplate", EffectTemplate = "Effect.Preset.ApplyForce2D" }
                },
                ControlEdges =
                {
                    new("t1", GraphControlFlowPorts.Next, "fx"),
                    new("fx", GraphControlFlowPorts.Next, "fy"),
                    new("fy", GraphControlFlowPorts.Next, "a1"),
                },
                ValueEdges =
                {
                    new("t1", GraphControlFlowPorts.Value, "a1", GraphControlFlowPorts.Target),
                    new("fx", GraphControlFlowPorts.Value, "a1", GraphControlFlowPorts.A),
                    new("fy", GraphControlFlowPorts.Value, "a1", GraphControlFlowPorts.B),
                },
            };

            var (pkg, _, diags) = GraphControlFlowCompiler.CompileWithOutputs(cfg);
            That(pkg.HasValue, Is.True);
            That(diags.Count, Is.EqualTo(0));

            var program = pkg.Value.Program;
            var ins = Array.Find(program, instruction => instruction.Op == (ushort)GraphNodeOp.ApplyEffectTemplate);
            That((GraphNodeOp)ins.Op, Is.EqualTo(GraphNodeOp.ApplyEffectTemplate));
            That(ins.A, Is.EqualTo(1));
            That(ins.B, Is.EqualTo(0));
            That(ins.C, Is.EqualTo(1));
            That(ins.Flags, Is.EqualTo(2));
        }

        [Test]
        public void GraphExecutor_ApplyEffectTemplate_WithArgs_PublishesEffectRequestPayload()
        {
            using var world = World.Create();
            var q = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: q);

            var target = world.Create();

            GraphInstruction i0 = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 12.5f };
            GraphInstruction i1 = new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = -7.0f };
            GraphInstruction i2 = new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 1, B = 0, C = 1, Flags = 2, Imm = 123 };
            GraphInstruction i3 = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt };

            GraphInstruction[] program = { i0, i1, i2, i3 };

            GraphExecutor.Execute(world, caster: default, explicitTarget: target, targetPosCm: new IntVector2(0, 0), program, api);

            That(q.Count, Is.EqualTo(1));
            var req = q[0];
            That(req.TemplateId, Is.EqualTo(123));
            // Legacy EffectArgs floats are now bridged to CallerParams
            That(req.HasCallerParams, Is.True);
            req.CallerParams.TryGetFloat(Ludots.Core.Gameplay.GAS.EffectParamKeys.ForceXAttribute, out float f0);
            req.CallerParams.TryGetFloat(Ludots.Core.Gameplay.GAS.EffectParamKeys.ForceYAttribute, out float f1);
            That(f0, Is.EqualTo(12.5f));
            That(f1, Is.EqualTo(-7.0f));
        }
    }
}
