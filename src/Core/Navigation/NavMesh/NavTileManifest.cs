using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Sidecar manifest for a map's nav tile artifacts. It records which inputs and which
    /// build capability produced the .ntil files so the loader can reject stale or foreign
    /// artifacts instead of querying geometry that no longer matches its source.
    /// Write time is recorded for humans only and never enters the content hash.
    /// </summary>
    public sealed class NavTileManifest
    {
        public const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("mapId")]
        public string MapId { get; set; } = string.Empty;

        /// <summary>Board id when the map is board-scoped; empty for single-board maps.</summary>
        [JsonPropertyName("boardId")]
        public string BoardId { get; set; } = string.Empty;

        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; set; } = NavTileBinary.FormatVersion;

        /// <summary>Identity of the source input the tiles were baked from.</summary>
        [JsonPropertyName("sourceRevision")]
        public string SourceRevision { get; set; } = string.Empty;

        /// <summary>
        /// Deterministic hash of the bake inputs and capability: map/board identity, tile
        /// geometry, profiles, layers, areas, algorithm and build config. Two bakes of the
        /// same inputs must produce the same value.
        /// </summary>
        [JsonPropertyName("buildHash")]
        public string BuildHash { get; set; } = string.Empty;

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = string.Empty;

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = string.Empty;

        /// <summary>Persistent tile version authored for this bake (not a runtime publish count).</summary>
        [JsonPropertyName("tileVersion")]
        public uint TileVersion { get; set; }

        [JsonPropertyName("tiles")]
        public NavTileManifestEntry[] Tiles { get; set; } = Array.Empty<NavTileManifestEntry>();

        /// <summary>Recorded for humans; excluded from <see cref="BuildHash"/>.</summary>
        [JsonPropertyName("writtenUtc")]
        public string WrittenUtc { get; set; } = string.Empty;

        /// <summary>
        /// Deterministic content hash over the identity-bearing fields. Excludes writtenUtc and
        /// any tile list ordering, so a rebuild of unchanged inputs yields the same value.
        /// </summary>
        public string ComputeBuildHash()
        {
            ulong h = 1469598103934665603UL;
            void Mix(string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                for (int i = 0; i < value.Length; i++)
                {
                    h ^= value[i];
                    h *= 1099511628211UL;
                }

                h ^= 0x1F;
                h *= 1099511628211UL;
            }

            void MixInt(long value)
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (byte)(value >> (i * 8));
                    h *= 1099511628211UL;
                }
            }

            Mix(MapId);
            Mix(BoardId);
            MixInt(FormatVersion);
            Mix(SourceRevision);
            Mix(Algorithm);
            Mix(Mode);
            MixInt(TileVersion);

            // Tiles are mixed in a canonical order so writer ordering cannot change the hash.
            string[] keys = new string[Tiles.Length];
            for (int i = 0; i < Tiles.Length; i++)
            {
                keys[i] = $"{Tiles[i].Layer}\u001F{Tiles[i].ProfileId}\u001F{Tiles[i].ChunkX}\u001F{Tiles[i].ChunkY}\u001F{Tiles[i].TileChecksum}";
            }

            Array.Sort(keys, StringComparer.Ordinal);
            for (int i = 0; i < keys.Length; i++)
            {
                Mix(keys[i]);
            }

            return "fnv1a64:" + h.ToString("x16");
        }
    }

    public sealed class NavTileManifestEntry
    {
        [JsonPropertyName("layer")]
        public int Layer { get; set; }

        [JsonPropertyName("profileId")]
        public string ProfileId { get; set; } = string.Empty;

        [JsonPropertyName("chunkX")]
        public int ChunkX { get; set; }

        [JsonPropertyName("chunkY")]
        public int ChunkY { get; set; }

        [JsonPropertyName("tileVersion")]
        public uint TileVersion { get; set; }

        /// <summary>Content checksum stored inside the .ntil header, for load-time validation.</summary>
        [JsonPropertyName("tileChecksum")]
        public string TileChecksum { get; set; } = string.Empty;
    }

    public static class NavTileManifestSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static void Write(string path, NavTileManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            manifest.SchemaVersion = NavTileManifest.CurrentSchemaVersion;
            manifest.BuildHash = manifest.ComputeBuildHash();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options) + Environment.NewLine);
        }

        public static NavTileManifest Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Nav tile manifest not found at '{path}'. Re-run the nav bake for this map so the manifest is written next to its .ntil files.",
                    path);
            }

            NavTileManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<NavTileManifest>(File.ReadAllText(path), Options);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Nav tile manifest at '{path}' is not valid JSON: {ex.Message}", ex);
            }

            if (manifest == null) throw new InvalidDataException($"Nav tile manifest at '{path}' deserialized to null.");
            if (manifest.SchemaVersion != NavTileManifest.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Nav tile manifest schema version {manifest.SchemaVersion} is not supported (expected {NavTileManifest.CurrentSchemaVersion}). Re-bake this map.");
            }

            if (manifest.FormatVersion != NavTileBinary.FormatVersion)
            {
                throw new InvalidDataException(
                    $"Nav tile manifest declares format version {manifest.FormatVersion} but the runtime reads {NavTileBinary.FormatVersion}. Re-bake this map.");
            }

            string expected = manifest.ComputeBuildHash();
            if (!string.Equals(manifest.BuildHash, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Nav tile manifest at '{path}' is tampered or stale: buildHash '{manifest.BuildHash}' does not match its own contents ('{expected}').");
            }

            return manifest;
        }
    }
}
