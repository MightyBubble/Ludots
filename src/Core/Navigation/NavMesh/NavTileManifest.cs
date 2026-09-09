using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Sidecar manifest for one (map, board) nav tile artifact set. It records which inputs and
    /// which build capability produced the .ntil files so the loader can reject stale or foreign
    /// artifacts instead of querying geometry that no longer matches its source.
    /// Write time is recorded for humans only and never enters the content hash.
    /// Full tile identity is mapId + boardId + layer + profileId + tileCoord: the map and board
    /// come from the manifest itself (one manifest per nav board), layer/profile/coord come from
    /// each <see cref="NavTileManifestEntry"/>. Duplicate full identities are a write error.
    /// </summary>
    public sealed class NavTileManifest
    {
        public const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("mapId")]
        public string MapId { get; set; } = string.Empty;

        /// <summary>Board id when the map is board-scoped; empty for a single-board map.</summary>
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
        public NavTileManifestEntry[]? Tiles { get; set; } = Array.Empty<NavTileManifestEntry>();

        /// <summary>Recorded for humans; excluded from <see cref="BuildHash"/>.</summary>
        [JsonPropertyName("writtenUtc")]
        public string WrittenUtc { get; set; } = string.Empty;

        /// <summary>
        /// Rejects malformed manifests before they are written or served:
        /// null/empty tile list, blank identity fields, and duplicate full identities
        /// (same layer + profile + tile coordinate within this board) all fail fast.
        /// </summary>
        public void ValidateStructure()
        {
            if (string.IsNullOrWhiteSpace(MapId))
            {
                throw new InvalidDataException("Nav tile manifest must declare a mapId.");
            }

            if (BoardId == null)
            {
                throw new InvalidDataException($"Nav tile manifest for map '{MapId}' must declare a boardId (empty string for a single-board map, never null).");
            }

            if (FormatVersion <= 0)
            {
                throw new InvalidDataException($"Nav tile manifest for map '{MapId}' declares an invalid formatVersion {FormatVersion}.");
            }

            if (string.IsNullOrWhiteSpace(SourceRevision))
            {
                throw new InvalidDataException($"Nav tile manifest for map '{MapId}' must record the sourceRevision the tiles were baked from.");
            }

            if (string.IsNullOrWhiteSpace(Algorithm) || string.IsNullOrWhiteSpace(Mode))
            {
                throw new InvalidDataException($"Nav tile manifest for map '{MapId}' must record algorithm and mode.");
            }

            NavTileManifestEntry[]? tiles = Tiles;
            if (tiles == null)
            {
                throw new InvalidDataException($"Nav tile manifest for map '{MapId}' has a null tile list; the manifest is incomplete.");
            }

            if (tiles.Length == 0)
            {
                throw new InvalidDataException(
                    $"Nav tile manifest for map '{MapId}' board '{BoardId}' declares zero tiles; an empty bake must not be published as if it were a valid artifact set.");
            }

            var seen = new HashSet<string>(tiles.Length, StringComparer.Ordinal);
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTileManifestEntry entry = tiles[i];
                if (entry == null)
                {
                    throw new InvalidDataException($"Nav tile manifest for map '{MapId}' contains a null entry at index {i}.");
                }

                if (string.IsNullOrWhiteSpace(entry.ProfileId))
                {
                    throw new InvalidDataException($"Nav tile manifest for map '{MapId}' contains an entry with no profileId (index {i}).");
                }

                string key = ManifestEntryKey(entry);
                if (!seen.Add(key))
                {
                    throw new InvalidDataException(
                        $"Nav tile manifest for map '{MapId}' board '{BoardId}' declares tile identity '{key}' more than once; tile identities must be unique.");
                }
            }
        }

        internal static string ManifestEntryKey(NavTileManifestEntry entry)
            => $"{entry.Layer}\u001F{entry.ProfileId}\u001F{entry.ChunkX}\u001F{entry.ChunkY}";

        /// <summary>
        /// Whether this manifest lists an artifact for the given full tile identity.
        /// The board identity is the manifest's own board, so callers only supply the
        /// layer/profile/coordinate part.
        /// </summary>
        public bool HasEntryFor(int layer, string profileId, int chunkX, int chunkY)
        {
            NavTileManifestEntry[]? tiles = Tiles;
            if (tiles == null) return false;
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTileManifestEntry e = tiles[i];
                if (e.Layer == layer &&
                    string.Equals(e.ProfileId, profileId, StringComparison.Ordinal) &&
                    e.ChunkX == chunkX &&
                    e.ChunkY == chunkY)
                {
                    return true;
                }
            }

            return false;
        }

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
            NavTileManifestEntry[] tiles = Tiles ?? Array.Empty<NavTileManifestEntry>();
            string[] keys = new string[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTileManifestEntry entry = tiles[i];
                keys[i] = $"{ManifestEntryKey(entry)}\u001F{entry.TileChecksum}\u001F{entry.TileVersion}";
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

            manifest.ValidateStructure();
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

            // Read must enforce the same structural rules the writer enforces, so a hand-edited
            // or foreign manifest cannot introduce null tiles / blank identities / duplicate keys.
            manifest.ValidateStructure();

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
