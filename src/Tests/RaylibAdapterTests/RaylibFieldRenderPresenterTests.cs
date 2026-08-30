using System.Diagnostics;
using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Vision;
using NUnit.Framework;
using Raylib_cs;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
[Category("ci-gate")]
[Category("raylib-field")]
public sealed class RaylibFieldRenderPresenterTests
{
    [Test]
    public void BuildTexturePlan_StagesGlobalFogFieldBufferIntoStableTexture()
    {
        GlobalFieldVisualBuffer buffer = CreateBuffer(out FogLayerId layerId, out FogField field);
        field.SetVisible(new FogCell(0, 0));
        field.SetExplored(new FogCell(1, 0));
        field.SetDenied(new FogCell(2, 0));
        ProjectFog(buffer, field);

        var presenter = new RaylibFieldRenderPresenter();
        ReadOnlySpan<RaylibFieldTexturePlan> plans = presenter.BuildTexturePlan(buffer);

        Assert.That(plans.Length, Is.EqualTo(1));
        Assert.That(presenter.LastFieldTextureCount, Is.EqualTo(1));
        Assert.That(presenter.LastFieldCellCount, Is.EqualTo(3));
        Assert.That(presenter.LastDirtyUploadCount, Is.EqualTo(1));
        Assert.That(presenter.LastDirtyUploadArea, Is.EqualTo(256));
        Assert.That(plans[0].FullUpload, Is.True);
        Assert.That(plans[0].Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Fog));
        Assert.That(plans[0].Id.ScopeKeyId, Is.EqualTo(1));
        Assert.That(plans[0].Id.LayerKeyId, Is.EqualTo(layerId.Value));
        Assert.That(plans[0].BoundsCells, Is.EqualTo(new Ludots.Core.Mathematics.IntRect(0, 0, 16, 16)));
        Assert.That(plans[0].TextureWidth, Is.EqualTo(16));
        Assert.That(plans[0].TextureHeight, Is.EqualTo(16));

        Assert.That(presenter.TryGetStagedPixel(plans[0].Id, new FieldCell2D(0, 0), out Color visible), Is.True);
        Assert.That(presenter.TryGetStagedPixel(plans[0].Id, new FieldCell2D(1, 0), out Color explored), Is.True);
        Assert.That(presenter.TryGetStagedPixel(plans[0].Id, new FieldCell2D(2, 0), out Color denied), Is.True);
        Assert.That(presenter.TryGetStagedPixel(plans[0].Id, new FieldCell2D(3, 0), out Color unseen), Is.True);
        Assert.That(visible, Is.EqualTo(RaylibFieldRenderPresenter.ResolveFogColor((byte)CellVisibility.Visible)));
        Assert.That(explored.a, Is.GreaterThan(visible.a));
        Assert.That(denied.r, Is.GreaterThan(denied.g));
        Assert.That(unseen.a, Is.GreaterThan(explored.a));
    }

    [Test]
    public void BuildTexturePlan_StagesDiscreteOwnershipBytePaletteWithoutFogFallback()
    {
        var buffer = new GlobalFieldVisualBuffer(2, 8, 2);
        var id = new GlobalFieldVisualId(
            GlobalFieldVisualKind.DiscreteOwnership,
            scopeKeyId: 1,
            layerKeyId: 3,
            surfaceKeyId: 0);
        var descriptor = new GlobalFieldVisualDescriptor(
            id,
            cellSizeCm: 100,
            WorldCmInt2.Zero,
            new IntRect(0, 0, 2, 1),
            GlobalFieldVisualValueKind.Byte,
            paletteId: 3);
        GlobalFieldVisualCell[] cells =
        {
            new(new FieldCell2D(0, 0), byteValue: 7),
        };
        IntRect[] dirty = { new(0, 0, 2, 1) };
        buffer.BeginFrame();
        buffer.Upsert(descriptor, cells, dirty);

        var presenter = new RaylibFieldRenderPresenter();
        ReadOnlySpan<RaylibFieldTexturePlan> plans = presenter.BuildTexturePlan(buffer);

        Assert.That(plans.Length, Is.EqualTo(1));
        Assert.That(plans[0].Id.Kind, Is.EqualTo(GlobalFieldVisualKind.DiscreteOwnership));
        Assert.That(presenter.LastUnsupportedFieldCount, Is.Zero);
        Assert.That(presenter.TryGetStagedPixel(id, new FieldCell2D(0, 0), out Color color), Is.True);
        Assert.That(color, Is.EqualTo(RaylibFieldRenderPresenter.ResolveDiscreteOwnershipColor(7, 3)));
        Assert.That(presenter.TryGetStagedPixel(id, new FieldCell2D(1, 0), out Color empty), Is.True);
        Assert.That(empty.a, Is.Zero);
    }

    [Test]
    public void BuildTexturePlan_StagesDiscreteOwnershipVectorPalette()
    {
        var buffer = new GlobalFieldVisualBuffer(2, 8, 2);
        var id = new GlobalFieldVisualId(
            GlobalFieldVisualKind.DiscreteOwnership,
            scopeKeyId: 1,
            layerKeyId: 1,
            surfaceKeyId: 0);
        var descriptor = new GlobalFieldVisualDescriptor(
            id,
            cellSizeCm: 100,
            WorldCmInt2.Zero,
            new IntRect(0, 0, 1, 1),
            GlobalFieldVisualValueKind.Vector4);
        GlobalFieldVisualCell[] cells =
        {
            new(new FieldCell2D(0, 0), new System.Numerics.Vector4(0.2f, 0.4f, 0.6f, 0.5f)),
        };
        buffer.BeginFrame();
        buffer.Upsert(descriptor, cells, new[] { new IntRect(0, 0, 1, 1) });

        var presenter = new RaylibFieldRenderPresenter();
        presenter.BuildTexturePlan(buffer);

        Assert.That(presenter.TryGetStagedPixel(id, new FieldCell2D(0, 0), out Color color), Is.True);
        Assert.That(color, Is.EqualTo(new Color(51, 102, 153, 128)));
    }

    [Test]
    public void ShouldDrapeDiscreteOwnership_UsesTexturePlaneForContinentalCellSizes()
    {
        Assert.That(
            RaylibFieldRenderPresenter.ShouldDrapeDiscreteOwnership(
                cellSizeCm: 250,
                drapeMaxCellSizeCm: 5_000),
            Is.True);
        Assert.That(
            RaylibFieldRenderPresenter.ShouldDrapeDiscreteOwnership(
                cellSizeCm: 5_000,
                drapeMaxCellSizeCm: 5_000),
            Is.False);
        Assert.That(
            RaylibFieldRenderPresenter.ShouldDrapeDiscreteOwnership(
                cellSizeCm: 7_142,
                drapeMaxCellSizeCm: 5_000),
            Is.False);
    }

    [Test]
    public void TryResolveDiscreteOwnershipDrape_MapsTexelCenterAndScalesSampledHeight()
    {
        var heightmap = new RecordingHeightmap(sampleResult: true, heightCm: 320f);

        bool resolved = RaylibFieldRenderPresenter.TryResolveDiscreteOwnershipDrape(
            heightmap,
            new IntRect(-3, 4, 8, 8),
            cellSizeCm: 250,
            textureX: 2,
            textureY: 3,
            heightSampleDisplayScale: 1.5f,
            drapeOffsetMeters: 2f,
            out System.Numerics.Vector3 centerMeters);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(heightmap.LastWorldXCm, Is.EqualTo(-125f));
            Assert.That(heightmap.LastWorldYCm, Is.EqualTo(1_875f));
            Assert.That(centerMeters.X, Is.EqualTo(-1.25f).Within(0.0001f));
            Assert.That(centerMeters.Y, Is.EqualTo(6.8f).Within(0.0001f));
            Assert.That(centerMeters.Z, Is.EqualTo(18.75f).Within(0.0001f));
        });
    }

    [Test]
    public void TryResolveDiscreteOwnershipDrape_SkipsCellWhenHeightCannotBeSampled()
    {
        var heightmap = new RecordingHeightmap(sampleResult: false, heightCm: 0f);

        bool resolved = RaylibFieldRenderPresenter.TryResolveDiscreteOwnershipDrape(
            heightmap,
            new IntRect(0, 0, 1, 1),
            cellSizeCm: 100,
            textureX: 0,
            textureY: 0,
            heightSampleDisplayScale: 1f,
            drapeOffsetMeters: 2f,
            out System.Numerics.Vector3 centerMeters);

        Assert.That(resolved, Is.False);
        Assert.That(centerMeters, Is.EqualTo(System.Numerics.Vector3.Zero));
    }

    [Test]
    public void BuildTexturePlan_ReusesScratchAndAllocatesZeroAfterWarmup()
    {
        GlobalFieldVisualBuffer buffer = CreateBuffer(out _, out FogField field);
        for (int i = 0; i < 256; i++)
        {
            field.SetExplored(new FogCell(i & 15, i >> 4));
        }
        ProjectFog(buffer, field);

        var presenter = new RaylibFieldRenderPresenter();
        presenter.BuildTexturePlan(buffer);
        presenter.BuildTexturePlan(buffer);
        _ = presenter.LastFieldCellCount;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.GetAllocatedBytesForCurrentThread();

        long before = GC.GetAllocatedBytesForCurrentThread();
        int observed = 0;
        for (int i = 0; i < 1_000; i++)
        {
            presenter.BuildTexturePlan(buffer);
            observed += presenter.LastFieldCellCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(observed, Is.EqualTo(256_000));
        Assert.That(allocated, Is.EqualTo(0));
    }

    [Test]
    public void RaylibFieldRenderPresenter_DoesNotExposeFogFieldStoreInput()
    {
        foreach (var method in typeof(RaylibFieldRenderPresenter).GetMethods())
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(FogFieldStore)), method.Name);
            }
        }
    }

    [Test]
    public void Benchmark_BuildTexturePlan_StagesQuarterMillionDirtyCellsZeroAlloc()
    {
        const int side = 512;
        const int cellCount = side * side;
        const int frames = 24;
        var cells = new GlobalFieldVisualCell[cellCount];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                byte value = ((x + y) & 3) == 0
                    ? (byte)CellVisibility.Visible
                    : (byte)CellVisibility.Explored;
                cells[(y * side) + x] = new GlobalFieldVisualCell(new FieldCell2D(x, y), value);
            }
        }

        IntRect[] dirtyRects = { new(0, 0, side, side) };
        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Fog, scopeKeyId: 1, layerKeyId: 1, surfaceKeyId: 0),
            cellSizeCm: 100,
            WorldCmInt2.Zero,
            new IntRect(0, 0, side, side),
            GlobalFieldVisualValueKind.Byte);
        var buffer = new GlobalFieldVisualBuffer(recordCapacity: 2, cellCapacity: cellCount, dirtyRectCapacity: 2);
        buffer.BeginFrame();
        buffer.Upsert(descriptor, cells, dirtyRects);

        var presenter = new RaylibFieldRenderPresenter();
        presenter.BuildTexturePlan(buffer);
        presenter.BuildTexturePlan(buffer);
        WarmUpGC();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        int stagedCells = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            presenter.BuildTexturePlan(buffer);
            stagedCells += presenter.LastFieldCellCount;
        }

        long stop = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
        double elapsedSeconds = Math.Max(Stopwatch.GetElapsedTime(start, stop).TotalSeconds, 0.000001d);
        double cellsPerSecond = stagedCells / elapsedSeconds;
        double planHz = frames / elapsedSeconds;

        Console.WriteLine("[Benchmark] RaylibFieldRenderPresenter.BuildTexturePlan.262k:");
        Console.WriteLine($"  CellsPerFrame: {cellCount}");
        Console.WriteLine($"  Frames: {frames}");
        Console.WriteLine($"  StagedCells: {stagedCells}");
        Console.WriteLine($"  TotalMs: {Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds:F2}");
        Console.WriteLine($"  CellsPerSecond: {cellsPerSecond:F0}");
        Console.WriteLine($"  PlanHz: {planHz:F1}");
        Console.WriteLine($"  DirtyUploadArea: {presenter.LastDirtyUploadArea}");
        Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocated}");

        Assert.That(stagedCells, Is.EqualTo(cellCount * frames));
        Assert.That(presenter.LastDirtyUploadArea, Is.EqualTo(cellCount));
        Assert.That(allocated, Is.EqualTo(0));
        Assert.That(cellsPerSecond, Is.GreaterThan(1_000_000d));
        Assert.That(planHz, Is.GreaterThan(60d));
    }

    private static GlobalFieldVisualBuffer CreateBuffer(out FogLayerId layerId, out FogField field)
    {
        var registry = new FogLayerRegistry();
        layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
        var store = new FogFieldStore(initialCapacity: 2, chunkSizeCells: 16);
        field = store.GetOrCreate(1, registry.Get(layerId));
        return new GlobalFieldVisualBuffer(recordCapacity: 4, cellCapacity: 512, dirtyRectCapacity: 4);
    }

    private static void ProjectFog(GlobalFieldVisualBuffer buffer, FogField field)
    {
        Span<GlobalFieldVisualCell> cells = stackalloc GlobalFieldVisualCell[field.NonDefaultCount];
        Span<FogCellState> fogCells = stackalloc FogCellState[field.NonDefaultCount];
        int cellCount = field.CopyCells(fogCells);
        for (int i = 0; i < cellCount; i++)
        {
            cells[i] = new GlobalFieldVisualCell(
                new Ludots.Core.Fields.FieldCell2D(fogCells[i].Cell.X, fogCells[i].Cell.Y),
                (byte)fogCells[i].Visibility);
        }

        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Fog, field.ScopeKeyId, field.LayerId.Value, surfaceKeyId: 0),
            field.CellSizeCm,
            Ludots.Platform.Abstractions.WorldCmInt2.Zero,
            new Ludots.Core.Mathematics.IntRect(0, 0, 16, 16),
            GlobalFieldVisualValueKind.Byte);
        buffer.BeginFrame();
        buffer.Upsert(descriptor, cells.Slice(0, cellCount), ReadOnlySpan<Ludots.Core.Mathematics.IntRect>.Empty);
    }

    private static void WarmUpGC()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.GetAllocatedBytesForCurrentThread();
    }

    private sealed class RecordingHeightmap : IContinuousHeightmap
    {
        private readonly bool _sampleResult;
        private readonly float _heightCm;

        public RecordingHeightmap(bool sampleResult, float heightCm)
        {
            _sampleResult = sampleResult;
            _heightCm = heightCm;
        }

        public float LastWorldXCm { get; private set; }
        public float LastWorldYCm { get; private set; }

        public bool TrySampleHeightCm(
            float worldXCm,
            float worldYCm,
            out float heightCm,
            int layerIndex = -1)
        {
            LastWorldXCm = worldXCm;
            LastWorldYCm = worldYCm;
            heightCm = _heightCm;
            return _sampleResult;
        }

        public bool SampleHeightsCm(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldYCm,
            Span<float> outHeightCm,
            int layerIndex = -1)
        {
            throw new NotSupportedException();
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
            throw new NotSupportedException();
        }
    }
}
