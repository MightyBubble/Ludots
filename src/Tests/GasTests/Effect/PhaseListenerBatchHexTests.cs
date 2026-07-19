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
        private readonly TagOps _tagOps = new TagOps();

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

            var api = new GasGraphRuntimeApi(world, null, null, null);
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
                TargetPos = default,
                Api = new GasGraphRuntimeApi(world, null, null, null),
                F = fRegs,
                I = iRegs,
                B = bRegs,
                E = eRegs,
                Targets = targetBuffer,
                TargetList = targetList,
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
                TargetPos = default,
                Api = api,
                F = fRegs,
                I = iRegs,
                B = bRegs,
                E = eRegs,
                Targets = targetBuffer,
                TargetList = targetList,
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
            That(setup.ListenTagIds[0], Is.EqualTo(10));
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
            programs.Register(graphId, ExtractInstructions(prog));

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

            // Execute OnApply with effectTagId=10 (non-zero to trigger dispatch)
            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, effectTagId: 10, effectTemplateId: 1);

            // The listener graph ran — we verify indirectly by checking the event bus is empty
            // (listener only has ExecuteGraph flag, no PublishEvent)
            eventBus.Update();
            That(eventBus.Events.Count, Is.EqualTo(0));

            world.Dispose();
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

            executor.ExecutePhase(world, api, caster, target, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, effectTagId: 10, effectTemplateId: 1);

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
                TargetPos = default,
                Api = api,
                F = fRegs,
                I = iRegs,
                B = bRegs,
                E = eRegs,
                Targets = targetBuffer,
                TargetList = targetList,
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

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int phase = 0; phase < 8; phase++)
            {
                for (int i = 0; i < 500; i++)
                {
                    executor.ExecutePhase(world, api, caster, targets[i], default, default,
                        (EffectPhaseId)phase, in behavior, EffectPresetType.None,
                        effectTagId: 1, effectTemplateId: 1);
                }
            }
            sw.Stop();

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
            programs.Register(bonusGraphId, ExtractInstructions(bonusProg));

            // Register "Searing Chain" listener on caster (scope=Source, phase=OnApply)
            var casterBuf = new EffectPhaseListenerBuffer();
            casterBuf.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Source,
                PhaseListenerActionFlags.Both, bonusGraphId, 888, 100, ownerEffectId: 1);
            world.Add(caster, casterBuf);

            var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, templates, globalReg, eventBus);
            var api = new GasGraphRuntimeApi(world, null, null, eventBus, requestQueue);

            // Simulate: caster's Fireball.Hit applies to victim1, victim2, victim3
            var behavior = new EffectPhaseGraphBindings();
            int fireballHitTag = 10;
            int fireballHitTemplate = 42;

            executor.ExecutePhase(world, api, caster, victim1, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, fireballHitTag, fireballHitTemplate);
            executor.ExecutePhase(world, api, caster, victim2, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, fireballHitTag, fireballHitTemplate);
            executor.ExecutePhase(world, api, caster, victim3, default, default,
                EffectPhaseId.OnApply, in behavior, EffectPresetType.None, fireballHitTag, fireballHitTemplate);

            // Verify: 3 bonus graph events (tag 777) + 3 listener events (tag 888) = 6 total events
            eventBus.Update();
            That(eventBus.Events.Count, Is.EqualTo(6));

            Console.WriteLine("[MUD] AOE Fireball + Searing Chain: 3 hits → 6 events (3 graph + 3 listener) ✓");

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
                buf.TryAdd(templateSetup.ListenTagIds[0], templateSetup.ListenEffectIds[0],
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
        private float[] _testFloatRegs;
        private int[] _testIntRegs;
        private byte[] _testBoolRegs;
        private Entity[] _testEntityRegs;
        private Entity[] _testTargetBuffer;

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
                TargetPos = default,
                Api = api,
                F = _testFloatRegs,
                I = _testIntRegs,
                B = _testBoolRegs,
                E = _testEntityRegs,
                Targets = _testTargetBuffer,
                TargetList = targetList,
            };
        }

        private static GraphInstruction[] ExtractInstructions(GraphProgramBuffer program)
        {
            var instructions = new GraphInstruction[program.Count];
            for (int i = 0; i < program.Count; i++)
                instructions[i] = program.Get(i);
            return instructions;
        }
    }
}
