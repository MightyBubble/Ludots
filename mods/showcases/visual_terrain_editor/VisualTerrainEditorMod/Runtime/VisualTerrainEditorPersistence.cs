using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace VisualTerrainEditorMod.Runtime;

internal static class VisualTerrainEditorPersistence
{
    private const string SaveFolderRelativePath = "mods\\showcases\\visual_terrain_editor\\VisualTerrainEditorMod\\assets\\UserMaps";
    private const string ManifestFileName = "map.json";
    private const string ChunkDirectoryName = "chunks";
    private const string ChunkFileExtension = ".vtchunk";
    private const string ChunkMagic = "VTCK";
    private const int ChunkVersion = 1;

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

        var manifest = new VisualTerrainEditorMapManifest
        {
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
            BindingKind = asset.Binding.Kind.ToString(),
            LogicalColumns = asset.Binding.LogicalColumns,
            LogicalRows = asset.Binding.LogicalRows,
            EditedChunkCount = chunkEntries.Count,
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
