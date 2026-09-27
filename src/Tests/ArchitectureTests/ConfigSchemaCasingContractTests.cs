using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// Config schema casing SSOT contract (cfg-04):
    /// catalog-registered content tables author entry schema keys in camelCase,
    /// Maps asset files author top-level keys in PascalCase. Component payloads
    /// and Overrides interiors follow each component's own registration contract
    /// and are out of scope here.
    /// </summary>
    [TestFixture]
    public sealed class ConfigSchemaCasingContractTests
    {
        private static readonly Regex CamelCaseKey = new("^[a-z][A-Za-z0-9]*$", RegexOptions.Compiled);
        private static readonly Regex PascalCaseKey = new("^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

        /// <summary>
        /// Table families whose authoring contracts predate the casing SSOT and are
        /// documented as migration debt in cfg-04: the AI domain authoring tables
        /// (loader whitelists Pascal property names) and the Tasks/Activities
        /// tables (DTO contracts declare snake_case field names).
        /// </summary>
        private static readonly string[] LegacyTablePathPrefixes =
        {
            "AI/",
            "Tasks/tasks.json",
            "Activities/activities.json",
        };

        private string _repoRoot = string.Empty;
        private List<string> _modAssetRoots = null!;
        private List<(string Path, string[] ShardDirectories)> _catalogTables = null!;

        [SetUp]
        public void SetUp()
        {
            _repoRoot = FindRepoRoot();
            _modAssetRoots = CollectModAssetRoots(Path.Combine(_repoRoot, "mods"));
            _catalogTables = CollectCatalogTables();
        }

        [Test]
        public void CatalogTables_EntrySchemaKeys_OutsideLegacyFamilies_AreCamelCase()
        {
            var violations = new List<string>();

            foreach ((string tablePath, string[] shardDirectories) in _catalogTables)
            {
                if (IsLegacyTable(tablePath))
                {
                    continue;
                }

                foreach (string assetRoot in _modAssetRoots)
                {
                    CollectTableViolations(Path.Combine(assetRoot, tablePath), tablePath, violations);
                    foreach (string shardDirectory in shardDirectories)
                    {
                        string shardRoot = Path.Combine(assetRoot, shardDirectory);
                        if (!Directory.Exists(shardRoot))
                        {
                            continue;
                        }

                        foreach (string shardFile in Directory.EnumerateFiles(shardRoot, "*.json"))
                        {
                            CollectTableViolations(shardFile, tablePath, violations);
                        }
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Catalog content-table entries must author schema keys in camelCase (cfg-04); offending file + key:\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void MapsAssetFiles_TopLevelKeys_ArePascalCase()
        {
            var violations = new List<string>();

            foreach (string assetRoot in _modAssetRoots)
            {
                string mapsRoot = Path.Combine(assetRoot, "Maps");
                if (!Directory.Exists(mapsRoot))
                {
                    continue;
                }

                foreach (string mapFile in Directory.EnumerateFiles(mapsRoot, "*.json"))
                {
                    JsonNode? node = ParseJsonFile(mapFile);
                    if (node is not JsonObject map)
                    {
                        continue;
                    }

                    foreach (var field in map)
                    {
                        string key = field.Key;
                        if (IsMarkerKey(key) || PascalCaseKey.IsMatch(key))
                        {
                            continue;
                        }

                        violations.Add($"{mapFile}: '{key}'");
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Maps asset files must author top-level keys in PascalCase (cfg-04); offending file + key:\n" +
                string.Join("\n", violations));
        }

        private static void CollectTableViolations(string file, string tablePath, List<string> violations)
        {
            if (!File.Exists(file))
            {
                return;
            }

            JsonNode? node = ParseJsonFile(file);
            if (node is not JsonArray entries)
            {
                return;
            }

            foreach (JsonNode? entry in entries)
            {
                if (entry is not JsonObject entryObject)
                {
                    continue;
                }

                foreach (var field in entryObject)
                {
                    string key = field.Key;
                    if (IsMarkerKey(key) || CamelCaseKey.IsMatch(key))
                    {
                        continue;
                    }

                    violations.Add($"{file} ({tablePath}): '{key}'");
                }
            }
        }

        private static bool IsMarkerKey(string key)
        {
            return key.StartsWith("_", StringComparison.Ordinal);
        }

        private static bool IsLegacyTable(string tablePath)
        {
            foreach (string prefix in LegacyTablePathPrefixes)
            {
                if (tablePath.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static JsonNode? ParseJsonFile(string path)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Config asset is not valid JSON: {path}: {ex.Message}", ex);
            }
        }

        private List<(string Path, string[] ShardDirectories)> CollectCatalogTables()
        {
            var tables = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            CollectCatalogTableEntries(Path.Combine(_repoRoot, "assets", "config_catalog.json"), tables);
            foreach (string assetRoot in _modAssetRoots)
            {
                CollectCatalogTableEntries(Path.Combine(assetRoot, "config_catalog.json"), tables);
            }

            var result = new List<(string, string[])>();
            foreach (var kvp in tables)
            {
                result.Add((kvp.Key, new List<string>(kvp.Value).ToArray()));
            }

            return result;
        }

        private static void CollectCatalogTableEntries(
            string catalogFile,
            Dictionary<string, SortedSet<string>> tables)
        {
            if (!File.Exists(catalogFile))
            {
                return;
            }

            JsonNode? node = ParseJsonFile(catalogFile);
            if (node is not JsonArray entries)
            {
                throw new InvalidOperationException($"Config catalog must be a JSON array: {catalogFile}");
            }

            foreach (JsonNode? entry in entries)
            {
                if (entry is not JsonObject entryObject ||
                    entryObject["Path"] is not JsonNode pathNode ||
                    pathNode.GetValue<string>() is not { Length: > 0 } tablePath)
                {
                    throw new InvalidOperationException($"Config catalog entry without Path: {catalogFile}");
                }

                if (!tables.TryGetValue(tablePath, out SortedSet<string>? shards))
                {
                    shards = new SortedSet<string>(StringComparer.Ordinal);
                    tables.Add(tablePath, shards);
                }

                if (entryObject["ShardDirectories"] is JsonArray shardArray)
                {
                    foreach (JsonNode? shard in shardArray)
                    {
                        if (shard != null)
                        {
                            shards.Add(shard.GetValue<string>());
                        }
                    }
                }
            }
        }

        private static List<string> CollectModAssetRoots(string modsRoot)
        {
            var roots = new List<string>();
            CollectAssetDirectories(modsRoot, roots);
            return roots;
        }

        private static void CollectAssetDirectories(string directory, List<string> roots)
        {
            foreach (string subdirectory in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(subdirectory);
                if (name == ".git")
                {
                    continue;
                }

                if (name == "assets")
                {
                    roots.Add(subdirectory);
                }
                else
                {
                    CollectAssetDirectories(subdirectory, roots);
                }
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "mods")) &&
                    File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }
    }
}
