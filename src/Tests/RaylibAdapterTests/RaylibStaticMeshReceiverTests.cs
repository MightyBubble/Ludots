using System;
using System.Numerics;
using Ludots.Adapter.Raylib;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibStaticMeshReceiverTests
{
    [Test]
    public void ReceiverSurface_ImplementsReceiverMeshProjectorContract()
    {
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        Assert.That(renderer.StaticMeshReceiverProjector, Is.InstanceOf<IRaylibReceiverMeshProjector>());
    }

    [Test]
    public void ReceiverSurface_AabbDrawRejectsNonFiniteBoundsBeforeTouchingGpu()
    {
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        IRaylibReceiverMeshProjector projector = renderer.StaticMeshReceiverProjector;
        Assert.That(
            () => projector.DrawMeshesOverlappingAabbMeters(
                float.NaN, 0f, 0f, 10f, 10f, 10f, default(Material)),
            Throws.ArgumentException.With.Message.Contains("finite"));
        Assert.That(
            () => projector.DrawMeshesOverlappingAabbMeters(
                0f, 0f, 0f, 10f, float.PositiveInfinity, 10f, default(Material)),
            Throws.ArgumentException.With.Message.Contains("finite"));
    }

    [Test]
    public void ReceiverSurface_AabbDrawRejectsInvertedBoundsBeforeTouchingGpu()
    {
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        IRaylibReceiverMeshProjector projector = renderer.StaticMeshReceiverProjector;
        Assert.That(
            () => projector.DrawMeshesOverlappingAabbMeters(
                10f, 0f, 0f, 0f, 10f, 10f, default(Material)),
            Throws.ArgumentException.With.Message.Contains("min must be <= max"));
    }

    [Test]
    public void ReceiverSurface_FitWithoutHeightSamplingThrowsInsteadOfUsingAuthoredY()
    {
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        IRaylibReceiverMeshProjector projector = renderer.StaticMeshReceiverProjector;
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => projector.FitYawedStampProjectorCenter(
                new Vector3(1f, 9f, 2f),
                0f,
                new Vector2(3.8f, 4.2f),
                81))!;
        Assert.That(ex.Message, Does.Contain("no height sampling"));
        Assert.That(ex.Message, Does.Contain("terrain receiver"));
    }

    [Test]
    public void ReceiverSurface_EmptyLaneDrawsZeroMeshesInsteadOfThrowing()
    {
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        IRaylibReceiverMeshProjector projector = renderer.StaticMeshReceiverProjector;
        Assert.That(
            projector.DrawMeshesOverlappingAabbMeters(0f, 0f, 0f, 10f, 10f, 10f, default(Material)),
            Is.EqualTo(0));
    }

    [Test]
    public unsafe void ComputeModelLocalAabbMeters_TracksLoadedMeshVertexExtents()
    {
        Span<float> vertices = stackalloc float[]
        {
            2f, 4f, 6f,
            -1f, 8f, 3f,
            0.5f, 5f, -2f,
        };
        fixed (float* p = vertices)
        {
            var mesh = new Mesh
            {
                vertexCount = 3,
                vertices = p,
            };
            RaylibPrimitiveRenderer.ComputeModelLocalAabbMeters(
                new[] { mesh },
                out Vector3 localMin,
                out Vector3 localMax);

            Assert.That(localMin, Is.EqualTo(new Vector3(-1f, 4f, -2f)));
            Assert.That(localMax, Is.EqualTo(new Vector3(2f, 8f, 6f)));
        }
    }

    [Test]
    public unsafe void ComputeModelLocalAabbMeters_ThrowsWhenModelHasNoVertices()
    {
        Assert.That(
            () => RaylibPrimitiveRenderer.ComputeModelLocalAabbMeters(
                new Mesh[] { new Mesh { vertexCount = 0, vertices = null } },
                out _,
                out _),
            Throws.InvalidOperationException.With.Message.Contains("no vertices"));
    }

    [Test]
    public void MapLaneComposite_DrawSumsTerrainAndStaticMeshReceivers()
    {
        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.ContinuousHeightmap, new RenderSourceHeightmapStub());
        var terrainReceiver = new CountingReceiverStub { DrawnCount = 2 };
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        var composite = new RaylibHostLoop.MapLaneReceiverMeshProjector(
            engine,
            terrainReceiver,
            new CountingReceiverStub(),
            renderer.StaticMeshReceiverProjector);

        int drawn = composite.DrawMeshesOverlappingAabbMeters(0f, 0f, 0f, 10f, 10f, 10f, default(Material));

        Assert.That(drawn, Is.EqualTo(2));
        Assert.That(terrainReceiver.DrawCalls, Is.EqualTo(1));
    }

    [Test]
    public void MapLaneComposite_DrawWithoutTerrainLaneDelegatesToStaticReceiverOnly()
    {
        using var engine = new GameEngine();
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        var composite = new RaylibHostLoop.MapLaneReceiverMeshProjector(
            engine,
            new CountingReceiverStub(),
            new CountingReceiverStub(),
            renderer.StaticMeshReceiverProjector);

        Assert.That(
            composite.DrawMeshesOverlappingAabbMeters(0f, 0f, 0f, 10f, 10f, 10f, default(Material)),
            Is.EqualTo(0));
    }

    [Test]
    public void MapLaneComposite_FitRoutesToTerrainReceiverNotStaticMesh()
    {
        using var engine = new GameEngine();
        engine.SetService(CoreServiceKeys.ContinuousHeightmap, new RenderSourceHeightmapStub());
        var terrainReceiver = new CountingReceiverStub { FittedY = 7f };
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        var composite = new RaylibHostLoop.MapLaneReceiverMeshProjector(
            engine,
            terrainReceiver,
            new CountingReceiverStub(),
            renderer.StaticMeshReceiverProjector);

        Vector3 center = composite.FitYawedStampProjectorCenter(
            new Vector3(1f, 99f, 2f),
            0f,
            new Vector2(3.8f, 4.2f),
            82);

        Assert.That(terrainReceiver.FitCalls, Is.EqualTo(1));
        Assert.That(center, Is.EqualTo(new Vector3(1f, 7f, 2f)));
    }

    [Test]
    public void MapLaneComposite_WithoutTerrainLaneFitThrowsInsteadOfLeavingAuthoredY()
    {
        using var engine = new GameEngine();
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        var composite = new RaylibHostLoop.MapLaneReceiverMeshProjector(
            engine,
            new CountingReceiverStub(),
            new CountingReceiverStub(),
            renderer.StaticMeshReceiverProjector);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => composite.FitYawedStampProjectorCenter(
                new Vector3(1f, 99f, 2f),
                0f,
                new Vector2(3.8f, 4.2f),
                83))!;
        Assert.That(ex.Message, Does.Contain("height-sampling terrain receiver"));
        Assert.That(ex.Message, Does.Contain("static mesh receiver cannot fit"));
    }

    [Test]
    public void MapLaneComposite_RejectsNullReceivers()
    {
        using var engine = new GameEngine();
        using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate);
        var receivers = new IRaylibReceiverMeshProjector[]
        {
            renderer.StaticMeshReceiverProjector,
            new CountingReceiverStub(),
        };

        Assert.That(
            () => new RaylibHostLoop.MapLaneReceiverMeshProjector(null!, receivers[1], receivers[1], receivers[0]),
            Throws.ArgumentNullException);
        Assert.That(
            () => new RaylibHostLoop.MapLaneReceiverMeshProjector(engine, null!, receivers[1], receivers[0]),
            Throws.ArgumentNullException);
        Assert.That(
            () => new RaylibHostLoop.MapLaneReceiverMeshProjector(engine, receivers[1], null!, receivers[0]),
            Throws.ArgumentNullException);
        Assert.That(
            () => new RaylibHostLoop.MapLaneReceiverMeshProjector(engine, receivers[1], receivers[1], null!),
            Throws.ArgumentNullException);
    }

    private sealed class CountingReceiverStub : IRaylibReceiverMeshProjector
    {
        public int DrawCalls;
        public int FitCalls;
        public int DrawnCount;
        public float FittedY;

        public int DrawMeshesOverlappingAabbMeters(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            Material material)
        {
            DrawCalls++;
            return DrawnCount;
        }

        public Vector3 FitYawedStampProjectorCenter(
            in Vector3 stampCenter,
            float yawRad,
            in Vector2 stampSizeMeters,
            int stableId)
        {
            FitCalls++;
            return new Vector3(stampCenter.X, FittedY, stampCenter.Z);
        }
    }

    private sealed class RenderSourceHeightmapStub : IContinuousHeightmap, IContinuousHeightmapRenderSource
    {
        public WorldAabbCm Bounds => new(0, 0, 100, 100);

        public int ChunkColumns => 1;

        public int ChunkRows => 1;

        public int SamplesPerChunkColumn => 4;

        public int SamplesPerChunkRow => 4;

        public int DefaultLayerIndex => 0;

        public int Revision => 1;

        public ContinuousHeightmapRenderProfile RenderProfile => new();

        public bool TryGetChunk(int chunkX, int chunkY, out ContinuousHeightmapRenderChunk chunk)
        {
            chunk = default;
            return false;
        }

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = 0f;
            return true;
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
            {
                return false;
            }

            outHeightCm.Clear();
            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            hit = default;
            return false;
        }

        public bool RaycastGroundBatch(
            ReadOnlySpan<float> originXMeters,
            ReadOnlySpan<float> originYMeters,
            ReadOnlySpan<float> originZMeters,
            ReadOnlySpan<float> directionX,
            ReadOnlySpan<float> directionY,
            ReadOnlySpan<float> directionZ,
            Span<float> outWorldXCm,
            Span<float> outWorldYCm,
            Span<float> outHeightCm,
            Span<float> outDistanceMeters,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outLayerIndex,
            Span<byte> outHitMask,
            int layerIndex = -1)
        {
            outHitMask.Clear();
            return false;
        }
    }
}
