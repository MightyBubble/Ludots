using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public class Navigation2DBenchmarkTests
    {
        [Test]
        public void Benchmark_Navigation2DSteering_10kAgents()
        {
            using var world = World.Create();
            using var runtime = new Navigation2DRuntime(maxAgents: 20000, gridCellSizeCm: 100, loadedChunks: null)
            {
                FlowIterationsPerTick = 0
            };

            var sys = new Navigation2DSteeringSystem2D(world, runtime);

            world.Create(new NavFlowGoal2D
            {
                FlowId = 0,
                GoalCm = Fix64Vec2.FromInt(0, 0),
                RadiusCm = Fix64.FromInt(0)
            });

            var kin = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(800),
                MaxAccelCmPerSec2 = Fix64.FromInt(8000),
                RadiusCm = Fix64.FromInt(30),
                NeighborDistCm = Fix64.FromInt(300),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 16
            };

            const int agentCount = 10_000;
            for (int i = 0; i < agentCount; i++)
            {
                int x = (i % 100) * 120;
                int y = (i / 100) * 120;

                world.Create(
                    new NavAgent2D(),
                    new NavFlowBinding2D { SurfaceId = 0, FlowId = 0 },
                    new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(0, 0), RadiusCm = Fix64.Zero },
                    kin,
                    new Position2D { Value = Fix64Vec2.FromInt(x, y) },
                    Velocity2D.Zero,
                    Mass2D.FromFloat(1f, 1f),
                    new ForceInput2D { Force = Fix64Vec2.Zero },
                    new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
                );
            }

            for (int i = 0; i < 60; i++)
            {
                sys.Update(1f / 60f);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            const int iterations = 300;
            for (int i = 0; i < iterations; i++)
            {
                sys.Update(1f / 60f);
            }

            sw.Stop();
            long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

            double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
            Console.WriteLine($"[Benchmark] Navigation2DSteeringSystem2D");
            Console.WriteLine($"  Agents: {agentCount}");
            Console.WriteLine($"  Iterations: {iterations}");
            Console.WriteLine($"  Total Time: {sw.Elapsed.TotalMilliseconds:F2}ms");
            Console.WriteLine($"  Avg per Tick: {avgMs:F4}ms");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {afterAlloc - beforeAlloc}");
        }
    }
}
