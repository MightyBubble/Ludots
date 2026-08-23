using System;
using System.IO;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;
using VisualTerrainEditorMod.Runtime;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class VisualTerrainEditorRuntimeConsistencyTests
{
    [Test]
    public void VisualTerrainEditor_ProceduralMeshVerticesMatchRuntimeHeightmapTruth()
    {
        using var document = new VisualTerrainEditorDocument(
            new VisualTerrainAssetDescriptor(
                id: $"visual_terrain_editor_mesh_truth_{Guid.NewGuid():N}",
                displayName: "Mesh Truth",
                bounds: new WorldAabbCm(0, 0, 20_000, 10_000),
                chunkColumns: 2,
                chunkRows: 1,
                samplesPerChunkColumn: 9,
                samplesPerChunkRow: 9,
                renderColumnsPerChunk: 9,
                renderRowsPerChunk: 9,
                defaultHeight01: 0.45f),
            defaultMaterialAssetId: 1);

        document.SetViewMode(TerrainViewMode.Eroded);
        document.EnsureChunkWindowLoaded(centerChunkX: 0, centerChunkY: 0, radius: 1);
        document.PaintWorld(4_000, 4_000);
        document.PaintWorld(13_000, 6_000);
        document.Update();

        var runtime = (ChunkedVisualHeightmapRuntime)document.HeightmapRuntime;
        for (int chunkX = 0; chunkX < 2; chunkX++)
        {
            Assert.That(document.TryGetChunkProceduralMesh(chunkX, 0, out var proceduralMesh), Is.True);
            Vector3 chunkCenterMeters = ChunkCenterMeters(document.Asset, chunkX, 0);
            for (int vertexIndex = 0; vertexIndex < proceduralMesh.VertexCount; vertexIndex++)
            {
                Vector3 position = ReadPosition(proceduralMesh, vertexIndex);
                float worldXCm = (chunkCenterMeters.X + position.X) * 100f;
                float worldYCm = (chunkCenterMeters.Z + position.Z) * 100f;

                Assert.That(runtime.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm), Is.True, $"chunk={chunkX} vertex={vertexIndex} position={position}");
                Assert.That(position.Y, Is.EqualTo(heightCm * 0.01f).Within(0.001f), $"chunk={chunkX} vertex={vertexIndex} position={position}");
            }
        }
    }

    [Test]
    public void VisualTerrainEditor_ProceduralMeshEdgesMeetInWorldCoordinates()
    {
        using var document = new VisualTerrainEditorDocument(
            new VisualTerrainAssetDescriptor(
                id: $"visual_terrain_editor_world_mesh_{Guid.NewGuid():N}",
                displayName: "World Mesh",
                bounds: new WorldAabbCm(-10_000, -5_000, 20_000, 10_000),
                chunkColumns: 2,
                chunkRows: 1,
                samplesPerChunkColumn: 9,
                samplesPerChunkRow: 9,
                renderColumnsPerChunk: 9,
                renderRowsPerChunk: 9,
                defaultHeight01: 0.45f),
            defaultMaterialAssetId: 1);

        document.SetViewMode(TerrainViewMode.Base);
        document.EnsureChunkWindowLoaded(centerChunkX: 0, centerChunkY: 0, radius: 1);
        document.Update();

        Assert.That(document.TryGetChunkProceduralMesh(0, 0, out var leftChunk), Is.True);
        Assert.That(document.TryGetChunkProceduralMesh(1, 0, out var rightChunk), Is.True);

        Assert.That(leftChunk.LocalBounds.Center.X, Is.EqualTo(0f).Within(0.001f));
        Assert.That(leftChunk.LocalBounds.Center.Z, Is.EqualTo(0f).Within(0.001f));
        Assert.That(rightChunk.LocalBounds.Center.X, Is.EqualTo(0f).Within(0.001f));
        Assert.That(rightChunk.LocalBounds.Center.Z, Is.EqualTo(0f).Within(0.001f));

        Vector3 leftEastEdge = ReadPosition(leftChunk, 8);
        Vector3 rightWestEdge = ReadPosition(rightChunk, 0);
        Vector3 leftCenterMeters = ChunkCenterMeters(document.Asset, 0, 0);
        Vector3 rightCenterMeters = ChunkCenterMeters(document.Asset, 1, 0);

        Assert.That(leftEastEdge.X, Is.EqualTo(50f).Within(0.001f));
        Assert.That(rightWestEdge.X, Is.EqualTo(-50f).Within(0.001f));
        Assert.That(leftCenterMeters.X + leftEastEdge.X, Is.EqualTo(rightCenterMeters.X + rightWestEdge.X).Within(0.001f));
    }

    [Test]
    public void VisualTerrainEditor_FlatImportedHeightmapBuildsFiniteTangents()
    {
        const int sampleCount = 257;
        short[] heightSamplesCm = new short[sampleCount * sampleCount];
        VisualHeightmapAsset source = VisualHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(0, 0, 10_000, 10_000),
            sampleCount,
            sampleCount,
            heightSamplesCm,
            "flat",
            VisualHeightmapInterpolationMode.TriangleHeightfield);

        using VisualTerrainEditorDocument document = VisualTerrainEditorDocument.CreateFromVisualHeightmapAsset(
            $"visual_terrain_editor_flat_import_{Guid.NewGuid():N}",
            "Flat Import",
            source,
            defaultMaterialAssetId: 1,
            defaultHeight01: 0.46f);

        Assert.That(document.TryGetChunkProceduralMesh(0, 0, out var proceduralMesh), Is.True);
        Assert.That(document.Asset.UseAbsoluteHeightColorRamp, Is.True);
        Assert.That(document.Asset.RenderColumnsPerChunk, Is.EqualTo(193));
        Assert.That(document.Asset.RenderRowsPerChunk, Is.EqualTo(193));
        for (int vertexIndex = 0; vertexIndex < proceduralMesh.VertexCount; vertexIndex++)
        {
            Vector3 tangent = ReadTangent(proceduralMesh, vertexIndex);
            Assert.That(float.IsFinite(tangent.X), Is.True, $"vertex={vertexIndex}");
            Assert.That(float.IsFinite(tangent.Y), Is.True, $"vertex={vertexIndex}");
            Assert.That(float.IsFinite(tangent.Z), Is.True, $"vertex={vertexIndex}");
            Assert.That(tangent.LengthSquared(), Is.GreaterThan(0.001f), $"vertex={vertexIndex}");
        }
    }

    [Test]
    public void VisualTerrainEditor_ImportedHeightmapKeepsAbsoluteHeightThroughMeshUpdate()
    {
        const int sampleCount = 257;
        const short plateauHeightCm = 4_102;
        short[] heightSamplesCm = new short[sampleCount * sampleCount];
        Array.Fill(heightSamplesCm, plateauHeightCm);
        VisualHeightmapAsset source = VisualHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(0, 0, 10_000, 10_000),
            sampleCount,
            sampleCount,
            heightSamplesCm,
            "plateau",
            VisualHeightmapInterpolationMode.TriangleHeightfield);

        using VisualTerrainEditorDocument document = VisualTerrainEditorDocument.CreateFromVisualHeightmapAsset(
            $"visual_terrain_editor_absolute_import_{Guid.NewGuid():N}",
            "Absolute Import",
            source,
            defaultMaterialAssetId: 1,
            defaultHeight01: 0.45f);

        // Isolate the height-preservation check from the imported default vertical
        // exaggeration so mesh vertex Y maps 1:1 to absolute centimeters.
        document.SetDisplayHeightScale(1f);
        Assert.That(document.HeightmapRuntime.TrySampleHeightCm(5_000f, 5_000f, out float beforeUpdateCm), Is.True);
        document.Update();
        Assert.That(document.HeightmapRuntime.TrySampleHeightCm(5_000f, 5_000f, out float afterUpdateCm), Is.True);
        Assert.That(afterUpdateCm, Is.EqualTo(beforeUpdateCm).Within(0.001f));
        Assert.That(afterUpdateCm, Is.EqualTo(plateauHeightCm).Within(0.001f));

        Assert.That(document.TryGetChunkProceduralMesh(0, 0, out var proceduralMesh), Is.True);
        Vector3 firstVertex = ReadPosition(proceduralMesh, 0);
        Assert.That(firstVertex.Y, Is.EqualTo(plateauHeightCm * 0.01f).Within(0.001f));
    }

    [Test]
    public void VisualTerrainEditor_ImportedHeightmapPublishesGlobalColorRangeForEditingMeshes()
    {
        const int sampleColumns = 513;
        const int sampleRows = 257;
        const short lowHeightCm = -1_200;
        const short highHeightCm = 4_102;
        short[] heightSamplesCm = new short[sampleColumns * sampleRows];
        for (int y = 0; y < sampleRows; y++)
        {
            for (int x = 0; x < sampleColumns; x++)
            {
                heightSamplesCm[(y * sampleColumns) + x] = x < 256 ? lowHeightCm : highHeightCm;
            }
        }

        VisualHeightmapAsset source = VisualHeightmapAsset.CreateSingleLayer(
            new WorldAabbCm(0, 0, 20_000, 10_000),
            sampleColumns,
            sampleRows,
            heightSamplesCm,
            "two-band",
            VisualHeightmapInterpolationMode.TriangleHeightfield);

        using VisualTerrainEditorDocument document = VisualTerrainEditorDocument.CreateFromVisualHeightmapAsset(
            $"visual_terrain_editor_color_range_import_{Guid.NewGuid():N}",
            "Color Range Import",
            source,
            defaultMaterialAssetId: 1,
            defaultHeight01: 0.45f);

        var presentation = (IVisualHeightmapRenderPresentation)document.HeightmapRuntime;
        document.SetDisplayColorMode(VisualHeightmapRenderColorMode.HeightmapGrayscale);
        document.Update();
        Assert.That(document.DisplayColorMode, Is.EqualTo(VisualHeightmapRenderColorMode.HeightmapGrayscale));
        Assert.That(presentation.RenderColorMode, Is.EqualTo(VisualHeightmapRenderColorMode.HeightmapGrayscale));
        Assert.That(presentation.RenderUseAbsoluteHeightColorRange, Is.True);
        Assert.That(presentation.RenderMinHeightCm, Is.EqualTo(lowHeightCm).Within(0.001f));
        Assert.That(presentation.RenderMaxHeightCm, Is.EqualTo(highHeightCm).Within(0.001f));

        Assert.That(document.TryGetChunkProceduralMesh(0, 0, out var lowChunk), Is.True);
        Assert.That(document.TryGetChunkProceduralMesh(1, 0, out var highChunk), Is.True);

        int centerVertexIndex = ((document.Asset.RenderRowsPerChunk / 2) * document.Asset.RenderColumnsPerChunk) + (document.Asset.RenderColumnsPerChunk / 2);
        (byte lowRed, byte lowGreen, byte lowBlue) = ReadColor(lowChunk, vertexIndex: centerVertexIndex);
        (byte highRed, byte highGreen, byte highBlue) = ReadColor(highChunk, vertexIndex: centerVertexIndex);
        Assert.That(lowGreen, Is.EqualTo(lowRed));
        Assert.That(lowBlue, Is.EqualTo(lowRed));
        Assert.That(highGreen, Is.EqualTo(highRed));
        Assert.That(highBlue, Is.EqualTo(highRed));
        Assert.That(highRed, Is.GreaterThan(lowRed + 80));
    }

    [Test]
    public void VisualTerrainEditor_DisplayPresentationDoesNotChangeRuntimeHeightTruth()
    {
        using var document = new VisualTerrainEditorDocument(
            new VisualTerrainAssetDescriptor(
                id: $"visual_terrain_editor_display_truth_{Guid.NewGuid():N}",
                displayName: "Display Truth",
                bounds: new WorldAabbCm(0, 0, 20_000, 20_000),
                chunkColumns: 2,
                chunkRows: 2,
                samplesPerChunkColumn: 9,
                samplesPerChunkRow: 9,
                renderColumnsPerChunk: 9,
                renderRowsPerChunk: 9,
                defaultHeight01: 0.45f),
            defaultMaterialAssetId: 1);

        document.EnsureChunkWindowLoaded(centerChunkX: 0, centerChunkY: 0, radius: 1);
        document.Update();
        var runtime = (ChunkedVisualHeightmapRuntime)document.HeightmapRuntime;
        var presentation = (IVisualHeightmapRenderPresentation)runtime;
        Assert.That(runtime.TrySampleHeightCm(5_000f, 5_000f, out float beforeHeightCm), Is.True);
        int storeRevision = runtime.Store.Revision;
        int presentationRevision = presentation.RenderPresentationRevision;

        document.AdjustDisplayHeightScale(1f);
        document.AdjustDisplayColorContrast(0.4f);
        document.SetDisplayFlatOverview(false);
        document.SetDisplayColorMode(VisualHeightmapRenderColorMode.HeightmapGrayscale);
        document.Update();

        Assert.That(runtime.TrySampleHeightCm(5_000f, 5_000f, out float afterHeightCm), Is.True);
        Assert.That(afterHeightCm, Is.EqualTo(beforeHeightCm).Within(0.001f));
        Assert.That(runtime.Store.Revision, Is.EqualTo(storeRevision));
        Assert.That(presentation.RenderPresentationRevision, Is.GreaterThan(presentationRevision));
        Assert.That(presentation.RenderDisplayHeightScale, Is.EqualTo(document.DisplayHeightScale).Within(0.0001f));
        Assert.That(presentation.RenderColorContrast, Is.EqualTo(document.DisplayColorContrast).Within(0.0001f));
        Assert.That(presentation.RenderFlatOverview, Is.False);
        Assert.That(presentation.RenderColorMode, Is.EqualTo(VisualHeightmapRenderColorMode.HeightmapGrayscale));
        Assert.That(presentation.RenderUseAbsoluteHeightColorRange, Is.False);
        Assert.That(presentation.RenderMinHeightCm, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(presentation.RenderMaxHeightCm, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void VisualTerrainEditor_PaintingBumpsSharedHeightmapRevision()
    {
        using var document = new VisualTerrainEditorDocument(
            new VisualTerrainAssetDescriptor(
                id: $"visual_terrain_editor_revision_{Guid.NewGuid():N}",
                displayName: "Revision",
                bounds: new WorldAabbCm(0, 0, 20_000, 20_000),
                chunkColumns: 2,
                chunkRows: 2,
                samplesPerChunkColumn: 9,
                samplesPerChunkRow: 9,
                renderColumnsPerChunk: 9,
                renderRowsPerChunk: 9,
                defaultHeight01: 0.45f),
            defaultMaterialAssetId: 1);

        document.EnsureChunkWindowLoaded(centerChunkX: 0, centerChunkY: 0, radius: 1);
        document.Update();
        var runtime = (ChunkedVisualHeightmapRuntime)document.HeightmapRuntime;
        int storeRevision = runtime.Store.Revision;

        document.PaintWorld(5_000, 5_000);
        document.Update();

        Assert.That(runtime.Store.Revision, Is.GreaterThan(storeRevision));
    }

    [Test]
    public void VisualTerrainEditor_StrategicScaleImportDoesNotUseSmallMapMeshPolicy()
    {
        var asset = new VisualTerrainAssetDescriptor(
            id: $"visual_terrain_editor_strategic_import_{Guid.NewGuid():N}",
            displayName: "Strategic Import",
            bounds: new WorldAabbCm(
                -450_326_016,
                -257_329_152,
                900_652_032,
                514_658_304),
            chunkColumns: 28,
            chunkRows: 16,
            samplesPerChunkColumn: 257,
            samplesPerChunkRow: 257,
            renderColumnsPerChunk: 129,
            renderRowsPerChunk: 129,
            defaultHeight01: 0.45f);

        Assert.That(asset.ChunkCount, Is.LessThan(512), "This locks the East Asia regression: chunk count alone made the map look small.");
        Assert.That(VisualTerrainEditorDocument.ShouldEagerBuildImportedProceduralMeshes(asset), Is.False);
        Assert.That(VisualTerrainEditorRuntime.ShouldUseLargeMapMode(asset), Is.True);
        Assert.That(VisualTerrainEditorRuntime.ShouldUseSharedTerrainOverview(asset, 675_000_000f), Is.True);
        Assert.That(VisualTerrainEditorRuntime.ResolvePreferredCameraDistanceCm(asset), Is.GreaterThan(900_000_000f));
    }

    [Test]
    public void VisualTerrainEditor_StrategicScaleCameraTargetIsClampedBeforeChunkWindowSelection()
    {
        var asset = new VisualTerrainAssetDescriptor(
            id: $"visual_terrain_editor_camera_clamp_{Guid.NewGuid():N}",
            displayName: "Camera Clamp",
            bounds: new WorldAabbCm(
                -450_326_016,
                -257_329_152,
                900_652_032,
                514_658_304),
            chunkColumns: 28,
            chunkRows: 16,
            samplesPerChunkColumn: 257,
            samplesPerChunkRow: 257,
            renderColumnsPerChunk: 129,
            renderRowsPerChunk: 129,
            defaultHeight01: 0.45f);

        Vector2 center = VisualTerrainEditorRuntime.GetWorldCenterCm(asset);
        Assert.That(center.X, Is.EqualTo(0f).Within(0.001f));
        Assert.That(center.Y, Is.EqualTo(0f).Within(0.001f));

        Vector2 outside = new(-354_313_300f, -1_155_685_700f);
        Vector2 clamped = VisualTerrainEditorRuntime.ResolveCameraTargetInsideBounds(asset, outside);

        Assert.That(clamped.X, Is.EqualTo(outside.X).Within(1f));
        Assert.That(clamped.Y, Is.EqualTo(asset.Bounds.Top).Within(1f));
    }

    [Test]
    public void VisualTerrainEditor_SaveLoadRoundTripsRuntimeContractAndSampleTruth()
    {
        string mapId = $"visual_terrain_editor_save_load_{Guid.NewGuid():N}";
        string? saveDirectory = null;

        try
        {
            using var document = new VisualTerrainEditorDocument(
                new VisualTerrainAssetDescriptor(
                    id: mapId,
                    displayName: "Save Load Truth",
                    bounds: new WorldAabbCm(0, 0, 20_000, 20_000),
                    chunkColumns: 2,
                    chunkRows: 2,
                    samplesPerChunkColumn: 9,
                    samplesPerChunkRow: 9,
                    renderColumnsPerChunk: 9,
                    renderRowsPerChunk: 9,
                    defaultHeight01: 0.45f,
                    storageLayout: VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters,
                    interpolationMode: VisualHeightmapInterpolationMode.TriangleHeightfield,
                    sampleScale: VisualHeightSampleScale.IdentityCentimeters),
                defaultMaterialAssetId: 1);

            document.EnsureChunkWindowLoaded(centerChunkX: 0, centerChunkY: 0, radius: 2);
            document.AdjustScale(0.03f);
            document.AdjustStrength(0.07f);
            document.AdjustGullyWeight(-0.15f);
            document.AdjustDetail(0.40f);
            document.AdjustOctaves(1);
            document.PaintWorld(4_500, 5_500);
            document.PaintWorld(15_500, 6_500);
            document.SetBrushMode(lowerBrush: true);
            document.PaintWorld(11_000, 14_000);
            document.SetBrushMode(lowerBrush: false);
            document.Update();

            VisualTerrainEditorDocument.VisualTerrainErosionSettingsSnapshot originalErosion = document.CreateErosionSettingsSnapshot();
            string manifestPath = VisualTerrainEditorPersistence.SaveMap(document);
            saveDirectory = Path.GetDirectoryName(manifestPath);

            using VisualTerrainEditorDocument loaded = VisualTerrainEditorPersistence.LoadMap(manifestPath);
            loaded.EnsureChunkWindowLoaded(centerChunkX: 0, centerChunkY: 0, radius: 2);
            loaded.Update();
            Assert.That(loaded.Asset.StorageLayout, Is.EqualTo(document.Asset.StorageLayout));
            Assert.That(loaded.Asset.InterpolationMode, Is.EqualTo(document.Asset.InterpolationMode));
            Assert.That(loaded.Asset.SampleScale, Is.EqualTo(document.Asset.SampleScale));
            Assert.That(loaded.Asset.DefaultLayerIndex, Is.EqualTo(document.Asset.DefaultLayerIndex));
            Assert.That(loaded.Asset.UseAbsoluteHeightColorRamp, Is.EqualTo(document.Asset.UseAbsoluteHeightColorRamp));
            Assert.That(loaded.CreateErosionSettingsSnapshot(), Is.EqualTo(originalErosion));

            float[] sampleXs = { 2_000f, 4_500f, 9_500f, 11_000f, 15_500f, 18_000f };
            float[] sampleYs = { 2_000f, 5_500f, 8_000f, 14_000f, 6_500f, 18_000f };
            for (int i = 0; i < sampleXs.Length; i++)
            {
                Assert.That(document.HeightmapRuntime.TrySampleHeightCm(sampleXs[i], sampleYs[i], out float originalHeightCm), Is.True);
                Assert.That(loaded.HeightmapRuntime.TrySampleHeightCm(sampleXs[i], sampleYs[i], out float loadedHeightCm), Is.True);
                Assert.That(loadedHeightCm, Is.EqualTo(originalHeightCm).Within(0.001f));
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(saveDirectory) && Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, recursive: true);
            }
        }
    }

    private static Vector3 ReadPosition(Ludots.Core.Presentation.Assets.ProceduralMeshAssetData proceduralMesh, int vertexIndex)
    {
        int floatOffset = vertexIndex * 3;
        return new Vector3(
            proceduralMesh.Positions[floatOffset + 0],
            proceduralMesh.Positions[floatOffset + 1],
            proceduralMesh.Positions[floatOffset + 2]);
    }

    private static Vector3 ReadTangent(Ludots.Core.Presentation.Assets.ProceduralMeshAssetData proceduralMesh, int vertexIndex)
    {
        int floatOffset = vertexIndex * 4;
        return new Vector3(
            proceduralMesh.Tangents[floatOffset + 0],
            proceduralMesh.Tangents[floatOffset + 1],
            proceduralMesh.Tangents[floatOffset + 2]);
    }

    private static (byte Red, byte Green, byte Blue) ReadColor(Ludots.Core.Presentation.Assets.ProceduralMeshAssetData proceduralMesh, int vertexIndex)
    {
        Assert.That(proceduralMesh.Colors32, Is.Not.Null);
        byte[] colors = proceduralMesh.Colors32!;
        int offset = vertexIndex * 4;
        return (colors[offset + 0], colors[offset + 1], colors[offset + 2]);
    }

    private static Vector3 ChunkCenterMeters(VisualTerrainAssetDescriptor asset, int chunkX, int chunkY)
    {
        float centerXCm = asset.Bounds.Left + ((chunkX + 0.5f) * asset.ChunkWorldWidthCm);
        float centerYCm = asset.Bounds.Top + ((chunkY + 0.5f) * asset.ChunkWorldHeightCm);
        return new Vector3(centerXCm * 0.01f, 0f, centerYCm * 0.01f);
    }
}
