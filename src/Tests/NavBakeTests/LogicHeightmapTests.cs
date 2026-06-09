using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.NavBake;

public sealed class LogicHeightmapTests
{
    [Test]
    public void VertexMapRoundTrip_PreservesLogicHeightUnits()
    {
        var map = new VertexMap();
        map.Initialize(1, 1);
        map.SetHeight(0, 0, 3);
        map.SetWaterHeight(0, 0, 5);
        map.SetBiome(0, 0, 7);
        map.SetBlocked(0, 0, true);
        map.SetRamp(0, 0, true);

        var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        LogicHeightmap logic = LogicHeightmapVertexMapAdapter.FromVertexMap(map, cfg);

        Assert.That(logic.GridKind, Is.EqualTo(LogicHeightmapGridKind.HexVertex));
        Assert.That(logic.GetHeightCm(0, 0), Is.EqualTo(600));
        Assert.That(logic.GetWaterHeightCm(0, 0), Is.EqualTo(1000));
        Assert.That(logic.GetAreaId(0, 0), Is.EqualTo(7));
        Assert.That(logic.IsBlocked(0, 0), Is.True);
        Assert.That(logic.IsRamp(0, 0), Is.True);

        VertexMap restored = LogicHeightmapVertexMapAdapter.ToVertexMap(logic, cfg);
        Assert.That(restored.GetHeight(0, 0), Is.EqualTo(3));
        Assert.That(restored.GetWaterHeight(0, 0), Is.EqualTo(5));
        Assert.That(restored.GetBiome(0, 0), Is.EqualTo(7));
        Assert.That(restored.IsBlocked(0, 0), Is.True);
        Assert.That(restored.IsRamp(0, 0), Is.True);
    }

    [Test]
    public void VisualHeightmapAdapter_PreservesCentimeterHeights()
    {
        var asset = VisualHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(0, 0, 400, 400),
            sampleColumns: 4,
            sampleRows: 4,
            heightSamplesCm: new short[]
            {
                100, 200, 300, 400,
                500, 600, 700, 800,
                900, 1000, 1100, 1200,
                1300, 1400, 1500, 1600
            },
            layerName: "logic",
            interpolationMode: VisualHeightmapInterpolationMode.BilinearHeightfield);

        LogicHeightmap logic = LogicHeightmapVisualHeightmapAdapter.FromVisualHeightmap(asset, layerIndex: 0, navChunkSamples: LogicHeightmapChunk.ChunkSize);

        Assert.That(logic.GridKind, Is.EqualTo(LogicHeightmapGridKind.QuadGrid));
        Assert.That(logic.WidthInChunks, Is.EqualTo(1));
        Assert.That(logic.HeightInChunks, Is.EqualTo(1));
        Assert.That(logic.GetHeightCm(0, 0), Is.EqualTo(100).Within(1));
        Assert.That(logic.GetHeightCm(63, 63), Is.EqualTo(1600).Within(1));
    }

    [Test]
    public void LogicHeightmapBinary_RoundTripsChunkData()
    {
        var logic = new LogicHeightmap();
        logic.Initialize(1, 1, LogicHeightmapGridKind.QuadGrid, 100, 100);
        logic.SetHeightCm(10, 11, 1234);
        logic.SetWaterHeightCm(10, 11, 1500);
        logic.SetAreaId(10, 11, 9);
        logic.SetBlocked(10, 11, true);

        using var ms = new MemoryStream();
        LogicHeightmapBinary.Write(ms, logic);
        ms.Position = 0;

        LogicHeightmap read = LogicHeightmapBinary.Read(ms);
        Assert.That(read.GridKind, Is.EqualTo(LogicHeightmapGridKind.QuadGrid));
        Assert.That(read.GetHeightCm(10, 11), Is.EqualTo(1234));
        Assert.That(read.GetWaterHeightCm(10, 11), Is.EqualTo(1500));
        Assert.That(read.GetAreaId(10, 11), Is.EqualTo(9));
        Assert.That(read.IsBlocked(10, 11), Is.True);
    }

    [Test]
    public void LogicHeightmapBinary_WriteChunked_WritesReadableChunkWindows()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "logic-heightmap-streaming-write-test.lhtm");
        using (var fs = File.Create(path))
        {
            LogicHeightmapBinary.WriteChunked(
                fs,
                widthInChunks: 2,
                heightInChunks: 2,
                LogicHeightmapGridKind.QuadGrid,
                cellSizeXCm: 250,
                cellSizeZCm: 300,
                (cx, cy) =>
                {
                    var chunk = new LogicHeightmapChunk();
                    chunk.SetHeightCm(0, 0, (cy * 10 + cx) * 100);
                    chunk.SetAreaId(0, 0, (byte)(cy * 10 + cx));
                    return chunk;
                });
        }

        using var reader = LogicHeightmapFileReader.Open(path);
        LogicHeightmap window = reader.ReadTileWindow(1, 1);

        Assert.That(reader.WidthInChunks, Is.EqualTo(2));
        Assert.That(reader.HeightInChunks, Is.EqualTo(2));
        Assert.That(reader.CellSizeXCm, Is.EqualTo(250));
        Assert.That(reader.CellSizeZCm, Is.EqualTo(300));
        Assert.That(window.ChunkCount, Is.EqualTo(4));
        Assert.That(window.GetHeightCm(LogicHeightmapChunk.ChunkSize, LogicHeightmapChunk.ChunkSize), Is.EqualTo(1100));
        Assert.That(window.GetAreaId(LogicHeightmapChunk.ChunkSize, LogicHeightmapChunk.ChunkSize), Is.EqualTo(11));
    }

    [Test]
    public void VertexMapAdapter_WriteVertexMap_WritesChunkedLogicHeightmap()
    {
        var map = new VertexMap();
        map.Initialize(2, 2);
        int sampleX = LogicHeightmapChunk.ChunkSize + 6;
        int sampleY = LogicHeightmapChunk.ChunkSize + 7;
        map.SetHeight(sampleX, sampleY, 4);
        map.SetWaterHeight(sampleX, sampleY, 5);
        map.SetBiome(sampleX, sampleY, 9);
        map.SetBlocked(sampleX, sampleY, true);
        map.SetRamp(sampleX, sampleY, true);

        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "vertex-map-streaming-write-test.lhtm");
        var cfg = new NavBuildConfig(heightScaleMeters: 2.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);

        using (var source = new MemoryStream())
        {
            VertexMapBinary.Write(source, map);
            source.Position = 0;
            using var output = File.Create(path);
            LogicHeightmapVertexMapAdapter.WriteVertexMap(output, source, cfg);
        }

        using var reader = LogicHeightmapFileReader.Open(path);
        LogicHeightmap window = reader.ReadTileWindow(1, 1);

        Assert.That(reader.WidthInChunks, Is.EqualTo(2));
        Assert.That(reader.HeightInChunks, Is.EqualTo(2));
        Assert.That(reader.GridKind, Is.EqualTo(LogicHeightmapGridKind.HexVertex));
        Assert.That(window.ChunkCount, Is.EqualTo(4));
        Assert.That(window.GetHeightCm(sampleX, sampleY), Is.EqualTo(800));
        Assert.That(window.GetWaterHeightCm(sampleX, sampleY), Is.EqualTo(1000));
        Assert.That(window.GetAreaId(sampleX, sampleY), Is.EqualTo(9));
        Assert.That(window.IsBlocked(sampleX, sampleY), Is.True);
        Assert.That(window.IsRamp(sampleX, sampleY), Is.True);
    }

    [Test]
    public void VisualHeightmapAdapter_WriteVisualHeightmap_WritesChunkedLogicHeightmap()
    {
        int widthSamples = LogicHeightmapChunk.ChunkSize * 2;
        int heightSamples = LogicHeightmapChunk.ChunkSize * 2;
        var heights = new short[widthSamples * heightSamples];
        int sampleX = LogicHeightmapChunk.ChunkSize + 9;
        int sampleY = LogicHeightmapChunk.ChunkSize + 11;
        heights[sampleY * widthSamples + sampleX] = 1234;

        var asset = VisualHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(0, 0, widthSamples * 100, heightSamples * 100),
            widthSamples,
            heightSamples,
            heights,
            layerName: "logic",
            interpolationMode: VisualHeightmapInterpolationMode.BilinearHeightfield);

        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "visual-heightmap-streaming-write-test.lhtm");
        using (var source = new MemoryStream())
        {
            VisualHeightmapBinary.Write(source, asset);
            source.Position = 0;
            using var output = File.Create(path);
            LogicHeightmapVisualHeightmapAdapter.WriteVisualHeightmap(output, source, layerIndex: 0, navChunkSamples: LogicHeightmapChunk.ChunkSize);
        }

        using var reader = LogicHeightmapFileReader.Open(path);
        LogicHeightmap window = reader.ReadTileWindow(1, 1);

        Assert.That(reader.WidthInChunks, Is.EqualTo(2));
        Assert.That(reader.HeightInChunks, Is.EqualTo(2));
        Assert.That(reader.GridKind, Is.EqualTo(LogicHeightmapGridKind.QuadGrid));
        Assert.That(window.ChunkCount, Is.EqualTo(4));
        Assert.That(window.GetHeightCm(sampleX, sampleY), Is.EqualTo(1234).Within(1));
        Assert.That(window.GetAreaId(sampleX, sampleY), Is.EqualTo(2));
    }

    [Test]
    public void QuadGridAdapter_PadsToChunkedLogicHeightmap()
    {
        LogicHeightmap logic = LogicHeightmapQuadGridAdapter.FromSamples(
            sampleColumns: 3,
            sampleRows: 2,
            heightCm: new[] { 100, 200, 300, 400, 500, 600 },
            cellSizeXCm: 250,
            cellSizeZCm: 300);

        Assert.That(logic.GridKind, Is.EqualTo(LogicHeightmapGridKind.QuadGrid));
        Assert.That(logic.WidthInChunks, Is.EqualTo(1));
        Assert.That(logic.HeightInChunks, Is.EqualTo(1));
        Assert.That(logic.CellSizeXCm, Is.EqualTo(250));
        Assert.That(logic.CellSizeZCm, Is.EqualTo(300));
        Assert.That(logic.GetHeightCm(0, 0), Is.EqualTo(100));
        Assert.That(logic.GetHeightCm(2, 1), Is.EqualTo(600));
        Assert.That(logic.GetHeightCm(63, 63), Is.EqualTo(600));
    }

    [Test]
    public void VertexMapAdapter_ToTileWindow_AllocatesOnlyNeighborChunks()
    {
        var logic = new LogicHeightmap();
        logic.Initialize(4, 4, LogicHeightmapGridKind.QuadGrid, 100, 100);
        int widthSamples = logic.WidthSamples;
        int heightSamples = logic.HeightSamples;
        for (int y = 0; y < heightSamples; y++)
        {
            for (int x = 0; x < widthSamples; x++)
            {
                logic.SetHeightCm(x, y, (x + y) % 1600);
            }
        }

        var cfg = new NavBuildConfig(heightScaleMeters: 1.0f, minWalkableUpDot: 0.6f, cliffHeightThreshold: 1);
        VertexMap window = LogicHeightmapVertexMapAdapter.ToVertexMapTileWindow(logic, chunkX: 2, chunkY: 2, cfg);

        Assert.That(window.WidthInChunks, Is.EqualTo(4));
        Assert.That(window.HeightInChunks, Is.EqualTo(4));
        Assert.That(window.ChunkCount, Is.EqualTo(9));
        Assert.That(window.GetChunk(2 * VertexChunk.ChunkSize, 2 * VertexChunk.ChunkSize), Is.Not.Null);
        Assert.That(window.GetChunk(0, 0), Is.Null);
    }

    [Test]
    public void LogicHeightmapFileReader_ReadTileWindow_LoadsOnlyNeighborChunks()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "logic-heightmap-window-test.lhtm");
        var logic = new LogicHeightmap();
        logic.Initialize(4, 4, LogicHeightmapGridKind.QuadGrid, 100, 100);
        for (int cy = 0; cy < logic.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < logic.WidthInChunks; cx++)
            {
                int sampleX = cx * LogicHeightmapChunk.ChunkSize;
                int sampleY = cy * LogicHeightmapChunk.ChunkSize;
                logic.SetHeightCm(sampleX, sampleY, (cy * 10 + cx) * 100);
            }
        }

        using (var fs = File.Create(path))
        {
            LogicHeightmapBinary.Write(fs, logic);
        }

        using var reader = LogicHeightmapFileReader.Open(path);
        LogicHeightmap window = reader.ReadTileWindow(centerChunkX: 2, centerChunkY: 2);

        Assert.That(reader.WidthInChunks, Is.EqualTo(4));
        Assert.That(reader.HeightInChunks, Is.EqualTo(4));
        Assert.That(window.ChunkCount, Is.EqualTo(9));
        Assert.That(window.GetChunk(2 * LogicHeightmapChunk.ChunkSize, 2 * LogicHeightmapChunk.ChunkSize), Is.Not.Null);
        Assert.That(window.GetChunk(0, 0), Is.Null);
        Assert.That(window.GetHeightCm(2 * LogicHeightmapChunk.ChunkSize, 2 * LogicHeightmapChunk.ChunkSize), Is.EqualTo(2200));
    }

    [Test]
    public void LogicHeightmapSemanticSummary_ReadsAreaWaterBlockedRampAndHeightSignals()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "logic-heightmap-semantic-summary-test.lhtm");
        var logic = new LogicHeightmap();
        logic.Initialize(2, 2, LogicHeightmapGridKind.QuadGrid, 100, 100);

        int riverX = LogicHeightmapChunk.ChunkSize;
        int riverY = 8;
        logic.SetHeightCm(0, 0, 400);
        logic.SetHeightCm(LogicHeightmapChunk.ChunkSize + 4, LogicHeightmapChunk.ChunkSize + 4, 1600);
        logic.SetAreaId(riverX, riverY, 5);
        logic.SetWaterHeightCm(riverX, riverY, 120);
        logic.SetBlocked(2, LogicHeightmapChunk.ChunkSize + 3, true);
        logic.SetRamp(LogicHeightmapChunk.ChunkSize + 2, LogicHeightmapChunk.ChunkSize + 5, true);

        using (var fs = File.Create(path))
        {
            LogicHeightmapBinary.Write(fs, logic);
        }

        LogicHeightmapSemanticSummary summary = LogicHeightmapSemanticSummary.FromFile(path);

        Assert.That(summary.Available, Is.True);
        Assert.That(summary.SampledChunks, Is.EqualTo(4));
        Assert.That(summary.SampledCells, Is.EqualTo(4 * LogicHeightmapChunk.TotalCells));
        Assert.That(summary.DistinctAreaCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(summary.WaterLikeCellCount, Is.GreaterThan(0));
        Assert.That(summary.BlockedCellCount, Is.EqualTo(1));
        Assert.That(summary.RampCellCount, Is.EqualTo(1));
        Assert.That(summary.HeightRangeCm, Is.GreaterThan(1000));
        Assert.That(summary.ChunkHasWaterLike(1, 0), Is.True);
        Assert.That(summary.ChunkHasBlocked(0, 1), Is.True);
        Assert.That(summary.ChunkHasRamp(1, 1), Is.True);
        Assert.That(summary.VisualizationSource, Is.EqualTo("logic_heightmap_sampled_view"));
    }

    [Test]
    public void LogicHeightmapEditPatch_AppliesFieldsAndWritesDirtyChunks()
    {
        string inputPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "logic-heightmap-edit-input.lhtm");
        string outputPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "logic-heightmap-edit-output.lhtm");
        var logic = new LogicHeightmap();
        logic.Initialize(2, 2, LogicHeightmapGridKind.QuadGrid, 100, 100);
        logic.SetHeightCm(2, 2, 600);
        logic.SetAreaId(2, 2, 0);

        using (var fs = File.Create(inputPath))
        {
            LogicHeightmapBinary.Write(fs, logic);
        }

        var patch = new LogicHeightmapEditPatch
        {
            SourcePath = inputPath,
            OutputPath = outputPath,
            Tool = "test"
        };
        patch.Operations.Add(new LogicHeightmapEditOperation
        {
            Tool = "Water",
            MinSampleX = LogicHeightmapChunk.ChunkSize - 1,
            MinSampleY = 3,
            MaxSampleX = LogicHeightmapChunk.ChunkSize + 2,
            MaxSampleY = 4,
            AreaId = 5,
            WaterHeightCm = 300,
            Blocked = false
        });

        LogicHeightmapEditPatch.ApplyResult result = patch.Apply(inputPath, outputPath, overwrite: true);

        Assert.That(result.OperationCount, Is.EqualTo(1));
        Assert.That(result.AppliedCellCount, Is.EqualTo(8));
        Assert.That(result.DirtyChunks, Does.Contain("0,0"));
        Assert.That(result.DirtyChunks, Does.Contain("1,0"));

        using var reader = LogicHeightmapFileReader.Open(outputPath);
        LogicHeightmap window = reader.ReadTileWindow(1, 0, radiusChunks: 1);
        Assert.That(window.GetAreaId(LogicHeightmapChunk.ChunkSize, 3), Is.EqualTo(5));
        Assert.That(window.GetWaterHeightCm(LogicHeightmapChunk.ChunkSize, 3), Is.EqualTo(300));
        Assert.That(window.IsBlocked(LogicHeightmapChunk.ChunkSize, 3), Is.False);
    }
}
