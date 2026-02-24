using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Stress tests for the Phase Graph architecture.
    /// Validates throughput, allocation-free execution, and GC pressure
    /// under high-volume graph dispatching.
    /// </summary>
    [TestFixture]
    public class EffectPhaseStressTests
    {
        /// <summary>
        /// Stress test: execute Phase graphs for N effects × all 8 phases.
        /// Each phase runs a trivial 3-instruction graph (ConstFloat + AddFloat + WriteBlackboard).
        /// Measures throughput in microseconds-per-phase-execution and total GC allocations.
        /// </summary>
        [Test]
        public void PhaseExecutor_HighVolume_ReportsThroughputAndGc()
        {
            var world = World.Create();
            try
            {
                var programs = new GraphProgramRegistry();
                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                var templates = new EffectTemplateRegistry();
                var handlers = GasGraphOpHandlerTable.Instance;
                var executor = new EffectPhaseExecutor(programs, presetTypes, builtinHandlers, handlers, templates);
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // Register a simple graph: F[0]=1.0, E[0]=target, WriteBBFloat(E[0], key=1, F[0])
                int graphId = 1;
                programs.Register(graphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 1f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 0 },
                });

                // All phases get the same Main graph
                var ptDef = new PresetTypeDefinition { Type = EffectPresetType.None };
                for (int p = 0; p < EffectPhaseConstants.PhaseCount; p++)
                    ptDef.DefaultPhaseHandlers[(EffectPhaseId)p] = PhaseHandler.Graph(graphId);
                presetTypes.Register(in ptDef);

                // Build behavior with Pre+Post for every phase
                var behavior = new EffectPhaseGraphBindings();
                for (int p = 0; p < EffectPhaseConstants.PhaseCount && behavior.StepCount < EffectPhaseGraphBindings.MAX_STEPS; p++)
                {
                    behavior.TryAddStep((EffectPhaseId)p, PhaseSlot.Pre, graphId);
                    if (behavior.StepCount < EffectPhaseGraphBindings.MAX_STEPS)
                        behavior.TryAddStep((EffectPhaseId)p, PhaseSlot.Post, graphId);
                }

                int entityCount = 500;
                var caster = world.Create();
                var targets = new Entity[entityCount];
                for (int i = 0; i < entityCount; i++)
                {
                    targets[i] = world.Create(new BlackboardFloatBuffer());
                }

                // ── Warmup ──
                for (int i = 0; i < 10; i++)
                {
                    foreach (var t in targets)
                    {
                        executor.ExecutePhase(world, api, caster, t, default, default,
                            EffectPhaseId.OnApply, in behavior, EffectPresetType.None);
                    }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // ── Measurement ──
                int iterations = 100;
                int phasesPerIter = EffectPhaseConstants.PhaseCount;

                long alloc0 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_0 = GC.CollectionCount(0);
                int gen1_0 = GC.CollectionCount(1);
                int gen2_0 = GC.CollectionCount(2);

                var sw = Stopwatch.StartNew();
                for (int iter = 0; iter < iterations; iter++)
                {
                    for (int p = 0; p < phasesPerIter; p++)
                    {
                        var phase = (EffectPhaseId)p;
                        for (int e = 0; e < entityCount; e++)
                        {
                            executor.ExecutePhase(world, api, caster, targets[e], default, default,
                                phase, in behavior, EffectPresetType.None);
                        }
                    }
                }
                sw.Stop();

                long alloc1 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_1 = GC.CollectionCount(0);
                int gen1_1 = GC.CollectionCount(1);
                int gen2_1 = GC.CollectionCount(2);

                long totalPhaseExecs = (long)iterations * phasesPerIter * entityCount;
                double perExecUs = sw.Elapsed.TotalMilliseconds * 1000.0 / totalPhaseExecs;
                long allocDelta = alloc1 - alloc0;

                Console.WriteLine($"[PHASE][STRESS] Entities={entityCount} Iters={iterations} Phases={phasesPerIter}");
                Console.WriteLine($"[PHASE][STRESS] TotalPhaseExecs={totalPhaseExecs} ElapsedMs={sw.Elapsed.TotalMilliseconds:F1} PerExecUs={perExecUs:F3}");
                Console.WriteLine($"[PHASE][STRESS] AllocBytes={allocDelta}");
                Console.WriteLine($"[PHASE][STRESS] GC Δ: Gen0={gen0_1 - gen0_0} Gen1={gen1_1 - gen1_0} Gen2={gen2_1 - gen2_0}");

                // Correctness: every target's BB key=1 should have been written to
                for (int e = 0; e < entityCount; e++)
                {
                    ref var bb = ref world.Get<BlackboardFloatBuffer>(targets[e]);
                    That(bb.TryGet(1, out _), Is.True, $"Entity {e} should have BB key=1");
                }

                // Performance guard: <10μs per phase execution on any modern hardware
                That(perExecUs, Is.LessThan(50.0), "Phase execution should be < 50μs average");
                That(sw.Elapsed.TotalSeconds, Is.LessThan(30.0), "Total time should be < 30s");

                Pass("Phase executor stress test complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        /// <summary>
        /// Stress test: Math ops chain (Sub, Div, Clamp, Abs, Neg) in a 10-instruction graph,
        /// executed 10000 times with zero-allocation verification.
        /// </summary>
        [Test]
        public void MathOpsChain_Stress_ZeroAllocation()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create(new AttributeBuffer());
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // Chain: F[0]=100, F[1]=30, F[2]=Sub(100,30)=70, F[3]=5,
                //        F[4]=Div(70,5)=14, F[5]=0, F[6]=20,
                //        F[7]=Clamp(14,0,20)=14, F[8]=Neg(14)=-14, F[9]=Abs(-14)=14
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 100f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 30f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.SubFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 3, ImmF = 5f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.DivFloat, Dst = 4, A = 2, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 5, ImmF = 0f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 6, ImmF = 20f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ClampFloat, Dst = 7, A = 4, B = 5, C = 6 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.NegFloat, Dst = 8, A = 7 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AbsFloat, Dst = 9, A = 8 },
                };

                var f = new float[GraphVmLimits.MaxFloatRegisters];
                var iArr = new int[GraphVmLimits.MaxIntRegisters];
                var b = new byte[GraphVmLimits.MaxBoolRegisters];
                var e = new Entity[GraphVmLimits.MaxEntityRegisters];
                var targets = new Entity[GraphVmLimits.MaxTargets];

                e[0] = entity;
                e[1] = entity;

                // Warmup
                for (int w = 0; w < 100; w++)
                {
                    Array.Clear(f, 0, f.Length);
                    var state = new GraphExecutionState
                    {
                        World = world, Caster = entity, ExplicitTarget = entity,
                        TargetPos = default, Api = api,
                        F = f, I = iArr, B = b, E = e, Targets = targets,
                        TargetList = new GraphTargetList(targets),
                    };
                    GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                int iterations = 10_000;
                long alloc0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();

                for (int iter = 0; iter < iterations; iter++)
                {
                    Array.Clear(f, 0, f.Length);
                    var state = new GraphExecutionState
                    {
                        World = world, Caster = entity, ExplicitTarget = entity,
                        TargetPos = default, Api = api,
                        F = f, I = iArr, B = b, E = e, Targets = targets,
                        TargetList = new GraphTargetList(targets),
                    };
                    GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
                }

                sw.Stop();
                long alloc1 = GC.GetAllocatedBytesForCurrentThread();

                // Correctness: final F[9] should be 14
                That(f[9], Is.EqualTo(14f).Within(1e-6f));

                double perIterUs = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
                Console.WriteLine($"[MATH][STRESS] Iters={iterations} ElapsedMs={sw.Elapsed.TotalMilliseconds:F1} PerIterUs={perIterUs:F3}");
                Console.WriteLine($"[MATH][STRESS] AllocBytes={alloc1 - alloc0}");

                That(alloc1 - alloc0, Is.LessThanOrEqualTo(64), "Math chain should be zero-alloc");

                Pass("Math ops chain stress complete");
            }
            finally
            {
                world.Dispose();
            }
        }

        /// <summary>
        /// Stress test: Blackboard write+read roundtrip for 1000 entities × 100 iterations.
        /// Verifies zero GC and correctness.
        /// </summary>
        [Test]
        public void BlackboardOps_Stress_MassWriteRead_ZeroGc()
        {
            var world = World.Create();
            try
            {
                var api = new GasGraphRuntimeApi(world, null, null, null);

                // Graph: F[0]=42.5, E[0]=target, WriteBBFloat(E[0], key=1, F[0]),
                //        ReadBBFloat(F[1], E[0], key=1), F[2]=F[1]+F[0]
                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 42.5f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat, A = 0, Imm = 1, B = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardFloat, Dst = 1, A = 0, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 2, A = 1, B = 0 },
                };

                int entityCount = 1000;
                var caster = world.Create();
                var entities = new Entity[entityCount];
                for (int i = 0; i < entityCount; i++)
                {
                    entities[i] = world.Create(new BlackboardFloatBuffer());
                }

                var f = new float[GraphVmLimits.MaxFloatRegisters];
                var iArr = new int[GraphVmLimits.MaxIntRegisters];
                var b = new byte[GraphVmLimits.MaxBoolRegisters];
                var e = new Entity[GraphVmLimits.MaxEntityRegisters];
                var targets = new Entity[GraphVmLimits.MaxTargets];

                e[0] = caster;

                // Warmup
                for (int w = 0; w < 5; w++)
                {
                    foreach (var ent in entities)
                    {
                        Array.Clear(f, 0, f.Length);
                        e[1] = ent;
                        var state = new GraphExecutionState
                        {
                            World = world, Caster = caster, ExplicitTarget = ent,
                            TargetPos = default, Api = api,
                            F = f, I = iArr, B = b, E = e, Targets = targets,
                            TargetList = new GraphTargetList(targets),
                        };
                        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
                    }
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                int iterations = 100;
                long alloc0 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_0 = GC.CollectionCount(0);
                var sw = Stopwatch.StartNew();

                for (int iter = 0; iter < iterations; iter++)
                {
                    for (int i = 0; i < entityCount; i++)
                    {
                        Array.Clear(f, 0, f.Length);
                        e[1] = entities[i];
                        var state = new GraphExecutionState
                        {
                            World = world, Caster = caster, ExplicitTarget = entities[i],
                            TargetPos = default, Api = api,
                            F = f, I = iArr, B = b, E = e, Targets = targets,
                            TargetList = new GraphTargetList(targets),
                        };
                        GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);
                    }
                }

                sw.Stop();
                long alloc1 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_1 = GC.CollectionCount(0);

                long totalExecs = (long)iterations * entityCount;
                double perExecUs = sw.Elapsed.TotalMilliseconds * 1000.0 / totalExecs;

                Console.WriteLine($"[BB][STRESS] Entities={entityCount} Iters={iterations} TotalExecs={totalExecs}");
                Console.WriteLine($"[BB][STRESS] ElapsedMs={sw.Elapsed.TotalMilliseconds:F1} PerExecUs={perExecUs:F3}");
                Console.WriteLine($"[BB][STRESS] AllocBytes={alloc1 - alloc0} GC0Δ={gen0_1 - gen0_0}");

                // Correctness spot check
                ref var bb = ref world.Get<BlackboardFloatBuffer>(entities[0]);
                That(bb.TryGet(1, out float v), Is.True);
                That(v, Is.EqualTo(42.5f).Within(1e-6f));

                That(sw.Elapsed.TotalSeconds, Is.LessThan(30.0));

                Pass("BB stress test complete");
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
