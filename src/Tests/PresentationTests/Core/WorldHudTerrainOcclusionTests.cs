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

/// <summary>
/// 地平线包络遮挡合同：开阔地与山脊正后方两类清晰锚点上与精确 raycast 一致；
/// 全场扫描下包络不得误藏任何精确判定可见的锚点（误差只允许偏向多显示）。
/// 包络投影器提供相机位；精确回退投影器不提供（系统回退逐项 raycast）。
/// </summary>
public sealed class HorizonOcclusionContractTests
{
    [Test]
    public void EnvelopeNeverWronglyHidesAndBlocksBehindRidge()
    {
        using var world = Arch.Core.World.Create();
        var exactProjector = new PoseProjector(provideCamera: false);
        var envelopeProjector = new PoseProjector(provideCamera: true);
        ContinuousHeightmapRuntime heightmap = new ContinuousHeightmapRuntime(ContinuousHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(-4000, -4000, 8000, 8000), 17, 5, BuildRidge(), interpolationMode: ContinuousHeightmapInterpolationMode.TriangleHeightfield));
        var hudExact = new WorldHudBatchBuffer(4096);
        var hudEnvelope = new WorldHudBatchBuffer(4096);
        var screenExact = new ScreenHudBatchBuffer(4096);
        var screenEnvelope = new ScreenHudBatchBuffer(4096);
        using var exact = new WorldHudToScreenSystem(world, hudExact, null, exactProjector, exactProjector, screenExact,
            heightmapProvider: () => heightmap);
        using var envelope = new WorldHudToScreenSystem(world, hudEnvelope, null, envelopeProjector, envelopeProjector, screenEnvelope,
            heightmapProvider: () => heightmap);

        // 锚点避开山脊坡脚的插值过渡带（col 6..12）：其余要么明显在相机侧开阔地，要么明显在脊后。
        int anchorCount = 0;
        for (int ix = -8; ix <= 8; ix++)
        {
            if (ix >= -3 && ix <= 4)
            {
                continue;
            }

            for (int iz = -2; iz <= 2; iz++)
            {
                var owner = world.Create(new CullState { IsVisible = true });
                var item = new WorldHudItem
                {
                    Owner = owner,
                    StableId = anchorCount + 1,
                    Kind = WorldHudItemKind.Bar,
                    WorldPosition = new Vector3(ix * 5f, 2f, iz * 5f),
                    Width = 40,
                    Height = 6,
                    DirtySerial = 1,
                };
                hudExact.TryAdd(in item);
                hudEnvelope.TryAdd(in item);
                anchorCount++;
            }
        }

        foreach (var pose in new[] { new Vector3(0, 45, -35), new Vector3(0, 6, -35), new Vector3(20, 3, -30) })
        {
            exactProjector.LookFrom(pose);
            envelopeProjector.LookFrom(pose);
            exact.Update(0);
            envelope.Update(0);
            var exactVisible = VisibleStableIds(screenExact);
            var envelopeVisible = VisibleStableIds(screenEnvelope);
            Assert.That(envelopeVisible, Is.SupersetOf(exactVisible),
                $"pose={pose}: envelope must not hide any anchor that exact raycast says visible");
        }

        exactProjector.LookFrom(new Vector3(0, 45, -35));
        exact.Update(0);
        Assert.That(VisibleStableIds(screenExact).Count, Is.EqualTo(anchorCount),
            "high pose over the ridge must show all open-field anchors");

        exactProjector.LookFrom(new Vector3(0, 6, -35));
        envelopeProjector.LookFrom(new Vector3(0, 6, -35));
        exact.Update(0);
        envelope.Update(0);
        Assert.That(VisibleStableIds(screenExact).Count, Is.LessThan(anchorCount),
            "low-graze pose must occlude anchors behind the ridge (exact)");
        Assert.That(VisibleStableIds(screenEnvelope).Count, Is.LessThan(anchorCount),
            "low-graze pose must occlude anchors behind the ridge (envelope)");
    }

    private static HashSet<int> VisibleStableIds(ScreenHudBatchBuffer screen)
    {
        var visible = new HashSet<int>();
        foreach (var bar in screen.GetBarSpan())
        {
            visible.Add(bar.StableId);
        }

        return visible;
    }

    private static short[] BuildRidge()
    {
        var h = new short[17 * 5];
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 17; x++)
            {
                h[y * 17 + x] = (short)(x >= 7 && x <= 11 ? 600 : 0);
            }
        }

        return h;
    }

    private sealed class PoseProjector : IScreenProjector, IProjectionSnapshotProvider, IViewController, IProjectionRevisionProvider
    {
        private readonly bool _provideCamera;
        private Matrix4x4 _matrix;
        private Vector3 _camera;
        public int ProjectionRevision { get; private set; }
        public Vector2 Resolution => new(1600, 900);
        public float Fov => 60f;
        public float AspectRatio => 16f / 9f;

        public PoseProjector(bool provideCamera)
        {
            _provideCamera = provideCamera;
            LookFrom(new Vector3(0, 45, -35));
        }

        public void LookFrom(Vector3 p)
        {
            _camera = p;
            _matrix = Matrix4x4.CreateLookAt(p, new Vector3(0, 2, 0), Vector3.UnitY) *
                      Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, AspectRatio, 0.1f, 1000f);
            ProjectionRevision++;
        }

        public bool TryGetProjectionSnapshot(out ProjectionSnapshot snapshot)
        {
            snapshot = _provideCamera
                ? new ProjectionSnapshot(_matrix, Resolution, _camera)
                : new ProjectionSnapshot(_matrix, Resolution);
            return true;
        }

        public Vector2 WorldToScreen(Vector3 position)
        {
            Vector4 clip = Vector4.Transform(new Vector4(position, 1), _matrix);
            return new Vector2((clip.X / clip.W + 1) * Resolution.X / 2, (1 - clip.Y / clip.W) * Resolution.Y / 2);
        }
    }
}
}
