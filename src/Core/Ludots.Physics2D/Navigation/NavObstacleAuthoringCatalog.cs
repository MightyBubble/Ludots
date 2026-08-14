using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Physics2D.Navigation;

public static class NavObstacleAuthoringCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static NavObstacleSet BuildForMap(
        string repoRoot,
        string mapId,
        string? targetModId = null,
        string layerId = "Ground")
    {
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            throw new InvalidOperationException("Nav obstacle authoring requires a repo root.");
        }

        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new InvalidOperationException("Nav obstacle authoring requires a mapId.");
        }

        string root = Path.GetFullPath(repoRoot);
        var mods = DiscoverMods(root);
        var loadOrder = string.IsNullOrWhiteSpace(targetModId)
            ? ResolveUniqueMapLoadOrder(root, mods, mapId)
            : ResolveLoadOrder(mods, targetModId!);

        MapConfig map = LoadMergedMap(root, mods, loadOrder, mapId);
        Dictionary<string, EntityTemplate> templates = LoadMergedTemplates(root, mods, loadOrder);
        return NavObstacleAuthoringAdapter.BuildFromMapAuthoring(map, templates, layerId);
    }

    private static List<ModInfo> DiscoverMods(string repoRoot)
    {
        string modsRoot = Path.Combine(repoRoot, "mods");
        if (!Directory.Exists(modsRoot))
        {
            return new List<ModInfo>();
        }

        return Directory.GetFiles(modsRoot, "mod.json", SearchOption.AllDirectories)
            .Select(path =>
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;
                string id = ReadRequiredString(root, "name", path);
                int priority = root.TryGetProperty("priority", out JsonElement priorityElement) &&
                    priorityElement.ValueKind == JsonValueKind.Number &&
                    priorityElement.TryGetInt32(out int parsedPriority)
                        ? parsedPriority
                        : 0;

                var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("dependencies", out JsonElement deps) && deps.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty dep in deps.EnumerateObject())
                    {
                        dependencies[dep.Name] = dep.Value.ValueKind == JsonValueKind.String
                            ? dep.Value.GetString() ?? string.Empty
                            : string.Empty;
                    }
                }

                return new ModInfo(id, priority, dependencies, Path.GetDirectoryName(path)!);
            })
            .OrderBy(mod => mod.Priority)
            .ThenBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ResolveUniqueMapLoadOrder(string repoRoot, IReadOnlyList<ModInfo> mods, string mapId)
    {
        var owners = new List<string>();
        for (int i = 0; i < mods.Count; i++)
        {
            ModInfo mod = mods[i];
            if (MapFileExists(mod.RootPath, mapId))
            {
                owners.Add(mod.Id);
            }
        }

        if (owners.Count == 0)
        {
            if (MapFileExists(repoRoot, mapId))
            {
                return new List<string>();
            }

            throw new InvalidOperationException($"Map '{mapId}' was not found in core assets or any mod.");
        }

        if (owners.Count > 1)
        {
            throw new InvalidOperationException(
                $"Map '{mapId}' is authored by multiple mods ({string.Join(", ", owners)}); pass an explicit modId.");
        }

        return ResolveLoadOrder(mods, owners[0]);
    }

    private static List<string> ResolveLoadOrder(IReadOnlyList<ModInfo> mods, string targetModId)
    {
        var byId = mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        if (!byId.ContainsKey(targetModId))
        {
            throw new InvalidOperationException($"Unknown mod '{targetModId}'.");
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string modId)
        {
            if (!required.Add(modId))
            {
                return;
            }

            if (!byId.TryGetValue(modId, out ModInfo? mod))
            {
                throw new InvalidOperationException($"Missing mod dependency '{modId}'.");
            }

            foreach (string dep in mod.Dependencies.Keys)
            {
                Add(dep);
            }
        }

        Add(targetModId);

        var result = new List<string>(required.Count);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string modId)
        {
            if (visited.Contains(modId))
            {
                return;
            }

            if (!visiting.Add(modId))
            {
                throw new InvalidOperationException($"Dependency cycle detected at '{modId}'.");
            }

            ModInfo mod = byId[modId];
            foreach (string dep in mod.Dependencies.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (required.Contains(dep))
                {
                    Visit(dep);
                }
            }

            visiting.Remove(modId);
            visited.Add(modId);
            result.Add(modId);
        }

        Visit(targetModId);
        return result;
    }

    private static MapConfig LoadMergedMap(
        string repoRoot,
        IReadOnlyList<ModInfo> mods,
        IReadOnlyList<string> loadOrder,
        string mapId)
    {
        var byId = mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var merged = new MapConfig { Id = mapId };
        bool foundAny = false;

        void TryLoad(string rootPath)
        {
            foreach (string path in MapCandidates(rootPath, mapId))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                MapConfig? source = JsonSerializer.Deserialize<MapConfig>(File.ReadAllText(path), JsonOptions);
                if (source == null)
                {
                    throw new InvalidOperationException($"Map config '{path}' could not be deserialized.");
                }

                MergeMap(merged, source);
                foundAny = true;
            }
        }

        TryLoad(repoRoot);
        for (int i = 0; i < loadOrder.Count; i++)
        {
            TryLoad(byId[loadOrder[i]].RootPath);
        }

        if (!foundAny)
        {
            throw new InvalidOperationException($"Map '{mapId}' was not found.");
        }

        if (!string.IsNullOrWhiteSpace(merged.ParentId))
        {
            MapConfig parent = LoadMergedMap(repoRoot, mods, loadOrder, merged.ParentId);
            MergeMap(parent, merged);
            return parent;
        }

        return merged;
    }

    private static Dictionary<string, EntityTemplate> LoadMergedTemplates(
        string repoRoot,
        IReadOnlyList<ModInfo> mods,
        IReadOnlyList<string> loadOrder)
    {
        var byId = mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var templates = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

        void Load(string rootPath)
        {
            foreach (string path in TemplateCandidates(rootPath))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
                if (node is not JsonArray arr)
                {
                    throw new InvalidOperationException($"Entity templates '{path}' must contain a JSON array.");
                }

                foreach (JsonNode? item in arr)
                {
                    if (item is not JsonObject obj)
                    {
                        continue;
                    }

                    if (!obj.TryGetPropertyValue("id", out JsonNode? idNode) ||
                        idNode?.GetValueKind() != JsonValueKind.String)
                    {
                        continue;
                    }

                    string id = idNode.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    if (templates.TryGetValue(id, out JsonNode? existing))
                    {
                        JsonMerger.Merge(existing, obj);
                    }
                    else
                    {
                        templates[id] = obj.DeepClone();
                    }
                }
            }
        }

        Load(repoRoot);
        for (int i = 0; i < loadOrder.Count; i++)
        {
            Load(byId[loadOrder[i]].RootPath);
        }

        return templates.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Deserialize<EntityTemplate>(JsonOptions)
                ?? throw new InvalidOperationException($"Entity template '{kvp.Key}' could not be deserialized."),
            StringComparer.Ordinal);
    }

    private static void MergeMap(MapConfig target, MapConfig source)
    {
        if (!string.IsNullOrWhiteSpace(source.Id))
        {
            target.Id = source.Id;
        }

        if (!string.IsNullOrWhiteSpace(source.ParentId))
        {
            target.ParentId = source.ParentId;
        }

        if (!string.IsNullOrWhiteSpace(source.VisualHeightmapAsset))
        {
            target.VisualHeightmapAsset = source.VisualHeightmapAsset;
        }

        if (source.TerrainPresentation != null)
        {
            target.TerrainPresentation = source.TerrainPresentation.Clone();
        }

        if (source.Dependencies != null)
        {
            foreach (KeyValuePair<string, string> kvp in source.Dependencies)
            {
                target.Dependencies[kvp.Key] = kvp.Value;
            }
        }

        if (source.Tags != null)
        {
            for (int i = 0; i < source.Tags.Count; i++)
            {
                string tag = source.Tags[i];
                if (!string.IsNullOrWhiteSpace(tag) &&
                    !target.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    target.Tags.Add(tag);
                }
            }
        }

        if (source.Metadata != null)
        {
            foreach (KeyValuePair<string, JsonNode> kvp in source.Metadata)
            {
                target.Metadata[kvp.Key] = kvp.Value?.DeepClone()!;
            }
        }

        if (source.Entities != null)
        {
            target.Entities.AddRange(source.Entities);
        }

        if (source.Boards != null)
        {
            foreach (var sourceBoard in source.Boards)
            {
                int existingIndex = target.Boards.FindIndex(board =>
                    string.Equals(board.Name, sourceBoard.Name, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    target.Boards[existingIndex] = sourceBoard.Clone();
                }
                else
                {
                    target.Boards.Add(sourceBoard.Clone());
                }
            }
        }

        if (source.Teams != null)
        {
            target.Teams.AddRange(source.Teams);
        }

        if (source.Players != null)
        {
            target.Players.AddRange(source.Players);
        }

        if (source.TriggerTypes != null)
        {
            foreach (string triggerType in source.TriggerTypes)
            {
                if (!target.TriggerTypes.Contains(triggerType, StringComparer.Ordinal))
                {
                    target.TriggerTypes.Add(triggerType);
                }
            }
        }

        if (source.DefaultCamera != null)
        {
            target.DefaultCamera = source.DefaultCamera;
        }

        if (source.VisualHeightmap != null)
        {
            target.VisualHeightmap = source.VisualHeightmap;
        }

        if (source.ParticipantRelationships != null)
        {
            target.ParticipantRelationships = source.ParticipantRelationships;
        }
    }

    private static bool MapFileExists(string rootPath, string mapId)
    {
        return MapCandidates(rootPath, mapId).Any(File.Exists);
    }

    private static IEnumerable<string> MapCandidates(string rootPath, string mapId)
    {
        yield return Path.Combine(rootPath, "assets", "Configs", "Maps", $"{mapId}.json");
        yield return Path.Combine(rootPath, "assets", "Maps", $"{mapId}.json");
    }

    private static IEnumerable<string> TemplateCandidates(string rootPath)
    {
        yield return Path.Combine(rootPath, "assets", "Configs", "Entities", "templates.json");
        yield return Path.Combine(rootPath, "assets", "Entities", "templates.json");
    }

    private static string ReadRequiredString(JsonElement root, string propertyName, string path)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidOperationException($"'{path}' requires a non-empty '{propertyName}' string.");
        }

        return element.GetString()!;
    }

    private sealed record ModInfo(
        string Id,
        int Priority,
        Dictionary<string, string> Dependencies,
        string RootPath);
}
