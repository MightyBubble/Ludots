using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Ludots.Core.Map
{
    public class MapManager : IMapManager
    {
        private readonly IVirtualFileSystem _vfs;
        private readonly TriggerManager _triggerManager;
        private readonly ModLoader _modLoader;
        private ConfigPipeline _configPipeline;

        // Registry for Map Definitions (Code-First)
        private readonly Dictionary<MapId, MapDefinition> _definitions = new Dictionary<MapId, MapDefinition>();
        private readonly Dictionary<Type, MapDefinition> _typeToDefinition = new Dictionary<Type, MapDefinition>();

        public MapManager(IVirtualFileSystem vfs, TriggerManager triggerManager, ModLoader modLoader, ConfigPipeline configPipeline = null)
        {
            _vfs = vfs;
            _triggerManager = triggerManager;
            _modLoader = modLoader;
            _configPipeline = configPipeline;
        }

        public void SetConfigPipeline(ConfigPipeline pipeline)
        {
            _configPipeline = pipeline;
        }

        public void RegisterMap(MapDefinition definition)
        {
            if (definition == null) return;
            if (_definitions.ContainsKey(definition.Id))
            {
                Log.Warn(in LogChannels.Map, $"Overwriting map definition for {definition.Id}");
            }
            _definitions[definition.Id] = definition;
            _typeToDefinition[definition.GetType()] = definition;
            Log.Info(in LogChannels.Map, $"Registered Map Definition: {definition.Id} ({definition.GetType().Name})");
        }
        
        public MapDefinition GetDefinition<T>() where T : MapDefinition
        {
            return _typeToDefinition.TryGetValue(typeof(T), out var def) ? def : null;
        }

        public MapDefinition GetDefinition(MapId mapId)
        {
            return _definitions.TryGetValue(mapId, out var def) ? def : null;
        }

        public MapConfig LoadMap(string mapId)
        {
            return LoadMap(new MapId(mapId));
        }

        public MapConfig LoadMap(MapId mapId)
        {
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chain = new List<string>(8);
            return LoadMapInternal(mapId, visiting, chain);
        }

        private MapConfig LoadMapInternal(MapId mapId, HashSet<string> visiting, List<string> chain)
        {
            Log.Info(in LogChannels.Map, $"Loading Map: {mapId}");

            var mapIdValue = mapId.Value;
            if (!visiting.Add(mapIdValue))
            {
                chain.Add(mapIdValue);
                var cycle = string.Join(" -> ", chain);
                throw new InvalidOperationException($"Cyclic map inheritance detected: {cycle}");
            }
            chain.Add(mapIdValue);
            
            try
            {
                // 0. Check if we have a code definition
                MapDefinition definition = null;
                if (_definitions.TryGetValue(mapId, out var def))
                {
                    definition = def;
                    Log.Info(in LogChannels.Map, $"Found Code Definition: {def.GetType().Name}");
                }

                // 1. Find all config fragments
                var configs = new List<MapConfig>();

                // If definition exists, use its DataFilePath. Otherwise use default convention.
                string jsonPath = definition != null ? definition.DataFilePath : $"Maps/{mapId}.json";

                // Normalize path to remove leading slash if any
                if (jsonPath.StartsWith("/") || jsonPath.StartsWith("\\")) jsonPath = jsonPath.Substring(1);

                if (_configPipeline == null)
                    throw new InvalidOperationException("MapManager requires ConfigPipeline. Call SetConfigPipeline before LoadMap.");

                var fragments = _configPipeline.CollectFragments(jsonPath);
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                for (int fi = 0; fi < fragments.Count; fi++)
                {
                    try
                    {
                        var jsonStr = fragments[fi].ToJsonString();
                        RejectLegacyWorldExtentKeys(fragments[fi], jsonPath);
                        RejectLegacyTriggerGraphMountKey(fragments[fi], jsonPath);
                        ValidateHeartbeatIntervalTicks(fragments[fi], jsonPath);
                        _ = MapVariableDeclarations.Parse(
                            fragments[fi] is JsonObject fragmentRoot &&
                            TryGetPropertyCaseInsensitive(fragmentRoot, "Variables", out JsonNode variablesNode)
                                ? variablesNode
                                : null,
                            mapId.Value);
                        var config = JsonSerializer.Deserialize<MapConfig>(jsonStr, jsonOptions);
                        if (config != null) configs.Add(config);
                    }
                    catch (JsonException ex)
                    {
                        Log.Error(in LogChannels.Map, $"Invalid JSON fragment for '{jsonPath}': {ex.Message}");
                    }
                }

                if (configs.Count == 0 && definition == null)
                {
                    Log.Error(in LogChannels.Map, $"Map '{mapId}' not found (No Definition, No Data).");
                    return null;
                }

                // 2. Merge configs
                var finalConfig = new MapConfig { Id = mapId.ToString() };
                foreach (var cfg in configs)
                {
                    MergeMapConfig(finalConfig, cfg);
                }
            
                // 3. Apply Definition Metadata (Tags + Boards from code-first)
                if (definition != null)
                {
                    if (finalConfig.Tags == null) finalConfig.Tags = new List<string>();

                    foreach (var tag in definition.Tags)
                    {
                        if (!finalConfig.Tags.Contains(tag.Name))
                        {
                            finalConfig.Tags.Add(tag.Name);
                        }
                    }

                    // Merge code-first Boards into finalConfig (by name, code-first is base)
                    if (definition.Boards != null && definition.Boards.Count > 0)
                    {
                        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var b in finalConfig.Boards)
                        {
                            if (!string.IsNullOrEmpty(b.Name)) existingNames.Add(b.Name);
                        }
                        foreach (var codeBoardCfg in definition.Boards)
                        {
                            if (!existingNames.Contains(codeBoardCfg.Name))
                            {
                                finalConfig.Boards.Add(codeBoardCfg.Clone());
                            }
                        }
                    }
                }

                // 4. Handle Inheritance (ParentId)
                if (!string.IsNullOrEmpty(finalConfig.ParentId))
                {
                    Log.Info(in LogChannels.Map, $"Loading Parent Map: {finalConfig.ParentId}");
                    var parentConfig = LoadMapInternal(new MapId(finalConfig.ParentId), visiting, chain);
                    if (parentConfig != null)
                    {
                        var childConfig = finalConfig;
                        finalConfig = parentConfig; 
                        MergeMapConfig(finalConfig, childConfig); 
                    }
                }
                
                Log.Info(in LogChannels.Map, $"Map '{mapId}' loaded.");
                return finalConfig;
            }
            finally
            {
                if (chain.Count > 0 && string.Equals(chain[^1], mapIdValue, StringComparison.OrdinalIgnoreCase))
                {
                    chain.RemoveAt(chain.Count - 1);
                }
                visiting.Remove(mapIdValue);
            }
        }

        private void MergeMapConfig(MapConfig target, MapConfig source)
        {
            if (!string.IsNullOrEmpty(source.ParentId)) target.ParentId = source.ParentId;
            if (!string.IsNullOrWhiteSpace(source.ContinuousHeightmapAsset))
            {
                target.ContinuousHeightmapAsset = source.ContinuousHeightmapAsset;
                if (target.ContinuousHeightmap != null && source.ContinuousHeightmap == null)
                {
                    target.ContinuousHeightmap.Asset = string.Empty;
                }
            }

            if (source.ContinuousHeightmap != null)
            {
                target.ContinuousHeightmap = source.ContinuousHeightmap.Clone();
                if (!string.IsNullOrWhiteSpace(target.ContinuousHeightmap.Asset))
                {
                    target.ContinuousHeightmapAsset = target.ContinuousHeightmap.Asset;
                }
            }

            if (source.TerrainPresentation != null) target.TerrainPresentation = source.TerrainPresentation.Clone();
            if (!string.IsNullOrWhiteSpace(source.StructureCollisionAsset)) target.StructureCollisionAsset = source.StructureCollisionAsset;
            if (source.StructureAwareGrounding) target.StructureAwareGrounding = true;
            if (source.StructureAwareNavigation) target.StructureAwareNavigation = true;

            if (source.Dependencies != null)
            {
                foreach (var kvp in source.Dependencies)
                {
                    target.Dependencies[kvp.Key] = kvp.Value;
                }
            }
            if (source.Entities != null) target.Entities.AddRange(source.Entities);
            if (source.Teams != null) target.Teams.AddRange(source.Teams);
            if (source.Players != null) target.Players.AddRange(source.Players);
            if (source.ParticipantRelationships != null)
            {
                if (target.ParticipantRelationships == null)
                {
                    target.ParticipantRelationships = new ParticipantRelationshipConfig();
                }

                if (source.ParticipantRelationships.Teams != null)
                {
                    target.ParticipantRelationships.Teams.AddRange(source.ParticipantRelationships.Teams);
                }

                if (source.ParticipantRelationships.Players != null)
                {
                    target.ParticipantRelationships.Players.AddRange(source.ParticipantRelationships.Players);
                }

                if (source.ParticipantRelationships.PlayerTeams != null)
                {
                    target.ParticipantRelationships.PlayerTeams.AddRange(source.ParticipantRelationships.PlayerTeams);
                }
            }

            if (source.Metadata != null)
            {
                foreach (var kvp in source.Metadata)
                {
                    target.Metadata[kvp.Key] = kvp.Value?.DeepClone();
                }
            }

            // Merge Tags
            if (source.Tags != null)
            {
                if (target.Tags == null) target.Tags = new List<string>();
                foreach (var t in source.Tags)
                {
                    if (!target.Tags.Contains(t)) target.Tags.Add(t);
                }
            }

            // Merge Boards (append by name: if same name exists, source overwrites)
            if (source.Boards != null)
            {
                foreach (var srcBoard in source.Boards)
                {
                    bool found = false;
                    for (int i = 0; i < target.Boards.Count; i++)
                    {
                        if (string.Equals(target.Boards[i].Name, srcBoard.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            target.Boards[i] = srcBoard.Clone();
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        target.Boards.Add(srcBoard.Clone());
                    }
                }
            }

            // Merge TriggerTypes (dedup)
            if (source.TriggerTypes != null)
            {
                foreach (var tt in source.TriggerTypes)
                {
                    if (!target.TriggerTypes.Contains(tt))
                    {
                        target.TriggerTypes.Add(tt);
                    }
                }
            }

            // Merge TriggerGraphs (append mount objects)
            if (source.TriggerGraphs != null)
            {
                if (target.TriggerGraphs is JsonArray targetArray && source.TriggerGraphs is JsonArray sourceArray)
                {
                    for (int i = 0; i < sourceArray.Count; i++)
                    {
                        targetArray.Add(sourceArray[i]?.DeepClone());
                    }
                }
                else
                {
                    target.TriggerGraphs = source.TriggerGraphs.DeepClone();
                }
            }

            // Merge Regions (append region objects)
            if (source.Regions != null)
            {
                if (target.Regions is JsonArray targetRegionArray && source.Regions is JsonArray sourceRegionArray)
                {
                    for (int i = 0; i < sourceRegionArray.Count; i++)
                    {
                        targetRegionArray.Add(sourceRegionArray[i]?.DeepClone());
                    }
                }
                else
                {
                    target.Regions = source.Regions.DeepClone();
                }
            }

            // Merge DefaultCamera (source wins)
            if (source.DefaultCamera != null) target.DefaultCamera = source.DefaultCamera;

            // Merge Fields (source wins; the enabled-layer list replaces as a whole)
            if (source.Fields != null) target.Fields = source.Fields;

            // Merge DeathRule (source wins)
            if (source.DeathRule != null)
            {
                target.DeathRule = source.DeathRule;
            }

            // Merge HeartbeatIntervalTicks (source wins)
            if (source.HeartbeatIntervalTicks.HasValue)
            {
                target.HeartbeatIntervalTicks = source.HeartbeatIntervalTicks;
            }

            // Merge Variables (later fragment / child map replaces same-name declaration)
            if (source.Variables != null && source.Variables.Count > 0)
            {
                foreach (var sourceVariable in source.Variables)
                {
                    string name = (sourceVariable.Name ?? string.Empty).Trim();
                    int existing = target.Variables.FindIndex(v =>
                        string.Equals((v.Name ?? string.Empty).Trim(), name, StringComparison.Ordinal));
                    if (existing >= 0)
                    {
                        target.Variables[existing] = sourceVariable;
                    }
                    else
                    {
                        target.Variables.Add(sourceVariable);
                    }
                }
            }
        }

        private static void RejectLegacyTriggerGraphMountKey(JsonNode fragment, string jsonPath)
        {
            if (fragment is not JsonObject root)
            {
                return;
            }

            foreach (var kvp in root)
            {
                if (string.Equals(kvp.Key, "MapTriggerGraphs", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Map config '{jsonPath}' uses legacy key '{kvp.Key}'. The mount field was renamed with the dialect (MapTrigger → TriggerGraph); use 'TriggerGraphs' instead.");
                }
            }
        }

        private static void ValidateHeartbeatIntervalTicks(JsonNode fragment, string jsonPath)
        {
            if (fragment is not JsonObject root ||
                !TryGetPropertyCaseInsensitive(root, "HeartbeatIntervalTicks", out JsonNode node))
            {
                return;
            }

            if (node is not JsonValue value ||
                !value.TryGetValue<int>(out int intervalTicks) ||
                intervalTicks < 1)
            {
                throw new InvalidOperationException(
                    $"Map config '{jsonPath}' field 'HeartbeatIntervalTicks' requires an integer >= 1.");
            }
        }

        private static void RejectLegacyWorldExtentKeys(JsonNode fragment, string jsonPath)
        {
            if (fragment is not JsonObject root)
            {
                return;
            }

            RejectLegacyKey(root, "WidthInTiles", "widthInMacroTiles", jsonPath);
            RejectLegacyKey(root, "HeightInTiles", "heightInMacroTiles", jsonPath);

            if (!TryGetPropertyCaseInsensitive(root, "boards", out JsonNode boardsNode) ||
                boardsNode is not JsonArray boards)
            {
                return;
            }

            for (int i = 0; i < boards.Count; i++)
            {
                if (boards[i] is not JsonObject board)
                {
                    continue;
                }

                RejectLegacyKey(board, "WidthInTiles", "widthInMacroTiles", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "HeightInTiles", "heightInMacroTiles", $"{jsonPath}.boards[{i}]");
            }
        }

        private static void RejectLegacyKey(JsonObject obj, string legacyName, string replacementName, string context)
        {
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, legacyName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Map config '{context}' uses legacy key '{kvp.Key}'. Use '{replacementName}' instead.");
                }
            }
        }

        private static bool TryGetPropertyCaseInsensitive(JsonObject obj, string name, out JsonNode node)
        {
            foreach (var kvp in obj)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    node = kvp.Value;
                    return true;
                }
            }

            node = null;
            return false;
        }
    }
}
