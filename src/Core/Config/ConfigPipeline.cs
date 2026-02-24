using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Modding;

namespace Ludots.Core.Config
{
    public class ConfigPipeline
    {
        private readonly VirtualFileSystem _vfs;
        private readonly ModLoader _modLoader;

        public ConfigPipeline(VirtualFileSystem vfs, ModLoader modLoader)
        {
            _vfs = vfs;
            _modLoader = modLoader;
        }

        /// <summary>
        /// Collects and deep-merges all game.json fragments into a single GameConfig.
        /// Merge order: Core:Configs/game.json -> Mods (by priority) -> Final
        /// </summary>
        public GameConfig MergeGameConfig()
        {
            var fragments = CollectFragments("game.json");
            
            // Start with empty JsonObject
            var merged = new JsonObject();
            
            foreach (var fragment in fragments)
            {
                if (fragment is JsonObject obj)
                {
                    DeepMerge(merged, obj);
                }
            }
            
            // Deserialize merged JSON to GameConfig
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var jsonString = merged.ToJsonString();
            var config = JsonSerializer.Deserialize<GameConfig>(jsonString, options) ?? new GameConfig();
            
            return config;
        }

        /// <summary>
        /// Deep merges source JsonObject into target JsonObject.
        /// - Scalars: source overwrites target
        /// - Arrays: source overwrites target (not appended)
        /// - Objects: recursively merged
        /// </summary>
        public static void DeepMerge(JsonObject target, JsonObject source)
        {
            foreach (var kvp in source)
            {
                var key = kvp.Key;
                var sourceValue = kvp.Value;
                
                if (sourceValue == null)
                {
                    target[key] = null;
                    continue;
                }
                
                if (!target.ContainsKey(key))
                {
                    // Key doesn't exist in target, just clone and add
                    target[key] = sourceValue.DeepClone();
                    continue;
                }
                
                var targetValue = target[key];
                
                if (sourceValue is JsonObject sourceObj && targetValue is JsonObject targetObj)
                {
                    // Both are objects - recursively merge
                    DeepMerge(targetObj, sourceObj);
                }
                else
                {
                    // Scalars or arrays - source overwrites target
                    target[key] = sourceValue.DeepClone();
                }
            }
        }

        /// <summary>
        /// Collects JsonNodes from all matching files in Core and Mods.
        /// Use this when each file represents a single configuration object (e.g., MapConfig).
        /// </summary>
        public List<JsonNode> CollectFragments(string relativePath)
        {
            var fragments = CollectFragmentsWithSources(relativePath);
            var nodes = new List<JsonNode>(fragments.Count);
            for (int i = 0; i < fragments.Count; i++) nodes.Add(fragments[i].Node);
            return nodes;
        }

        public List<ConfigFragment> CollectFragmentsWithSources(string relativePath)
        {
            var fragments = new List<ConfigFragment>();
            LoadFromAllSources(relativePath, (stream, sourceUri) =>
            {
                try
                {
                    var node = JsonNode.Parse(stream);
                    if (node != null)
                    {
                        fragments.Add(new ConfigFragment(node, sourceUri));
                        Console.WriteLine($"[ConfigPipeline] Merged fragment from: {sourceUri}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigPipeline] Error parsing JSON from {sourceUri}: {ex.Message}");
                }
            });
            return fragments;
        }

        public JsonNode? MergeFromCatalog(in ConfigCatalogEntry entry)
        {
            var fragments = CollectFragments(entry.RelativePath);
            return ConfigMerger.MergeMany(fragments, in entry);
        }

        public JsonNode? MergeFromCatalog(in ConfigCatalogEntry entry, ConfigConflictReport report)
        {
            var fragments = CollectFragmentsWithSources(entry.RelativePath);
            return ConfigMerger.MergeManyWithReport(fragments, in entry, report);
        }

        /// <summary>
        /// Collects JsonArrays from all matching files in Core and Mods.
        /// Use this when each file contains a list of objects (e.g., templates.json).
        /// </summary>
        public List<JsonArray> CollectJsonArrays(string relativePath)
        {
            var arrays = new List<JsonArray>();
            LoadFromAllSources(relativePath, (stream, sourceUri) =>
            {
                try
                {
                    var node = JsonNode.Parse(stream);
                    if (node is JsonArray arr)
                    {
                        arrays.Add(arr);
                    }
                    else
                    {
                        Console.WriteLine($"[ConfigPipeline] Warning: Expected JsonArray but got {node?.GetType().Name} in {relativePath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigPipeline] Error parsing JSON Array from {sourceUri}: {ex.Message}");
                }
            });
            return arrays;
        }

        private void LoadFromAllSources(string relativePath, Action<Stream, string> onStreamOpened)
        {
            // Normalize path
            if (relativePath.StartsWith("/") || relativePath.StartsWith("\\")) 
                relativePath = relativePath.Substring(1);

            // 1. Core Configs (highest priority - engine defaults)
            TryLoad(ConfigSourcePaths.CoreConfig(relativePath), onStreamOpened);

            // 2. Mods (in dependency/priority order)
            if (_modLoader != null && _modLoader.LoadedModIds != null)
            {
                foreach (var modId in _modLoader.LoadedModIds)
                {
                    TryLoad(ConfigSourcePaths.ModAssets(modId, relativePath), onStreamOpened);
                    TryLoad(ConfigSourcePaths.ModConfigs(modId, relativePath), onStreamOpened);
                }
            }
        }

        private void TryLoad(string uri, Action<Stream, string> onStreamOpened)
        {
            try
            {
                using (var stream = _vfs.GetStream(uri))
                {
                    onStreamOpened(stream, uri);
                }
            }
            catch (FileNotFoundException)
            {
                // Ignore missing files
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigPipeline] Error loading {uri}: {ex.Message}");
            }
        }
    }
}
