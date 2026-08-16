using System;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;
using Raylib_cs;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibVisualHeightmapRendererTests
{
    [Test]
    public void ShouldUseOverviewMesh_WhenCameraFramesEastAsiaScaleTerrain()
    {
        var source = new FakeVisualHeightmapRenderSource(
            new WorldAabbCm(-450_326_016, -257_329_152, 900_652_032, 514_658_304),
            chunkColumns: 224,
            chunkRows: 128);
        var camera = new Camera3D
        {
            position = new System.Numerics.Vector3(0f, 6_750_000f, 0f),
            target = System.Numerics.Vector3.Zero,
            fovy = 55f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        bool useOverview = RaylibVisualHeightmapRenderer.ShouldUseOverviewMesh(
            source,
            in camera,
            aspect: 16f / 9f,
            detailVisibleRadiusCm: 140_000f,
            activationMultiplier: 2f);

        Assert.That(useOverview, Is.True);
    }

    [Test]
    public void ShouldUseOverviewMesh_WhenCameraIsNearTerrain_ReturnsFalse()
    {
        var source = new FakeVisualHeightmapRenderSource(
            new WorldAabbCm(-450_326_016, -257_329_152, 900_652_032, 514_658_304),
            chunkColumns: 224,
            chunkRows: 128);
        var camera = new Camera3D
        {
            position = new System.Numerics.Vector3(0f, 1_000f, 0f),
            target = System.Numerics.Vector3.Zero,
            fovy = 55f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        bool useOverview = RaylibVisualHeightmapRenderer.ShouldUseOverviewMesh(
            source,
            in camera,
            aspect: 16f / 9f,
            detailVisibleRadiusCm: 140_000f,
            activationMultiplier: 2f);

        Assert.That(useOverview, Is.False);
    }

    [Test]
    public void ResolveOverviewStepChunks_KeepsLargeMapOverviewUnderRaylibVertexLimit()
    {
        int step = RaylibVisualHeightmapRenderer.ResolveOverviewStepChunks(
            chunkColumns: 1024,
            chunkRows: 1024,
            maxVertices: 60_000);

        int columns = RaylibVisualHeightmapRenderer.ResolveOverviewAxisPointCount(1024, step);
        int rows = RaylibVisualHeightmapRenderer.ResolveOverviewAxisPointCount(1024, step);

        Assert.That(columns * rows, Is.LessThanOrEqualTo(60_000));
        Assert.That(columns * rows, Is.LessThanOrEqualTo(ushort.MaxValue));
    }

    [Test]
    public void ResolveOverviewTextureSize_UsesScreenScaledResolutionForEastAsiaEditing()
    {
        RaylibVisualHeightmapRenderer.ResolveOverviewTextureSize(
            new WorldAabbCm(-450_326_016, -257_329_152, 900_652_032, 514_658_304),
            screenWidth: 1600,
            screenHeight: 900,
            out int textureWidth,
            out int textureHeight);

        Assert.That(textureWidth, Is.EqualTo(3072));
        Assert.That(textureHeight, Is.EqualTo(1755));
        Assert.That(textureWidth, Is.GreaterThan(112 * 8));
        Assert.That(textureHeight, Is.GreaterThan(64 * 8));
    }

    [Test]
    public void ResolveChunkSampleStride_KeepsImportedEditorChunkUnderRaylibVertexLimit()
    {
        int stride = RaylibVisualHeightmapRenderer.ResolveChunkSampleStride(
            sampleColumns: 257,
            sampleRows: 257);

        int columns = RaylibVisualHeightmapRenderer.ResolveChunkSampleAxisPointCount(257, stride);
        int rows = RaylibVisualHeightmapRenderer.ResolveChunkSampleAxisPointCount(257, stride);

        Assert.That(stride, Is.EqualTo(2));
        Assert.That(columns, Is.EqualTo(129));
        Assert.That(rows, Is.EqualTo(129));
        Assert.That(columns * rows, Is.LessThanOrEqualTo(ushort.MaxValue));
        Assert.That(RaylibVisualHeightmapRenderer.ResolveChunkSourceSampleIndex(columns - 1, 257, stride), Is.EqualTo(256));
    }

    [Test]
    public void ResolveChunkRenderSampling_UsesDecimatedGridForEditorChunk()
    {
        RaylibVisualHeightmapRenderer.ResolveChunkRenderSampling(
            sampleColumns: 257,
            sampleRows: 257,
            out int renderColumns,
            out int renderRows,
            out int sampleStride);

        Assert.That(sampleStride, Is.EqualTo(2));
        Assert.That(renderColumns, Is.EqualTo(129));
        Assert.That(renderRows, Is.EqualTo(129));
        Assert.That(renderColumns * renderRows, Is.LessThanOrEqualTo(ushort.MaxValue));
        Assert.That(
            RaylibVisualHeightmapRenderer.ResolveChunkSourceSampleIndex(renderColumns - 1, 257, sampleStride),
            Is.EqualTo(256));
    }

    private sealed class FakeVisualHeightmapRenderSource : IVisualHeightmapRenderSource
    {
        public FakeVisualHeightmapRenderSource(WorldAabbCm bounds, int chunkColumns, int chunkRows)
        {
            Bounds = bounds;
            ChunkColumns = chunkColumns;
            ChunkRows = chunkRows;
        }

        public WorldAabbCm Bounds { get; }

        public int ChunkColumns { get; }

        public int ChunkRows { get; }

        public int SamplesPerChunkColumn => 33;

        public int SamplesPerChunkRow => 33;

        public int DefaultLayerIndex => 0;

        public int Revision => 0;

        public VisualHeightmapRenderProfile RenderProfile { get; } = VisualHeightmapRenderProfile.CreateDefault();

        public bool TryGetChunk(int chunkX, int chunkY, out VisualHeightmapRenderChunk chunk)
        {
            throw new NotSupportedException();
        }
    }
}
