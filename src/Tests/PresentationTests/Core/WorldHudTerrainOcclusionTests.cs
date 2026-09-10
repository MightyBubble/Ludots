using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class WorldHudTerrainOcclusionTests
{
    [Test]
    public void NonAdjacentSameAnchor_RemainsHiddenFromProjectionCache()
    {
        using var fixture = new Fixture();
        fixture.AddPair(new Vector3(0, 1.5f, 10));
        fixture.AddPair(new Vector3(0, 1.5f, -10));
        var duplicate = fixture.Hud.GetSpan()[0];
        duplicate.StableId = 5;
        duplicate.Kind = WorldHudItemKind.Text;
        duplicate.FontSize = 12;
        fixture.Hud.TryAdd(duplicate);
        fixture.System.Update(0);
        Assert.That(fixture.Screen.BarCount, Is.EqualTo(1));
        Assert.That(fixture.Screen.TextCount, Is.EqualTo(1));
        Assert.That(fixture.Screen.GetTextSpan()[0].StableId, Is.EqualTo(4));
    }

    [Test]
    public void ReplacingTerrainAndChangingOnlyHealth_InvalidatesOcclusion()
    {
        using var fixture = new Fixture();
        fixture.AddPair(new Vector3(0, 1.5f, 10));
        var ridge = fixture.Terrain;
        fixture.Terrain = null;
        fixture.System.Update(0);
        Assert.That(fixture.Screen.Count, Is.EqualTo(2));
        fixture.Terrain = ridge;
        fixture.System.Update(0);
        Assert.That(fixture.Screen.Count, Is.Zero);
        var bar = fixture.Hud.GetSpan()[0];
        bar.Value0 = .5f;
        bar.DirtySerial++;
        fixture.Hud.TryAdd(bar);
        fixture.System.Update(0);
        Assert.That(fixture.Screen.Count, Is.Zero);
        fixture.Terrain = new ContinuousHeightmapRuntime(ContinuousHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(-2000, -2000, 4000, 4000), 2, 2, new short[4],
            interpolationMode: ContinuousHeightmapInterpolationMode.TriangleHeightfield));
        fixture.System.Update(0);
        Assert.That(fixture.Screen.Count, Is.EqualTo(2));
        Assert.That(fixture.Screen.GetBarSpan()[0].Value0, Is.EqualTo(.5f));
    }

    [Test]
    public void RidgeHidesBarsAndNumbers_RotatingCameraRevealsThem()
    {
        using var fixture = new Fixture();
        fixture.AddPair(new Vector3(0, 1.5f, 10));
        fixture.AddPair(new Vector3(0, 1.5f, -10));
        fixture.Projector.LookFrom(new Vector3(0, 8, -30));
        fixture.System.Update(0);
        Assert.That(fixture.Screen.GetBarSpan().ToArray().Select(x => x.StableId), Is.EquivalentTo(new[] { 3 }));
        Assert.That(fixture.Screen.GetTextSpan().ToArray().Select(x => x.StableId), Is.EquivalentTo(new[] { 4 }));

        fixture.Projector.LookFrom(new Vector3(0, 8, 30));
        fixture.System.Update(0);
        Assert.That(fixture.Screen.GetBarSpan().ToArray().Select(x => x.StableId), Is.EquivalentTo(new[] { 1 }));
        Assert.That(fixture.Screen.GetTextSpan().ToArray().Select(x => x.StableId), Is.EquivalentTo(new[] { 2 }));
    }

    [TestCase(1000, false)]
    [TestCase(5000, false)]
    [TestCase(10000, false)]
    [TestCase(1000, true)]
    [TestCase(5000, true)]
    [TestCase(10000, true)]
    public void ProjectionScale(int count, bool occlusion)
    {
        using var fixture = new Fixture(count * 2, occlusion);
        for (int i = 0; i < count; i++)
            fixture.AddPair(new Vector3((i % 100) * .1f - 5, 1.5f, i % 2 == 0 ? 10 : -10));
        for (int i = 0; i < 100; i++)
        {
            fixture.Projector.LookFrom(new Vector3(0, 8, -30 - i * .001f));
            fixture.System.Update(0);
        }
        var times = new double[31];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < times.Length; i++)
        {
            fixture.Projector.LookFrom(new Vector3(0, 8, -30 - i * .001f));
            long start = Stopwatch.GetTimestamp();
            fixture.System.Update(0);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(times);
        TestContext.Out.WriteLine($"occlusion={occlusion}, owners={count}, median_ms={times[15]:F4}, p95_ms={times[29]:F4}, bytes={allocated}, bars={fixture.Screen.BarCount}, text={fixture.Screen.TextCount}");
        Assert.That(allocated, Is.Zero);
        Assert.That(fixture.Screen.BarCount, Is.EqualTo(occlusion ? count / 2 : count));
        Assert.That(fixture.Screen.TextCount, Is.EqualTo(occlusion ? count / 2 : count));
    }

    private sealed class Fixture : IDisposable
    {
        public readonly World World = World.Create();
        public readonly WorldHudBatchBuffer Hud;
        public readonly ScreenHudBatchBuffer Screen;
        public readonly Projector Projector = new();
        public IContinuousHeightmap? Terrain;
        public readonly WorldHudToScreenSystem System;
        private int _id;

        public Fixture(int capacity = 8, bool occlusion = true)
        {
            Hud = new WorldHudBatchBuffer(capacity);
            Screen = new ScreenHudBatchBuffer(capacity);
            Terrain = new ContinuousHeightmapRuntime(ContinuousHeightmapAsset.CreateSingleLayer(
                new WorldAabbCm(-2000, -2000, 4000, 4000), 3, 5,
                new short[] { 0, 0, 0, 0, 0, 0, 600, 600, 600, 0, 0, 0, 0, 0, 0 },
                interpolationMode: ContinuousHeightmapInterpolationMode.TriangleHeightfield));
            System = new WorldHudToScreenSystem(World, Hud, null, Projector, Projector, Screen, heightmapProvider: () => occlusion ? Terrain : null);
        }

        public void AddPair(Vector3 position)
        {
            var owner = World.Create(new CullState { IsVisible = true });
            Hud.TryAdd(new WorldHudItem { Owner = owner, StableId = ++_id, Kind = WorldHudItemKind.Bar,
                WorldPosition = position, Width = 40, Height = 6, Value0 = .75f, DirtySerial = 1 });
            Hud.TryAdd(new WorldHudItem { Owner = owner, StableId = ++_id, Kind = WorldHudItemKind.Text,
                WorldPosition = position + Vector3.UnitY * .2f, FontSize = 12, Value0 = 75, DirtySerial = 1 });
        }

        public void Dispose() => World.Destroy(World);
    }

    private sealed class Projector : IScreenProjector, IProjectionSnapshotProvider, IViewController
    {
        private Matrix4x4 _matrix;
        public int ProjectionRevision { get; private set; }
        public Vector2 Resolution => new(1600, 900);
        public float Fov => 60;
        public float AspectRatio => 16f / 9f;
        public Projector() => LookFrom(new Vector3(0, 8, -30));
        public void LookFrom(Vector3 position)
        {
            _matrix = Matrix4x4.CreateLookAt(position, new Vector3(0, 2, 0), Vector3.UnitY)
                * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, AspectRatio, .1f, 1000);
            ProjectionRevision++;
        }
        public bool TryGetProjectionSnapshot(out ProjectionSnapshot snapshot)
        {
            snapshot = new ProjectionSnapshot(_matrix, Resolution);
            return true;
        }
        public Vector2 WorldToScreen(Vector3 position)
        {
            Vector4 clip = Vector4.Transform(new Vector4(position, 1), _matrix);
            return new Vector2((clip.X / clip.W + 1) * Resolution.X / 2, (1 - clip.Y / clip.W) * Resolution.Y / 2);
        }
    }
}

/// <summary>
/// Locks the capacity contract of the terrain occlusion cache. A field-wide anchor set
/// (10K anchors spread across the whole play area, as the large-world crowd showcase produces)
/// must stay resident: the table clears itself entirely once it overflows, and every anchor then
/// re-raycasts every frame. An undersized capacity is a pure performance defect with no
/// correctness symptom, so it needs a test rather than a comment.
/// </summary>
[TestFixture]
public sealed class WorldHudTerrainOcclusionCacheCapacityTests
{
    [Test]
    public void FieldWideAnchorSet_FitsWithoutOverflowClear()
    {
        const int capacity = 131_072;
        var cache = new TerrainHudOcclusionCache(capacity);

        // Mirror the key shape the projector builds: one camera cell, 100x100 anchor cells,
        // plus neighbouring camera cells so the key set spans what a field-wide crowd touches.
        int generated = 0;
        for (int cameraCellX = 0; cameraCellX <= 2; cameraCellX++)
        {
            for (int cameraCellZ = 0; cameraCellZ <= 2; cameraCellZ++)
            {
                for (int anchorCellX = 0; anchorCellX < 100; anchorCellX++)
                {
                    for (int anchorCellZ = 0; anchorCellZ < 100; anchorCellZ++)
                    {
                        long key = TerrainHudOcclusionCache.ComposeKey(
                            heightmapRevision: 3,
                            cameraCellX: cameraCellX,
                            cameraCellZ: cameraCellZ,
                            anchorCellX: anchorCellX,
                            anchorCellZ: anchorCellZ,
                            heightBucket: 1);
                        cache.Set(key, visible: true);
                        generated++;
                    }
                }
            }
        }

        Assert.That(generated, Is.EqualTo(90_000),
            "The test must generate a key set comparable to a field-wide crowd.");
        Assert.That(cache.OverflowClearCount, Is.Zero,
            $"A {capacity}-entry cache must hold the generated {generated} field-wide keys; " +
            "any overflow clear means every anchor re-raycasts each frame.");
        Assert.That(cache.EntryCount, Is.EqualTo(generated));

        // Second pass over the same keys: still resident, still no clear.
        for (int anchorCellX = 0; anchorCellX < 100; anchorCellX++)
        {
            for (int anchorCellZ = 0; anchorCellZ < 100; anchorCellZ++)
            {
                long key = TerrainHudOcclusionCache.ComposeKey(3, 1, 1, anchorCellX, anchorCellZ, 1);
                Assert.That(cache.TryGet(key, out bool visible), Is.True,
                    "A resident key must hit rather than miss into a raycast.");
                Assert.That(visible, Is.True);
            }
        }

        Assert.That(cache.OverflowClearCount, Is.Zero);
    }

    [Test]
    public void UndersizedCapacity_OverflowsAndClearsInsteadOfSilentlyDegrading()
    {
        var cache = new TerrainHudOcclusionCache(4096);

        for (int anchorCellX = 0; anchorCellX < 100; anchorCellX++)
        {
            for (int anchorCellZ = 0; anchorCellZ < 100; anchorCellZ++)
            {
                long key = TerrainHudOcclusionCache.ComposeKey(1, 1, 1, anchorCellX, anchorCellZ, 1);
                cache.Set(key, visible: true);
            }
        }

        Assert.That(cache.OverflowClearCount, Is.GreaterThan(0),
            "An undersized cache must report the overflow explicitly rather than degrade silently.");
    }
}

public sealed class CachedBucketedOcclusionMatchesExactTests
    {
        [Test]
        public void BucketCacheMatchesExactAcrossRidgeAndCameraPoses()
        {
            using var world = Arch.Core.World.Create();
            var projOn = new CamProj(); var projOff = new CamProj();
            projOn.LookFrom(new Vector3(0, 45, -60)); projOff.LookFrom(new Vector3(0, 45, -60));
            var hudOn = new WorldHudBatchBuffer(4096); var hudOff = new WorldHudBatchBuffer(4096);
            var screenOn = new ScreenHudBatchBuffer(4096); var screenOff = new ScreenHudBatchBuffer(4096);
            var heightmap = new ContinuousHeightmapRuntime(ContinuousHeightmapAsset.CreateSingleLayer(
                new WorldAabbCm(-4000, -4000, 8000, 8000), 17, 5, BuildRidge(), interpolationMode: ContinuousHeightmapInterpolationMode.TriangleHeightfield));
            using var sysOn = new WorldHudToScreenSystem(world, hudOn, null, projOn, projOn, screenOn,
                heightmapProvider: () => heightmap, occlusionConfig: new TerrainHudOcclusionConfig(8192, 8, 100));
            using var sysOff = new WorldHudToScreenSystem(world, hudOff, null, projOff, projOff, screenOff,
                heightmapProvider: () => heightmap, occlusionConfig: TerrainHudOcclusionConfig.Disabled);

            var set = new HashSet<int>();
            int id = 1;
            for (int i = -20; i <= 20; i++, id++)
            {
                var pos = new Vector3(i * 12f, 2f, 0f);
                var owner = world.Create(new CullState { IsVisible = true });
                var item = new WorldHudItem { Owner = owner, StableId = id, Kind = WorldHudItemKind.Bar, WorldPosition = pos, Width = 40, Height = 6, DirtySerial = 1 };
                hudOn.TryAdd(in item); hudOff.TryAdd(in item);
                set.Add(id);
            }

            AssertCountsMatch(sysOn, sysOff, screenOn, screenOff, set, "high-cam");
            // 低机位掠脊 + 锚点微移，考验跨帧/跨机位缓存沿用
            projOn.LookFrom(new Vector3(0, 6, -40)); projOff.LookFrom(new Vector3(0, 6, -40));
            var movedOn = hudOn.GetSpan()[0]; movedOn.WorldPosition.X += 0.05f;
            hudOn.UpdatePosition(movedOn.StableId, in movedOn.WorldPosition);
            var movedOff = hudOff.GetSpan()[0]; movedOff.WorldPosition.X += 0.05f;
            hudOff.UpdatePosition(movedOff.StableId, in movedOff.WorldPosition);
            AssertCountsMatch(sysOn, sysOff, screenOn, screenOff, set, "low-graze");
        }

        private static void AssertCountsMatch(WorldHudToScreenSystem on, WorldHudToScreenSystem off,
            ScreenHudBatchBuffer sOn, ScreenHudBatchBuffer sOff, HashSet<int> set, string tag)
        {
            on.Update(0);
            off.Update(0);
            var a = new HashSet<int>(); var b = new HashSet<int>();
            foreach (var bar in sOn.GetBarSpan()) a.Add(bar.StableId);
            foreach (var bar in sOff.GetBarSpan()) b.Add(bar.StableId);
            Assert.That(a, Is.EquivalentTo(b), $"{tag}: bucketed cache diverged from exact occlusion over {set.Count} anchors.");
        }

        private static short[] BuildRidge()
        {
            var h = new short[17 * 5];
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 17; x++)
                    h[y * 17 + x] = (short)(x >= 7 && x <= 11 ? 600 : 0);
            return h;
        }

        private sealed class CamProj : IScreenProjector, IProjectionSnapshotProvider, IViewController
        {
            private Matrix4x4 _matrix;
            private Vector3 _camera;
            public Vector2 Resolution => new(1600, 900);
            public float Fov => 60f;
            public float AspectRatio => 16f / 9f;
            public void LookFrom(Vector3 p)
            {
                _camera = p;
                _matrix = Matrix4x4.CreateLookAt(p, new Vector3(0, 2, 0), Vector3.UnitY)
                    * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, AspectRatio, 0.1f, 1000f);
            }

            public int ProjectionRevision => 1;
            public bool TryGetProjectionSnapshot(out ProjectionSnapshot snapshot)
            {
                snapshot = new ProjectionSnapshot(_matrix, Resolution, _camera);
                return true;
            }

            public Vector2 WorldToScreen(Vector3 position)
            {
                Vector4 clip = Vector4.Transform(new Vector4(position, 1), _matrix);
                return new Vector2((clip.X / clip.W + 1) * Resolution.X / 2, (1 - clip.Y / clip.W) * Resolution.Y / 2);
            }
        }
    }
