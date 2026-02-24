using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Systems;
using NUnit.Framework;
using Schedulers;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Terrain
{
    [TestFixture]
    public class VisualSyncSystemCullTests
    {
        private sealed class TestMapper : ICoordinateMapper
        {
            public float ScaleFactor => 1f;

            public Vector3 LogicToVisual(IntVector2 logicPos, int heightLevel)
            {
                return new Vector3(logicPos.X, heightLevel, logicPos.Y);
            }

            public IntVector2 VisualToLogic(Vector3 visualPos)
            {
                return new IntVector2((int)visualPos.X, (int)visualPos.Z);
            }

            public void BatchLogicToVisual(ReadOnlySpan<IntVector2> logicPositions, ReadOnlySpan<int> heights, Span<Vector3> visualPositions)
            {
                for (int i = 0; i < logicPositions.Length; i++)
                {
                    int h = (i < heights.Length) ? heights[i] : 0;
                    visualPositions[i] = LogicToVisual(logicPositions[i], h);
                }
            }
        }

        [SetUp]
        public void Setup()
        {
            if (World.SharedJobScheduler == null)
            {
                World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
                {
                    ThreadPrefixName = "VisualSyncTests",
                    ThreadCount = 0,
                    MaxExpectedConcurrentJobs = 64,
                    StrictAllocationMode = false
                });
            }
        }

        [Test]
        public void VisualSync_SkipsInvisibleEntitiesWithCullState()
        {
            using var world = World.Create();
            var system = new GridVisualSyncSystem(world, new TestMapper());

            var eNoCull = world.Create();
            world.Add(eNoCull, new Position { GridPos = new IntVector2(1, 2) });
            world.Add(eNoCull, VisualTransform.Default);
            world.Get<VisualTransform>(eNoCull).Position = new Vector3(999, 999, 999);

            var eInvisible = world.Create();
            world.Add(eInvisible, new Position { GridPos = new IntVector2(3, 4) });
            world.Add(eInvisible, VisualTransform.Default);
            world.Add(eInvisible, new CullState { IsVisible = false });
            world.Get<VisualTransform>(eInvisible).Position = new Vector3(999, 999, 999);

            var eVisible = world.Create();
            world.Add(eVisible, new Position { GridPos = new IntVector2(5, 6) });
            world.Add(eVisible, VisualTransform.Default);
            world.Add(eVisible, new CullState { IsVisible = true });
            world.Get<VisualTransform>(eVisible).Position = new Vector3(999, 999, 999);

            system.Update(0.016f);

            That(world.Get<VisualTransform>(eNoCull).Position, Is.EqualTo(new Vector3(1, 0, 2)));
            That(world.Get<VisualTransform>(eVisible).Position, Is.EqualTo(new Vector3(5, 0, 6)));
            That(world.Get<VisualTransform>(eInvisible).Position, Is.EqualTo(new Vector3(999, 999, 999)));
        }
    }
}
