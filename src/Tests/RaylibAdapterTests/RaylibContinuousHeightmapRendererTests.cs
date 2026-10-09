using System;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;
using Raylib_cs;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibContinuousHeightmapRendererTests
{
    [Test]
    public void ShouldUseOverviewMesh_WhenCameraFramesEastAsiaScaleTerrain()
    {
        var source = new FakeContinuousHeightmapRenderSource(
            new WorldAabbCm(-3_199_616, -1_828_352, 6_399_232, 3_656_704),
            chunkColumns: 224,
            chunkRows: 128);
        var camera = new Camera3D
        {
            position = new System.Numerics.Vector3(0f, 70_000f, 0f),
            target = System.Numerics.Vector3.Zero,
            fovy = 55f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        bool useOverview = RaylibContinuousHeightmapRenderer.ShouldUseOverviewMesh(
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
        var source = new FakeContinuousHeightmapRenderSource(
            new WorldAabbCm(-3_199_616, -1_828_352, 6_399_232, 3_656_704),
            chunkColumns: 224,
            chunkRows: 128);
        var camera = new Camera3D
        {
            position = new System.Numerics.Vector3(0f, 1_000f, 0f),
            target = System.Numerics.Vector3.Zero,
            fovy = 55f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        bool useOverview = RaylibContinuousHeightmapRenderer.ShouldUseOverviewMesh(
            source,
            in camera,
            aspect: 16f / 9f,
            detailVisibleRadiusCm: 140_000f,
            activationMultiplier: 2f);

        Assert.That(useOverview, Is.False);
    }

    [Test]
    public void ResolveAbsoluteHeightBand_TreatsOceanSentinelAbovePeakSpanAsOpenWater()
    {
        Assert.That(
            RaylibContinuousHeightmapRenderer.ResolveAbsoluteHeightBand(
                heightCm: 2_132f,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 5_000f),
            Is.EqualTo(2_132f / 5_000f).Within(0.0001f));
        Assert.That(
            RaylibContinuousHeightmapRenderer.ResolveAbsoluteHeightBand(
                heightCm: 59_563f,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 5_000f),
            Is.LessThan(0f));
        Assert.That(
            RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                heightCm: 59_563f,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 5_000f),
            Is.EqualTo(0f));
        Assert.That(
            RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                heightCm: 2_132f,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 5_000f),
            Is.EqualTo(2_132f));
        Assert.That(
            RaylibContinuousHeightmapRenderer.ResolveAbsoluteDisplayHeightCm(
                heightCm: -6_000f,
                seaLevelCm: 0f,
                absolutePeakSpanCm: 5_000f),
            Is.EqualTo(0f),
            "Authored bathymetry below sea must flatten under absolute display so continental scale does not dig ocean pits.");
    }

    [Test]
    public void ResolveOverviewStepChunks_KeepsLargeMapOverviewUnderRaylibVertexLimit()
    {
        int step = RaylibContinuousHeightmapRenderer.ResolveOverviewStepChunks(
            chunkColumns: 1024,
            chunkRows: 1024,
            maxVertices: 60_000);

        int columns = RaylibContinuousHeightmapRenderer.ResolveOverviewAxisPointCount(1024, step);
        int rows = RaylibContinuousHeightmapRenderer.ResolveOverviewAxisPointCount(1024, step);

        Assert.That(columns * rows, Is.LessThanOrEqualTo(60_000));
        Assert.That(columns * rows, Is.LessThanOrEqualTo(ushort.MaxValue));
    }

    [Test]
    public void ResolveOverviewTextureSize_UsesScreenScaledResolutionForEastAsiaEditing()
    {
        RaylibContinuousHeightmapRenderer.ResolveOverviewTextureSize(
            new WorldAabbCm(-3_199_616, -1_828_352, 6_399_232, 3_656_704),
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
        int stride = RaylibContinuousHeightmapRenderer.ResolveChunkSampleStride(
            sampleColumns: 257,
            sampleRows: 257);

        int columns = RaylibContinuousHeightmapRenderer.ResolveChunkSampleAxisPointCount(257, stride);
        int rows = RaylibContinuousHeightmapRenderer.ResolveChunkSampleAxisPointCount(257, stride);

        Assert.That(stride, Is.EqualTo(2));
        Assert.That(columns, Is.EqualTo(129));
        Assert.That(rows, Is.EqualTo(129));
        Assert.That(columns * rows, Is.LessThanOrEqualTo(ushort.MaxValue));
        Assert.That(RaylibContinuousHeightmapRenderer.ResolveChunkSourceSampleIndex(columns - 1, 257, stride), Is.EqualTo(256));
    }

    [Test]
    public void ResolveChunkRenderSampling_UsesDecimatedGridForEditorChunk()
    {
        RaylibContinuousHeightmapRenderer.ResolveChunkRenderSampling(
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
            RaylibContinuousHeightmapRenderer.ResolveChunkSourceSampleIndex(renderColumns - 1, 257, sampleStride),
            Is.EqualTo(256));
    }

    private sealed class FakeContinuousHeightmapRenderSource : IContinuousHeightmapRenderSource
    {
        public FakeContinuousHeightmapRenderSource(WorldAabbCm bounds, int chunkColumns, int chunkRows)
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

        public ContinuousHeightmapRenderProfile RenderProfile { get; } = ContinuousHeightmapRenderProfile.CreateDefault();

        public bool TryGetChunk(int chunkX, int chunkY, out ContinuousHeightmapRenderChunk chunk)
        {
            throw new NotSupportedException();
        }
    }

    [Test]
    public void ExtractFrustumPlanes_AppOrbitConvention_KeepsChunkUnderTarget()
    {
        // 复刻应用的轨道相机:position = target + VisualCameraTargetToCameraOffset(yaw, pitch, dist)
        var target = new System.Numerics.Vector3(2048f, 90f, 2048f);
        System.Numerics.Vector3 offset = WorldPlane2D.VisualCameraTargetToCameraOffset(40f, 46f, 52f);
        var camera = new Camera3D
        {
            position = target + offset,
            target = target,
            up = System.Numerics.Vector3.UnitY,
            fovy = 48f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };
        Span<System.Numerics.Vector4> planes = stackalloc System.Numerics.Vector4[6];
        RaylibContinuousHeightmapRenderer.ExtractFrustumPlanes(in camera, aspect: 16f / 9f, planes);

        // 目标脚下 64m chunk:必须保留
        Assert.That(
            RaylibContinuousHeightmapRenderer.IsAabbOutsideFrustum(
                planes, target.X - 32f, target.Y - 20f, target.Z - 32f, target.X + 32f, target.Y + 20f, target.Z + 32f),
            Is.False,
            $"planes={string.Join(';', planes.ToArray())} offset={offset}");
    }

    [Test]
    public void ExtractFrustumPlanes_KeepsBoxInFront_AndCullsBehindAndSides()
    {
        var camera = new Camera3D
        {
            position = new System.Numerics.Vector3(0f, 500f, 500f),
            target = new System.Numerics.Vector3(0f, 0f, 0f),
            up = System.Numerics.Vector3.UnitY,
            fovy = 60f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };
        Span<System.Numerics.Vector4> planes = stackalloc System.Numerics.Vector4[6];
        RaylibContinuousHeightmapRenderer.ExtractFrustumPlanes(in camera, aspect: 16f / 9f, planes);

        // 正前方地面块:保留
        Assert.That(
            RaylibContinuousHeightmapRenderer.IsAabbOutsideFrustum(planes, -50f, -10f, -50f, 50f, 10f, 50f),
            Is.False);
        // 相机正后方(沿视线反向 300m,高度同层):剔除
        Assert.That(
            RaylibContinuousHeightmapRenderer.IsAabbOutsideFrustum(planes, -50f, 750f, 750f, 50f, 850f, 850f),
            Is.True);
        // 远超左侧视野:剔除
        Assert.That(
            RaylibContinuousHeightmapRenderer.IsAabbOutsideFrustum(planes, -50_000f, -10f, -50f, -49_000f, 10_000f, 50f),
            Is.True);
        // 覆盖相机位置的巨型块(横跨视野):保留
        Assert.That(
            RaylibContinuousHeightmapRenderer.IsAabbOutsideFrustum(planes, -5_000f, -10f, -5_000f, 5_000f, 3_000f, 5_000f),
            Is.False);
    }

    [Test]
    public void ExtractFrustumPlanes_DegenerateUp_LeavesPlanesZeroed()
    {
        var camera = new Camera3D
        {
            position = new System.Numerics.Vector3(0f, 500f, 0f),
            target = System.Numerics.Vector3.Zero,
            up = System.Numerics.Vector3.Zero,
            fovy = 60f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };
        Span<System.Numerics.Vector4> planes = stackalloc System.Numerics.Vector4[6];
        RaylibContinuousHeightmapRenderer.ExtractFrustumPlanes(in camera, aspect: 16f / 9f, planes);

        // 视线与 up 平行时无剔除(全零平面):任何 AABB 都保留
        Assert.That(
            RaylibContinuousHeightmapRenderer.IsAabbOutsideFrustum(planes, 1e6f, -1f, 1e6f, 1e6f + 1f, 1f, 1e6f + 1f),
            Is.False);
    }

    [Test]
    public void ComputeNormal_BorderSampleWithWorldSampler_MatchesUnifiedFieldNormal()
    {
        // 非线性高程:统一场 5 列高度 0/10/35/40/50 cm,步长 100cm
        short[] field = { 0, 10, 35, 40, 50 };
        short[] fieldRows = new short[field.Length * 2];
        field.CopyTo(fieldRows, 0);
        field.CopyTo(fieldRows, field.Length);
        var sampler = new GridHeightmapSampler(bounds: new WorldAabbCm(0, 0, 500, 200), columns: 5, rows: 2, samples: fieldRows);
        ContinuousHeightmapRenderChunk leftChunk = CreateChunk(chunkX: 0, columns: 3, samples: field.AsMemory(0, 3));
        ContinuousHeightmapRenderChunk rightChunk = CreateChunk(chunkX: 1, columns: 3, samples: field.AsMemory(2, 3));
        ContinuousHeightmapRenderChunk unified = CreateChunk(chunkX: 0, columns: 5, samples: field.AsMemory(0, 5));

        // 左块右边界(x=2,世界列 2):真中心差分应取世界列 1/3(10 与 40)
        System.Numerics.Vector3 withSampler = RaylibContinuousHeightmapRenderer.ComputeNormal(
            in leftChunk, x: 2, y: 0,
            worldXCm: 200f, worldYCm: 50f,
            stepXCm: 100f, stepYCm: 100f,
            displayHeightScale: 1f, absoluteSeaCm: null, absolutePeakSpanCm: 3600f,
            worldSampler: sampler);
        System.Numerics.Vector3 unifiedNormal = RaylibContinuousHeightmapRenderer.ComputeNormal(
            in unified, x: 2, y: 0,
            worldXCm: 200f, worldYCm: 50f,
            stepXCm: 100f, stepYCm: 100f,
            displayHeightScale: 1f, absoluteSeaCm: null, absolutePeakSpanCm: 3600f,
            worldSampler: null);

        Assert.That(withSampler.X, Is.EqualTo(unifiedNormal.X).Within(1e-4f));
        Assert.That(withSampler.Z, Is.EqualTo(unifiedNormal.Z).Within(1e-4f));

        // 右块左边界(x=0,同一世界列 2):跨界采样后与左块边界法线一致(接缝消除的合同)
        System.Numerics.Vector3 rightWithSampler = RaylibContinuousHeightmapRenderer.ComputeNormal(
            in rightChunk, x: 0, y: 0,
            worldXCm: 200f, worldYCm: 50f,
            stepXCm: 100f, stepYCm: 100f,
            displayHeightScale: 1f, absoluteSeaCm: null, absolutePeakSpanCm: 3600f,
            worldSampler: sampler);
        Assert.That(rightWithSampler.X, Is.EqualTo(withSampler.X).Within(1e-4f));

        // 无采样器回退(旧行为):单边差分,与统一场法线不一致——证明接缝来源
        System.Numerics.Vector3 clamped = RaylibContinuousHeightmapRenderer.ComputeNormal(
            in leftChunk, x: 2, y: 0,
            worldXCm: 200f, worldYCm: 50f,
            stepXCm: 100f, stepYCm: 100f,
            displayHeightScale: 1f, absoluteSeaCm: null, absolutePeakSpanCm: 3600f,
            worldSampler: null);
        Assert.That(clamped.X, Is.Not.EqualTo(unifiedNormal.X).Within(1e-3f));
    }

    private static ContinuousHeightmapRenderChunk CreateChunk(int chunkX, int columns, ReadOnlyMemory<short> samples)
    {
        // chunk 契约要求 sampleRows >= 2:单行高程复制为两行
        short[] row = samples.ToArray();
        short[] buffer = new short[row.Length * 2];
        row.CopyTo(buffer, 0);
        row.CopyTo(buffer, row.Length);
        return new ContinuousHeightmapRenderChunk(
            chunkX,
            chunkY: 0,
            bounds: new WorldAabbCm(chunkX * (columns - 1) * 100, 0, (columns - 1) * 100, 100),
            sampleColumns: columns,
            sampleRows: 2,
            sampleStepXCm: 100f,
            sampleStepYCm: 100f,
            heightSamplesCm: buffer,
            heightSamplesRaw: default,
            sampleScale: default,
            storageLayout: ContinuousHeightmapStorageLayout.RowMajorInt16Centimeters,
            sampleStride: columns,
            layerSampleOffset: 0,
            revision: 1);
    }

    private sealed class GridHeightmapSampler : IContinuousHeightmap
    {
        private readonly WorldAabbCm _bounds;
        private readonly int _columns;
        private readonly int _rows;
        private readonly short[] _samples;

        public GridHeightmapSampler(WorldAabbCm bounds, int columns, int rows, short[] samples)
        {
            _bounds = bounds;
            _columns = columns;
            _rows = rows;
            _samples = samples;
        }

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = 0f;
            if (worldXCm < _bounds.Left || worldXCm > _bounds.Right || worldYCm < _bounds.Top || worldYCm > _bounds.Bottom)
            {
                return false;
            }

            int x = (int)MathF.Round((worldXCm - _bounds.Left) / 100f);
            int y = (int)MathF.Round((worldYCm - _bounds.Top) / 100f);
            if ((uint)x >= (uint)_columns || (uint)y >= (uint)_rows)
            {
                return false;
            }

            heightCm = _samples[(y * _columns) + x];
            return true;
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            throw new NotSupportedException();
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            throw new NotSupportedException();
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
            throw new NotSupportedException();
        }
    }
}
