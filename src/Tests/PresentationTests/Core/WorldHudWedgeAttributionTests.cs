using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

/// <summary>
/// 变体B归因诊断：把 WorldHudToScreenSystem.Update 的成本拆到
/// 地形遮挡 raycast（缓存命中/溢出全清/关闭）与值绑定刷新两条链上。
/// 只输出测量，不做门断言。
/// </summary>
[TestFixture]
[Explicit("变体B归因诊断")]
public sealed class WorldHudWedgeAttributionTests
{
    private const int Owners = 10000;

    [Test]
    public void OcclusionCostMatrix()
    {
        Run("dense      pan    horizon ", dispersed: false, pan: 10f, heightmap: true);
        Run("dispersed  hold   horizon ", dispersed: true, pan: 0.001f, heightmap: true);
        Run("dispersed  pan    horizon ", dispersed: true, pan: 10f, heightmap: true);
        Run("dispersed  pan    occl-off", dispersed: true, pan: 10f, heightmap: false);
    }

    /// <summary>
    /// 持续平移无悬崖合同：地平线包络每帧重算、无跨帧状态，
    /// 平移距离不应改变单帧成本（首尾桶中位数差应在噪声内）。
    /// </summary>
    [Test]
    public void SustainedPanNoDegradation()
    {
        const int totalFrames = 260;
        const int bucket = 40;
        using var fixture = new WedgeFixture(dispersed: true, heightmap: true);
        var times = new double[totalFrames];
        for (int frame = 0; frame < totalFrames; frame++)
        {
            fixture.Camera(frame * 10f);
            long start = Stopwatch.GetTimestamp();
            fixture.System.Update(0);
            times[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        var medians = new double[totalFrames / bucket];
        for (int b = 0; b < totalFrames / bucket; b++)
        {
            var slice = times[(b * bucket)..((b + 1) * bucket)];
            Array.Sort(slice);
            medians[b] = slice[bucket / 2];
            TestContext.Out.WriteLine(
                $"horizon sustained pan frames[{b * bucket},{(b + 1) * bucket}): median={medians[b]:F3}ms max={slice[^1]:F3}ms");
        }

        Assert.That(medians[^1], Is.LessThan(medians[0] * 3f + 1.0),
            "sustained panning must not build a cost cliff (first vs last bucket median)");
    }

    [Test]
    public void ValueBoundRefreshCost_LayoutCorrelation()
    {
        using var fixture = new WedgeFixture(dispersed: true, heightmap: false);
        fixture.Camera(0f);
        fixture.System.Update(0);
        Assert.That(fixture.Screen.TextCount, Is.GreaterThan(0));

        double medianStatic = MedianRefresh(fixture, churn: false);
        double medianChurn = MedianRefresh(fixture, churn: true);
        fixture.DecorrelateOwnerLayout(rounds: 4);
        double medianChurnDecorrelated = MedianRefresh(fixture, churn: true);
        TestContext.Out.WriteLine(
            $"refresh 10k value-bound texts: static={medianStatic:F4}ms churn={medianChurn:F4}ms churnAfterArchetypeShuffle={medianChurnDecorrelated:F4}ms");
    }

    private static void Run(string label, bool dispersed, float pan, bool heightmap)
    {
        using var fixture = new WedgeFixture(dispersed, heightmap);
        const int warm = 12;
        const int measure = 40;
        var times = new double[measure];
        for (int frame = 0; frame < warm + measure; frame++)
        {
            fixture.Camera(frame * pan);
            long start = Stopwatch.GetTimestamp();
            fixture.System.Update(0);
            if (frame >= warm)
            {
                times[frame - warm] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }

        Array.Sort(times);
        TestContext.Out.WriteLine(
            $"{label}: median={times[measure / 2]:F3}ms p95={times[measure * 19 / 20]:F3}ms " +
            $"max={times[^1]:F3}ms projected={fixture.Screen.Count}");
    }

    private static double MedianRefresh(WedgeFixture fixture, bool churn)
    {
        const int warm = 8;
        const int measure = 30;
        var times = new double[measure];
        double rebuildMedian = 0;
        var snapshot = new Ludots.Core.Presentation.Hud.HudOwnerFrameSnapshot();
        snapshot.RegisterTrackedAttribute(3);
        var rebuildTimes = new double[measure];
        for (int frame = 0; frame < warm + measure; frame++)
        {
            if (churn)
            {
                fixture.ChurnBoundValues(frame);
            }

            long rebuildStart = Stopwatch.GetTimestamp();
            snapshot.Rebuild(fixture.World);
            if (frame >= warm)
            {
                rebuildTimes[frame - warm] = Stopwatch.GetElapsedTime(rebuildStart).TotalMilliseconds;
            }

            long start = Stopwatch.GetTimestamp();
            fixture.Screen.RefreshAttributeBoundTexts(snapshot, fixture.World);
            if (frame >= warm)
            {
                times[frame - warm] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }

        Array.Sort(times);
        Array.Sort(rebuildTimes);
        rebuildMedian = rebuildTimes[measure / 2];
        TestContext.Out.WriteLine($"    snapshot rebuild median={rebuildMedian:F4}ms");
        return times[measure / 2];
    }

    private sealed class WedgeFixture : IDisposable
    {
        private const int BoundAttributeId = 3;
        private const float ExtentMeters = 400f;

        public readonly World World = World.Create();
        public readonly WorldHudBatchBuffer Hud;
        public readonly ScreenHudBatchBuffer Screen;
        public readonly PanningProjector Projector = new();
        public readonly WorldHudToScreenSystem System;
        private readonly bool _heightmap;
        private readonly ContinuousHeightmapRuntime? _terrain;
        private readonly List<Entity> _owners = new();

        public WedgeFixture(bool dispersed, bool heightmap)
        {
            _heightmap = heightmap;
            if (heightmap)
            {
                const int samples = 129;
                var heights = new short[samples * samples];
                for (int y = 0; y < samples; y++)
                {
                    for (int x = 0; x < samples; x++)
                    {
                        heights[y * samples + x] = (short)(((x * 7 + y * 13) % 23) * 30);
                    }
                }

                int sideCm = (int)(ExtentMeters * 2 * 100);
                int originCm = -sideCm / 2;
                _terrain = new ContinuousHeightmapRuntime(ContinuousHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(originCm, originCm, sideCm, sideCm), 3, 43, heights,
                    interpolationMode: ContinuousHeightmapInterpolationMode.TriangleHeightfield));
            }

            Hud = new WorldHudBatchBuffer(Owners * 2);
            Screen = new ScreenHudBatchBuffer(Owners * 2);
            System = new WorldHudToScreenSystem(
                World, Hud, null, Projector, Projector, Screen,
                heightmapProvider: () => _heightmap ? _terrain : null);

            int side = (int)Math.Ceiling(Math.Sqrt(Owners));
            for (int i = 0; i < Owners; i++)
            {
                float x;
                float z;
                if (dispersed)
                {
                    x = (i % side - side / 2f) * (ExtentMeters * 2 / side) + ((i * 37) % 7) * 0.1f;
                    z = (i / side - side / 2f) * (ExtentMeters * 2 / side) + ((i * 53) % 11) * 0.1f;
                }
                else
                {
                    x = ((i % 50) - 25) * 0.8f;
                    z = ((i / 50) - 100) * 0.8f;
                }

                var owner = World.Create(new CullState { IsVisible = true }, new Ludots.Core.Gameplay.GAS.Components.AttributeBuffer());
                _owners.Add(owner);
                Hud.TryAdd(new WorldHudItem
                {
                    Owner = owner,
                    StableId = i * 2 + 1,
                    Kind = WorldHudItemKind.Bar,
                    WorldPosition = new Vector3(x, 2f, z),
                    Width = 12,
                    Height = 3,
                    Value0 = 0.5f,
                    DirtySerial = 1,
                });
                Hud.TryAdd(new WorldHudItem
                {
                    Owner = owner,
                    StableId = i * 2 + 2,
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = new Vector3(x, 2.2f, z),
                    FontSize = 12,
                    Value0 = 0,
                    Id1 = (int)WorldHudValueMode.Constant,
                    ValueBound = 1,
                    BoundAttributeId = BoundAttributeId,
                    DirtySerial = 1,
                });
            }
        }

        public void Camera(float panMetersX)
        {
            Projector.LookFrom(new Vector3(panMetersX, 900f, -900f));
        }

        public void ChurnBoundValues(int frame)
        {
            foreach (var entity in _owners)
            {
                World.Get<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(entity).SetCurrent(BoundAttributeId, frame);
            }
        }

        public void DecorrelateOwnerLayout(int rounds)
        {
            for (int round = 0; round < rounds; round++)
            {
                for (int index = 0; index < _owners.Count; index += 2)
                {
                    World.Add<ShuffleTag>(_owners[index]);
                }

                for (int index = 0; index < _owners.Count; index += 2)
                {
                    World.Remove<ShuffleTag>(_owners[index]);
                }
            }
        }

        public void Dispose() => World.Destroy(World);
    }

    private struct ShuffleTag;

    private sealed class PanningProjector : IScreenProjector, IProjectionSnapshotProvider, IViewController, IProjectionRevisionProvider
    {
        private Matrix4x4 _matrix;
        private Vector3 _camera;
        public int ProjectionRevision { get; private set; }
        public Vector2 Resolution => new(1600, 900);
        public float Fov => 60f;
        public float AspectRatio => 16f / 9f;

        public void LookFrom(Vector3 position)
        {
            _camera = position;
            _matrix = Matrix4x4.CreateLookAt(position, new Vector3(position.X, 0, 0), Vector3.UnitY) *
                      Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, AspectRatio, 0.1f, 5000f);
            ProjectionRevision++;
        }

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
