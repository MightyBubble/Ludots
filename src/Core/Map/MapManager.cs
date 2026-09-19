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

        /// <summary>最近一次 LoadMap 的跨 mod 片段合并报告（纯记录型；每次 LoadMap 重置）。</summary>
        public MapMergeReport LastMergeReport { get; } = new MapMergeReport();

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
            LastMergeReport.Clear();
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chain = new List<string>(8);
            MapConfig config = LoadMapInternal(mapId, visiting, chain);
            ResolvePendingTombstones(config);
            return config;
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

                var fragments = _configPipeline.CollectFragmentsWithSources(jsonPath);
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                for (int fi = 0; fi < fragments.Count; fi++)
                {
                    try
                    {
                        var jsonStr = fragments[fi].Node.ToJsonString();
                        RejectLegacyWorldExtentKeys(fragments[fi].Node, jsonPath);
                        RejectLegacyTriggerGraphMountKey(fragments[fi].Node, jsonPath);
                        ValidateHeartbeatIntervalTicks(fragments[fi].Node, jsonPath);
                        _ = MapVariableDeclarations.Parse(
                            fragments[fi].Node is JsonObject fragmentRoot &&
                            TryGetPropertyCaseInsensitive(fragmentRoot, "Variables", out JsonNode variablesNode)
                                ? variablesNode
                                : null,
                            mapId.Value);
                        var config = JsonSerializer.Deserialize<MapConfig>(jsonStr, jsonOptions);
                        if (config != null)
                        {
                            config.MergeSourceUri = fragments[fi].SourceUri;
                            configs.Add(config);
                        }
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
            if (source.Entities != null) MergeEntityFragments(target, source);

            if (source.PendingEntityTombstones != null && source.PendingEntityTombstones.Count > 0)
            {
                target.PendingEntityTombstones ??= new List<(string, string)>();
                target.PendingEntityTombstones.AddRange(source.PendingEntityTombstones);
            }
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
                string varSourceLabel = MapMergeReport.DescribeSource(source.MergeSourceUri, "<unknown-fragment>");
                foreach (var sourceVariable in source.Variables)
                {
                    if (sourceVariable == null)
                    {
                        continue;
                    }

                    string name = sourceVariable.Name ?? string.Empty;
                    if (sourceVariable.Delete == true)
                    {
                        target.PendingVariableTombstones ??= new List<(string, string)>();
                        target.PendingVariableTombstones.Add((name, varSourceLabel));
                        continue;
                    }

                    // 同名重新声明撤销先前变量墓碑（复活）。
                    target.PendingVariableTombstones?.RemoveAll(t => string.Equals(t.Name, name, StringComparison.Ordinal));
                    int existing = target.Variables.FindIndex(v =>
                        string.Equals(v.Name ?? string.Empty, name, StringComparison.Ordinal));
                    if (existing >= 0)
                    {
                        if (target.Variables[existing].Type != sourceVariable.Type)
                        {
                            throw new InvalidOperationException(
                                $"Map '{target.Id}' fragment '{varSourceLabel}' redeclares variable '{name}' with type {sourceVariable.Type} (was {target.Variables[existing].Type}); cross-fragment type changes are contract breaks, delete-then-redeclare instead.");
                        }

                        target.Variables[existing] = sourceVariable;
                    }
                    else
                    {
                        target.Variables.Add(sourceVariable);
                    }
                }
            }

            if (source.PendingVariableTombstones != null && source.PendingVariableTombstones.Count > 0)
            {
                target.PendingVariableTombstones ??= new List<(string, string)>();
                target.PendingVariableTombstones.AddRange(source.PendingVariableTombstones);
            }
        }

        /// <summary>
        /// 墓碑在继承链展开后才消化：TryRemove 命中记 Deleted、未命中记 DeletionsNotFound。
        /// 此时父图实体已合入，子图墓碑可正确命中父图实例（继承方向的删除语义）。
        /// </summary>
        private void ResolvePendingTombstones(MapConfig config)
        {
            if (config.PendingEntityTombstones != null && config.PendingEntityTombstones.Count > 0)
            {
                foreach (var (instanceId, sourceLabel) in config.PendingEntityTombstones)
                {
                    if (TryRemoveEntityById(config, instanceId))
                    {
                        LastMergeReport.RecordDeletion(config.Id, instanceId, sourceLabel);
                    }
                    else
                    {
                        LastMergeReport.RecordDeletionNotFound(config.Id, instanceId, sourceLabel);
                    }
                }

                config.PendingEntityTombstones.Clear();
            }

            if (config.PendingVariableTombstones != null && config.PendingVariableTombstones.Count > 0)
            {
                foreach (var (name, sourceLabel) in config.PendingVariableTombstones)
                {
                    int index = config.Variables.FindIndex(v =>
                        string.Equals(v.Name ?? string.Empty, name, StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        config.Variables.RemoveAt(index);
                        LastMergeReport.RecordVariableDeletion(config.Id, name, sourceLabel);
                    }
                    else
                    {
                        LastMergeReport.RecordVariableDeletionNotFound(config.Id, name, sourceLabel);
                    }
                }

                config.PendingVariableTombstones.Clear();
            }
        }

        /// <summary>
        /// 地图实体跨片段合并：键 = instanceId（ordinal 精确匹配，不 trim——未 trim 的写法由
        /// 装载期 Register 的 trim 校验 fail-fast，合并层不做静默归一）。同 id 字段级深合并、
        /// 后写赢；__delete 墓碑删实例（与资产层 ConfigMerger 同键，更晚片段可复活）；匿名
        /// 实体纯追加，非首片段的匿名实体记入合并报告。继承链与跨 mod 片段共用本语义。
        /// </summary>
        private void MergeEntityFragments(MapConfig target, MapConfig source)
        {
            string sourceLabel = MapMergeReport.DescribeSource(source.MergeSourceUri, "<unknown-fragment>");
            bool isBaseFragment = target.Entities.Count == 0;
            var seenInFragment = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < source.Entities.Count; i++)
            {
                EntitySpawnData incoming = source.Entities[i];
                if (incoming == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(incoming.InstanceId))
                {
                    if (!seenInFragment.Add(incoming.InstanceId))
                    {
                        throw new InvalidOperationException(
                            $"Map '{target.Id}' fragment '{sourceLabel}' declares duplicate InstanceId '{incoming.InstanceId}' within the same fragment; intra-fragment duplicates are authoring errors.");
                    }
                }

                if (incoming.Delete == true)
                {
                    if (string.IsNullOrWhiteSpace(incoming.InstanceId))
                    {
                        throw new InvalidOperationException(
                            $"Map '{target.Id}' fragment '{sourceLabel}' authors __delete on an entity without InstanceId; tombstones must target an addressable instance.");
                    }

                    if (!string.Equals(incoming.InstanceId, incoming.InstanceId.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Map '{target.Id}' fragment '{sourceLabel}' tombstone InstanceId '{incoming.InstanceId}' must be trimmed.");
                    }

                    target.PendingEntityTombstones ??= new List<(string, string)>();
                    target.PendingEntityTombstones.Add((incoming.InstanceId, sourceLabel));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(incoming.InstanceId))
                {
                    if (!isBaseFragment)
                    {
                        LastMergeReport.RecordAnonymousNonBaseFragment(target.Id, i, sourceLabel);
                    }

                    target.Entities.Add(incoming);
                    continue;
                }

                // 同 id 重新声明即撤销先前墓碑（复活）；跨片段同 id = 深合并。
                target.PendingEntityTombstones?.RemoveAll(t => string.Equals(t.InstanceId, incoming.InstanceId, StringComparison.Ordinal));

                int existingIndex = FindEntityIndex(target, incoming.InstanceId);
                if (existingIndex < 0)
                {
                    target.Entities.Add(incoming);
                    LastMergeReport.RecordWinner(target.Id, incoming.InstanceId, sourceLabel);
                    continue;
                }

                MergeEntityData(target.Entities[existingIndex], incoming);
                LastMergeReport.RecordWinner(target.Id, incoming.InstanceId, sourceLabel);
            }
        }

        private static bool TryRemoveEntityById(MapConfig target, string instanceId)
        {
            for (int i = 0; i < target.Entities.Count; i++)
            {
                if (string.Equals(target.Entities[i]?.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    target.Entities.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private static int FindEntityIndex(MapConfig target, string instanceId)
        {
            for (int i = 0; i < target.Entities.Count; i++)
            {
                if (string.Equals(target.Entities[i]?.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void MergeEntityData(EntitySpawnData target, EntitySpawnData source)
        {
            if (!string.IsNullOrWhiteSpace(source.Template))
            {
                target.Template = source.Template;
            }

            if (source.PositionXCm.HasValue)
            {
                target.PositionXCm = source.PositionXCm;
            }

            if (source.PositionYCm.HasValue)
            {
                target.PositionYCm = source.PositionYCm;
            }

            if (source.Overrides != null)
            {
                target.Overrides ??= new Dictionary<string, JsonNode>();
                foreach (var kvp in source.Overrides)
                {
                    if (target.Overrides.TryGetValue(kvp.Key, out JsonNode? existing) &&
                        existing is JsonObject existingObject &&
                        kvp.Value is JsonObject incomingObject)
                    {
                        ConfigMerger.MergeObject(existingObject, incomingObject, Array.Empty<string>());
                        continue;
                    }

                    target.Overrides[kvp.Key] = kvp.Value?.DeepClone();
                }
            }

            if (source.PresenterParamOverrides != null)
            {
                foreach (var incoming in source.PresenterParamOverrides)
                {
                    int index = target.PresenterParamOverrides.FindIndex(p =>
                        string.Equals(p.ParamKey, incoming.ParamKey, StringComparison.Ordinal) &&
                        p.Lane == incoming.Lane);
                    if (index >= 0)
                    {
                        target.PresenterParamOverrides[index] = incoming;
                    }
                    else
                    {
                        target.PresenterParamOverrides.Add(incoming);
                    }
                }
            }

            if (source.Relations != null)
            {
                target.Relations ??= new List<EntityRelationAuthoring>();
                foreach (var relation in source.Relations)
                {
                    if (relation == null)
                    {
                        continue;
                    }

                    int index = target.Relations.FindIndex(r =>
                        string.Equals(r?.To, relation.To, StringComparison.Ordinal) &&
                        string.Equals(r?.Type, relation.Type, StringComparison.Ordinal));
                    if (relation.Delete == true)
                    {
                        if (index >= 0)
                        {
                            target.Relations.RemoveAt(index);
                        }

                        continue;
                    }

                    if (index >= 0)
                    {
                        EntityRelationAuthoring existing = target.Relations[index];
                        if (relation.Metric != null)
                        {
                            existing.Metric ??= new Dictionary<string, int>();
                            foreach (var metricKvp in relation.Metric)
                            {
                                existing.Metric[metricKvp.Key] = metricKvp.Value;
                            }
                        }
                    }
                    else
                    {
                        target.Relations.Add(relation);
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
