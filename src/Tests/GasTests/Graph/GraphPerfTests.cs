using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using Ludots.Core.Mathematics;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GraphPerfTests
    {
        [Test]
        public void Benchmark_GraphExecutor_SmallProgram()
        {
            var world = World.Create();
            try
            {
                var caster = world.Create(new AttributeBuffer(), new Position { GridPos = new IntVector2(0, 0) });
                world.Get<AttributeBuffer>(caster).SetCurrent(0, 0f);

                var target = world.Create(new AttributeBuffer(), new Position { GridPos = new IntVector2(1, 0) });
                world.Get<AttributeBuffer>(target).SetCurrent(0, 7f);

                var effectRequests = new EffectRequestQueue();
                var eventBus = new GameplayEventBus();
                var api = new GasGraphRuntimeApi(world, spatialQueries: null, eventBus: eventBus, effectRequests: effectRequests);

                var program = new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.LoadAttribute, Dst = 0, A = 2, Imm = 0 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 1f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.AddFloat, Dst = 2, A = 0, B = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 3, ImmF = 2f },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.MulFloat, Dst = 4, A = 2, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.CompareGtFloat, Dst = 0, A = 4, B = 3 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 2 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, A = 2, B = 4, Imm = 123 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.Jump, Imm = 1 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.SendEvent, A = 2, B = 1, Imm = 999 },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 2, Imm = 42 }
                };

                for (int i = 0; i < 1024; i++)
                {
                    GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);
                    effectRequests.Clear();
                    eventBus.Update();
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long alloc0 = GC.GetAllocatedBytesForCurrentThread();
                int iterations = 1_000_000;
                var sw = Stopwatch.StartNew();

                for (int i = 0; i < iterations; i++)
                {
                    GraphExecutor.Execute(world, caster, target, new IntVector2(0, 0), program, api);
                    effectRequests.Clear();
                    eventBus.Update();
                }

                sw.Stop();
                long alloc1 = GC.GetAllocatedBytesForCurrentThread();

                double totalUs = sw.Elapsed.TotalMilliseconds * 1000.0;
                double perExecUs = totalUs / iterations;

                Console.WriteLine("[GraphPerf] GraphExecutor small program:");
                Console.WriteLine($"  Iterations: {iterations}");
                Console.WriteLine($"  TotalMs: {sw.Elapsed.TotalMilliseconds:F2}");
                Console.WriteLine($"  PerExecUs: {perExecUs:F4}");
                Console.WriteLine($"  AllocBytes(CurrentThread): {alloc1 - alloc0}");
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
