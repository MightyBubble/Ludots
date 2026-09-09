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
