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
using Ludots.Core.Spatial;
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
            MapConfig? config = LoadMapInternal(mapId, visiting, chain);
            if (config != null)
            {
                ResolvePendingTombstones(mapId.Value, config);
                // Backfill runs once at the top level only: parent configs must stay
                // un-backfilled so child conflict checks compare authored values (#1567).
                ApplyWorldTuningToBoards(config);
            }
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
                        childConfig.MergeSourceUri ??= $"map:{mapIdValue}";
                        finalConfig = parentConfig; 
                        MergeMapConfig(finalConfig, childConfig); 
                    }
                }
                
                ValidateSpatialDeclaration(finalConfig, mapId);
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

            if (!string.IsNullOrWhiteSpace(source.RootBoard))
            {
                target.RootBoard = source.RootBoard;
            }

            if (source.World is { } srcWorld && (srcWorld.WidthCm > 0 || srcWorld.HeightCm > 0))
            {
                target.World = srcWorld.Clone();
            }

            if (source.Tuning is { } srcTuning && srcTuning.IsAuthored)
            {
                target.Tuning = srcTuning.Clone();
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
                    if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Map {target.Id} fragment {varSourceLabel} variable name {name} must be trimmed.");
                    }

                    if (sourceVariable.Delete == true)
                    {
                        target.PendingVariableTombstones ??= new List<(string, string)>();
                        target.PendingVariableTombstones.Add((name, varSourceLabel));
                        continue;
                    }

                    // 同名重新声明撤销先前变量墓碑（复活）；墓碑标记过的 stale 条目允许改型替换
                    // （delete-then-redeclare 的 redeclare 半边），未墓碑的活条目改型仍 fail-fast。
                    bool wasTombstoned = (target.PendingVariableTombstones?.RemoveAll(
                        t => string.Equals(t.Name, name, StringComparison.Ordinal)) ?? 0) > 0;
                    int existing = target.Variables.FindIndex(v =>
                        string.Equals(v.Name ?? string.Empty, name, StringComparison.Ordinal));
                    if (existing >= 0)
                    {
                        if (target.Variables[existing].Type != sourceVariable.Type && !wasTombstoned)
                        {
                            throw new InvalidOperationException(
                                $"Map {target.Id} fragment {varSourceLabel} redeclares variable {name} with type {sourceVariable.Type} (was {target.Variables[existing].Type}); live variables cannot change type, __delete first then redeclare.");
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
        private void ResolvePendingTombstones(string requestedMapId, MapConfig config)
        {
            if (config.PendingEntityTombstones != null && config.PendingEntityTombstones.Count > 0)
            {
                foreach (var (instanceId, sourceLabel) in config.PendingEntityTombstones)
                {
                    if (TryRemoveEntityById(config, instanceId))
                    {
                        LastMergeReport.RecordDeletion(requestedMapId, instanceId, sourceLabel);
                    }
                    else
                    {
                        LastMergeReport.RecordDeletionNotFound(requestedMapId, instanceId, sourceLabel);
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
                        LastMergeReport.RecordVariableDeletion(requestedMapId, name, sourceLabel);
                    }
                    else
                    {
                        LastMergeReport.RecordVariableDeletionNotFound(requestedMapId, name, sourceLabel);
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

        public static void ValidateSpatialDeclaration(MapConfig config, MapId mapId)
        {
            ValidateTuningValues(config.Tuning, mapId);

            if (config.Boards is not { Count: > 0 })
            {
                // Boardless maps are first-class; they may declare the host world directly
                // (nothing else anchors it) but are not required to (non-spatial maps).
                var boardless = config.World;
                if (boardless is { } bw && (bw.WidthCm > 0 || bw.HeightCm > 0 || bw.CellSizeCm != Ludots.Core.Spatial.SpatialScaleDefaults.CellCm))
                {
                    if (bw.WidthCm <= 0 || bw.HeightCm <= 0 || bw.CellSizeCm <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId}' declares a partial World; WidthCm/HeightCm/CellSizeCm must all be positive or all omitted (#1567).");
                    }
                }

                return;
            }

            if (config.World is { } declared && (declared.WidthCm > 0 || declared.HeightCm > 0))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' has boards and a World declaration; board-bearing maps root the host world on RootBoard, World is boardless-only (#1567).");
            }

            foreach (var board in config.Boards)
            {
                string spatialType = (board.SpatialType ?? "Grid").Trim();
                if (!spatialType.Equals("Grid", StringComparison.OrdinalIgnoreCase) &&
                    !spatialType.Equals("HexGrid", StringComparison.OrdinalIgnoreCase) &&
                    !spatialType.Equals("Hex", StringComparison.OrdinalIgnoreCase) &&
                    !spatialType.Equals("Hybrid", StringComparison.OrdinalIgnoreCase) &&
                    !spatialType.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' board '{board.Name}' has unknown SpatialType '{spatialType}'; use Grid/HexGrid/NodeGraph.");
                }

                if (board.TransportNetwork is { } transportDeclaration)
                {
                    if (!spatialType.Equals("NodeGraph", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId}' board '{board.Name}' declares TransportNetwork but SpatialType is '{spatialType}'; transport networks require a NodeGraph board.");
                    }

                    transportDeclaration.Validate(mapId.Value, board.Name);
                }
            }

            BoardConfig root = ResolveRootBoard(config, mapId);

            foreach (var board in config.Boards)
            {
                ValidateBoardPlacement(board, root, mapId);
                ValidateBoardAgainstWorldTuning(board, config.Tuning, mapId);
            }
        }

        public static BoardConfig ResolveRootBoardFor(MapConfig config, string mapId)
        {
            return ResolveRootBoard(config, new MapId(mapId));
        }

        internal static BoardConfig ResolveRootBoard(MapConfig config, MapId mapId)
        {
            string rootDesignation = config.RootBoard?.Trim();
            if (!string.IsNullOrWhiteSpace(rootDesignation))
            {
                foreach (var board in config.Boards)
                {
                    if (string.Equals(board.Name, rootDesignation, StringComparison.OrdinalIgnoreCase))
                    {
                        return board;
                    }
                }

                throw new InvalidOperationException(
                    $"Map '{mapId}' RootBoard '{rootDesignation}' matches no board; fix the designation or omit it to root the first board (#1567).");
            }

            return config.Boards[0];
        }

        private static void ValidateTuningValues(Ludots.Core.Config.WorldTuningConfig tuning, MapId mapId)
        {
            if (tuning is null || !tuning.IsAuthored)
            {
                return;
            }

            if (tuning.PartitionChunkCells is int partition &&
                (partition <= 0 || (partition & (partition - 1)) != 0))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' Tuning.PartitionChunkCells must be positive and a power of two; got {partition}.");
            }

            if (tuning.LoadedChunkCapacity is int capacity && capacity <= 0)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' Tuning.LoadedChunkCapacity must be positive; got {capacity}.");
            }
        }

        public static void ApplyWorldTuningToBoards(MapConfig config)
        {
            if (config?.Boards is not { Count: > 0 })
            {
                return;
            }

            var tuning = config.Tuning;
            int partition = tuning?.PartitionChunkCells
                ?? Ludots.Core.Spatial.SpatialScaleDefaults.PartitionChunkCells;
            int capacity = tuning?.LoadedChunkCapacity
                ?? Ludots.Core.Spatial.SpatialScaleDefaults.DefaultLoadedChunkCapacity;

            foreach (var board in config.Boards)
            {
                board.ChunkSizeCells = partition;
                board.LoadedChunkCapacity = capacity;
            }
        }

        private static void ValidateBoardAgainstWorldTuning(BoardConfig board, Ludots.Core.Config.WorldTuningConfig tuning, MapId mapId)
        {
            if (tuning is null || !tuning.IsAuthored)
            {
                return;
            }

            if (tuning.PartitionChunkCells is int partitionValue &&
                board.ChunkSizeCells != Ludots.Core.Spatial.SpatialScaleDefaults.PartitionChunkCells &&
                board.ChunkSizeCells != partitionValue)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' declares ChunkSizeCells={board.ChunkSizeCells}, conflicting with Tuning.PartitionChunkCells={partitionValue}; remove the board-level field or align it (single world budget, #1567).");
            }

            if (tuning.LoadedChunkCapacity is int capacityValue &&
                board.LoadedChunkCapacity > 0 &&
                board.LoadedChunkCapacity != capacityValue)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' declares LoadedChunkCapacity={board.LoadedChunkCapacity}, conflicting with Tuning.LoadedChunkCapacity={capacityValue}; remove the board-level field or align it (single world budget, #1567).");
            }
        }

        private static string spatialTypeOf(BoardConfig board) =>
            (board.SpatialType ?? "Grid").Trim();

        private static void ValidateBoardPlacement(BoardConfig board, BoardConfig root, MapId mapId)
        {
            if (board.Anchor == null)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' requires Anchor (LocalXCm/LocalYCm/WorldXCm/WorldYCm).");
            }

            if (board.Grid == null || board.Grid.CellSizeCm <= 0)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' requires Grid.CellSizeCm > 0.");
            }

            if (board.WidthCm <= 0 || board.HeightCm <= 0 || board.WidthCells <= 0 || board.HeightCells <= 0)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' requires a positive WidthCm/HeightCm that covers at least one cell of Grid.CellSizeCm.");
            }

            bool isHex = spatialTypeOf(board) is "HexGrid" or "Hex";
            if (isHex)
            {
                if (board.Hex == null || board.Hex.EdgeLengthCm <= 0)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' board '{board.Name}' is {board.SpatialType} and requires Hex.EdgeLengthCm > 0.");
                }

                var metrics = new Ludots.Core.Map.Hex.HexMetrics(board.Hex.EdgeLengthCm);
                if (!metrics.TryCountFittingHexes(board.WidthCm, board.HeightCm, out _, out _))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' board '{board.Name}' rectangle {board.WidthCm}x{board.HeightCm}cm fits no whole hex of edge {board.Hex.EdgeLengthCm}cm.");
                }
            }
            else if (board.Hex != null)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' declares Hex but SpatialType is '{board.SpatialType}'; hex metrics are HexGrid-only.");
            }

            if (ReferenceEquals(board, root))
            {
                if (board.Anchor.WorldXCm != 0 || board.Anchor.WorldYCm != 0)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' root board '{board.Name}' Anchor.World must be (0, 0); that point is the Ludots origin.");
                }

                return;
            }

            BoardExtentSpec boardExtent = board.ResolveExtent();
            BoardExtentSpec rootExtent = root.ResolveExtent();
            long boardWidthCm = boardExtent.WidthCm;
            long boardHeightCm = boardExtent.HeightCm;
            long rootWidthCm = rootExtent.WidthCm;
            long rootHeightCm = rootExtent.HeightCm;
            if (boardWidthCm > rootWidthCm || boardHeightCm > rootHeightCm)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' extent {boardWidthCm}x{boardHeightCm}cm exceeds root board '{root.Name}' extent {rootWidthCm}x{rootHeightCm}cm; enlarge the root board or shrink the satellite.");
            }

            long minX = boardExtent.TopologyOriginXCm;
            long minY = boardExtent.TopologyOriginYCm;
            long rootMinX = rootExtent.TopologyOriginXCm;
            long rootMinY = rootExtent.TopologyOriginYCm;
            if (minX < rootMinX || minY < rootMinY ||
                minX + boardWidthCm > rootMinX + rootWidthCm ||
                minY + boardHeightCm > rootMinY + rootHeightCm)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{board.Name}' rectangle ({minX},{minY})+{boardWidthCm}x{boardHeightCm}cm exits the root board frame ({rootMinX},{rootMinY})+{rootWidthCm}x{rootHeightCm}cm; place the satellite fully inside the world.");
            }
        }

        private static void RejectLegacyWorldExtentKeys(JsonNode fragment, string jsonPath)
        {
            if (fragment is not JsonObject root)
            {
                return;
            }

            RejectLegacyKey(root, "WidthInTiles", "Boards[].WidthCm", jsonPath);
            RejectLegacyKey(root, "HeightInTiles", "Boards[].HeightCm", jsonPath);
            RejectLegacyKey(root, "WidthInMacroTiles", "Boards[].WidthCm", jsonPath);
            RejectLegacyKey(root, "HeightInMacroTiles", "Boards[].HeightCm", jsonPath);

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

                RejectLegacyKey(board, "WidthInTiles", "Boards[].WidthCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "HeightInTiles", "Boards[].HeightCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "WidthInMacroTiles", "Boards[].WidthCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "HeightInMacroTiles", "Boards[].HeightCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "WidthCells", "Boards[].WidthCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "HeightCells", "Boards[].HeightCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "GridCellSizeCm", "Boards[].Grid.CellSizeCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "OriginXCm", "Boards[].Anchor", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "OriginYcm", "Boards[].Anchor", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "OriginYCm", "Boards[].Anchor", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "WidthHexes", "Boards[].WidthCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "HeightHexes", "Boards[].HeightCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "HexEdgeLengthCm", "Boards[].Hex.EdgeLengthCm", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "ChunkSizeCells", "Tuning.PartitionChunkCells", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "LoadedChunkCapacity", "Tuning.LoadedChunkCapacity", $"{jsonPath}.boards[{i}]");
                RejectLegacyKey(board, "NavTileGrid", "Navigation/navmesh.json maps.<mapId>.boards.<name>", $"{jsonPath}.boards[{i}]");
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
