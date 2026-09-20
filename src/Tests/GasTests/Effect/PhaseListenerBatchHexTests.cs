using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Comprehensive tests for Phase Listener, Batch Ops, and Hex spatial query systems.
    /// </summary>
    [TestFixture]
    public class PhaseListenerBatchHexTests
    {
        private readonly TagOps _tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());

        // ════════════════════════════════════════════════════════════════════
        //  Module C: HexCoordinates Utility Tests
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void HexCoordinates_Distance_Zero()
        {
            var a = new HexCoordinates(0, 0);
            That(HexCoordinates.Distance(a, a), Is.EqualTo(0));
        }

        [Test]
        public void HexCoordinates_Distance_Adjacent()
        {
            var center = new HexCoordinates(0, 0);
            var dirs = HexCoordinates.Directions;
            for (int i = 0; i < 6; i++)
            {
                That(HexCoordinates.Distance(center, dirs[i]), Is.EqualTo(1), $"Direction {i}");
            }
        }

        [Test]
        public void HexCoordinates_Distance_TwoSteps()
        {
            var a = new HexCoordinates(0, 0);
            var b = new HexCoordinates(2, 0);
            That(HexCoordinates.Distance(a, b), Is.EqualTo(2));

            var c = new HexCoordinates(1, 1);
            That(HexCoordinates.Distance(a, c), Is.EqualTo(2));
        }

        [Test]
        public void HexCoordinates_GetNeighbors_Returns6()
        {
            var center = new HexCoordinates(3, 4);
            Span<HexCoordinates> neighbors = stackalloc HexCoordinates[6];
            HexCoordinates.GetNeighbors(center, neighbors);

            for (int i = 0; i < 6; i++)
            {
                That(HexCoordinates.Distance(center, neighbors[i]), Is.EqualTo(1), $"Neighbor {i}");
            }

            // All unique
            for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++)
                    That(neighbors[i], Is.Not.EqualTo(neighbors[j]), $"Neighbor {i} vs {j}");
        }

        [Test]
        public void HexCoordinates_GetRing_Radius0_ReturnsCenter()
        {
            var center = new HexCoordinates(1, 2);
            Span<HexCoordinates> output = stackalloc HexCoordinates[1];
            int count = HexCoordinates.GetRing(center, 0, output);
            That(count, Is.EqualTo(1));
            That(output[0], Is.EqualTo(center));
        }

        [Test]
        public void HexCoordinates_GetRing_Radius1_Returns6()
        {
            var center = new HexCoordinates(0, 0);
            Span<HexCoordinates> output = stackalloc HexCoordinates[6];
            int count = HexCoordinates.GetRing(center, 1, output);
            That(count, Is.EqualTo(6));
            for (int i = 0; i < count; i++)
            {
                That(HexCoordinates.Distance(center, output[i]), Is.EqualTo(1), $"Ring[{i}]");
            }
        }

        [Test]
        public void HexCoordinates_GetRing_Radius2_Returns12()
        {
            var center = new HexCoordinates(0, 0);
            int expectedCount = HexCoordinates.RingCount(2);
            That(expectedCount, Is.EqualTo(12));
            Span<HexCoordinates> output = stackalloc HexCoordinates[12];
            int count = HexCoordinates.GetRing(center, 2, output);
            That(count, Is.EqualTo(12));
            for (int i = 0; i < count; i++)
            {
                That(HexCoordinates.Distance(center, output[i]), Is.EqualTo(2), $"Ring2[{i}]");
            }
        }

        [Test]
        public void HexCoordinates_GetRange_Radius0_Returns1()
        {
            var center = new HexCoordinates(0, 0);
            Span<HexCoordinates> output = stackalloc HexCoordinates[1];
            int count = HexCoordinates.GetRange(center, 0, output);
            That(count, Is.EqualTo(1));
            That(output[0], Is.EqualTo(center));
        }

        [Test]
        public void HexCoordinates_GetRange_Radius1_Returns7()
        {
            var center = new HexCoordinates(0, 0);
            int expectedCount = HexCoordinates.RangeCount(1);
            That(expectedCount, Is.EqualTo(7));
            Span<HexCoordinates> output = stackalloc HexCoordinates[7];
            int count = HexCoordinates.GetRange(center, 1, output);
            That(count, Is.EqualTo(7));
        }

        [Test]
        public void HexCoordinates_GetRange_Radius2_Returns19()
        {
            var center = new HexCoordinates(0, 0);
            int expectedCount = HexCoordinates.RangeCount(2);
            That(expectedCount, Is.EqualTo(19));
            Span<HexCoordinates> output = stackalloc HexCoordinates[19];
            int count = HexCoordinates.GetRange(center, 2, output);
            That(count, Is.EqualTo(19));
            for (int i = 0; i < count; i++)
            {
                That(HexCoordinates.Distance(center, output[i]), Is.LessThanOrEqualTo(2), $"Range2[{i}]");
            }
        }

        [Test]
        public void HexCoordinates_RangeCount_Formula()
        {
            // 1 + 3*r*(r+1)
            That(HexCoordinates.RangeCount(0), Is.EqualTo(1));
            That(HexCoordinates.RangeCount(1), Is.EqualTo(7));
            That(HexCoordinates.RangeCount(2), Is.EqualTo(19));
            That(HexCoordinates.RangeCount(3), Is.EqualTo(37));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Module B: Batch / Iteration Ops
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AddInt_BasicArithmetic()
        {
            var program = new GraphProgramBuffer();
            program.Add((ushort)GraphNodeOp.ConstInt, dst: 0, imm: 7);    // I[0] = 7
            program.Add((ushort)GraphNodeOp.ConstInt, dst: 1, imm: 3);    // I[1] = 3
            program.Add((ushort)GraphNodeOp.AddInt, dst: 2, a: 0, b: 1);  // I[2] = 7 + 3 = 10

            var world = World.Create();
            var caster = world.Create();
            var state = SetupExecution(world, caster, caster, program);
            var instructions = ExtractInstructions(program);
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);
            That(state.I[2], Is.EqualTo(10));
            world.Dispose();
        }

        [Test]
        public void CompareLtInt_ReturnsCorrectBool()
        {
            var program = new GraphProgramBuffer();
            program.Add((ushort)GraphNodeOp.ConstInt, dst: 0, imm: 3);      // I[0] = 3
            program.Add((ushort)GraphNodeOp.ConstInt, dst: 1, imm: 7);      // I[1] = 7
            program.Add((ushort)GraphNodeOp.CompareLtInt, dst: 0, a: 0, b: 1); // B[0] = (3 < 7) = 1
            program.Add((ushort)GraphNodeOp.CompareLtInt, dst: 1, a: 1, b: 0); // B[1] = (7 < 3) = 0

            var world = World.Create();
            var caster = world.Create();
            var state = SetupExecution(world, caster, caster, program);
            var instructions = ExtractInstructions(program);
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);
            That(state.B[0], Is.EqualTo(1));
            That(state.B[1], Is.EqualTo(0));
            world.Dispose();
        }

        [Test]
        public void CompareEqInt_ReturnsCorrectBool()
        {
            var program = new GraphProgramBuffer();
            program.Add((ushort)GraphNodeOp.ConstInt, dst: 0, imm: 5);      // I[0] = 5
            program.Add((ushort)GraphNodeOp.ConstInt, dst: 1, imm: 5);      // I[1] = 5
            program.Add((ushort)GraphNodeOp.ConstInt, dst: 2, imm: 3);      // I[2] = 3
            program.Add((ushort)GraphNodeOp.CompareEqInt, dst: 0, a: 0, b: 1); // B[0] = (5 == 5) = 1
            program.Add((ushort)GraphNodeOp.CompareEqInt, dst: 1, a: 0, b: 2); // B[1] = (5 == 3) = 0

            var world = World.Create();
            var caster = world.Create();
            var state = SetupExecution(world, caster, caster, program);
            var instructions = ExtractInstructions(program);
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);
            That(state.B[0], Is.EqualTo(1));
            That(state.B[1], Is.EqualTo(0));
            world.Dispose();
        }

        [Test]
        public void HasTag_ChecksEntityTag()
        {
            var world = World.Create();
            var entity = world.Create(new GameplayTagContainer(), new TagCountContainer());
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            ref var counts = ref world.Get<TagCountContainer>(entity);
            int tagId = TagRegistry.Register("Test.HasTagOp");
            _tagOps.AddTag(ref tags, ref counts, tagId);

            var api = new GasGraphRuntimeApi(world, tagOps: _tagOps);
            var program = new GraphProgramBuffer();
            program.Add((ushort)GraphNodeOp.LoadCaster, dst: 0);             // E[0] = caster
            program.Add((ushort)GraphNodeOp.HasTag, dst: 0, a: 0, imm: tagId); // B[0] = HasTag(E[0], tagId)

            var state = SetupExecution(world, entity, entity, program, api);
            var instructions = ExtractInstructions(program);
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);
            That(state.B[0], Is.EqualTo(1));
            world.Dispose();
        }

        [Test]
        public void TargetListGet_ReadsEntityFromList()
        {
            var world = World.Create();
            var e0 = world.Create();
            var e1 = world.Create();
            var e2 = world.Create();

            // Manually populate TargetList
            var targetBuffer = new Entity[64];
            targetBuffer[0] = e0;
            targetBuffer[1] = e1;
            targetBuffer[2] = e2;

            var fRegs = new float[16];
            var iRegs = new int[16];
            var bRegs = new byte[16];
            var eRegs = new Entity[16];
            eRegs[0] = e0; // caster

            var targetList = new GraphTargetList(targetBuffer);
            targetList.SetCount(3);

            var state = new GraphExecutionState
            {
                World = world,
                Caster = e0,
                ExplicitTarget = e0,
                TargetPosCm = default,
                Api = new GasGraphRuntimeApi(world, null, null, null),
                F = fRegs,
                I = iRegs,
                B = bRegs,
                E = eRegs,
                Targets = targetBuffer,
                TargetList = targetList,
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            // I[0] = 1 (index)
            iRegs[0] = 1;
            var program = new GraphProgramBuffer();
            program.Add((ushort)GraphNodeOp.TargetListGet, dst: 3, a: 0, flags: 0); // E[3] = Targets[I[0]], B[0] = valid

            var instructions = ExtractInstructions(program);
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);

            That(state.E[3], Is.EqualTo(e1));
            That(state.B[0], Is.EqualTo(1));

            // Out of bounds
            iRegs[0] = 99;
            program.Clear();
            program.Add((ushort)GraphNodeOp.TargetListGet, dst: 4, a: 0, flags: 1);
            instructions = ExtractInstructions(program);
            state.TargetList = targetList; // re-attach (ref struct)
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);
            That(state.B[1], Is.EqualTo(0));

            world.Dispose();
        }

        [Test]
        public void FanOutApplyEffect_PublishesRequestPerTarget()
        {
            var world = World.Create();
            var caster = world.Create();
            var targets = new Entity[5];
            for (int i = 0; i < 5; i++) targets[i] = world.Create();

            var requestQueue = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, requestQueue);

            var fRegs = new float[16];
            var iRegs = new int[16];
            var bRegs = new byte[16];
            var eRegs = new Entity[16];
            eRegs[0] = caster;

            var targetBuffer = new Entity[64];
            Array.Copy(targets, targetBuffer, 5);
            var targetList = new GraphTargetList(targetBuffer);
            targetList.SetCount(5);

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = caster,
                TargetPosCm = default,
                Api = api,
                F = fRegs,
                I = iRegs,
                B = bRegs,
                E = eRegs,
                Targets = targetBuffer,
                TargetList = targetList,
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            var program = new GraphProgramBuffer();
            program.Add((ushort)GraphNodeOp.FanOutApplyEffect, imm: 42); // templateId = 42

            var instructions = ExtractInstructions(program);
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);

            // Check all 5 requests were published
            That(requestQueue.Count, Is.EqualTo(5));
            for (int i = 0; i < 5; i++)
            {
                var req = requestQueue[i];
                That(req.Source, Is.EqualTo(caster));
                That(req.Target, Is.EqualTo(targets[i]));
                That(req.TemplateId, Is.EqualTo(42));
            }

            world.Dispose();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Module A: Phase Listener
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public unsafe void ListenerBuffer_TryAdd_And_Collect()
        {
            var buf = new EffectPhaseListenerBuffer();
            That(buf.TryAdd(10, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, 100, 0, 50, 1), Is.True);
            That(buf.TryAdd(20, 0, EffectPhaseId.OnApply, PhaseListenerScope.Source,
                PhaseListenerActionFlags.PublishEvent, 0, 200, 30, 2), Is.True);
            That(buf.Count, Is.EqualTo(2));

            Span<PhaseListenerCollectedAction> actions = stackalloc PhaseListenerCollectedAction[8];

            // Match scope=Target, tag=10
            int n = buf.Collect(10, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target, actions);
            That(n, Is.EqualTo(1));
            That(actions[0].GraphProgramId, Is.EqualTo(100));

            // Match scope=Source, tag=20
            n = buf.Collect(20, 0, EffectPhaseId.OnApply, PhaseListenerScope.Source, actions);
            That(n, Is.EqualTo(1));
            That(actions[0].EventTagId, Is.EqualTo(200));

            // No match
            n = buf.Collect(99, 0, EffectPhaseId.OnHit, PhaseListenerScope.Target, actions);
            That(n, Is.EqualTo(0));
        }

        [Test]
        public unsafe void ListenerBuffer_ListenEffectId_Matching()
        {
            var buf = new EffectPhaseListenerBuffer();
            // Listener that matches specific effectTemplateId=42
            buf.TryAdd(0, 42, EffectPhaseId.OnApply, PhaseListenerScope.Source,
                PhaseListenerActionFlags.ExecuteGraph, 100, 0, 50, 1);

            Span<PhaseListenerCollectedAction> actions = stackalloc PhaseListenerCollectedAction[8];

            // Matches: effectTemplateId=42
            int n = buf.Collect(0, 42, EffectPhaseId.OnApply, PhaseListenerScope.Source, actions);
            That(n, Is.EqualTo(1));

            // No match: effectTemplateId=99
            n = buf.Collect(0, 99, EffectPhaseId.OnApply, PhaseListenerScope.Source, actions);
            That(n, Is.EqualTo(0));

            // Wildcard listenEffectId=0 matches any
            var buf2 = new EffectPhaseListenerBuffer();
            buf2.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Source,
                PhaseListenerActionFlags.ExecuteGraph, 200, 0, 50, 1);
            n = buf2.Collect(0, 99, EffectPhaseId.OnApply, PhaseListenerScope.Source, actions);
            That(n, Is.EqualTo(1));
        }

        [Test]
        public unsafe void ListenerBuffer_RemoveByOwner()
        {
            var buf = new EffectPhaseListenerBuffer();
            buf.TryAdd(10, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, 100, 0, 50, ownerEffectId: 1);
            buf.TryAdd(20, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, 200, 0, 30, ownerEffectId: 2);
            buf.TryAdd(30, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, 300, 0, 10, ownerEffectId: 1);
            That(buf.Count, Is.EqualTo(3));

            buf.RemoveByOwner(1);
            That(buf.Count, Is.EqualTo(1));

            Span<PhaseListenerCollectedAction> actions = stackalloc PhaseListenerCollectedAction[8];
            int n = buf.Collect(20, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target, actions);
            That(n, Is.EqualTo(1));
            That(actions[0].GraphProgramId, Is.EqualTo(200));
        }

        [Test]
        public unsafe void ListenerBuffer_Capacity_Overflow()
        {
            var buf = new EffectPhaseListenerBuffer();
            for (int i = 0; i < EffectPhaseListenerBuffer.CAPACITY; i++)
            {
                That(buf.TryAdd(i, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                    PhaseListenerActionFlags.ExecuteGraph, i + 1, 0, i, i), Is.True);
            }
            // Overflow
            That(buf.TryAdd(99, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, 999, 0, 99, 99), Is.False);
            That(buf.Count, Is.EqualTo(EffectPhaseListenerBuffer.CAPACITY));
        }

        [Test]
        public void GlobalListenerRegistry_RegisterAndCollect()
        {
            var reg = new GlobalPhaseListenerRegistry();
            reg.Register(10, 0, EffectPhaseId.OnApply, PhaseListenerActionFlags.ExecuteGraph, 100, 0, 50);
            reg.Register(20, 0, EffectPhaseId.OnHit, PhaseListenerActionFlags.PublishEvent, 0, 200, 30);
            That(reg.Count, Is.EqualTo(2));

            Span<PhaseListenerCollectedAction> actions = stackalloc PhaseListenerCollectedAction[8];

            // Match phase=OnApply, tag=10
            int n = reg.Collect(EffectPhaseId.OnApply, 10, 0, actions);
            That(n, Is.EqualTo(1));
            That(actions[0].GraphProgramId, Is.EqualTo(100));

            // No match: wrong phase
            n = reg.Collect(EffectPhaseId.OnExpire, 10, 0, actions);
            That(n, Is.EqualTo(0));
        }

        [Test]
        public void GlobalListenerRegistry_Unregister()
        {
            var reg = new GlobalPhaseListenerRegistry();
            reg.Register(10, 0, EffectPhaseId.OnApply, PhaseListenerActionFlags.ExecuteGraph, 100, 0, 50);
            That(reg.Count, Is.EqualTo(1));

            That(reg.Unregister(10, 0, EffectPhaseId.OnApply), Is.True);
            That(reg.Count, Is.EqualTo(0));

            That(reg.Unregister(10, 0, EffectPhaseId.OnApply), Is.False);
        }

        [Test]
        public unsafe void TryAddTemplate_Roundtrip()
        {
            var setup = new EffectPhaseListenerBuffer();
            That(setup.TryAddTemplate(10, 42, EffectPhaseId.OnApply, PhaseListenerScope.Source,
                PhaseListenerActionFlags.Both, 100, 200, 50), Is.True);
            That(setup.Count, Is.EqualTo(1));
            That(setup.ListenCategoryIds[0], Is.EqualTo(10));
            That(setup.ListenEffectIds[0], Is.EqualTo(42));
            That(setup.Phases[0], Is.EqualTo((byte)EffectPhaseId.OnApply));
            That(setup.Scopes[0], Is.EqualTo((byte)PhaseListenerScope.Source));
            // TryAddTemplate sets OwnerEffectIds to 0
            That(setup.OwnerEffectIds[0], Is.EqualTo(0));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Module A: Executor Dispatch Integration
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ExecutorDispatch_FiresListenerGraph_OnTargetBuffer()
        {
            var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            // Register a graph program that writes I[0] = 42
            var programs = new GraphProgramRegistry();
            var prog = new GraphProgramBuffer();
            prog.Add((ushort)GraphNodeOp.ConstInt, dst: 0, imm: 42);
            int graphId = 1;
            programs.Register(graphId, ExtractInstructions(prog), GraphKind.Effect);

            // Register listener on target entity (scope=Target, phase=OnApply, tag=wildcard)
            var listenerBuf = new EffectPhaseListenerBuffer();
            listenerBuf.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, graphId, 0, 50, ownerEffectId: 1);
            world.Add(target, listenerBuf);

            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var eventBus = new GameplayEventBus();
            var globalReg = new GlobalPhaseListenerRegistry();

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates, globalReg, eventBus);

            var behavior = new EffectPhaseGraphBindings();
            var api = new GasGraphRuntimeApi(world, null, null, eventBus);
            var context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = target,
                TargetContext = default,
            };
            var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 2);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            // Execute OnApply with effectCategoryId=10 (non-zero to trigger dispatch)
            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, effectCategoryId: 10, effectTemplateId: 1);
            transaction.Commit();
            api.EndEffectSideEffectTransaction(transaction);

            // The listener graph ran — we verify indirectly by checking the event bus is empty
            // (listener only has ExecuteGraph flag, no PublishEvent)
            eventBus.Update();
            That(eventBus.Events.Count, Is.EqualTo(0));

            transaction.Dispose();
            world.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecutorDispatch_UnsupportedListenerGraph_FailsBeforeExecution(bool useGlobalListener)
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var programs = new GraphProgramRegistry();
            const int graphId = 31;
            programs.Register(graphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.BeginLifecycleTransaction },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            var globalListeners = new GlobalPhaseListenerRegistry();
            if (useGlobalListener)
            {
                That(globalListeners.Register(
                    listenCategoryId: 0,
                    listenEffectId: 0,
                    EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.ExecuteGraph,
                    graphId,
                    eventTagId: 0,
                    priority: 0), Is.True);
            }
            else
            {
                var listenerBuffer = new EffectPhaseListenerBuffer();
                That(listenerBuffer.TryAdd(
                    listenCategoryId: 0,
                    listenEffectId: 0,
                    EffectPhaseId.OnApply,
                    PhaseListenerScope.Target,
                    PhaseListenerActionFlags.ExecuteGraph,
                    graphId,
                    eventTagId: 0,
                    priority: 0,
                    ownerEffectId: 1), Is.True);
                world.Add(target, listenerBuffer);
            }

            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry(),
                globalListeners,
                new GameplayEventBus());
            var api = new GasGraphRuntimeApi(world);
            EffectPhaseGraphBindings behavior = default;

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    api,
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    in behavior,
                    EffectPresetType.None,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            That(error.Message, Does.StartWith(GraphKindOperationPolicy.ListenerOperationNotAllowedError));
        }

        [Test]
        public void ExecutorDispatch_ListenerInvokeBuiltinReportsMissingOwnerTemplateContext()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();
            const int graphId = 35;
            var programs = new GraphProgramRegistry();
            programs.Register(graphId,
            [
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.InvokeBuiltin,
                    Imm = (int)BuiltinHandlerId.ApplyModifiers,
                },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, graphId, 0, priority: 0, ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            EffectPhaseGraphBindings behavior = default;

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    new GasGraphRuntimeApi(world),
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    in behavior,
                    EffectPresetType.None,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            That(error.Message, Does.StartWith(GraphKindOperationPolicy.ListenerOperationNotAllowedError));
            That(error.Message, Does.Contain("owner EffectTemplate context"));
        }

        [Test]
        public void ListenerRegistration_ExecuteGraphWithoutGraphId_FailsClosed()
        {
            EffectPhaseListenerBuffer buffer = default;
            InvalidOperationException entityError = Throws<InvalidOperationException>(() =>
                buffer.TryAdd(
                    listenCategoryId: 0,
                    listenEffectId: 0,
                    EffectPhaseId.OnApply,
                    PhaseListenerScope.Target,
                    PhaseListenerActionFlags.Both,
                    graphProgramId: 0,
                    eventTagId: 10,
                    priority: 0,
                    ownerEffectId: 1))!;
            var globalListeners = new GlobalPhaseListenerRegistry();
            InvalidOperationException globalError = Throws<InvalidOperationException>(() =>
                globalListeners.Register(
                    listenCategoryId: 0,
                    listenEffectId: 0,
                    EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.Both,
                    graphProgramId: 0,
                    eventTagId: 10,
                    priority: 0))!;

            That(entityError.Message, Does.StartWith("GAS.PHASE_LISTENER.ERR.InvalidRegistration"));
            That(globalError.Message, Does.StartWith("GAS.PHASE_LISTENER.ERR.InvalidRegistration"));
            That(buffer.Count, Is.EqualTo(0));
            That(globalListeners.Count, Is.EqualTo(0));
        }

        [TestCase(PhaseListenerActionFlags.PublishEvent, -1, 10)]
        [TestCase(PhaseListenerActionFlags.ExecuteGraph, 10, -1)]
        public void ListenerRegistration_UnusedIdsMustBeExactlyZero(
            PhaseListenerActionFlags flags,
            int graphProgramId,
            int eventTagId)
        {
            EffectPhaseListenerBuffer buffer = default;
            var globalListeners = new GlobalPhaseListenerRegistry();

            InvalidOperationException entityError = Throws<InvalidOperationException>(() =>
                buffer.TryAdd(
                    0,
                    0,
                    EffectPhaseId.OnApply,
                    PhaseListenerScope.Target,
                    flags,
                    graphProgramId,
                    eventTagId,
                    priority: 0,
                    ownerEffectId: 1))!;
            InvalidOperationException globalError = Throws<InvalidOperationException>(() =>
                globalListeners.Register(
                    0,
                    0,
                    EffectPhaseId.OnApply,
                    flags,
                    graphProgramId,
                    eventTagId,
                    priority: 0))!;

            That(entityError.Message, Does.StartWith(EffectPhaseListenerContract.InvalidRegistrationError));
            That(globalError.Message, Does.StartWith(EffectPhaseListenerContract.InvalidRegistrationError));
        }

        [Test]
        public void ListenerRegistration_FullCapacityDoesNotMaskInvalidInput()
        {
            EffectPhaseListenerBuffer buffer = default;
            for (int i = 0; i < EffectPhaseListenerBuffer.CAPACITY; i++)
            {
                That(buffer.TryAdd(
                    i,
                    0,
                    EffectPhaseId.OnApply,
                    PhaseListenerScope.Target,
                    PhaseListenerActionFlags.ExecuteGraph,
                    graphProgramId: i + 1,
                    eventTagId: 0,
                    priority: 0,
                    ownerEffectId: 1), Is.True);
            }

            var globalListeners = new GlobalPhaseListenerRegistry();
            for (int i = 0; i < GlobalPhaseListenerRegistry.MAX_LISTENERS; i++)
            {
                That(globalListeners.Register(
                    i,
                    0,
                    EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.ExecuteGraph,
                    graphProgramId: i + 1,
                    eventTagId: 0,
                    priority: 0), Is.True);
            }

            InvalidOperationException entityError = Throws<InvalidOperationException>(() =>
                buffer.TryAdd(
                    0,
                    0,
                    EffectPhaseId.OnApply,
                    PhaseListenerScope.Target,
                    PhaseListenerActionFlags.ExecuteGraph,
                    graphProgramId: 0,
                    eventTagId: 0,
                    priority: 0,
                    ownerEffectId: 1))!;
            InvalidOperationException globalError = Throws<InvalidOperationException>(() =>
                globalListeners.Register(
                    0,
                    0,
                    EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.ExecuteGraph,
                    graphProgramId: 0,
                    eventTagId: 0,
                    priority: 0))!;

            That(entityError.Message, Does.StartWith(EffectPhaseListenerContract.InvalidRegistrationError));
            That(globalError.Message, Does.StartWith(EffectPhaseListenerContract.InvalidRegistrationError));
        }

        [TestCase(EffectPhaseId.OnPropose)]
        [TestCase(EffectPhaseId.OnCalculate)]
        public void ListenerRegistration_PurePhaseEvent_FailsClosed(EffectPhaseId phase)
        {
            EffectPhaseListenerBuffer buffer = default;
            InvalidOperationException entityError = Throws<InvalidOperationException>(() =>
                buffer.TryAdd(
                    listenCategoryId: 0,
                    listenEffectId: 0,
                    phase,
                    PhaseListenerScope.Target,
                    PhaseListenerActionFlags.PublishEvent,
                    graphProgramId: 0,
                    eventTagId: 10,
                    priority: 0,
                    ownerEffectId: 1))!;
            var globalListeners = new GlobalPhaseListenerRegistry();
            InvalidOperationException globalError = Throws<InvalidOperationException>(() =>
                globalListeners.Register(
                    listenCategoryId: 0,
                    listenEffectId: 0,
                    phase,
                    PhaseListenerActionFlags.PublishEvent,
                    graphProgramId: 0,
                    eventTagId: 10,
                    priority: 0))!;

            That(entityError.Message, Does.Contain("pure phase"));
            That(globalError.Message, Does.Contain("pure phase"));
            That(buffer.Count, Is.EqualTo(0));
            That(globalListeners.Count, Is.EqualTo(0));
        }

        [Test]
        public void ExecutorDispatch_EventListenerWithoutEventBus_FailsBeforeDispatch()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                listenCategoryId: 0,
                listenEffectId: 0,
                EffectPhaseId.OnApply,
                PhaseListenerScope.Target,
                PhaseListenerActionFlags.PublishEvent,
                graphProgramId: 0,
                eventTagId: 10,
                priority: 0,
                ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            EffectPhaseGraphBindings behavior = default;

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    new GasGraphRuntimeApi(world),
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    in behavior,
                    EffectPresetType.None,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            That(error.Message, Does.StartWith(EffectPhaseExecutor.MissingListenerEventBusError));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecutorDispatch_EventOnlyBatchCapacityFailureLeavesNoPartialEvent(bool useGlobalListener)
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();
            var eventBus = new GameplayEventBus();
            for (int i = 0; i < eventBus.Capacity - 1; i++)
            {
                eventBus.Publish(new GameplayEvent { TagId = 1, Source = caster, Target = target });
            }

            var globalListeners = new GlobalPhaseListenerRegistry();
            if (useGlobalListener)
            {
                That(globalListeners.Register(
                    0, 0, EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.PublishEvent, 0, 501, priority: 10), Is.True);
                That(globalListeners.Register(
                    0, 0, EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.PublishEvent, 0, 502, priority: 0), Is.True);
            }
            else
            {
                EffectPhaseListenerBuffer listeners = default;
                That(listeners.TryAdd(
                    0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                    PhaseListenerActionFlags.PublishEvent, 0, 501, priority: 10, ownerEffectId: 1), Is.True);
                That(listeners.TryAdd(
                    0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                    PhaseListenerActionFlags.PublishEvent, 0, 502, priority: 0, ownerEffectId: 1), Is.True);
                world.Add(target, listeners);
            }

            var executor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry(),
                globalListeners,
                eventBus);
            var api = new GasGraphRuntimeApi(world, eventBus: eventBus);
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 2);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.DispatchPhaseListeners(
                    world,
                    api,
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            That(error.Message, Does.StartWith(EffectPhaseSideEffectTransaction.CapacityExceededError));
            That(eventBus.AvailableNextCapacity, Is.EqualTo(1));
            api.EndEffectSideEffectTransaction(transaction);
            transaction.Rollback();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecutorDispatch_InvalidLaterGraphLeavesNoEarlierEvent(bool useGlobalListener)
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();
            const int missingGraphId = 987;
            const int eventTagId = 654;
            var globalListeners = new GlobalPhaseListenerRegistry();
            if (useGlobalListener)
            {
                That(globalListeners.Register(
                    0, 0, EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.PublishEvent, 0, eventTagId, priority: 100), Is.True);
                That(globalListeners.Register(
                    0, 0, EffectPhaseId.OnApply,
                    PhaseListenerActionFlags.ExecuteGraph, missingGraphId, 0, priority: 0), Is.True);
            }
            else
            {
                EffectPhaseListenerBuffer listeners = default;
                That(listeners.TryAdd(
                    0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                    PhaseListenerActionFlags.PublishEvent, 0, eventTagId, priority: 100, ownerEffectId: 1), Is.True);
                That(listeners.TryAdd(
                    0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                    PhaseListenerActionFlags.ExecuteGraph, missingGraphId, 0, priority: 0, ownerEffectId: 1), Is.True);
                world.Add(target, listeners);
            }
            var eventBus = new GameplayEventBus();
            var executor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry(),
                globalListeners,
                eventBus);

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.DispatchPhaseListeners(
                    world,
                    new GasGraphRuntimeApi(world, eventBus: eventBus),
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            eventBus.Update();
            That(error.Message, Does.Contain($"graphId={missingGraphId}"));
            That(eventBus.Events.Count, Is.Zero);
        }

        [Test]
        public void ExecutorDispatch_ReusedGraphIdWithInvalidRegisterFailsBeforeEarlierEvent()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();
            const int graphId = 990;
            const int eventTagId = 991;
            var programs = new GraphProgramRegistry();
            programs.Register(graphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.PublishEvent, 0, eventTagId, priority: 100, ownerEffectId: 1), Is.True);
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, graphId, 0, priority: 0, ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var eventBus = new GameplayEventBus();
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry(),
                eventBus: eventBus);
            var api = new GasGraphRuntimeApi(world, eventBus: eventBus);
            EffectPhaseGraphBindings behavior = default;
            using (var transaction = new EffectPhaseSideEffectTransaction(
                       world,
                       tagOps: null,
                       effectRequests: null,
                       spawnRequests: null,
                       presentationEvents: null,
                       attributeEntityCapacity: 2))
            {
                transaction.Begin();
                api.BeginEffectSideEffectTransaction(transaction);
                executor.ExecutePhase(
                    world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None,
                    effectCategoryId: 1, effectTemplateId: 1);
                transaction.Commit();
                api.EndEffectSideEffectTransaction(transaction);
            }
            eventBus.Update();
            That(eventBus.Events.Count, Is.EqualTo(1));
            eventBus.Update();

            programs.Clear();
            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                programs.Register(graphId,
            [
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.ConstFloat,
                    Dst = GraphVmLimits.MaxFloatRegisters,
                    ImmF = 1f,
                },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect))!;

            eventBus.Update();
            That(error.Message, Does.StartWith(GraphKindOperationPolicy.RegisterOutOfRangeError));
            That(error.Message, Does.Contain("operand=Dst"));
            That(error.Message, Does.Contain($"registerIndex={GraphVmLimits.MaxFloatRegisters}"));
            That(eventBus.Events.Count, Is.Zero);
        }

        [Test]
        public void ExecutorDispatch_NonPureListenerGraphRequiresTransactionBeforePhaseWrite()
        {
            using var world = World.Create();
            int healthId = AttributeRegistry.Register("Test.ListenerTransactionRequired.Health");
            Entity caster = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(healthId, 100f);
            const int phaseGraphId = 992;
            const int listenerGraphId = 993;
            var programs = new GraphProgramRegistry();
            programs.Register(phaseGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = -25f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 0, Imm = healthId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            programs.Register(listenerGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            EffectPhaseGraphBindings behavior = default;
            That(behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, phaseGraphId), Is.True);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, listenerGraphId, 0, priority: 0, ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    new GasGraphRuntimeApi(world, tagOps: _tagOps),
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    in behavior,
                    EffectPresetType.None,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            That(error.Message, Does.StartWith(EffectPhaseExecutor.ListenerTransactionRequiredError));
            That(world.Get<AttributeBuffer>(target).GetCurrent(healthId), Is.EqualTo(100f));
        }

        [Test]
        public void ExecutorDispatch_ListenerSendEventRequiresGraphRuntimeBusBeforePhaseWrite()
        {
            using var world = World.Create();
            int healthId = AttributeRegistry.Register("Test.ListenerGraphEventBus.Health");
            Entity caster = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(healthId, 100f);
            const int phaseGraphId = 994;
            const int listenerGraphId = 995;
            var programs = new GraphProgramRegistry();
            programs.Register(phaseGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = -25f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 0, Imm = healthId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            programs.Register(listenerGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            EffectPhaseGraphBindings behavior = default;
            That(behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, phaseGraphId), Is.True);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, listenerGraphId, 0, priority: 0, ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry(),
                eventBus: new GameplayEventBus());
            var api = new GasGraphRuntimeApi(world, tagOps: _tagOps);
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: _tagOps,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 2);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world, api, caster, target, default, default,
                    EffectPhaseId.OnApply, in behavior, EffectPresetType.None,
                    effectCategoryId: 1, effectTemplateId: 1))!;
            api.EndEffectSideEffectTransaction(transaction);
            transaction.Rollback();

            That(error.Message, Does.Contain(EffectPhaseExecutor.MissingListenerEventBusError));
            That(world.Get<AttributeBuffer>(target).GetCurrent(healthId), Is.EqualTo(100f));
        }

        [Test]
        public void ExecutorPhase_InvalidListenerFailsBeforePhaseGraphWrites()
        {
            using var world = World.Create();
            int healthId = AttributeRegistry.Register("Test.ListenerPreflight.Health");
            Entity caster = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(healthId, 100f);
            const int phaseGraphId = 988;
            const int missingListenerGraphId = 989;
            var programs = new GraphProgramRegistry();
            programs.Register(phaseGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = -25f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 0, Imm = healthId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            EffectPhaseGraphBindings behavior = default;
            That(behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Pre, phaseGraphId), Is.True);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, missingListenerGraphId, 0, priority: 0, ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());

            Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    new GasGraphRuntimeApi(world, tagOps: _tagOps),
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    in behavior,
                    EffectPresetType.None,
                    effectCategoryId: 1,
                    effectTemplateId: 1));

            That(world.Get<AttributeBuffer>(target).GetCurrent(healthId), Is.EqualTo(100f));
        }

        [Test]
        public void Executor_OnProposeRequiresValidationEntryPoint()
        {
            using var world = World.Create();
            var executor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            EffectPhaseGraphBindings behavior = default;

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    new GasGraphRuntimeApi(world),
                    world.Create(),
                    world.Create(),
                    default,
                    default,
                    EffectPhaseId.OnPropose,
                    in behavior,
                    EffectPresetType.None))!;

            That(error.Message, Does.StartWith("GAS.EFFECT_PHASE.ERR.ValidationEntryPointRequired"));
        }

        [Test]
        public void Executor_OnProposeValidationListener_UsesValidationGraphKind()
        {
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            const int graphId = 32;
            var programs = new GraphProgramRegistry();
            programs.Register(graphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Validation);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                listenCategoryId: 0,
                listenEffectId: 0,
                EffectPhaseId.OnPropose,
                PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph,
                graphId,
                eventTagId: 0,
                priority: 0,
                ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            EffectPhaseGraphBindings behavior = default;
            EffectConfigParams mergedParams = default;

            bool accepted = executor.ExecutePhaseWithValidationResult(
                world,
                new GasGraphRuntimeApi(world),
                caster,
                target,
                default,
                default,
                EffectPhaseId.OnPropose,
                in behavior,
                EffectPresetType.None,
                effectCategoryId: 1,
                effectTemplateId: 1,
                in mergedParams);

            That(accepted, Is.True);
        }

        [Test]
        public void Registry_EmptyValidationListenerGraph_FailsClosed()
        {
            const int graphId = 33;
            var programs = new GraphProgramRegistry();

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                programs.Register(graphId, Array.Empty<GraphInstruction>(), GraphKind.Validation))!;

            That(error.Message, Does.StartWith(GraphKindOperationPolicy.MissingHaltError));
            That(error.Message, Does.Contain("GraphProgramRegistry"));
            That(error.Message, Does.Contain("graphId=33"));
        }

        [Test]
        public void Executor_OnProposeListenerRejectionCannotBeOverwrittenByLaterPass()
        {
            using var world = World.Create();
            Entity caster = world.Create();
            Entity target = world.Create();
            const int rejectGraphId = 36;
            const int passGraphId = 37;
            var programs = new GraphProgramRegistry();
            programs.Register(rejectGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Validation);
            programs.Register(passGraphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Validation);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnPropose, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, rejectGraphId, 0, priority: 100, ownerEffectId: 1), Is.True);
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnPropose, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, passGraphId, 0, priority: 0, ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            EffectPhaseGraphBindings behavior = default;
            EffectConfigParams mergedParams = default;

            bool accepted = executor.ExecutePhaseWithValidationResult(
                world,
                new GasGraphRuntimeApi(world),
                caster,
                target,
                default,
                default,
                EffectPhaseId.OnPropose,
                in behavior,
                EffectPresetType.None,
                effectCategoryId: 1,
                effectTemplateId: 1,
                in mergedParams);

            That(accepted, Is.False);
        }

        [Test]
        public void Executor_OnCalculateListenerWriteFailsBeforeExecution()
        {
            using var world = World.Create();
            int healthId = AttributeRegistry.Register("Test.OnCalculateListener.Health");
            Entity caster = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            world.Get<AttributeBuffer>(target).SetBase(healthId, 100f);
            const int graphId = 38;
            var programs = new GraphProgramRegistry();
            programs.Register(graphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = -25f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 0, Imm = healthId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            ], GraphKind.Effect);
            EffectPhaseListenerBuffer listeners = default;
            That(listeners.TryAdd(
                0, 0, EffectPhaseId.OnCalculate, PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph, graphId, 0, priority: 0, ownerEffectId: 1), Is.True);
            world.Add(target, listeners);
            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            EffectPhaseGraphBindings behavior = default;

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    new GasGraphRuntimeApi(world, tagOps: new TagOps(new DirtyEntityQueue(8), new TagRuleRegistry())),
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnCalculate,
                    in behavior,
                    EffectPresetType.None,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            That(error.Message, Does.StartWith(GraphKindOperationPolicy.ListenerOperationNotAllowedError));
            That(world.Get<AttributeBuffer>(target).GetCurrent(healthId), Is.EqualTo(100f));
        }

        [Test]
        public void ExecutorDispatch_ListenerGraphBuiltin_IsRejectedByListenerOperationPolicy()
        {
            // Main's GraphKindOperationPolicy bans InvokeBuiltin in listener graphs because listener
            // execution carries no owner EffectTemplate context; the PR-era "listener builtin receives
            // effect context" contract was retired with that policy decision.
            using var world = World.Create();
            var caster = world.Create();
            var target = world.Create();
            var programs = new GraphProgramRegistry();
            const int graphId = 2;
            programs.Register(graphId,
            [
                new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeBuiltin, Imm = (int)BuiltinHandlerId.ApplyModifiers },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            ], GraphKind.Effect);
            var listenerBuffer = new EffectPhaseListenerBuffer();
            That(listenerBuffer.TryAdd(
                listenCategoryId: 0,
                listenEffectId: 0,
                EffectPhaseId.OnApply,
                PhaseListenerScope.Target,
                PhaseListenerActionFlags.ExecuteGraph,
                graphId,
                eventTagId: 0,
                priority: 0,
                ownerEffectId: 1), Is.True);
            world.Add(target, listenerBuffer);

            var executor = new EffectPhaseExecutor(
                programs,
                new PresetTypeRegistry(),
                new BuiltinHandlerRegistry(),
                GasGraphOpHandlerTable.Instance,
                new EffectTemplateRegistry());
            var api = new GasGraphRuntimeApi(world);
            EffectPhaseGraphBindings behavior = default;

            InvalidOperationException error = Throws<InvalidOperationException>(() =>
                executor.ExecutePhase(
                    world,
                    api,
                    caster,
                    target,
                    default,
                    default,
                    EffectPhaseId.OnApply,
                    in behavior,
                    EffectPresetType.None,
                    effectCategoryId: 1,
                    effectTemplateId: 1))!;

            That(error.Message, Does.StartWith(GraphKindOperationPolicy.ListenerOperationNotAllowedError));
        }
        [Test]
        public void ExecutorDispatch_PublishesEvent_OnCasterBuffer()
        {
            var world = World.Create();
            var caster = world.Create();
            var target = world.Create();

            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var eventBus = new GameplayEventBus();
            var globalReg = new GlobalPhaseListenerRegistry();

            int eventTag = 999;
            // Register listener on caster entity (scope=Source)
            var listenerBuf = new EffectPhaseListenerBuffer();
            listenerBuf.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Source,
                PhaseListenerActionFlags.PublishEvent, 0, eventTag, 50, ownerEffectId: 1);
            world.Add(caster, listenerBuf);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates, globalReg, eventBus);

            var behavior = new EffectPhaseGraphBindings();
            var api = new GasGraphRuntimeApi(world, null, null, eventBus);
            var context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = target,
                TargetContext = default,
            };
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 2);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, effectCategoryId: 10, effectTemplateId: 1);
            transaction.Commit();
            api.EndEffectSideEffectTransaction(transaction);

            eventBus.Update();
            That(eventBus.Events.Count, Is.EqualTo(1));

            world.Dispose();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Stress Tests
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Stress_FanOutApplyEffect_1000Targets()
        {
            var world = World.Create();
            var caster = world.Create();
            var targetEntities = new Entity[1000];
            for (int i = 0; i < 1000; i++) targetEntities[i] = world.Create();

            var requestQueue = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, null, null, null, requestQueue);

            var fRegs = new float[16];
            var iRegs = new int[16];
            var bRegs = new byte[16];
            var eRegs = new Entity[16];
            eRegs[0] = caster;

            var targetBuffer = new Entity[1024];
            Array.Copy(targetEntities, targetBuffer, 1000);
            var targetList = new GraphTargetList(targetBuffer);
            targetList.SetCount(1000);

            var state = new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = caster,
                TargetPosCm = default,
                Api = api,
                F = fRegs,
                I = iRegs,
                B = bRegs,
                E = eRegs,
                Targets = targetBuffer,
                TargetList = targetList,
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };

            var program = new GraphProgramBuffer();
            program.Add((ushort)GraphNodeOp.FanOutApplyEffect, imm: 1);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var instructions = ExtractInstructions(program);
            GasGraphOpHandlerTable.Execute(ref state, instructions, GasGraphOpHandlerTable.Instance);
            sw.Stop();

            That(requestQueue.Count, Is.EqualTo(1000));
            Console.WriteLine($"[Stress] FanOutApplyEffect 1000 targets: {sw.Elapsed.TotalMilliseconds:F2}ms");

            world.Dispose();
        }

        [Test]
        public void Stress_ListenerDispatch_500Entities_8Phases()
        {
            var world = World.Create();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var eventBus = new GameplayEventBus();
            var globalReg = new GlobalPhaseListenerRegistry();

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates, globalReg, eventBus);
            var api = new GasGraphRuntimeApi(world, null, null, eventBus);

            var caster = world.Create();
            var targets = new Entity[500];
            for (int i = 0; i < 500; i++)
            {
                targets[i] = world.Create();
                var buf = new EffectPhaseListenerBuffer();
                buf.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                    PhaseListenerActionFlags.PublishEvent, 0, 1, 0, 1);
                world.Add(targets[i], buf);
            }

            var behavior = new EffectPhaseGraphBindings();
            EffectConfigParams mergedParams = default;
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 1);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int phase = 0; phase < 8; phase++)
            {
                for (int i = 0; i < 500; i++)
                {
                    if (phase == (int)EffectPhaseId.OnPropose)
                    {
                        _ = executor.ExecutePhaseWithValidationResult(
                            world,
                            api,
                            caster,
                            targets[i],
                            default,
                            default,
                            EffectPhaseId.OnPropose,
                            in behavior,
                            EffectPresetType.None,
                            effectCategoryId: 1,
                            effectTemplateId: 1,
                            in mergedParams);
                    }
                    else
                    {
                        executor.ExecutePhase(world, api, caster, targets[i], default, default,
                            (EffectPhaseId)phase, in behavior, EffectPresetType.None,
                            effectCategoryId: 1, effectTemplateId: 1);
                    }
                }
            }
            sw.Stop();
            transaction.Commit();
            api.EndEffectSideEffectTransaction(transaction);

            // Only phase OnApply(4) should trigger → 500 events
            eventBus.Update();
            That(eventBus.Events.Count, Is.EqualTo(500));
            Console.WriteLine($"[Stress] 500 entities x 8 phases listener dispatch: {sw.Elapsed.TotalMilliseconds:F2}ms");

            world.Dispose();
        }

        // ════════════════════════════════════════════════════════════════════
        //  MUD: AOE Fireball + Searing Chain + Flame Spread
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void MUD_AOEFireball_SearingChain_Scenario()
        {
            // Setup
            var world = World.Create();
            var caster = world.Create();
            var victim1 = world.Create();
            var victim2 = world.Create();
            var victim3 = world.Create();

            var requestQueue = new EffectRequestQueue();
            var eventBus = new GameplayEventBus();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var templates = new EffectTemplateRegistry();
            var globalReg = new GlobalPhaseListenerRegistry();

            // Graph.SearingChainBonus: publishes event so we can detect it fired
            var bonusProg = new GraphProgramBuffer();
            bonusProg.Add((ushort)GraphNodeOp.LoadCaster, dst: 0);              // E[0] = caster
            bonusProg.Add((ushort)GraphNodeOp.LoadExplicitTarget, dst: 1);      // E[1] = target
            bonusProg.Add((ushort)GraphNodeOp.ConstFloat, dst: 0, immF: 50f);   // F[0] = 50 (bonus damage)
            bonusProg.Add((ushort)GraphNodeOp.SendEvent, a: 1, imm: 777, b: 0); // SendEvent(target, tag=777, F[0])
            int bonusGraphId = 1;
            programs.Register(bonusGraphId, ExtractInstructions(bonusProg), GraphKind.Effect);

            // Register "Searing Chain" listener on caster (scope=Source, phase=OnApply)
            var casterBuf = new EffectPhaseListenerBuffer();
            casterBuf.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Source,
                PhaseListenerActionFlags.Both, bonusGraphId, 888, 100, ownerEffectId: 1);
            world.Add(caster, casterBuf);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates, globalReg, eventBus);
            var api = new GasGraphRuntimeApi(world, null, null, eventBus, requestQueue);
            var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: requestQueue,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 4);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            // Simulate: caster's Fireball.Hit applies to victim1, victim2, victim3
            var behavior = new EffectPhaseGraphBindings();
            int fireballHitTag = 10;
            int fireballHitTemplate = 42;
            var victim1Context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = victim1,
                TargetContext = default,
            };
            var victim2Context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = victim2,
                TargetContext = default,
            };
            var victim3Context = new EffectContext
            {
                RootId = 0,
                Source = caster,
                Target = victim3,
                TargetContext = default,
            };

            executor.ExecutePhase(world, api, victim1Context.Source, victim1Context.Target, victim1Context.TargetContext, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, fireballHitTag, fireballHitTemplate);
            executor.ExecutePhase(world, api, victim2Context.Source, victim2Context.Target, victim2Context.TargetContext, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, fireballHitTag, fireballHitTemplate);
            executor.ExecutePhase(world, api, victim3Context.Source, victim3Context.Target, victim3Context.TargetContext, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, fireballHitTag, fireballHitTemplate);
            transaction.Commit();
            api.EndEffectSideEffectTransaction(transaction);

            // Verify: 3 bonus graph events (tag 777) + 3 listener events (tag 888) = 6 total events
            eventBus.Update();
            That(eventBus.Events.Count, Is.EqualTo(6));

            Console.WriteLine("[MUD] AOE Fireball + Searing Chain: 3 hits → 6 events (3 graph + 3 listener) ✓");

            transaction.Dispose();
            world.Dispose();
        }

        [Test]
        public void MUD_ListenerLifecycle_RegisterAndUnregister()
        {
            var world = World.Create();
            var target = world.Create();

            // Simulate OnApply registering a listener via TryAddTemplate (compile-time setup)
            var templateSetup = new EffectPhaseListenerBuffer();
            templateSetup.TryAddTemplate(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.PublishEvent, 0, 500, 10);

            // Manually register (simulating what EffectApplicationSystem does at runtime)
            if (!world.Has<EffectPhaseListenerBuffer>(target))
                world.Add(target, new EffectPhaseListenerBuffer());

            ref var buf = ref world.Get<EffectPhaseListenerBuffer>(target);
            unsafe
            {
                buf.TryAdd(templateSetup.ListenCategoryIds[0], templateSetup.ListenEffectIds[0],
                    (EffectPhaseId)templateSetup.Phases[0], (PhaseListenerScope)templateSetup.Scopes[0],
                    (PhaseListenerActionFlags)templateSetup.ActionFlags[0],
                    templateSetup.GraphProgramIds[0], templateSetup.EventTagIds[0], templateSetup.Priorities[0],
                    ownerEffectId: 99);
            }
            That(buf.Count, Is.EqualTo(1));

            // Simulate OnRemove unregistering
            buf.RemoveByOwner(99);
            That(buf.Count, Is.EqualTo(0));

            Console.WriteLine("[MUD] Listener lifecycle: register on OnApply, unregister on OnRemove ✓");

            world.Dispose();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════

        // Helper fields for test state — ref struct cannot be returned from methods.
        private float[] _testFloatRegs = Array.Empty<float>();
        private int[] _testIntRegs = Array.Empty<int>();
        private byte[] _testBoolRegs = Array.Empty<byte>();
        private Entity[] _testEntityRegs = Array.Empty<Entity>();
        private Entity[] _testTargetBuffer = Array.Empty<Entity>();

        private GraphExecutionState SetupExecution(
            World world, Entity caster, Entity target, GraphProgramBuffer program,
            IGraphRuntimeApi api = null)
        {
            _testFloatRegs = new float[16];
            _testIntRegs = new int[16];
            _testBoolRegs = new byte[16];
            _testEntityRegs = new Entity[16];
            _testTargetBuffer = new Entity[64];

            _testEntityRegs[0] = caster;
            _testEntityRegs[1] = target;

            api ??= new GasGraphRuntimeApi(world, null, null, null);

            var targetList = new GraphTargetList(_testTargetBuffer);

            return new GraphExecutionState
            {
                World = world,
                Caster = caster,
                ExplicitTarget = target,
                TargetPosCm = default,
                Api = api,
                F = _testFloatRegs,
                I = _testIntRegs,
                B = _testBoolRegs,
                E = _testEntityRegs,
                Targets = _testTargetBuffer,
                TargetList = targetList,
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };
        }

        private static GraphInstruction[] ExtractInstructions(GraphProgramBuffer program)
        {
            var hasHalt = program.Count > 0 &&
                program.Get(program.Count - 1).Op == (ushort)GraphNodeOp.HaltReturnInt;
            var instructions = new GraphInstruction[program.Count + (hasHalt ? 0 : 1)];
            for (int i = 0; i < program.Count; i++)
                instructions[i] = program.Get(i);
            if (!hasHalt)
            {
                instructions[^1] = new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt };
            }
            return instructions;
        }
    }
}
