using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using Ludots.Core.Mathematics;
using NUnit.Framework;
using static NUnit.Framework.Assert;

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
        public void GraphCompiler_ApplyEffectTemplate_WithTwoFloatArgs_EncodesFlagsAndRegs()
        {
            var cfg = new GraphConfig
            {
                Id = "Test.ApplyEffectTemplate.Args",
                Kind = "Effect",
                Entry = "t1",
                Nodes =
                {
                    new GraphNodeConfig { Id = "t1", Op = "LoadExplicitTarget", Next = "fx" },
                    new GraphNodeConfig { Id = "fx", Op = "ConstFloat", FloatValue = 12.5f, Next = "fy" },
                    new GraphNodeConfig { Id = "fy", Op = "ConstFloat", FloatValue = -7.0f, Next = "a1" },
                    new GraphNodeConfig { Id = "a1", Op = "ApplyEffectTemplate", EffectTemplate = "Effect.Preset.ApplyForce2D", Inputs = { "t1", "fx", "fy" } }
                }
            };

            var (pkg, diags) = GraphCompiler.Compile(cfg);
            That(pkg.HasValue, Is.True);
            That(diags.Count, Is.EqualTo(0));

            var program = pkg.Value.Program;
            ref readonly var ins = ref program[program.Length - 1];
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

            GraphInstruction[] program = { i0, i1, i2 };

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

        [Test]
        public void GraphExecutor_ApplyEffectTemplate_InEffectContext_InheritsRootId()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, effectRequests: requests);
            var effectEntity = world.Create();
            var caster = world.Create();
            var target = world.Create();
            var context = new EffectContext
            {
                RootId = 47,
                Source = caster,
                Target = target,
            };
            EffectConfigParams mergedParams = default;

            api.BeginBuiltinInvocation(
                new BuiltinHandlerRegistry(),
                new EffectTemplateRegistry(),
                builtinRuntime: null,
                effectEntity,
                effectTemplateId: 1,
                in context,
                in mergedParams);
            try
            {
                GraphInstruction[] program =
                {
                    new() { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 1, Imm = 123 },
                };

                GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);
            }
            finally
            {
                api.EndBuiltinInvocation();
            }

            That(requests.Count, Is.EqualTo(1));
            That(requests[0].RootId, Is.EqualTo(47));
        }

        [Test]
        public void GraphExecutor_ApplyEffectTemplate_WithoutEffectContext_AssignsDistinctRoots()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, effectRequests: requests);
            var caster = world.Create();
            var target = world.Create();
            GraphInstruction[] program =
            {
                new() { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 1, Imm = 123 },
            };

            GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);
            GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);

            That(requests.Count, Is.EqualTo(2));
            That(requests[0].RootId, Is.GreaterThan(0));
            That(requests[1].RootId, Is.GreaterThan(0));
            That(requests[1].RootId, Is.Not.EqualTo(requests[0].RootId));
        }
    }
}
