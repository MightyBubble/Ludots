using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;

namespace VisualTerrainEditorMod.Runtime;

internal static class VisualTerrainEditorPersistence
{
    private const string SaveFolderRelativePath = "mods\\showcases\\visual_terrain_editor\\VisualTerrainEditorMod\\assets\\UserMaps";
    private const string ManifestFileName = "map.json";
    private const string ChunkDirectoryName = "chunks";
    private const string ChunkFileExtension = ".vtchunk";
    private const string ChunkMagic = "VTCK";
    private const int ChunkVersion = 1;
    private const int ManifestVersion = 3;

    public static string SaveMap(VisualTerrainEditorDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        VisualTerrainAssetDescriptor asset = document.Asset;
        string workspaceRoot = ResolveWorkspaceRoot();
        string saveDirectory = Path.Combine(workspaceRoot, SaveFolderRelativePath, asset.Id);
        Directory.CreateDirectory(saveDirectory);

        string chunkDirectory = Path.Combine(saveDirectory, ChunkDirectoryName);
        Directory.CreateDirectory(chunkDirectory);
        foreach (string existingChunkPath in Directory.EnumerateFiles(chunkDirectory, $"*{ChunkFileExtension}", SearchOption.TopDirectoryOnly))
        {
            File.Delete(existingChunkPath);
        }

        List<VisualTerrainEditorChunkManifestEntry> chunkEntries = new();
        foreach (VisualTerrainEditorDocument.SavedChunkData chunk in document.EnumerateEditedChunks()
                     .OrderBy(static c => c.ChunkY)
                     .ThenBy(static c => c.ChunkX))
        {
            string fileName = $"chunk_{chunk.ChunkX}_{chunk.ChunkY}{ChunkFileExtension}";
            string chunkPath = Path.Combine(chunkDirectory, fileName);
            WriteChunk(chunkPath, chunk);
            chunkEntries.Add(new VisualTerrainEditorChunkManifestEntry
            {
                ChunkX = chunk.ChunkX,
                ChunkY = chunk.ChunkY,
                File = fileName,
            });
        }

        VisualTerrainEditorDocument.VisualTerrainErosionSettingsSnapshot erosion = document.CreateErosionSettingsSnapshot();
        var manifest = new VisualTerrainEditorMapManifest
        {
            Version = ManifestVersion,
            MapId = asset.Id,
            DisplayName = asset.DisplayName,
            ChunkDirectory = ChunkDirectoryName,
            BoundsLeftCm = asset.Bounds.Left,
            BoundsTopCm = asset.Bounds.Top,
            BoundsWidthCm = asset.Bounds.Width,
            BoundsHeightCm = asset.Bounds.Height,
            ChunkColumns = asset.ChunkColumns,
            ChunkRows = asset.ChunkRows,
            SamplesPerChunkColumn = asset.SamplesPerChunkColumn,
            SamplesPerChunkRow = asset.SamplesPerChunkRow,
            SampleColumns = asset.SampleColumns,
            SampleRows = asset.SampleRows,
            RenderColumnsPerChunk = asset.RenderColumnsPerChunk,
            RenderRowsPerChunk = asset.RenderRowsPerChunk,
            RenderColumns = asset.RenderColumns,
            RenderRows = asset.RenderRows,
            DefaultHeight01 = asset.DefaultHeight01,
            DefaultLayerIndex = asset.DefaultLayerIndex,
            StorageLayout = asset.StorageLayout.ToString(),
            InterpolationMode = asset.InterpolationMode.ToString(),
            SampleScaleOffsetCm = asset.SampleScale.OffsetCm,
            SampleScaleUnitsPerSampleNumeratorCm = asset.SampleScale.UnitsPerSampleNumeratorCm,
            SampleScaleUnitsPerSampleDenominator = asset.SampleScale.UnitsPerSampleDenominator,
            BindingKind = asset.Binding.Kind.ToString(),
            LogicalColumns = asset.Binding.LogicalColumns,
            LogicalRows = asset.Binding.LogicalRows,
            EditedChunkCount = chunkEntries.Count,
            Erosion = new VisualTerrainEditorErosionManifest
            {
                Scale = erosion.Scale,
                Strength = erosion.Strength,
                GullyWeight = erosion.GullyWeight,
                Detail = erosion.Detail,
                RidgeRounding = erosion.RidgeRounding,
                CreaseRounding = erosion.CreaseRounding,
                InputRoundingMultiplier = erosion.InputRoundingMultiplier,
                OctaveRoundingMultiplier = erosion.OctaveRoundingMultiplier,
                InputOnset = erosion.InputOnset,
                OctaveOnset = erosion.OctaveOnset,
                RidgeMapInputOnset = erosion.RidgeMapInputOnset,
                RidgeMapOctaveOnset = erosion.RidgeMapOctaveOnset,
                AssumedSlopeValue = erosion.AssumedSlopeValue,
                AssumedSlopeMix = erosion.AssumedSlopeMix,
                CellScale = erosion.CellScale,
                Normalization = erosion.Normalization,
                Octaves = erosion.Octaves,
                Lacunarity = erosion.Lacunarity,
                Gain = erosion.Gain,
            },
            Chunks = chunkEntries,
        };

        string manifestPath = Path.Combine(saveDirectory, ManifestFileName);
        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(manifestPath, manifestJson);
        return manifestPath;
    }

    public static VisualTerrainEditorDocument LoadMap(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(manifestPath));
        }

        string resolvedManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(resolvedManifestPath))
        {
            throw new FileNotFoundException("Visual terrain editor manifest was not found.", resolvedManifestPath);
        }

        VisualTerrainEditorMapManifest manifest = ReadManifest(resolvedManifestPath);
        VisualTerrainAssetDescriptor asset = CreateAssetDescriptor(manifest);
        var document = new VisualTerrainEditorDocument(asset);
        document.ApplyErosionSettingsSnapshot(CreateErosionSnapshot(manifest));

        string manifestDirectory = Path.GetDirectoryName(resolvedManifestPath)
            ?? throw new DirectoryNotFoundException("Visual terrain editor manifest directory could not be resolved.");
        string chunkDirectory = Path.Combine(manifestDirectory, manifest.ChunkDirectory);
        int expectedChunkSampleCount = checked(manifest.SamplesPerChunkColumn * manifest.SamplesPerChunkRow);
        for (int i = 0; i < manifest.Chunks.Count; i++)
        {
            VisualTerrainEditorChunkManifestEntry entry = manifest.Chunks[i];
            string chunkPath = Path.Combine(chunkDirectory, entry.File);
            VisualTerrainEditorDocument.SavedChunkData chunk = ReadChunk(chunkPath, expectedChunkSampleCount);
            if (chunk.ChunkX != entry.ChunkX || chunk.ChunkY != entry.ChunkY)
            {
                throw new InvalidDataException("Visual terrain chunk header does not match its manifest entry.");
            }

            document.RestoreEditedChunk(chunk.ChunkX, chunk.ChunkY, chunk.BaseHeight);
        }

        document.Update();
        return document;
    }

    private static VisualTerrainEditorMapManifest ReadManifest(string manifestPath)
    {
        string manifestJson = File.ReadAllText(manifestPath);
        VisualTerrainEditorMapManifest manifest = JsonSerializer.Deserialize<VisualTerrainEditorMapManifest>(manifestJson)
            ?? throw new InvalidDataException("Visual terrain editor manifest is empty.");
        if (manifest.Version != ManifestVersion)
        {
            throw new InvalidDataException($"Unsupported visual terrain editor manifest version: {manifest.Version}.");
        }

        if (manifest.Erosion == null)
        {
            throw new InvalidDataException("Visual terrain editor manifest is missing erosion settings.");
        }

        return manifest;
    }

    private static VisualTerrainAssetDescriptor CreateAssetDescriptor(VisualTerrainEditorMapManifest manifest)
    {
        if (manifest.DefaultLayerIndex != 0)
        {
            throw new InvalidDataException("Visual terrain editor currently supports only a single default heightmap layer.");
        }

        if (!Enum.TryParse(manifest.StorageLayout, ignoreCase: false, out VisualHeightmapStorageLayout storageLayout))
        {
            throw new InvalidDataException($"Unknown visual terrain storage layout '{manifest.StorageLayout}'.");
        }

        if (!Enum.TryParse(manifest.InterpolationMode, ignoreCase: false, out VisualHeightmapInterpolationMode interpolationMode))
        {
            throw new InvalidDataException($"Unknown visual terrain interpolation mode '{manifest.InterpolationMode}'.");
        }

        if (!Enum.TryParse(manifest.BindingKind, ignoreCase: false, out VisualTerrainBindingKind bindingKind))
        {
            throw new InvalidDataException($"Unknown visual terrain binding kind '{manifest.BindingKind}'.");
        }

        return new VisualTerrainAssetDescriptor(
            manifest.MapId,
            manifest.DisplayName,
            new WorldAabbCm(
                manifest.BoundsLeftCm,
                manifest.BoundsTopCm,
                manifest.BoundsWidthCm,
                manifest.BoundsHeightCm),
            manifest.ChunkColumns,
            manifest.ChunkRows,
            manifest.SamplesPerChunkColumn,
            manifest.SamplesPerChunkRow,
            manifest.RenderColumnsPerChunk,
            manifest.RenderRowsPerChunk,
            manifest.DefaultHeight01,
            new VisualTerrainBindingDescriptor(bindingKind, manifest.LogicalColumns, manifest.LogicalRows),
            storageLayout,
            interpolationMode,
            new VisualHeightSampleScale(
                manifest.SampleScaleOffsetCm,
                manifest.SampleScaleUnitsPerSampleNumeratorCm,
                manifest.SampleScaleUnitsPerSampleDenominator));
    }

    private static VisualTerrainEditorDocument.VisualTerrainErosionSettingsSnapshot CreateErosionSnapshot(VisualTerrainEditorMapManifest manifest)
    {
        VisualTerrainEditorErosionManifest erosion = manifest.Erosion
            ?? throw new InvalidDataException("Visual terrain editor manifest is missing erosion settings.");
        return new VisualTerrainEditorDocument.VisualTerrainErosionSettingsSnapshot(
            erosion.Scale,
            erosion.Strength,
            erosion.GullyWeight,
            erosion.Detail,
            erosion.RidgeRounding,
            erosion.CreaseRounding,
            erosion.InputRoundingMultiplier,
            erosion.OctaveRoundingMultiplier,
            erosion.InputOnset,
            erosion.OctaveOnset,
            erosion.RidgeMapInputOnset,
            erosion.RidgeMapOctaveOnset,
            erosion.AssumedSlopeValue,
            erosion.AssumedSlopeMix,
            erosion.CellScale,
            erosion.Normalization,
            erosion.Octaves,
            erosion.Lacunarity,
            erosion.Gain);
    }

    private static void WriteChunk(string chunkPath, VisualTerrainEditorDocument.SavedChunkData chunk)
    {
        using FileStream stream = File.Create(chunkPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes(ChunkMagic));
        writer.Write(ChunkVersion);
        writer.Write(chunk.ChunkX);
        writer.Write(chunk.ChunkY);
        writer.Write(chunk.BaseHeight.Length);
        for (int i = 0; i < chunk.BaseHeight.Length; i++)
        {
            writer.Write(chunk.BaseHeight[i]);
        }
    }

    private static VisualTerrainEditorDocument.SavedChunkData ReadChunk(string chunkPath, int expectedSampleCount)
    {
        using FileStream stream = File.OpenRead(chunkPath);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (!string.Equals(magic, ChunkMagic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid visual terrain editor chunk magic.");
        }

        int version = reader.ReadInt32();
        if (version != ChunkVersion)
        {
            throw new InvalidDataException($"Unsupported visual terrain editor chunk version: {version}.");
        }

        int chunkX = reader.ReadInt32();
        int chunkY = reader.ReadInt32();
        int sampleCount = reader.ReadInt32();
        if (sampleCount != expectedSampleCount)
        {
            throw new InvalidDataException("Visual terrain editor chunk sample count does not match the manifest contract.");
        }

        float[] baseHeight = new float[sampleCount];
        for (int i = 0; i < baseHeight.Length; i++)
        {
            baseHeight[i] = reader.ReadSingle();
        }

        return new VisualTerrainEditorDocument.SavedChunkData(chunkX, chunkY, baseHeight);
    }

    private static string ResolveWorkspaceRoot()
    {
        string currentDirectory = Directory.GetCurrentDirectory();
        string? root = FindWorkspaceRoot(currentDirectory);
        if (root != null)
        {
            return root;
        }

        string baseDirectory = AppContext.BaseDirectory;
        root = FindWorkspaceRoot(baseDirectory);
        if (root != null)
        {
            return root;
        }

        throw new DirectoryNotFoundException("Unable to resolve Ludots workspace root for visual terrain map persistence.");
    }

    private static string? FindWorkspaceRoot(string startDirectory)
    {
        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current != null)
        {
            string gitbookReadme = Path.Combine(current.FullName, "gitbook", "README.md");
            string modsFolder = Path.Combine(current.FullName, "mods");
            if (File.Exists(gitbookReadme) && Directory.Exists(modsFolder))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
