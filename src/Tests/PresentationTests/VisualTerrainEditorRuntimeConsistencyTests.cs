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
    public void VisualTerrainEditor_RuntimeMeshVerticesMatchRuntimeHeightmapTruth()
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
                defaultHeight01: 0.45f));

        document.SetViewMode(TerrainViewMode.Eroded);
        document.EnsureChunkWindowLoaded(centerChunkX: 0, centerChunkY: 0, radius: 1);
        document.PaintWorld(4_000, 4_000);
        document.PaintWorld(13_000, 6_000);
        document.Update();

        var runtime = (ChunkedVisualHeightmapRuntime)document.HeightmapRuntime;
        for (int chunkX = 0; chunkX < 2; chunkX++)
        {
            Assert.That(document.TryGetChunkRuntimeMesh(chunkX, 0, out var runtimeMesh), Is.True);
            for (int vertexIndex = 0; vertexIndex < runtimeMesh.VertexCount; vertexIndex++)
            {
                Vector3 position = ReadPosition(runtimeMesh, vertexIndex);
                float worldXCm = position.X * 100f;
                float worldYCm = position.Z * 100f;

                Assert.That(runtime.TrySampleHeightCm(worldXCm, worldYCm, out float heightCm), Is.True, $"chunk={chunkX} vertex={vertexIndex} position={position}");
                Assert.That(position.Y, Is.EqualTo(heightCm * 0.01f).Within(0.001f), $"chunk={chunkX} vertex={vertexIndex} position={position}");
            }
        }
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
                    sampleScale: VisualHeightSampleScale.IdentityCentimeters));

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

    private static Vector3 ReadPosition(Ludots.Core.Presentation.Assets.RuntimeMeshAssetData runtimeMesh, int vertexIndex)
    {
        int floatOffset = vertexIndex * 3;
        return new Vector3(
            runtimeMesh.Vertices[floatOffset + 0],
            runtimeMesh.Vertices[floatOffset + 1],
            runtimeMesh.Vertices[floatOffset + 2]);
    }
}
