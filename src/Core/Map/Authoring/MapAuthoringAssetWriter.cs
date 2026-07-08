using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;

namespace Ludots.Core.Map.Authoring
{
    public sealed class MapAuthoringSaveRequest
    {
        public MapSession Session { get; set; } = null!;
        public LogicTerrainField? LogicTerrain { get; set; }
        public IReadOnlyList<EntitySpawnData>? Entities { get; set; }
        public bool WriteNavTiles { get; set; } = true;
    }

    public sealed class MapAuthoringSaveResult
    {
        public MapAuthoringSaveResult(
            string modId,
            string mapConfigPath,
            IReadOnlyList<string> terrainPaths,
            int entityCount,
            int navTileCount)
        {
            ModId = modId ?? throw new ArgumentNullException(nameof(modId));
            MapConfigPath = mapConfigPath ?? throw new ArgumentNullException(nameof(mapConfigPath));
            TerrainPaths = terrainPaths ?? throw new ArgumentNullException(nameof(terrainPaths));
            EntityCount = entityCount;
            NavTileCount = navTileCount;
        }

        public string ModId { get; }

        public string MapConfigPath { get; }

        public IReadOnlyList<string> TerrainPaths { get; }

        public int EntityCount { get; }

        public int NavTileCount { get; }
    }

    public sealed class MapAuthoringConfigSaveResult
    {
        public MapAuthoringConfigSaveResult(string modId, string mapConfigPath, string mapId, int boardCount)
        {
            ModId = modId ?? throw new ArgumentNullException(nameof(modId));
            MapConfigPath = mapConfigPath ?? throw new ArgumentNullException(nameof(mapConfigPath));
            MapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
            BoardCount = boardCount;
        }

        public string ModId { get; }

        public string MapConfigPath { get; }

        public string MapId { get; }

        public int BoardCount { get; }
    }

    public sealed class MapAuthoringAssetWriter
    {
        private static readonly JsonSerializerOptions MapJsonOptions = new() { WriteIndented = true };

        private readonly GameEngine _engine;

        public MapAuthoringAssetWriter(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public string ResolveWritableTargetModId(MapSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(session.MapId.Value))
            {
                throw new InvalidOperationException("Map authoring target resolution requires a focused map id.");
            }

            return ResolveWritableMapConfigPath(session.MapId.Value).ModId;
        }

        public MapAuthoringConfigSaveResult SaveConfig(
            string modId,
            MapConfig mapConfig,
            bool overwriteExisting)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Target mod id is required.", nameof(modId));
            }

            if (mapConfig == null) throw new ArgumentNullException(nameof(mapConfig));
            if (string.IsNullOrWhiteSpace(mapConfig.Id))
            {
                throw new InvalidOperationException("MapConfig.Id is required for authoring save.");
            }

            if (mapConfig.Boards == null || mapConfig.Boards.Count == 0)
            {
                throw new InvalidOperationException("Map authoring save requires at least one board.");
            }

            string mapId = mapConfig.Id.Trim();
            string mapPath = ResolveWritableMapConfigPathForMod(modId.Trim(), mapId, overwriteExisting);
            Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
            File.WriteAllText(mapPath, JsonSerializer.Serialize(mapConfig, MapJsonOptions));
            return new MapAuthoringConfigSaveResult(modId.Trim(), mapPath, mapId, mapConfig.Boards.Count);
        }

        public MapAuthoringSaveResult Save(MapAuthoringSaveRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapSession session = request.Session ?? throw new ArgumentNullException(nameof(request.Session));
            if (string.IsNullOrWhiteSpace(session.MapId.Value))
            {
                throw new InvalidOperationException("Map authoring save requires a focused map id.");
            }

            string mapId = session.MapId.Value;
            (string modId, string mapPath) = ResolveWritableMapConfigPath(mapId);
            var terrainPaths = new List<string>(session.AllBoards.Count);
            if (request.LogicTerrain != null)
            {
                SaveLogicTerrainForPrimaryGridBoard(modId, session, request.LogicTerrain, terrainPaths);
            }

            if (request.Entities != null)
            {
                session.MapConfig.Entities = new List<EntitySpawnData>(request.Entities);
            }

            session.MapConfig.Id = mapId;
            Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
            File.WriteAllText(mapPath, JsonSerializer.Serialize(session.MapConfig, MapJsonOptions));

            int navTileCount = request.WriteNavTiles
                ? SaveLoadedNavTiles(modId, mapId)
                : 0;

            return new MapAuthoringSaveResult(
                modId,
                mapPath,
                terrainPaths,
                session.MapConfig.Entities?.Count ?? 0,
                navTileCount);
        }

        private void SaveLogicTerrainForPrimaryGridBoard(
            string modId,
            MapSession session,
            LogicTerrainField terrain,
            List<string> terrainPaths)
        {
            if (terrain.Topology != LogicTerrainTopology.Grid)
            {
                throw new InvalidOperationException(
                    $"Live map editor save only supports Grid LogicTerrain, got '{terrain.Topology}'.");
            }

            IBoard primary = session.PrimaryBoard
                ?? throw new InvalidOperationException("Map authoring save requires a primary board.");
            BoardConfig boardConfig = ResolveBoardConfig(session.MapConfig, primary.Name);
            if (!string.Equals(boardConfig.SpatialType, "Grid", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Live map editor save only writes Grid boards. Board '{boardConfig.Name}' is '{boardConfig.SpatialType}'.");
            }

            string dataFile = boardConfig.DataFile;
            if (string.IsNullOrWhiteSpace(dataFile) ||
                !string.Equals(Path.GetExtension(dataFile), ".ltrn", StringComparison.OrdinalIgnoreCase))
            {
                dataFile = $"{SanitizePathSegment(session.MapId.Value)}_{SanitizePathSegment(boardConfig.Name)}.ltrn";
                boardConfig.DataFile = dataFile;
            }

            string path = ResolveWritableDataFilePath(modId, dataFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (FileStream stream = File.Create(path))
            {
                LogicTerrainBinary.Write(stream, terrain);
            }

            terrainPaths.Add(path);
        }

        private int SaveLoadedNavTiles(string modId, string mapId)
        {
            if (_engine.GetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue) is RuntimeIncrementalNavMeshRebuildQueue queue &&
                queue.PendingTileCount > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot save map '{mapId}' while runtime nav rebuild has {queue.PendingTileCount} pending tiles.");
            }

            if (_engine.GetService(CoreServiceKeys.NavQueryServices) is not NavQueryServiceRegistry navRegistry ||
                _engine.GetService(CoreServiceKeys.NavMeshProfiles) is not NavMeshProfileRegistry profiles)
            {
                throw new InvalidOperationException(
                    $"Cannot save nav tiles for map '{mapId}' because NavQueryServices or NavMeshProfiles are missing.");
            }

            int written = 0;
            IReadOnlyList<KeyValuePair<NavQueryServiceKey, NavTileStore>> stores = navRegistry.SnapshotStores();
            for (int i = 0; i < stores.Count; i++)
            {
                NavQueryServiceKey key = stores[i].Key;
                string profileId = profiles.GetId(key.Profile);
                NavTile[] tiles = stores[i].Value.SnapshotLoadedTiles();
                for (int t = 0; t < tiles.Length; t++)
                {
                    NavTile tile = tiles[t];
                    string rel = NavAssetPaths.GetNavTileRelativePath(
                        mapId,
                        key.Layer,
                        profileId,
                        tile.TileId.ChunkX,
                        tile.TileId.ChunkY);
                    if (!_engine.VFS.TryResolveFullPath($"{modId}:{rel}", out string path))
                    {
                        throw new InvalidOperationException($"Cannot resolve NavTile write path '{modId}:{rel}'.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using FileStream stream = File.Create(path);
                    NavTileBinary.Write(stream, tile);
                    written++;
                }
            }

            return written;
        }

        private (string ModId, string Path) ResolveWritableMapConfigPath(string mapId)
        {
            var matches = new List<MapConfigPathMatch>(4);
            for (int i = 0; i < _engine.ModLoader.LoadedModIds.Count; i++)
            {
                string modId = _engine.ModLoader.LoadedModIds[i];
                AddExistingMapPath(matches, modId, $"assets/Maps/{mapId}.json");
                AddExistingMapPath(matches, modId, $"assets/maps/{mapId}.json");
            }

            MapConfigPathMatch? explicitMatch = null;
            for (int i = 0; i < matches.Count; i++)
            {
                if (!matches[i].ExplicitSaveTarget)
                {
                    continue;
                }

                if (explicitMatch.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' has multiple explicit live editor save targets: {explicitMatch.Value.ModId}, {matches[i].ModId}.");
                }

                explicitMatch = matches[i];
            }

            if (explicitMatch.HasValue)
            {
                MapConfigPathMatch match = explicitMatch.Value;
                return (match.ModId, match.Path);
            }

            if (matches.Count == 1)
            {
                MapConfigPathMatch match = matches[0];
                return (match.ModId, match.Path);
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' has multiple writable authoring map fragments with boards: {string.Join(", ", matches.ConvertAll(static m => m.ModId))}.");
            }

            throw new FileNotFoundException(
                $"Map '{mapId}' has no writable authoring map fragment with boards under loaded mod assets/Maps; refusing to save to tag-only overlays or an implicit location.");
        }

        private void AddExistingMapPath(List<MapConfigPathMatch> matches, string modId, string relativePath)
        {
            if (!_engine.VFS.TryResolveFullPath($"{modId}:{relativePath}", out string path) || !File.Exists(path))
            {
                return;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                if (string.Equals(matches[i].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            if (root is not JsonObject obj)
            {
                throw new InvalidDataException($"Map config '{path}' must be a JSON object.");
            }

            bool? saveTarget = ReadLiveEditorSaveTarget(obj);
            if (saveTarget == false || !DeclaresAuthoringBoards(obj))
            {
                return;
            }

            matches.Add(new MapConfigPathMatch(modId, path, saveTarget == true));
        }

        private string ResolveWritableDataFilePath(string modId, string dataFile)
        {
            if (string.IsNullOrWhiteSpace(dataFile))
            {
                throw new ArgumentException("DataFile is required.", nameof(dataFile));
            }

            string rel = dataFile.Replace('\\', '/').TrimStart('/');
            var candidates = new List<string>(3);
            if (rel.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(rel);
            }
            else
            {
                candidates.Add($"assets/Data/Maps/{rel}");
                candidates.Add($"assets/{rel}");
                candidates.Add(rel);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (_engine.VFS.TryResolveFullPath($"{modId}:{candidates[i]}", out string path) && File.Exists(path))
                {
                    return path;
                }
            }

            if (!_engine.VFS.TryResolveFullPath($"{modId}:{candidates[0]}", out string createdPath))
            {
                throw new InvalidOperationException($"Cannot resolve writable DataFile path '{modId}:{candidates[0]}'.");
            }

            return createdPath;
        }

        private string ResolveWritableMapConfigPathForMod(string modId, string mapId, bool overwriteExisting)
        {
            string relativePath = $"assets/Maps/{SanitizePathSegment(mapId)}.json";
            if (!_engine.VFS.TryResolveFullPath($"{modId}:{relativePath}", out string path))
            {
                throw new InvalidOperationException($"Cannot resolve writable map config path '{modId}:{relativePath}'.");
            }

            if (!overwriteExisting && File.Exists(path))
            {
                throw new IOException($"Map '{mapId}' already exists in mod '{modId}'.");
            }

            return path;
        }

        private static BoardConfig ResolveBoardConfig(MapConfig mapConfig, string boardName)
        {
            if (mapConfig.Boards == null || mapConfig.Boards.Count == 0)
            {
                throw new InvalidOperationException("MapConfig.Boards is empty.");
            }

            for (int i = 0; i < mapConfig.Boards.Count; i++)
            {
                BoardConfig board = mapConfig.Boards[i];
                if (string.Equals(board.Name, boardName, StringComparison.Ordinal))
                {
                    return board;
                }
            }

            throw new InvalidOperationException($"MapConfig does not contain board '{boardName}'.");
        }

        private static string SanitizePathSegment(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ArgumentException("Path segment is required.", nameof(raw));
            }

            Span<char> buffer = raw.Length <= 128 ? stackalloc char[raw.Length] : new char[raw.Length];
            int written = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                buffer[written++] =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '_' ||
                    c == '-'
                        ? c
                        : '_';
            }

            return new string(buffer[..written]);
        }

        private static bool DeclaresAuthoringBoards(JsonObject root)
        {
            foreach (KeyValuePair<string, JsonNode?> kvp in root)
            {
                if (!string.Equals(kvp.Key, "boards", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return kvp.Value is JsonArray boards && boards.Count > 0;
            }

            return false;
        }

        private static bool? ReadLiveEditorSaveTarget(JsonObject root)
        {
            if (!TryGetObjectCaseInsensitive(root, "metadata", out JsonObject? metadata) ||
                !TryGetObjectCaseInsensitive(metadata, "liveMapEditor", out JsonObject? liveMapEditor))
            {
                return null;
            }

            foreach (KeyValuePair<string, JsonNode?> kvp in liveMapEditor)
            {
                if (string.Equals(kvp.Key, "saveTarget", StringComparison.OrdinalIgnoreCase) &&
                    kvp.Value is JsonValue value &&
                    value.TryGetValue(out bool saveTarget))
                {
                    return saveTarget;
                }
            }

            return null;
        }

        private static bool TryGetObjectCaseInsensitive(JsonObject root, string name, out JsonObject? value)
        {
            foreach (KeyValuePair<string, JsonNode?> kvp in root)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase) &&
                    kvp.Value is JsonObject obj)
                {
                    value = obj;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private readonly record struct MapConfigPathMatch(string ModId, string Path, bool ExplicitSaveTarget);
    }
}
