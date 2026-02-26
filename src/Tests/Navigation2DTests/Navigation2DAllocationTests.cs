using System;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    [NonParallelizable]
    public class Navigation2DAllocationTests
    {
        [Test]
        public void Navigation2DSteering_AllocatesZero()
        {
            using var world = World.Create();
            using var runtime = new Navigation2DRuntime(maxAgents: 4096, gridCellSizeCm: 100, loadedChunks: null);
            var sys = new Navigation2DSteeringSystem2D(world, runtime);

            world.Create(new NavFlowGoal2D
            {
                FlowId = 0,
                GoalCm = Fix64Vec2.FromInt(0, 0),
                RadiusCm = Fix64.FromInt(0)
            });

            var kin = new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.FromInt(600),
                MaxAccelCmPerSec2 = Fix64.FromInt(6000),
                RadiusCm = Fix64.FromInt(30),
                NeighborDistCm = Fix64.FromInt(300),
                TimeHorizonSec = Fix64.FromInt(2),
                MaxNeighbors = 16
            };

            for (int i = 0; i < 1024; i++)
            {
                int x = (i % 64) * 100;
                int y = (i / 64) * 100;

                world.Create(
                    new NavAgent2D(),
                    new NavFlowBinding2D { SurfaceId = 0, FlowId = 0 },
                    new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = Fix64Vec2.FromInt(0, 0), RadiusCm = Fix64.Zero },
                    kin,
                    new Position2D { Value = Fix64Vec2.FromInt(x, y) },
                    new Velocity2D { Linear = Fix64Vec2.Zero, Angular = Fix64.Zero },
                    Mass2D.FromFloat(1f, 1f),
                    new ForceInput2D { Force = Fix64Vec2.Zero },
                    new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero }
                );
            }

            for (int i = 0; i < 32; i++)
            {
                sys.Update(1f / 60f);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                sys.Update(1f / 60f);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            That(after - before, Is.LessThanOrEqualTo(64));
        }
    }
}
