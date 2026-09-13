using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class CameraCullingSpatialCadenceHysteresisTests
    {
        private const int WarmupFrames = 4;

        [Test]
        public void CullingCadence_StaticCamera_QueriesSpatialOncePerInterval()
        {
            using World world = World.Create();
            var camera = CreateCamera(targetX: 0f, targetY: 0f, distanceCm: 20000f);
            Entity entity = CreateCullableEntity(world, 100, 100);
            var spatial = new CountingSpatialQueryService(entity);
            using var system = CreateSystem(world, camera, spatial);

            for (int frame = 0; frame < WarmupFrames + CadenceIntervalFrames; frame++)
            {
                system.Update(0.016f);
            }

            Assert.That(spatial.QueryAabbCalls, Is.EqualTo(WarmupFrames + 1),
                "After the camera-stable warm-up the spatial query must run once per cadence interval, not per frame.");
            Assert.That(world.Get<CullState>(entity).IsVisible, Is.True);
        }

        [Test]
        public void CullingCadence_CameraMove_AfterCadenceEngaged_QueriesImmediately()
        {
            using World world = World.Create();
            var camera = CreateCamera(targetX: 0f, targetY: 0f, distanceCm: 20000f);
            Entity entity = CreateCullableEntity(world, 100, 100);
            var spatial = new CountingSpatialQueryService(entity);
            using var system = CreateSystem(world, camera, spatial);

            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                system.Update(0.016f);
            }

            int callsAfterWarmup = spatial.QueryAabbCalls;
            camera.State.TargetCm = new Vector2(600f, 0f);
            system.Update(0.016f);

            Assert.That(spatial.QueryAabbCalls, Is.EqualTo(callsAfterWarmup + 1),
                "A camera move beyond the refresh tolerance must re-query candidates on the same frame.");
        }

        [Test]
        public void CullingHysteresis_UnmovedEntity_StaleVisibilityHeldBetweenRefreshes()
        {
            using World world = World.Create();
            var camera = CreateCamera(targetX: 0f, targetY: 0f, distanceCm: 2000f);
            // At this rig (yaw 45) the viewport rect reaches ~4811cm on X and default visual
            // bounds add ~50cm, so 4760cm is inside the viewport and 4880cm is outside; the
            // 120cm move stays below the 128cm displacement threshold.
            Entity entity = CreateCullableEntity(world, 4760, 0);
            var spatial = new CountingSpatialQueryService(entity);
            using var system = CreateSystem(world, camera, spatial);

            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                system.Update(0.016f);
            }

            Assert.That(world.Get<CullState>(entity).IsVisible, Is.True);

            system.Update(0.016f);
            MoveEntity(world, entity, 4880, 0);
            system.Update(0.016f);
            Assert.That(world.Get<CullState>(entity).IsVisible, Is.True,
                "Below the displacement threshold the previous evaluation is held on cadence frames.");

            for (int frame = 0; frame < CadenceIntervalFrames; frame++)
            {
                system.Update(0.016f);
            }

            Assert.That(world.Get<CullState>(entity).IsVisible, Is.False,
                "The cadence refresh must re-evaluate and cull the entity now outside the viewport.");
        }

        [Test]
        public void CullingHysteresis_MovedBeyondThreshold_ReprocessedOnSkipFrame()
        {
            using World world = World.Create();
            var camera = CreateCamera(targetX: 0f, targetY: 0f, distanceCm: 30000f);
            Entity entity = CreateCullableEntity(world, 5000, 0);
            var spatial = new CountingSpatialQueryService(entity);
            using var system = CreateSystem(world, camera, spatial);

            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                system.Update(0.016f);
            }

            Assert.That(world.Get<CullState>(entity).LOD, Is.EqualTo(LODLevel.Medium));

            world.Set(entity, WorldPositionCm.FromCm(12000, 0));
            MoveEntityVisual(world, entity, 12000, 0);
            system.Update(0.016f);

            Assert.That(world.Get<CullState>(entity).LOD, Is.EqualTo(LODLevel.Low),
                "Movement beyond the displacement threshold must be re-evaluated on the same cadence frame.");
        }

        [Test]
        public void CullingHysteresis_ForeignOwnerAnchor_IsNotTrusted()
        {
            using World world = World.Create();
            var cameraA = CreateCamera(targetX: 0f, targetY: 0f, distanceCm: 20000f);
            var cameraB = CreateCamera(targetX: 50000f, targetY: 50000f, distanceCm: 20000f);
            Entity entity = CreateCullableEntity(world, 100, 100);
            var spatial = new CountingSpatialQueryService(entity);
            using var systemA = CreateSystem(world, cameraA, spatial);
            using var systemB = CreateSystem(world, cameraB, spatial);

            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                systemB.Update(0.016f);
            }

            Assert.That(world.Get<CullState>(entity).IsVisible, Is.False,
                "The entity is far outside binding B's viewport.");

            for (int frame = 0; frame < WarmupFrames; frame++)
            {
                systemA.Update(0.016f);
            }

            Assert.That(world.Get<CullState>(entity).IsVisible, Is.True,
                "Binding A re-evaluates and re-anchors the shared CullState.");

            // B's first cadence-skip frame: the anchor still belongs to A, so B must fully
            // re-evaluate instead of skipping and holding A's visibility.
            systemB.Update(0.016f);
            Assert.That(world.Get<CullState>(entity).IsVisible, Is.False,
                "A culling system must not treat another instance's hysteresis anchor as its own.");
        }

        [Test]
        public void CullingCadence_MultiBindingPasses_QueryEveryPassEveryFrame()
        {
            using World world = World.Create();
            var camera = CreateCamera(targetX: 0f, targetY: 0f, distanceCm: 20000f);
            Entity entity = CreateCullableEntity(world, 100, 100);
            var spatial = new CountingSpatialQueryService(entity);
            var view = new StubViewController();
            using var system = new CameraCullingSystem(
                world,
                camera,
                spatial,
                view,
                cullingConfig: CreateConfig());
            system.RebindPresentBindings(new[]
            {
                new PresentBindingCullPass(null, camera, view),
                new PresentBindingCullPass(null, camera, view),
            });

            for (int frame = 0; frame < WarmupFrames + CadenceIntervalFrames; frame++)
            {
                system.Update(0.016f);
            }

            int frames = WarmupFrames + CadenceIntervalFrames;
            Assert.That(spatial.QueryAabbCalls, Is.EqualTo(frames * 2),
                "Multi-binding unions replace the shared candidate set per pass; each pass must query every frame.");
            Assert.That(world.Get<CullState>(entity).IsVisible, Is.True);
        }

        private const int CadenceIntervalFrames = 4;

        private static CameraManager CreateCamera(float targetX, float targetY, float distanceCm)
        {
            var camera = new CameraManager();
            camera.State.TargetCm = new Vector2(targetX, targetY);
            camera.State.DistanceCm = distanceCm;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = 60f;
            return camera;
        }

        private static CameraCullingRuntimeConfig CreateConfig() => new CameraCullingRuntimeConfig
        {
            HighLodDistanceCm = 4000f,
            MediumLodDistanceCm = 10000f,
            LowLodDistanceCm = 20000f,
        };

        private static CameraCullingSystem CreateSystem(World world, CameraManager camera, CountingSpatialQueryService spatial)
        {
            return new CameraCullingSystem(world, camera, spatial, new StubViewController(), cullingConfig: CreateConfig());
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

        private static void MoveEntity(World world, Entity entity, int xCm, int yCm)
        {
            world.Set(entity, WorldPositionCm.FromCm(xCm, yCm));
            MoveEntityVisual(world, entity, xCm, yCm);
        }

        private static void MoveEntityVisual(World world, Entity entity, int xCm, int yCm)
        {
            VisualTransform visual = world.Get<VisualTransform>(entity);
            visual.Position = new Vector3(xCm * 0.01f, 0f, yCm * 0.01f);
            world.Set(entity, visual);
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution { get; } = new(1920f, 1080f);
            public float Fov { get; } = 60f;
            public float AspectRatio { get; } = 16f / 9f;
        }

        private sealed class CountingSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity _entity;
            public int QueryAabbCalls { get; private set; }

            public CountingSpatialQueryService(Entity entity)
            {
                _entity = entity;
            }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
            {
                QueryAabbCalls++;
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
