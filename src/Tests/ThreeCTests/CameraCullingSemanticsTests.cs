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
        public void Culling_GroundPastLookAtBox_StaysVisibleWhenItProjectsOnScreen()
        {
            using World world = World.Create();
            var camera = new CameraManager();
            camera.State.TargetCm = Vector2.Zero;
            camera.State.DistanceCm = 8000f;
            camera.State.Pitch = 22f;
            camera.State.Yaw = 0f;
            camera.State.FovYDeg = 42f;

            var view = new StubViewController();
            CameraStateSnapshot snapshot = CameraStateSnapshot.FromState(camera.State);
            (float _, float logicHeight) = CameraViewportUtil.ComputeViewportExtent(
                camera.State.DistanceCm,
                camera.State.FovYDeg,
                camera.State.Pitch,
                view.AspectRatio);
            float lookAtHalfHeightCm = logicHeight * 0.5f;

            bool found = false;
            int foundX = 0;
            int foundY = 0;
            for (float screenY = 4f; screenY < view.Resolution.Y * 0.5f; screenY += 8f)
            {
                if (!CameraViewportUtil.TryGroundCmUnderPointer(
                        in snapshot,
                        new Vector2(view.Resolution.X * 0.5f, screenY),
                        view.Resolution,
                        view.AspectRatio,
                        out Vector2 groundCm) ||
                    groundCm.Y <= lookAtHalfHeightCm + 200f)
                {
                    continue;
                }

                CameraRenderState3D render = CameraViewportUtil.StateToRenderState(in snapshot);
                Vector2 projected = CameraViewportUtil.WorldToScreen(
                    new Vector3(groundCm.X * 0.01f, 0f, groundCm.Y * 0.01f),
                    in render,
                    view.Resolution,
                    view.AspectRatio);
                if (float.IsNaN(projected.X) || float.IsNaN(projected.Y))
                {
                    continue;
                }

                foundX = (int)MathF.Round(groundCm.X);
                foundY = (int)MathF.Round(groundCm.Y);
                found = true;
                break;
            }

            Assert.That(found, Is.True, "A shallow camera shows ground beyond the look-at box.");

            Entity entity = CreateCullableEntity(world, foundX, foundY);
            var spatial = new StubSpatialQueryService(entity);
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
            Assert.That(cull.IsVisible, Is.True,
                $"Ground at ({foundX},{foundY}) cm projects on screen and must keep its mesh.");
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

        [Test]
        public void Culling_RepeatedForceCull_DoesNotAdvanceVisibilityRevision()
        {
            using World world = World.Create();
            var camera = new CameraManager();
            camera.State.TargetCm = Vector2.Zero;
            camera.State.DistanceCm = 2000f;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = 60f;

            Entity entity = world.Create(
                WorldPositionCm.FromCm(50000, 50000),
                new CullState { IsVisible = false, LOD = LODLevel.Low },
                new VisualTransform
                {
                    Position = new Vector3(500f, 0f, 500f),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                });
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
            int firstRevision = system.DebugState.VisibilityRevision;
            system.Update(0.016f);

            ref CullState cull = ref world.Get<CullState>(entity);
            Assert.That(cull.IsVisible, Is.False);
            Assert.That(cull.LOD, Is.EqualTo(LODLevel.Low));
            Assert.That(system.DebugState.VisibilityRevision, Is.EqualTo(firstRevision),
                "Already hidden entities must not invalidate retained HUD projection every frame.");
        }

        [Test]
        public void Culling_LoadedChunkGate_UsesLoadedSourceWorldKeyResolver()
        {
            using World world = World.Create();
            var camera = new CameraManager();
            camera.State.TargetCm = Vector2.Zero;
            camera.State.DistanceCm = 20000f;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = 60f;

            Entity entity = CreateCullableEntity(world, 7000, 100);
            var spatial = new StubSpatialQueryService(entity);
            var view = new StubViewController();
            var loadedChunks = new WorldGridLoadedChunks(chunkSizeCm: 6400, loadedChunkCapacity: 1);
            loadedChunks.SetLoaded(GraphChunkKey.Pack(1, 0), loaded: true);

            using var system = new CameraCullingSystem(
                world,
                camera,
                spatial,
                view,
                loadedChunks: loadedChunks,
                cullingConfig: new CameraCullingRuntimeConfig
                {
                    HighLodDistanceCm = 4000f,
                    MediumLodDistanceCm = 10000f,
                    LowLodDistanceCm = 20000f,
                });
            system.Update(0.016f);

            ref CullState cull = ref world.Get<CullState>(entity);
            Assert.That(cull.IsVisible, Is.True,
                "Camera culling must ask the loaded-chunk source for its world key instead of assuming hex-cell chunk math.");
        }

        [Test]
        public void Culling_LoadedChunkGate_RequiresWorldKeyResolver()
        {
            using World world = World.Create();
            var camera = new CameraManager();
            camera.State.TargetCm = Vector2.Zero;
            camera.State.DistanceCm = 20000f;
            camera.State.Pitch = 45f;
            camera.State.FovYDeg = 60f;

            Entity entity = CreateCullableEntity(world, 7000, 100);
            var spatial = new StubSpatialQueryService(entity);
            var view = new StubViewController();
            var loadedChunks = new LoadedChunksWithoutWorldKeyResolver();
            loadedChunks.SetLoaded(1);

            using var system = new CameraCullingSystem(
                world,
                camera,
                spatial,
                view,
                loadedChunks: loadedChunks,
                cullingConfig: new CameraCullingRuntimeConfig
                {
                    HighLodDistanceCm = 4000f,
                    MediumLodDistanceCm = 10000f,
                    LowLodDistanceCm = 20000f,
                });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f));
            Assert.That(ex!.Message, Does.Contain(nameof(IWorldChunkKeyResolver)));
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

        private sealed class LoadedChunksWithoutWorldKeyResolver : ILoadedChunks
        {
            private readonly HashSet<long> _active = new();

            public IReadOnlyCollection<long> ActiveChunkKeys => _active;

            public event Action<long> ChunkLoaded
            {
                add { }
                remove { }
            }

            public event Action<long> ChunkUnloaded
            {
                add { }
                remove { }
            }

            public bool IsLoaded(long chunkKey) => _active.Contains(chunkKey);

            public void SetLoaded(long chunkKey)
            {
                _active.Add(chunkKey);
            }
        }
    }
}
