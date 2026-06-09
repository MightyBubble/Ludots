using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class CameraCullingSemanticsTests
    {
        [Test]
        public void Culling_ViewportVisibleBeyondLowDistance_RemainsVisibleWithLowLod()
        {
            using World world = World.Create();
            var camera = new CameraManager();
            camera.State.TargetCm = Vector2.Zero;
            camera.State.DistanceCm = 30000f;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = 60f;

            Entity entity = CreateCullableEntity(world, 20000, 20000);
            var spatial = new StubSpatialQueryService(entity);
            var view = new StubViewController();
            var config = new CameraCullingRuntimeConfig
            {
                HighLodDistanceCm = 4000f,
                MediumLodDistanceCm = 10000f,
                LowLodDistanceCm = 20000f,
            };

            using var system = new CameraCullingSystem(
                world,
                camera,
                spatial,
                view,
                cullingConfig: config);
            system.Update(0.016f);

            ref CullState cull = ref world.Get<CullState>(entity);
            Assert.That(cull.IsVisible, Is.True);
            Assert.That(cull.LOD, Is.EqualTo(LODLevel.Low));
        }

        [Test]
        public void Culling_OutsideViewport_UsesVisibilityWithoutOverwritingLod()
        {
            using World world = World.Create();
            var camera = new CameraManager();
            camera.State.TargetCm = Vector2.Zero;
            camera.State.DistanceCm = 2000f;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = 60f;

            Entity entity = CreateCullableEntity(world, 50000, 50000);
            var spatial = new StubSpatialQueryService(entity);
            var view = new StubViewController();

            using var system = new CameraCullingSystem(
                world,
                camera,
                spatial,
                view,
                cullingConfig: new CameraCullingRuntimeConfig
                {
                    HighLodDistanceCm = 4000f,
                    MediumLodDistanceCm = 10000f,
                    LowLodDistanceCm = 20000f,
                });
            system.Update(0.016f);

            ref CullState cull = ref world.Get<CullState>(entity);
            Assert.That(cull.IsVisible, Is.False);
            Assert.That(cull.LOD, Is.EqualTo(LODLevel.High));
        }

        private static Entity CreateCullableEntity(World world, int xCm, int yCm)
        {
            return world.Create(
                WorldPositionCm.FromCm(xCm, yCm),
                new CullState(),
                new VisualTransform
                {
                    Position = new Vector3(xCm * 0.01f, 0f, yCm * 0.01f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution { get; } = new(1920f, 1080f);
            public float Fov { get; } = 60f;
            public float AspectRatio { get; } = 16f / 9f;
        }

        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity _entity;

            public StubSpatialQueryService(Entity entity)
            {
                _entity = entity;
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
            {
                if (buffer.Length == 0)
                {
                    return new SpatialQueryResult(0, 1);
                }

                buffer[0] = _entity;
                return new SpatialQueryResult(1, 0);
            }

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryHexRange(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryHexRing(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => throw new NotSupportedException();
        }
    }
}
