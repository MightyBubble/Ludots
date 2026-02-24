using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Unit tests for new Graph operations added during the architecture overhaul:
    ///   - LoadContextSource / LoadContextTarget / LoadContextTargetContext
    ///   - ApplyEffectDynamic / FanOutApplyEffectDynamic
    /// </summary>
    [TestFixture]
    public class NewGraphOpsTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        // ════════════════════════════════════════════════════════════════════
        //  LoadContextSource / LoadContextTarget / LoadContextTargetContext
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_LoadContextSource_LoadsFromExecutionState()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            // LoadContextSource loads the caster entity into an entity register
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadContextSource, Dst = 2 },
            };

            var result = ExecuteAndGetEntity(world, api, caster, target, program, entityReg: 2);
            That(result, Is.EqualTo(caster));
        }

        [Test]
        public void GraphOps_LoadContextTarget_LoadsFromExecutionState()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 3 },
            };

            var result = ExecuteAndGetEntity(world, api, caster, target, program, entityReg: 3);
            That(result, Is.EqualTo(target));
        }

        [Test]
        public void GraphOps_LoadContextTargetContext_LoadsFromExecutionState()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var targetCtx = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadContextTargetContext, Dst = 4 },
            };

            var result = ExecuteAndGetEntityWithTargetContext(
                world, api, caster, target, targetCtx, program, entityReg: 4);
            That(result, Is.EqualTo(targetCtx));
        }

        // ════════════════════════════════════════════════════════════════════
        //  ApplyEffectDynamic
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_ApplyEffectDynamic_PublishesEffectRequest()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, effectRequests: requests);

            var caster = world.Create();
            var target = world.Create();

            // I[0] = templateId, E[0] = caster, E[1] = target
            // ApplyEffectDynamic: source=Caster(implicit), target=E[A], templateId=I[B]
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
                new() { Op = (ushort)GraphNodeOp.ConstInt, Dst = 0, Imm = 42 },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectDynamic, A = 1, B = 0 },
            };

            ExecuteProgram(world, api, caster, target, program);

            That(requests.Count, Is.EqualTo(1));
            var req = requests[0];
            That(req.TemplateId, Is.EqualTo(42));
            That(req.Source, Is.EqualTo(caster));
            That(req.Target, Is.EqualTo(target));
        }

        // ════════════════════════════════════════════════════════════════════
        //  FanOutApplyEffectDynamic
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_FanOutApplyEffectDynamic_PublishesForAllTargets()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, effectRequests: requests);

            var caster = world.Create();
            var t1 = world.Create();
            var t2 = world.Create();
            var t3 = world.Create();

            // Build target list with 3 targets, then FanOut with templateId from I[B]
            // FanOutApplyEffectDynamic: source=E[A], templateId=I[B], targets=TargetList
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var iArr = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];

            e[0] = caster;
            e[1] = t1; // not used directly
            targets[0] = t1;
            targets[1] = t2;
            targets[2] = t3;
            iArr[0] = 99; // templateId

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = t1,
                TargetPos = default,
                Api = api,
                F = f,
                I = iArr,
                B = b,
                E = e,
                Targets = targets,
                TargetList = new GraphTargetList(targets) { Count = 3 },
            };

            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.FanOutApplyEffectDynamic, A = 0, B = 0 },
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            That(requests.Count, Is.EqualTo(3));
            That(requests[0].TemplateId, Is.EqualTo(99));
            That(requests[0].Source, Is.EqualTo(caster));
            That(requests[0].Target, Is.EqualTo(t1));
            That(requests[1].Target, Is.EqualTo(t2));
            That(requests[2].Target, Is.EqualTo(t3));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Combined: LoadConfig → ApplyEffectDynamic
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphOps_LoadConfigEffectId_ThenDynamicApply_WorksEndToEnd()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, effectRequests: requests);

            var caster = world.Create();
            var target = world.Create();

            // Set up config context with a payload effect ID
            var config = new EffectConfigParams();
            int payloadKey = EffectParamKeys.PayloadEffectId;
            config.TryAddEffectTemplateId(payloadKey, 777);
            api.SetConfigContext(in config);

            // Graph: load payload effect ID from config, then apply it dynamically
            // ApplyEffectDynamic: source=Caster(implicit), target=E[A], templateId=I[B]
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 0 },
                new() { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 1 },
                new() { Op = (ushort)GraphNodeOp.LoadConfigEffectId, Dst = 0, Imm = payloadKey },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectDynamic, A = 1, B = 0 },
            };

            ExecuteProgram(world, api, caster, target, program);
            api.ClearConfigContext();

            That(requests.Count, Is.EqualTo(1));
            That(requests[0].TemplateId, Is.EqualTo(777));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════

        private static Entity ExecuteAndGetEntity(World world, IGraphRuntimeApi api,
            Entity caster, Entity target, GraphInstruction[] program, int entityReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world, Caster = caster, ExplicitTarget = target,
                TargetPos = default, Api = api,
                F = f, I = i, B = b, E = e,
                Targets = targets, TargetList = new GraphTargetList(targets),
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return e[entityReg];
        }

        private static Entity ExecuteAndGetEntityWithTargetContext(World world, IGraphRuntimeApi api,
            Entity caster, Entity target, Entity targetCtx, GraphInstruction[] program, int entityReg)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world, Caster = caster, ExplicitTarget = target,
                TargetPos = default, Api = api,
                F = f, I = i, B = b, E = e,
                Targets = targets, TargetList = new GraphTargetList(targets),
                TargetContext = targetCtx,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
            return e[entityReg];
        }

        private static void ExecuteProgram(World world, IGraphRuntimeApi api,
            Entity caster, Entity target, GraphInstruction[] program)
        {
            var f = new float[GraphVmLimits.MaxFloatRegisters];
            var i = new int[GraphVmLimits.MaxIntRegisters];
            var b = new byte[GraphVmLimits.MaxBoolRegisters];
            var e = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            e[0] = caster;
            e[1] = target;

            var state = new GraphExecutionState
            {
                World = world, Caster = caster, ExplicitTarget = target,
                TargetPos = default, Api = api,
                F = f, I = i, B = b, E = e,
                Targets = targets, TargetList = new GraphTargetList(targets),
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
        }
    }
}
