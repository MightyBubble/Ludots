using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public sealed class LogicHeightmapEditPatch
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string SourcePath { get; set; } = string.Empty;

        public string OutputPath { get; set; } = string.Empty;

        public string Tool { get; set; } = string.Empty;

        public string AuthoringMode { get; set; } = "logic_heightmap_layer_area_editor";

        public List<LogicHeightmapEditOperation> Operations { get; set; } = new();

        public List<string> DirtyChunks { get; set; } = new();

        public static LogicHeightmapEditPatch Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Patch path is required.", nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException($"LogicHeightmap edit patch not found: {path}", path);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            LogicHeightmapEditPatch patch = JsonSerializer.Deserialize<LogicHeightmapEditPatch>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException($"LogicHeightmap edit patch is empty: {path}");
            patch.Validate();
            return patch;
        }

        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Patch path is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RefreshDirtyChunks();
            File.WriteAllText(fullPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }));
        }

        public ApplyResult Apply(string inputPath, string outputPath, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("Input .lhtm path is required.", nameof(inputPath));
            if (!File.Exists(inputPath)) throw new FileNotFoundException($"Input .lhtm not found: {inputPath}", inputPath);
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output .lhtm path is required.", nameof(outputPath));

            string fullOutput = Path.GetFullPath(outputPath);
            if (File.Exists(fullOutput) && !overwrite)
            {
                throw new IOException($"Output exists: {fullOutput} (pass --overwrite to replace)");
            }

            LogicHeightmap map;
            using (var input = File.OpenRead(inputPath))
            {
                map = LogicHeightmapBinary.Read(input);
            }

            var dirty = new SortedSet<string>(StringComparer.Ordinal);
            int appliedCells = 0;
            foreach (LogicHeightmapEditOperation op in Operations)
            {
                op.Validate();
                for (int y = op.MinSampleY; y <= op.MaxSampleY; y++)
                {
                    for (int x = op.MinSampleX; x <= op.MaxSampleX; x++)
                    {
                        ApplyOperation(map, op, x, y);
                        dirty.Add(ToChunkKey(x >> LogicHeightmapChunk.ChunkSizeShift, y >> LogicHeightmapChunk.ChunkSizeShift));
                        appliedCells++;
                    }
                }
            }

            string? outputDirectory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            using (var output = File.Create(fullOutput))
            {
                LogicHeightmapBinary.Write(output, map);
            }

            DirtyChunks = dirty.ToList();
            SourcePath = Path.GetFullPath(inputPath);
            OutputPath = fullOutput;
            return new ApplyResult(Operations.Count, appliedCells, DirtyChunks.ToArray());
        }

        public void RefreshDirtyChunks()
        {
            var dirty = new SortedSet<string>(StringComparer.Ordinal);
            foreach (LogicHeightmapEditOperation op in Operations)
            {
                op.Validate();
                int minChunkX = op.MinSampleX >> LogicHeightmapChunk.ChunkSizeShift;
                int minChunkY = op.MinSampleY >> LogicHeightmapChunk.ChunkSizeShift;
                int maxChunkX = op.MaxSampleX >> LogicHeightmapChunk.ChunkSizeShift;
                int maxChunkY = op.MaxSampleY >> LogicHeightmapChunk.ChunkSizeShift;
                for (int cy = minChunkY; cy <= maxChunkY; cy++)
                {
                    for (int cx = minChunkX; cx <= maxChunkX; cx++)
                    {
                        dirty.Add(ToChunkKey(cx, cy));
                    }
                }
            }

            DirtyChunks = dirty.ToList();
        }

        public void Validate()
        {
            if (SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported LogicHeightmap edit patch schemaVersion: {SchemaVersion}");
            }

            if (Operations == null)
            {
                throw new InvalidDataException("LogicHeightmap edit patch operations cannot be null.");
            }

            foreach (LogicHeightmapEditOperation op in Operations)
            {
                op.Validate();
            }
        }

        private static void ApplyOperation(LogicHeightmap map, LogicHeightmapEditOperation op, int sampleX, int sampleY)
        {
            if ((uint)sampleX >= (uint)map.WidthSamples || (uint)sampleY >= (uint)map.HeightSamples)
            {
                return;
            }

            if (op.AreaId.HasValue)
            {
                map.SetAreaId(sampleX, sampleY, op.AreaId.Value);
            }

            if (op.Blocked.HasValue)
            {
                map.SetBlocked(sampleX, sampleY, op.Blocked.Value);
            }

            if (op.Ramp.HasValue)
            {
                map.SetRamp(sampleX, sampleY, op.Ramp.Value);
            }

            if (op.HeightCm.HasValue)
            {
                map.SetHeightCm(sampleX, sampleY, op.HeightCm.Value);
            }

            if (op.WaterHeightCm.HasValue)
            {
                map.SetWaterHeightCm(sampleX, sampleY, op.WaterHeightCm.Value);
            }
        }

        private static string ToChunkKey(int chunkX, int chunkY) => $"{chunkX},{chunkY}";

        public readonly record struct ApplyResult(int OperationCount, int AppliedCellCount, string[] DirtyChunks);
    }

    public sealed class LogicHeightmapEditOperation
    {
        public string Tool { get; set; } = "area";

        public int MinSampleX { get; set; }

        public int MinSampleY { get; set; }

        public int MaxSampleX { get; set; }

        public int MaxSampleY { get; set; }

        public byte? AreaId { get; set; }

        public bool? Blocked { get; set; }

        public bool? Ramp { get; set; }

        public int? HeightCm { get; set; }

        public int? WaterHeightCm { get; set; }

        public void Validate()
        {
            if (MinSampleX < 0 || MinSampleY < 0)
            {
                throw new InvalidDataException("LogicHeightmap edit operation min sample must be non-negative.");
            }

            if (MaxSampleX < MinSampleX || MaxSampleY < MinSampleY)
            {
                throw new InvalidDataException("LogicHeightmap edit operation max sample must be >= min sample.");
            }

            if (!AreaId.HasValue && !Blocked.HasValue && !Ramp.HasValue && !HeightCm.HasValue && !WaterHeightCm.HasValue)
            {
                throw new InvalidDataException("LogicHeightmap edit operation must change at least one field.");
            }
        }
    }
}
