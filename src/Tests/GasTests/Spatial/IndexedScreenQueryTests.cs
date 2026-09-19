using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.AimSource;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[NonParallelizable]
public sealed class IndexedScreenQueryTests
{
    [TestCase(100)]
    [TestCase(10000)]
    public void DenseRegion_IsCompleteAndDistantCandidatesDoNotIncreaseWarmWork(int distant)
    {
        using var world = World.Create();
        var partition = new ChunkedGridSpatialPartitionWorld();
        var spec = new WorldSizeSpec(new WorldAabbCm(-200000, -200000, 400000, 400000), 100);
        var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
        spatial.BindBoundsWorld(world);
        spatial.SetPositionProvider(e => world.Get<WorldPositionCm>(e).Value.ToWorldCmInt2());
        world.Create(new Ludots.Core.Presentation.Components.PresentationFrameState { Enabled = true, InterpolationAlpha = 1f },
            new Ludots.Core.Presentation.Components.PresentationFrameStateTag());
        var map = new MapId("screen-test");
        Entity Spawn(int x, int y)
        {
            int cellX = (int)MathF.Floor(x / 100f), cellY = (int)MathF.Floor(y / 100f);
            Entity entity = world.Create(new MapEntity { MapId = map }, new Team { Id = 1 },
                WorldPositionCm.FromCm(x, y), new SpatialCellRef { CellX = cellX, CellY = cellY, State = SpatialMembershipState.Active });
            partition.Add(entity, cellX, cellY);
            return entity;
        }
        for (int i = 0; i < 500; i++) Spawn(i % 10 * 10, i / 10 * 10);
        Entity edge = Spawn(600, 0);
        world.Add(edge, new SpatialBounds { Kind = SpatialBoundsKind.Box3D },
            new SpatialBox3D { HalfSizeXCm = 150, HalfSizeYCm = 300, HalfSizeZCm = 150 });
        for (int i = 0; i < distant; i++) Spawn(50000 + i, 50000);
        using var source = new DerivedEntityIndex(world, map, new QueryDescription().WithAll<MapEntity, Team>(),
            new[] { Component<Team>.ComponentType }, static (w, e) => w.Get<Team>(e).Id == 1);
        var projection = new OrthographicProjection();
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.SpatialQueryService.Name] = spatial,
            [CoreServiceKeys.WorldSizeSpec.Name] = spec,
            [CoreServiceKeys.ScreenRayProvider.Name] = projection,
            [CoreServiceKeys.ScreenProjector.Name] = projection
        };
        var aim = new GraphAimSourceRuntime(world, globals);
        var rect = new ScreenRect(-500, -500, 500, 500);
        Assert.That(aim.QueryScreenRegion(source, rect, null).Length, Is.EqualTo(501));
        Assert.That(aim.QueryScreenRegion(source, rect, null).Contains(edge), Is.True);
        for (int i = 0; i < 20; i++) aim.QueryScreenRegion(source, rect, null);
        long before = aim.RegionExactTests;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++) aim.QueryScreenRegion(source, rect, null);
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Assert.That(aim.RegionExactTests - before, Is.EqualTo(50100));
        Assert.That(bytes, Is.Zero);
        TestContext.Out.WriteLine(FormattableString.Invariant($"distant={distant}, queries=100, exact_tests=50100, elapsed_ms={milliseconds:F6}, bytes={bytes}"));
        spatial.UnbindBoundsWorld();
    }

    [Test]
    public void HoverPointQuery_AtLargeWorldCoordinates_StaysLocal()
    {
        using var world = World.Create();
        var partition = new ChunkedGridSpatialPartitionWorld();
        var spec = new WorldSizeSpec(new WorldAabbCm(0, 0, 345_600_000, 345_600_000), 250);
        var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
        spatial.BindBoundsWorld(world);
        spatial.SetPositionProvider(e => world.Get<WorldPositionCm>(e).Value.ToWorldCmInt2());
        world.Create(new Ludots.Core.Presentation.Components.PresentationFrameState { Enabled = true, InterpolationAlpha = 1f },
            new Ludots.Core.Presentation.Components.PresentationFrameStateTag());
        var map = new MapId("hover-large-coords");
        // Representative large-coordinate rig: strategy camera deep inside a 3456 km world.
        var cameraTarget = new Vector3(846.42f, 141.04f, 12568.92f);
        var camera = new CameraRenderState3D(
            cameraTarget + new Vector3(0, 65f * .8660254f, -32.5f), cameraTarget, Vector3.UnitY, 50f);
        var projection = new PerspectiveCameraProjection(camera, new Vector2(1526, 892), 1526f / 892f);
        Entity Spawn(int xCm, int yCm)
        {
            int cellX = (int)MathF.Floor(xCm / 250f), cellY = (int)MathF.Floor(yCm / 250f);
            Entity entity = world.Create(new MapEntity { MapId = map }, new Team { Id = 1 },
                WorldPositionCm.FromCm(xCm, yCm), new SpatialCellRef { CellX = cellX, CellY = cellY, State = SpatialMembershipState.Active });
            partition.Add(entity, cellX, cellY);
            return entity;
        }
        Entity nearEntity = Spawn(84_642, 1_256_892);
        for (int i = 0; i < 40; i++) Spawn(84_000 + i * 30, 1_257_000 + i * 30);
        for (int i = 0; i < 60; i++) Spawn(30_000_000 + i * 500, 2_400_000 + i * 500);
        using var source = new DerivedEntityIndex(world, map, new QueryDescription().WithAll<MapEntity, Team>(),
            new[] { Component<Team>.ComponentType }, static (w, e) => w.Get<Team>(e).Id == 1);
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.SpatialQueryService.Name] = spatial,
            [CoreServiceKeys.WorldSizeSpec.Name] = spec,
            [CoreServiceKeys.ScreenRayProvider.Name] = projection,
            [CoreServiceKeys.ScreenProjector.Name] = projection
        };
        var aim = new GraphAimSourceRuntime(world, globals);

        // A zero-area hover rect on the near cluster must keep the coarse phase local:
        // distant entities must never even become broadphase candidates.
        var aimPixel = projection.WorldToScreen(new Vector3(846.42f, 0f, 12568.92f));
        var hoverRect = new ScreenRect(aimPixel.X, aimPixel.Y, aimPixel.X, aimPixel.Y);
        ReadOnlySpan<Entity> hits = aim.QueryScreenRegion(source, hoverRect, null);
        Assert.That(hits.Contains(nearEntity), Is.True, "hover over the cluster must pick the near entity");
        long candidates = aim.RegionBroadphaseCandidates;
        Assert.That(candidates, Is.LessThanOrEqualTo(101), "broadphase must not sweep the world for a 1px hover");

        for (int i = 0; i < 20; i++) aim.QueryScreenRegion(source, hoverRect, null);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            aim.QueryScreenRegion(source, hoverRect, null);
        }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - bytes, Is.Zero);
        spatial.UnbindBoundsWorld();
    }

    private sealed class OrthographicProjection : IScreenProjector, IScreenRayProvider
    {
        public Vector2 WorldToScreen(Vector3 world) => new(world.X * 100, world.Z * 100);
        public ScreenRay GetRay(Vector2 screen) => new(new Vector3(screen.X / 100, 20, screen.Y / 100), -Vector3.UnitY);
    }

    private sealed class PerspectiveCameraProjection(CameraRenderState3D camera, Vector2 resolution, float aspect)
        : IScreenProjector, IScreenRayProvider
    {
        public Vector2 WorldToScreen(Vector3 world) => Ludots.Core.Gameplay.Camera.CameraViewportUtil.WorldToScreen(world, in camera, resolution, aspect);
        public ScreenRay GetRay(Vector2 screen) => Ludots.Core.Gameplay.Camera.CameraViewportUtil.ScreenToRay(screen, in camera, resolution, aspect);
    }
}
