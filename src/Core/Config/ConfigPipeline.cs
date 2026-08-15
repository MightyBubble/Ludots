using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Diagnostics;
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
            
            // Deserialize merged JSON to GameConfig.
            var options = StrictJsonOptions.CreateCamelCase();
            options.Converters.Add(new JsonStringEnumConverter());
            
            var jsonString = merged.ToJsonString();
            var config = JsonSerializer.Deserialize<GameConfig>(jsonString, options)
                ?? throw new InvalidOperationException("Merged game.json deserialized to null GameConfig.");
            
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

        public List<JsonNode> CollectFragments(in ConfigCatalogEntry entry)
        {
            var fragments = CollectFragmentsWithSources(in entry);
            var nodes = new List<JsonNode>(fragments.Count);
            for (int i = 0; i < fragments.Count; i++) nodes.Add(fragments[i].Node);
            return nodes;
        }

        public List<ConfigFragment> CollectFragmentsWithSources(string relativePath)
        {
            var fragments = new List<ConfigFragment>();
            LoadFromAllSources(relativePath, (stream, sourceUri) =>
            {
                var node = JsonNode.Parse(stream)
                    ?? throw new JsonException($"JSON root in {sourceUri} is null.");
                fragments.Add(new ConfigFragment(node, sourceUri));
                Log.Info(in LogChannels.Config, $"Merged fragment from: {sourceUri}");
            });
            return fragments;
        }

        public List<ConfigFragment> CollectFragmentsWithSources(in ConfigCatalogEntry entry)
        {
            var fragments = new List<ConfigFragment>();
            LoadFromAllSources(in entry, (stream, sourceUri) =>
            {
                var node = JsonNode.Parse(stream)
                    ?? throw new JsonException($"JSON root in {sourceUri} is null.");
                fragments.Add(new ConfigFragment(node, sourceUri));
                Log.Info(in LogChannels.Config, $"Merged fragment from: {sourceUri}");
            });
            return fragments;
        }

        public JsonNode? MergeFromCatalog(in ConfigCatalogEntry entry)
        {
            var fragments = CollectFragments(in entry);
            return ConfigMerger.MergeMany(fragments, in entry);
        }

        public JsonNode? MergeFromCatalog(in ConfigCatalogEntry entry, ConfigConflictReport report)
        {
            var fragments = CollectFragmentsWithSources(in entry);
            return ConfigMerger.MergeManyWithReport(fragments, in entry, report);
        }

        /// <summary>
        /// ArrayById convenience: returns ordered MergedConfigEntry list for compile-phase consumption.
        /// </summary>
        public IReadOnlyList<MergedConfigEntry> MergeArrayByIdFromCatalog(
            in ConfigCatalogEntry entry, ConfigConflictReport report = null)
        {
            RequirePolicy(in entry, ConfigMergePolicy.ArrayById);
            var fragments = CollectFragmentsWithSources(in entry);
            return ConfigMerger.MergeArrayByIdToEntries(fragments, in entry, report);
        }

        /// <summary>
        /// DeepObject convenience: returns a single merged JsonObject.
        /// </summary>
        public JsonObject MergeDeepObjectFromCatalog(
            in ConfigCatalogEntry entry, ConfigConflictReport report = null)
        {
            RequirePolicy(in entry, ConfigMergePolicy.DeepObject);
            var result = report != null
                ? MergeFromCatalog(in entry, report)
                : MergeFromCatalog(in entry);
            if (result == null)
            {
                return null;
            }

            return result as JsonObject
                ?? throw new InvalidOperationException(
                    $"Config '{entry.RelativePath}' must merge to a JSON object for DeepObject policy.");
        }

        public static ConfigCatalogEntry RequireEntry(
            ConfigCatalog catalog, string path,
            ConfigMergePolicy defaultPolicy, string defaultIdField = "id")
        {
            if (catalog != null && catalog.TryGet(path, out var found))
            {
                RequirePolicy(in found, defaultPolicy);
                if (defaultPolicy == ConfigMergePolicy.ArrayById &&
                    !string.Equals(found.IdField, defaultIdField, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Config catalog entry '{path}' must declare IdField '{defaultIdField}', but declares '{found.IdField}'.");
                }

                return found;
            }

            throw new InvalidOperationException(
                $"Config catalog must explicitly declare '{path}' with policy '{defaultPolicy}'.");
        }

        private static void RequirePolicy(in ConfigCatalogEntry entry, ConfigMergePolicy expected)
        {
            if (entry.MergePolicy != expected)
            {
                throw new InvalidOperationException(
                    $"Config catalog entry '{entry.RelativePath}' declares policy '{entry.MergePolicy}', but loader requires '{expected}'.");
            }
        }

        private void LoadFromAllSources(string relativePath, Action<Stream, string> onStreamOpened)
        {
            // Normalize path
            if (relativePath.StartsWith("/") || relativePath.StartsWith("\\"))
                relativePath = relativePath.Substring(1);

            // 1. Core Configs (highest priority - engine defaults)
            TryLoad(ConfigSourcePaths.CoreConfig(relativePath), onStreamOpened);
            // Also try Core:{path} directly (for Maps/ and other non-Configs paths)
            TryLoad($"Core:{relativePath}", onStreamOpened);

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

        private void LoadFromAllSources(in ConfigCatalogEntry entry, Action<Stream, string> onStreamOpened)
        {
            string relativePath = NormalizeRelativePath(entry.RelativePath);
            string[] shardDirectories = entry.ShardDirectories ?? Array.Empty<string>();

            LoadFromCoreSource(relativePath, shardDirectories, configsRoot: true, onStreamOpened);
            LoadFromCoreSource(relativePath, shardDirectories, configsRoot: false, onStreamOpened);

            if (_modLoader != null && _modLoader.LoadedModIds != null)
            {
                foreach (var modId in _modLoader.LoadedModIds)
                {
                    LoadFromModSource(modId, relativePath, shardDirectories, configsRoot: false, onStreamOpened);
                    LoadFromModSource(modId, relativePath, shardDirectories, configsRoot: true, onStreamOpened);
                }
            }
        }

        private void LoadFromCoreSource(
            string relativePath,
            string[] shardDirectories,
            bool configsRoot,
            Action<Stream, string> onStreamOpened)
        {
            string mainUri = configsRoot
                ? ConfigSourcePaths.CoreConfig(relativePath)
                : $"Core:{relativePath}";
            TryLoad(mainUri, onStreamOpened);

            if (!configsRoot)
            {
                return;
            }

            for (int i = 0; i < shardDirectories.Length; i++)
            {
                string dir = NormalizeRelativePath(shardDirectories[i]);
                string dirUri = ConfigSourcePaths.CoreConfig(dir);
                LoadShardDirectory(dirUri, onStreamOpened);
            }
        }

        private void LoadFromModSource(
            string modId,
            string relativePath,
            string[] shardDirectories,
            bool configsRoot,
            Action<Stream, string> onStreamOpened)
        {
            string mainUri = configsRoot
                ? ConfigSourcePaths.ModConfigs(modId, relativePath)
                : ConfigSourcePaths.ModAssets(modId, relativePath);
            TryLoad(mainUri, onStreamOpened);

            if (!configsRoot)
            {
                return;
            }

            for (int i = 0; i < shardDirectories.Length; i++)
            {
                string dir = NormalizeRelativePath(shardDirectories[i]);
                string dirUri = ConfigSourcePaths.ModConfigs(modId, dir);
                LoadShardDirectory(dirUri, onStreamOpened);
            }
        }

        private void LoadShardDirectory(string directoryUri, Action<Stream, string> onStreamOpened)
        {
            IReadOnlyList<string> files;
            try
            {
                files = _vfs.EnumerateFiles(directoryUri, "*.json");
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException($"Error enumerating {directoryUri}: {ex.Message}", ex);
            }

            for (int i = 0; i < files.Count; i++)
            {
                TryLoad(files[i], onStreamOpened);
            }
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            relativePath = relativePath.Replace('\\', '/');
            while (relativePath.StartsWith("/", StringComparison.Ordinal))
            {
                relativePath = relativePath.Substring(1);
            }

            return relativePath;
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
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException($"Error loading {uri}: {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                throw new JsonException($"Error parsing JSON from {uri}: {ex.Message}", ex);
            }
        }
    }
}
