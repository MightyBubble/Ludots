using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Ludots.Core.Map
{
    public class MapManager : IMapManager
    {
        private readonly IVirtualFileSystem _vfs;
        private readonly TriggerManager _triggerManager;
        private readonly ModLoader _modLoader;
        
        // Registry for Map Definitions (Code-First)
        private readonly Dictionary<MapId, MapDefinition> _definitions = new Dictionary<MapId, MapDefinition>();
        private readonly Dictionary<Type, MapDefinition> _typeToDefinition = new Dictionary<Type, MapDefinition>();

        public MapManager(IVirtualFileSystem vfs, TriggerManager triggerManager, ModLoader modLoader)
        {
            _vfs = vfs;
            _triggerManager = triggerManager;
            _modLoader = modLoader;
        }

        public void RegisterMap(MapDefinition definition)
        {
            if (definition == null) return;
            if (_definitions.ContainsKey(definition.Id))
            {
                Console.WriteLine($"[MapManager] Warning: Overwriting map definition for {definition.Id}");
            }
            _definitions[definition.Id] = definition;
            _typeToDefinition[definition.GetType()] = definition;
            Console.WriteLine($"[MapManager] Registered Map Definition: {definition.Id} ({definition.GetType().Name})");
        }
        
        public MapDefinition GetDefinition<T>() where T : MapDefinition
        {
            return _typeToDefinition.TryGetValue(typeof(T), out var def) ? def : null;
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
            Console.WriteLine($"[MapManager] Loading Map: {mapId}");

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
                    Console.WriteLine($"[MapManager] Found Code Definition: {def.GetType().Name}");
                }

                // 1. Find all config fragments
                var configs = new List<MapConfig>();
            
                // If definition exists, use its DataFilePath. Otherwise use default convention.
                string jsonPath = definition != null ? definition.DataFilePath : $"Maps/{mapId}.json";
            
                // Normalize path to remove leading slash if any
                if (jsonPath.StartsWith("/") || jsonPath.StartsWith("\\")) jsonPath = jsonPath.Substring(1);

                // 1a. Core Assets (Try Configs/ first, then assets/)
                TryLoadConfigFromUri($"Core:Configs/{jsonPath}", configs);
                TryLoadConfigFromUri($"Core:assets/{jsonPath}", configs);
                // Also try root of assets if not found (e.g. Core:Maps/...)
                TryLoadConfigFromUri($"Core:{jsonPath}", configs);

                // 1b. Mods
                foreach (var modId in _modLoader.LoadedModIds)
                {
                    TryLoadConfigFromUri($"{modId}:assets/{jsonPath}", configs);
                    TryLoadConfigFromUri($"{modId}:{jsonPath}", configs);
                }

                if (configs.Count == 0 && definition == null)
                {
                    Console.WriteLine($"[MapManager] Error: Map '{mapId}' not found (No Definition, No Data).");
                    return null;
                }

                // 2. Merge configs
                var finalConfig = new MapConfig { Id = mapId.ToString() };
                foreach (var cfg in configs)
                {
                    MergeMapConfig(finalConfig, cfg);
                }
            
                // 3. Apply Definition Metadata (Tags)
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
                }

                // 4. Handle Inheritance (ParentId)
                if (!string.IsNullOrEmpty(finalConfig.ParentId))
                {
                    Console.WriteLine($"[MapManager] Loading Parent Map: {finalConfig.ParentId}");
                    var parentConfig = LoadMapInternal(new MapId(finalConfig.ParentId), visiting, chain);
                    if (parentConfig != null)
                    {
                        var childConfig = finalConfig;
                        finalConfig = parentConfig; 
                        MergeMapConfig(finalConfig, childConfig); 
                    }
                }
                
                Console.WriteLine($"[MapManager] Map '{mapId}' loaded.");
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

        private void TryLoadConfigFromUri(string uri, List<MapConfig> configs)
        {
            try 
            {
                using (var stream = _vfs.GetStream(uri))
                using (var reader = new StreamReader(stream))
                {
                    string json = reader.ReadToEnd();
                    var config = JsonSerializer.Deserialize<MapConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (config != null)
                    {
                        configs.Add(config);
                    }
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[MapManager] Invalid JSON at '{uri}': {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapManager] Failed to load '{uri}': {ex.Message}");
            }
        }

        private void MergeMapConfig(MapConfig target, MapConfig source)
        {
            if (!string.IsNullOrEmpty(source.DataFile)) target.DataFile = source.DataFile;
            if (!string.IsNullOrEmpty(source.ParentId)) target.ParentId = source.ParentId;
            if (source.Spatial != null) target.Spatial = CloneSpatial(source.Spatial);
            
            if (source.Dependencies != null)
            {
                foreach (var kvp in source.Dependencies)
                {
                    target.Dependencies[kvp.Key] = kvp.Value;
                }
            }
            if (source.Entities != null) target.Entities.AddRange(source.Entities);
            
            // Merge Tags
            if (source.Tags != null)
            {
                if (target.Tags == null) target.Tags = new List<string>();
                foreach (var t in source.Tags)
                {
                    if (!target.Tags.Contains(t)) target.Tags.Add(t);
                }
            }
        }

        private static MapSpatialConfig CloneSpatial(MapSpatialConfig source)
        {
            return new MapSpatialConfig
            {
                SpatialType = source.SpatialType,
                WidthInTiles = source.WidthInTiles,
                HeightInTiles = source.HeightInTiles,
                GridCellSizeCm = source.GridCellSizeCm,
                HexEdgeLengthCm = source.HexEdgeLengthCm,
                ChunkSizeCells = source.ChunkSizeCells
            };
        }
    }
}
