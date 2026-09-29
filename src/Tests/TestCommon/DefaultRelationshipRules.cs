using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Tests
{
    /// <summary>
    /// Installs the link rules that <c>assets/Relationships/catalog.json</c> declares, for every catalog type the
    /// test runtime has registered, so hand-built test runtimes link with the same constraints as the engine.
    /// </summary>
    internal static class DefaultRelationshipRules
    {
        private static readonly Lazy<RelationshipCatalogConfig> Catalog = new(LoadCatalog);

        public static void Install(RelationshipRuntime relationships)
        {
            ArgumentNullException.ThrowIfNull(relationships);
            RelationshipTypeRegistry types = relationships.TypeRegistry;
            var registered = new RelationshipCatalogConfig();
            foreach (RelationshipTypeConfig type in Catalog.Value.Types)
            {
                if (type.Rules != null &&
                    types.TryGetId(type.Id, out _) &&
                    AllRegistered(types, type.Rules))
                {
                    registered.Types.Add(type);
                }
            }

            relationships.Rules.InstallFromCatalog(registered);
        }

        private static bool AllRegistered(RelationshipTypeRegistry types, RelationshipTypeRulesConfig rules)
        {
            foreach (string name in rules.BlockedAny)
            {
                if (!types.TryGetId(name, out _))
                {
                    return false;
                }
            }

            foreach (string name in rules.Removed)
            {
                if (!types.TryGetId(name, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private static RelationshipCatalogConfig LoadCatalog()
        {
            string path = FindCatalogPath();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter() },
            };
            return JsonSerializer.Deserialize<RelationshipCatalogConfig>(File.ReadAllText(path), options)
                ?? throw new InvalidOperationException($"'{path}' is empty.");
        }

        private static string FindCatalogPath()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "assets", "Relationships", "catalog.json");
                if (File.Exists(candidate) && Directory.Exists(Path.Combine(directory.FullName, "src", "Core")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate assets/Relationships/catalog.json above the test output directory.");
        }
    }
}
