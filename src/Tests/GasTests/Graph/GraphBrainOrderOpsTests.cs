using System;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GraphBrains;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Unit tests for the order-driven graph brain slice (issue #1536 切 A):
    ///   - Pure ops: LoadEntityPosX/Y, IntToFloat, FloatToInt, SqrtFloat
    ///   - Order action ops: SubmitAssignedOrder, CompleteActiveOrder (runtime API bridge)
    ///   - Order type symbol resolution (semantic key → id, fail-closed)
    ///   - Compile validation/emit and kind policy for the new ops
    ///   - GraphActionBrainHostSystem glue contract
    /// </summary>
    [TestFixture, NonParallelizable]
    public class GraphBrainOrderOpsTests
    {
        // ════════════════════════════════════════════════════════════════════
        //  Pure ops
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void LoadEntityPosXY_ReadsWorldCentimeters()
        {
            using var world = World.Create();
            Entity actor = world.Create(Ludots.Core.Components.WorldPositionCm.FromCm(650, -520));
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var state = Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.LoadEntityPosX, A = 0, Dst = 3, Flags = 2 },
                    new() { Op = (ushort)GraphNodeOp.LoadEntityPosY, A = 0, Dst = 4, Flags = 3 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 3 },
                },
                seedEntity: (0, actor));

            That(state.I[3], Is.EqualTo(650));
            That(state.I[4], Is.EqualTo(-520));
            That(state.B[2], Is.EqualTo(1));
            That(state.B[3], Is.EqualTo(1));
        }

        [Test]
        public void LoadEntityPos_GuardsMissingPositionWithFlagZero()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var state = Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.LoadEntityPosX, A = 0, Dst = 3, Flags = 2 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 3 },
                },
                seedEntity: (0, actor));

            That(state.I[3], Is.EqualTo(0));
            That(state.B[2], Is.EqualTo(0));
        }

        [Test]
        public void IntToFloat_SqrtFloat_FloatToInt_Chain_ComputesWithoutSqrtBias()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var state = Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.IntToFloat, A = 1, Dst = 0 },
                    new() { Op = (ushort)GraphNodeOp.SqrtFloat, A = 0, Dst = 1 },
                    new() { Op = (ushort)GraphNodeOp.FloatToInt, A = 1, Dst = 5 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 5 },
                },
                seedInt: (1, 9));

            That(state.F[0], Is.EqualTo(9f));
            That(state.F[1], Is.EqualTo(3f).Within(0.0001f));
            That(state.I[5], Is.EqualTo(3));
        }

        [Test]
        public void FloatToInt_RoundsHalfAwayFromZero()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var state = Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.FloatToInt, A = 0, Dst = 1 },
                    new() { Op = (ushort)GraphNodeOp.FloatToInt, A = 2, Dst = 3 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedFloat: (0, 2.5f),
                seedFloat2: (2, -2.5f));

            That(state.I[1], Is.EqualTo(3));
            That(state.I[3], Is.EqualTo(-3));
        }

        [Test]
        public void SqrtFloat_FailsClosedOnNegativeInput()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            var api = new GasGraphRuntimeApi(world, null, null, null);

            InvalidOperationException error = Throws<InvalidOperationException>(() => Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SqrtFloat, A = 0, Dst = 1 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedFloat: (0, -1f)))!;

            That(error.Message, Does.Contain("SqrtNegativeInput"));
        }

        // ════════════════════════════════════════════════════════════════════
        //  SubmitAssignedOrder / CompleteActiveOrder (runtime API bridge)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void SubmitAssignedOrder_EnqueuesImmediateOrderWithOwnerAndSpatialArgs()
        {
            using var world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var admissionResults = new OrderAdmissionResultBuffer(64, 64);
            var orders = new OrderQueue(64, admissionResults);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindOrderPipeline(orders, orderTypes);

            Entity target = world.Create();
            Entity actor = world.Create(new PlayerOwner { PlayerId = 4 });

            Execute(
                world, api, actor, target,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SubmitAssignedOrder, A = 0, B = 1, C = 2, Imm = 101 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedEntity: (0, target),
                seedInt: (1, 650),
                seedInt2: (2, 520));

            That(orders.TryDequeue(out Order outbound), Is.True);
            That(outbound.Actor, Is.EqualTo(actor));
            That(outbound.PlayerId, Is.EqualTo(4));
            That(outbound.OrderTypeId, Is.EqualTo(101));
            That(outbound.Target, Is.EqualTo(target));
            That(outbound.Args.Spatial.WorldCm.X, Is.EqualTo(650f));
            That(outbound.Args.Spatial.WorldCm.Z, Is.EqualTo(520f));
            That(outbound.SubmitMode, Is.EqualTo(OrderSubmitMode.Immediate));
        }

        [Test]
        public void SubmitAssignedOrder_FailsClosedWithoutPipeline()
        {
            using var world = World.Create();
            Entity actor = world.Create(new PlayerOwner { PlayerId = 1 });
            var api = new GasGraphRuntimeApi(world, null, null, null);

            InvalidOperationException error = Throws<InvalidOperationException>(() => Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SubmitAssignedOrder, A = 0, B = 1, C = 2, Imm = 101 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedInt: (1, 0),
                seedInt2: (2, 0)))!;

            That(error.Message, Does.Contain("MissingOrderPipeline"));
        }

        [Test]
        public void SubmitAssignedOrder_FailsClosedOnUnknownOrderTypeAndMissingOwner()
        {
            using var world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindOrderPipeline(orders, orderTypes);

            Entity actor = world.Create(new PlayerOwner { PlayerId = 1 });
            Entity target = world.Create();

            InvalidOperationException unknownType = Throws<InvalidOperationException>(() => Execute(
                world, api, actor, target,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SubmitAssignedOrder, A = 0, B = 1, C = 2, Imm = 999 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedEntity: (0, target),
                seedInt: (1, 0),
                seedInt2: (2, 0)))!;
            That(unknownType.Message, Does.Contain("UnknownOrderType"));

            Entity ownerless = world.Create();
            InvalidOperationException missingOwner = Throws<InvalidOperationException>(() => Execute(
                world, api, ownerless, target,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SubmitAssignedOrder, A = 0, B = 1, C = 2, Imm = 101 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedEntity: (0, target),
                seedInt: (1, 0),
                seedInt2: (2, 0)))!;
            That(missingOwner.Message, Does.Contain("ActorMissingOwner"));
        }

        [Test]
        public void CompleteActiveOrder_PublishesTerminalResultAndFailsClosedWithoutActiveOrder()
        {
            using var world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindOrderPipeline(new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64)), orderTypes);

            Entity target = world.Create();
            Entity actor = world.Create(
                ActiveOrder(orderId: 9, orderTypeId: 102, target: target),
                new PlayerOwner { PlayerId = 1 });
            world.Set(actor, ActiveOrder(orderId: 9, orderTypeId: 102, target: target));

            Execute(
                world, api, actor, target,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.CompleteActiveOrder },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedEntity: (0, target),
                seedInt: (1, 0));

            That(orderTypes.TerminalResults.Count, Is.GreaterThan(0));

            InvalidOperationException noActive = Throws<InvalidOperationException>(() => Execute(
                world, api, actor, target,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.CompleteActiveOrder },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedEntity: (0, target),
                seedInt: (1, 0)))!;
            That(noActive.Message, Does.Contain("NoActiveOrderToComplete"));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Symbol resolution
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ResolveOrderType_ResolvesSemanticKeyAndFailsClosed()
        {
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var resolver = new GasGraphSymbolResolver(
                new Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipMetricRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipFlagRegistry(),
                new Ludots.Core.Gameplay.GAS.TargetDispatchPresetRegistry(),
                orderTypes: orderTypes);

            That(resolver.ResolveOrderType("moveTo"), Is.EqualTo(101));
            That(resolver.ResolveOrderType("attackTarget"), Is.EqualTo(102));

            InvalidOperationException unknown = Throws<InvalidOperationException>(
                () => resolver.ResolveOrderType("nope"))!;
            That(unknown.Message, Does.Contain("unknown order type 'nope'"));

            var unbound = new GasGraphSymbolResolver(
                new Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipMetricRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipFlagRegistry(),
                new Ludots.Core.Gameplay.GAS.TargetDispatchPresetRegistry());
            InvalidOperationException unboundError = Throws<InvalidOperationException>(
                () => unbound.ResolveOrderType("moveTo"))!;
            That(unboundError.Message, Does.Contain("no OrderTypeRegistry"));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Compile + kind policy + metadata
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void SubmitAssignedOrder_CompilesFromDocumentWithSemanticOrderType()
        {
            var cfg = new GraphControlFlowDocument
            {
                Id = "Test.Brain.Submit",
                Kind = "Script",
                Entry = "entry",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "entry", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "self", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "px", Op = "LoadTargetPosX" },
                    new GraphControlFlowNode { Id = "py", Op = "LoadTargetPosY" },
                    new GraphControlFlowNode { Id = "submit", Op = "SubmitAssignedOrder", OrderType = "moveTo" },
                    new GraphControlFlowNode { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges =
                {
                    new("entry", GraphControlFlowPorts.Next, "self"),
                    new("self", GraphControlFlowPorts.Next, "px"),
                    new("px", GraphControlFlowPorts.Next, "py"),
                    new("py", GraphControlFlowPorts.Next, "submit"),
                    new("submit", GraphControlFlowPorts.Next, "halt"),
                },
                ValueEdges =
                {
                    new("self", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.Target),
                    new("px", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.A),
                    new("py", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.B),
                    new("px", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value),
                },
            };

            var (pkg, _, diags) = GraphControlFlowCompiler.CompileWithOutputs(cfg);
            That(diags, Is.Empty);
            That(pkg.HasValue, Is.True);
            That(Array.Exists(pkg!.Value.Program, i => i.Op == (ushort)GraphNodeOp.SubmitAssignedOrder), Is.True);
        }

        [Test]
        public void SubmitAssignedOrder_WithoutOrderType_FailsValidation()
        {
            var cfg = new GraphControlFlowDocument
            {
                Id = "Test.Brain.Submit.Missing",
                Kind = "Script",
                Entry = "entry",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "entry", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "self", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "px", Op = "LoadTargetPosX" },
                    new GraphControlFlowNode { Id = "py", Op = "LoadTargetPosY" },
                    new GraphControlFlowNode { Id = "submit", Op = "SubmitAssignedOrder" },
                    new GraphControlFlowNode { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges =
                {
                    new("entry", GraphControlFlowPorts.Next, "self"),
                    new("self", GraphControlFlowPorts.Next, "px"),
                    new("px", GraphControlFlowPorts.Next, "py"),
                    new("py", GraphControlFlowPorts.Next, "submit"),
                    new("submit", GraphControlFlowPorts.Next, "halt"),
                },
                ValueEdges =
                {
                    new("self", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.Target),
                    new("px", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.A),
                    new("py", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.B),
                    new("px", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value),
                },
            };

            var (_, _, diags) = GraphControlFlowCompiler.CompileWithOutputs(cfg);
            That(diags.Count, Is.GreaterThan(0));
        }

        [Test]
        public void SubmitAssignedOrder_WithoutTarget_CompilesAndRegistersAbsentSentinel()
        {
            GraphControlFlowDocument cfg = AssignedOrderDocument(includeTarget: false, includeA: true, includeB: true);
            var (pkg, _, diags) = GraphControlFlowCompiler.CompileWithOutputs(cfg);
            That(diags, Is.Empty);
            That(pkg.HasValue, Is.True);

            GraphInstruction submit = Array.Find(
                pkg!.Value.Program,
                static instruction => instruction.Op == (ushort)GraphNodeOp.SubmitAssignedOrder);
            That(submit.A, Is.EqualTo(byte.MaxValue));
            That(submit.B, Is.LessThan(GraphVmLimits.MaxFloatRegisters));
            That(submit.C, Is.LessThan(GraphVmLimits.MaxFloatRegisters));

            GraphIdRegistry.Clear();
            int graphId = GraphIdRegistry.Register("test.brain.submit.no-target");
            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                pkg.Value.Program,
                GraphKind.Script,
                GraphInstructionSourceMap.Empty,
                pkg.Value.Symbols);
        }

        [Test]
        public void SubmitAssignedOrder_WithoutCoordinates_FailsValidation()
        {
            var (_, _, missingA) = GraphControlFlowCompiler.CompileWithOutputs(
                AssignedOrderDocument(includeTarget: false, includeA: false, includeB: true));
            That(missingA.Count, Is.GreaterThan(0));

            var (_, _, missingB) = GraphControlFlowCompiler.CompileWithOutputs(
                AssignedOrderDocument(includeTarget: false, includeA: true, includeB: false));
            That(missingB.Count, Is.GreaterThan(0));
        }

        [Test]
        public void SubmitAssignedOrder_AbsentTarget_EnqueuesNullTargetWithSpatialArgs()
        {
            using var world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindOrderPipeline(orders, orderTypes);

            Entity actor = world.Create(new PlayerOwner { PlayerId = 4 });
            Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SubmitAssignedOrder, A = byte.MaxValue, B = 1, C = 2, Imm = 101 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedInt: (1, 650),
                seedInt2: (2, 520));

            That(orders.TryDequeue(out Order outbound), Is.True);
            That(outbound.Actor, Is.EqualTo(actor));
            That(outbound.PlayerId, Is.EqualTo(4));
            That(outbound.OrderTypeId, Is.EqualTo(101));
            That(outbound.Target, Is.EqualTo(Entity.Null));
            That(outbound.Args.Spatial.WorldCm.X, Is.EqualTo(650f));
            That(outbound.Args.Spatial.WorldCm.Z, Is.EqualTo(520f));
        }

        [Test]
        public void SubmitAssignedOrder_WiredDeadTarget_KeepsThatEntity()
        {
            using var world = World.Create();
            OrderTypeRegistry orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var api = new GasGraphRuntimeApi(world, null, null, null);
            api.BindOrderPipeline(orders, orderTypes);

            Entity actor = world.Create(new PlayerOwner { PlayerId = 4 });
            Entity corpse = world.Create();
            world.Destroy(corpse);

            Execute(
                world, api, actor, actor,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SubmitAssignedOrder, A = 0, B = 1, C = 2, Imm = 101 },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 1 },
                },
                seedEntity: (0, corpse),
                seedInt: (1, 10),
                seedInt2: (2, 20));

            That(orders.TryDequeue(out Order outbound), Is.True);
            That(outbound.Target, Is.EqualTo(corpse));
            That(outbound.Target, Is.Not.EqualTo(Entity.Null));
        }

        [Test]
        public void KindPolicy_RejectsOrderActionOpsOutsideScriptHosts()
        {
            var submitProgram = new GraphInstruction[] { new() { Op = (ushort)GraphNodeOp.SubmitAssignedOrder, A = 0, B = 1, C = 2, Imm = 101 } };
            var completeProgram = new GraphInstruction[] { new() { Op = (ushort)GraphNodeOp.CompleteActiveOrder } };

            That(GraphKindOperationPolicy.TryFindViolation(GraphKind.Query, submitProgram, GasGraphOpHandlerTable.Instance, out _), Is.True);
            That(GraphKindOperationPolicy.TryFindViolation(GraphKind.Score, submitProgram, GasGraphOpHandlerTable.Instance, out _), Is.True);
            That(GraphKindOperationPolicy.TryFindViolation(GraphKind.Validation, completeProgram, GasGraphOpHandlerTable.Instance, out _), Is.True);
            That(GraphKindOperationPolicy.TryFindViolation(GraphKind.Script, submitProgram, GasGraphOpHandlerTable.Instance, out _), Is.False);
            That(GraphKindOperationPolicy.TryFindViolation(GraphKind.TriggerGraph, submitProgram, GasGraphOpHandlerTable.Instance, out _), Is.False);
        }

        [Test]
        public void OperationMetadata_ClassifiesNewOps()
        {
            That(GasGraphOpHandlerTable.Instance.TryGetOperationMetadata(GraphNodeOp.SubmitAssignedOrder, out var submit), Is.True);
            That(submit.Kind, Is.EqualTo(EffectOperationKind.Unsupported));
            That(submit.Domain, Is.EqualTo(EffectAtomicDomain.Order));

            That(GasGraphOpHandlerTable.Instance.TryGetOperationMetadata(GraphNodeOp.CompleteActiveOrder, out var complete), Is.True);
            That(complete.Kind, Is.EqualTo(EffectOperationKind.Unsupported));

            That(GasGraphOpHandlerTable.Instance.TryGetOperationMetadata(GraphNodeOp.SqrtFloat, out var sqrt), Is.True);
            That(sqrt.Kind, Is.EqualTo(EffectOperationKind.Pure));
            That(GasGraphOpHandlerTable.Instance.TryGetOperationMetadata(GraphNodeOp.IntToFloat, out var convert), Is.True);
            That(convert.Kind, Is.EqualTo(EffectOperationKind.Pure));
            That(GasGraphOpHandlerTable.Instance.TryGetOperationMetadata(GraphNodeOp.LoadEntityPosX, out var posX), Is.True);
            That(posX.Kind, Is.EqualTo(EffectOperationKind.Pure));
        }

        // ════════════════════════════════════════════════════════════════════
        //  GraphActionBrainHostSystem
        // ════════════════════════════════════════════════════════════════════

        private sealed class OpenGate : IGameplayAdvanceGate
        {
            public static readonly OpenGate Instance = new();
            public bool CanAdvanceGameplay => true;
        }

        private sealed class ClosedGate : IGameplayAdvanceGate
        {
            public bool CanAdvanceGameplay => false;
        }

        [Test]
        public void BrainHost_RunsScriptPerThinkTickAndSweepsDeadEntities()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            GraphIdRegistry.Clear();
            int graphId = GraphIdRegistry.Register("test.brain.halt");
            programs.Register(graphId, new GraphInstruction[] { new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 } }, GraphKind.Script);
            var api = new GasGraphRuntimeApi(world, null, null, null);
            var system = new GraphActionBrainHostSystem(world, programs, api, OpenGate.Instance);

            Entity target = world.Create();
            Entity actor = world.Create(
                new GraphActionBrain { ScriptKey = "test.brain.halt", ThinkEveryNTicks = 1 },
                ActiveOrder(orderId: 3, orderTypeId: 102, target: target),
                new PlayerOwner { PlayerId = 1 },
                new Ludots.Core.Gameplay.GAS.Components.BlackboardIntBuffer(),
                new Ludots.Core.Gameplay.GAS.Components.BlackboardEntityBuffer());

            system.Update(1f / 30f);
            system.Update(1f / 30f);
            That(system.TotalSteps, Is.GreaterThan(0));

            world.Destroy(actor);
            system.Update(1f / 30f);
            system.Update(1f / 30f);
            That(system.TotalSteps, Is.GreaterThan(0));
        }

        [Test]
        public void BrainHost_FailsClosedOnUnknownScriptKeyAndHonorsGate()
        {
            using var world = World.Create();
            var programs = new GraphProgramRegistry();
            GraphIdRegistry.Clear();
            _ = GraphIdRegistry.Register("test.brain.halt");
            var api = new GasGraphRuntimeApi(world, null, null, null);

            Entity actor = world.Create(
                new GraphActionBrain { ScriptKey = "test.brain.missing", ThinkEveryNTicks = 1 },
                new OrderBuffer(),
                new Ludots.Core.Gameplay.Components.PlayerOwner { PlayerId = 1 },
                new Ludots.Core.Gameplay.GAS.Components.BlackboardIntBuffer(),
                new Ludots.Core.Gameplay.GAS.Components.BlackboardEntityBuffer());
            var gated = new GraphActionBrainHostSystem(world, programs, api, new ClosedGate());
            gated.Update(1f / 30f);

            var system = new GraphActionBrainHostSystem(world, programs, api, OpenGate.Instance);
            InvalidOperationException error = Throws<InvalidOperationException>(() => system.Update(1f / 30f))!;
            That(error.Message, Does.Contain("UnknownScriptKey"));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════

        private static GraphExecutionState Execute(
            World world,
            IGraphRuntimeApi api,
            Entity caster,
            Entity target,
            GraphInstruction[] program,
            (int Index, Entity Value)? seedEntity = null,
            (int Index, int Value)? seedInt = null,
            (int Index, int Value)? seedInt2 = null,
            (int Index, float Value)? seedFloat = null,
            (int Index, float Value)? seedFloat2 = null)
        {
            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetPosCm = default,
                Api = api,
                F = new float[GraphVmLimits.MaxFloatRegisters],
                I = new int[GraphVmLimits.MaxIntRegisters],
                B = new byte[GraphVmLimits.MaxBoolRegisters],
                E = new Entity[GraphVmLimits.MaxEntityRegisters],
                Targets = new Entity[GraphVmLimits.MaxTargets],
                TargetList = new GraphTargetList(new Entity[GraphVmLimits.MaxTargets]),
                CallStack = new int[GraphVmLimits.MaxCallStackDepth],
                CallStackCount = 0,
            };
            state.E[0] = caster;
            state.E[1] = target;
            if (seedEntity.HasValue) state.E[seedEntity.Value.Index] = seedEntity.Value.Value;
            if (seedInt.HasValue) state.I[seedInt.Value.Index] = seedInt.Value.Value;
            if (seedInt2.HasValue) state.I[seedInt2.Value.Index] = seedInt2.Value.Value;
            if (seedFloat.HasValue) state.F[seedFloat.Value.Index] = seedFloat.Value.Value;
            if (seedFloat2.HasValue) state.F[seedFloat2.Value.Index] = seedFloat2.Value.Value;

            GasGraphOpHandlerTable.Execute(ref state, WithHalt(program), GasGraphOpHandlerTable.Instance);
            return state;
        }

        private static GraphInstruction[] WithHalt(GraphInstruction[] program)
        {
            bool endsWithHalt = program.Length > 0 && program[^1].Op == (ushort)GraphNodeOp.HaltReturnInt;
            if (endsWithHalt)
            {
                return program;
            }

            var withHalt = new GraphInstruction[program.Length + 1];
            program.AsSpan().CopyTo(withHalt);
            withHalt[^1] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 };
            return withHalt;
        }

        private static OrderTypeRegistry CreateOrderTypes()
        {
            var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            registry.Register(new OrderTypeConfig
            {
                Key = "moveTo",
                OrderTypeId = 101,
                MaxQueueSize = 1,
                SameTypePolicy = SameTypePolicy.Replace,
                QueueFullPolicy = QueueFullPolicy.RejectNew,
                Priority = 100,
                BufferWindowMs = 0,
                PendingBufferWindowMs = 0,
                CanInterruptSelf = true,
                QueuedModeMaxSize = 1,
                AllowQueuedMode = false,
                ClearQueueOnActivate = true,
            });
            registry.Register(new OrderTypeConfig
            {
                Key = "attackTarget",
                OrderTypeId = 102,
                MaxQueueSize = 1,
                SameTypePolicy = SameTypePolicy.Replace,
                QueueFullPolicy = QueueFullPolicy.RejectNew,
                Priority = 100,
                BufferWindowMs = 0,
                PendingBufferWindowMs = 0,
                CanInterruptSelf = true,
                QueuedModeMaxSize = 1,
                AllowQueuedMode = false,
                ClearQueueOnActivate = true,
            });
            return registry;
        }

        private static GraphControlFlowDocument AssignedOrderDocument(bool includeTarget, bool includeA, bool includeB)
        {
            var cfg = new GraphControlFlowDocument
            {
                Id = "Test.Brain.Submit.OptionalTarget",
                Kind = "Script",
                Entry = "entry",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "entry", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "self", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "px", Op = "ConstInt", IntValue = 650 },
                    new GraphControlFlowNode { Id = "py", Op = "ConstInt", IntValue = 520 },
                    new GraphControlFlowNode { Id = "submit", Op = "SubmitAssignedOrder", OrderType = "moveTo" },
                    new GraphControlFlowNode { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges =
                {
                    new("entry", GraphControlFlowPorts.Next, "self"),
                    new("self", GraphControlFlowPorts.Next, "px"),
                    new("px", GraphControlFlowPorts.Next, "py"),
                    new("py", GraphControlFlowPorts.Next, "submit"),
                    new("submit", GraphControlFlowPorts.Next, "halt"),
                },
            };
            if (includeTarget)
            {
                cfg.ValueEdges.Add(new("self", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.Target));
            }

            if (includeA)
            {
                cfg.ValueEdges.Add(new("px", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.A));
                cfg.ValueEdges.Add(new("px", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value));
            }
            else
            {
                cfg.ValueEdges.Add(new("py", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value));
            }

            if (includeB)
            {
                cfg.ValueEdges.Add(new("py", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.B));
            }

            return cfg;
        }

        private static OrderBuffer ActiveOrder(int orderId, int orderTypeId, Entity target) => new()
        {
            ActiveIndex = 0,
            ActiveOrder = new QueuedOrder
            {
                Order = new Order
                {
                    OrderId = orderId,
                    OrderTypeId = orderTypeId,
                    PlayerId = 1,
                    Target = target,
                    Args = default,
                },
            },
        };
    [Test]
    public void CompiledLoadEntityPosOpsGetDistinctAllocatedBoolScratches()
    {
        var cfg = new GraphControlFlowDocument
        {
            Id = "Test.Brain.PosScratch",
            Kind = "Script",
            Entry = "self",
            Nodes =
            {
                new GraphControlFlowNode { Id = "self", Op = "LoadCaster" },
                new GraphControlFlowNode { Id = "posX", Op = "LoadEntityPosX" },
                new GraphControlFlowNode { Id = "posY", Op = "LoadEntityPosY" },
                new GraphControlFlowNode { Id = "halt", Op = "HaltReturnInt" },
            },
            ControlEdges =
            {
                new("self", GraphControlFlowPorts.Next, "posX"),
                new("posX", GraphControlFlowPorts.Next, "posY"),
                new("posY", GraphControlFlowPorts.Next, "halt"),
            },
            ValueEdges =
            {
                new("self", GraphControlFlowPorts.Value, "posX", GraphControlFlowPorts.Source),
                new("self", GraphControlFlowPorts.Value, "posY", GraphControlFlowPorts.Source),
                new("posX", GraphControlFlowPorts.Value, "halt", GraphControlFlowPorts.Value),
            },
        };

        var (pkg, _, diags) = GraphControlFlowCompiler.CompileWithOutputs(cfg);
        That(diags, Is.Empty);
        GraphInstruction posX = pkg!.Value.Program.First(i => i.Op == (ushort)GraphNodeOp.LoadEntityPosX);
        GraphInstruction posY = pkg.Value.Program.First(i => i.Op == (ushort)GraphNodeOp.LoadEntityPosY);
        That(posX.Flags, Is.Not.EqualTo(posY.Flags),
            "the guarded valid flags must land in distinct allocated bool scratches, not the unset default");
    }
}
}
