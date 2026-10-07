using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class ParticipantRelationshipDataContractTests
    {
        [Test]
        public void AuthoredMapTeamComponents_HaveExplicitParticipantTeamBindings()
        {
            string repoRoot = FindRepoRoot();
            var failures = new List<string>();

            foreach (string mapPath in EnumerateSourceMapFiles(repoRoot))
            {
                using JsonDocument mapDocument = JsonDocument.Parse(File.ReadAllText(mapPath));
                JsonElement mapRoot = mapDocument.RootElement;
                string relativeMapPath = ToRepoRelativePath(repoRoot, mapPath);
                Dictionary<string, int> templateTeams = LoadTemplateTeamIds(mapPath);
                var boundTeamIds = new HashSet<int>();
                var boundRepresentatives = new List<string>();
                var entityInstanceIds = new HashSet<string>(StringComparer.Ordinal);
                var authoredTeamIds = new HashSet<int>();

                if (TryGetArray(mapRoot, "Teams", out JsonElement teams))
                {
                    foreach (JsonElement team in teams.EnumerateArray())
                    {
                        if (TryGetInt32(team, "TeamId", out int teamId))
                        {
                            boundTeamIds.Add(teamId);
                        }

                        if (!TryGetString(team, "RepresentativeInstanceId", out string representativeInstanceId) ||
                            string.IsNullOrWhiteSpace(representativeInstanceId))
                        {
                            failures.Add($"{relativeMapPath}: Teams entry requires a non-empty RepresentativeInstanceId.");
                        }
                        else
                        {
                            boundRepresentatives.Add(representativeInstanceId);
                        }
                    }
                }

                if (TryGetArray(mapRoot, "Entities", out JsonElement entities))
                {
                    foreach (JsonElement entity in entities.EnumerateArray())
                    {
                        if (TryGetString(entity, "InstanceId", out string instanceId) &&
                            !string.IsNullOrWhiteSpace(instanceId))
                        {
                            entityInstanceIds.Add(instanceId);
                        }

                        if (TryGetEntityTeamId(entity, templateTeams, out int teamId) && teamId > 0)
                        {
                            authoredTeamIds.Add(teamId);
                        }
                    }
                }

                foreach (int teamId in authoredTeamIds.OrderBy(static id => id))
                {
                    if (!boundTeamIds.Contains(teamId))
                    {
                        failures.Add($"{relativeMapPath}: authored Team {teamId} has no Teams binding.");
                    }
                }

                if (TryGetArray(mapRoot, "Teams", out teams))
                {
                    for (int i = 0; i < boundRepresentatives.Count; i++)
                    {
                        if (!entityInstanceIds.Contains(boundRepresentatives[i]))
                        {
                            failures.Add($"{relativeMapPath}: Teams representative '{boundRepresentatives[i]}' does not match any map Entity.InstanceId.");
                        }
                    }
                }
            }

            Assert.That(
                failures,
                Is.Empty,
                "Team-authored map data must be migrated into explicit participant bindings so relationship-domain MemberOf edges can be built without runtime fallback:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void MapRelations_AreEntityInstanceRelationsWithDeclaredTypes()
        {
            string repoRoot = FindRepoRoot();
            HashSet<string> declaredTypeIds = LoadDeclaredRelationshipTypeIds(repoRoot);
            var failures = new List<string>();

            foreach (string mapPath in EnumerateSourceMapFiles(repoRoot))
            {
                using JsonDocument mapDocument = JsonDocument.Parse(File.ReadAllText(mapPath));
                JsonElement mapRoot = mapDocument.RootElement;
                string relativeMapPath = ToRepoRelativePath(repoRoot, mapPath);
                if (TryGetPropertyIgnoreCase(mapRoot, "ParticipantRelationships", out _))
                {
                    failures.Add($"{relativeMapPath}: ParticipantRelationships is not a map section; write the edge as Entities[].Relations on the representative entity.");
                }

                if (!TryGetArray(mapRoot, "Entities", out JsonElement entities))
                {
                    continue;
                }

                foreach (JsonElement entity in entities.EnumerateArray())
                {
                    if (!TryGetPropertyIgnoreCase(entity, "Relations", out JsonElement relations) ||
                        relations.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    TryGetString(entity, "InstanceId", out string owner);
                    int index = 0;
                    foreach (JsonElement relation in relations.EnumerateArray())
                    {
                        if (!TryGetPropertyIgnoreCase(relation, "Type", out JsonElement type) ||
                            type.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(type.GetString()))
                        {
                            failures.Add($"{relativeMapPath}: entity '{owner}' Relations[{index}].Type must be non-empty.");
                        }
                        else if (!declaredTypeIds.Contains(type.GetString()!))
                        {
                            failures.Add($"{relativeMapPath}: entity '{owner}' Relations[{index}].Type '{type.GetString()}' is not declared in relationship catalog data.");
                        }

                        index++;
                    }
                }
            }

            Assert.That(
                failures,
                Is.Empty,
                "Map relationships are instance relations on entities, and their types must be declared by relationship catalog data before map load resolves them:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, failures));
        }

        private static IEnumerable<string> EnumerateSourceMapFiles(string repoRoot)
        {
            string modsRoot = Path.Combine(repoRoot, "mods");
            return Directory.EnumerateFiles(modsRoot, "*.json", SearchOption.AllDirectories)
                .Where(static path => path.Contains($"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}Maps{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        }

        private static Dictionary<string, int> LoadTemplateTeamIds(string mapPath)
        {
            string? modRoot = FindNearestModRoot(mapPath);
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(modRoot))
            {
                return result;
            }

            string templatePath = Path.Combine(modRoot, "assets", "Entities", "templates.json");
            if (!File.Exists(templatePath))
            {
                return result;
            }

            using JsonDocument templateDocument = JsonDocument.Parse(File.ReadAllText(templatePath));
            JsonElement root = templateDocument.RootElement;
            IEnumerable<JsonElement> templates = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : TryGetArray(root, "templates", out JsonElement lowerTemplates)
                    ? lowerTemplates.EnumerateArray()
                    : TryGetArray(root, "Templates", out JsonElement upperTemplates)
                        ? upperTemplates.EnumerateArray()
                        : Array.Empty<JsonElement>();

            foreach (JsonElement template in templates)
            {
                if (!TryGetString(template, "id", out string id) || string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (!template.TryGetProperty("components", out JsonElement components) ||
                    !components.TryGetProperty("Team", out JsonElement team) ||
                    !TryReadTeamId(team, out int teamId))
                {
                    continue;
                }

                result[id] = teamId;
            }

            return result;
        }

        private static HashSet<string> LoadDeclaredRelationshipTypeIds(string repoRoot)
        {
            var declaredTypeIds = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<string> catalogPaths = Directory.EnumerateFiles(Path.Combine(repoRoot, "mods"), "catalog.json", SearchOption.AllDirectories)
                .Where(static path => path.Contains($"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}Relationships{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Append(Path.Combine(repoRoot, "assets", "Relationships", "catalog.json"));
            foreach (string catalogPath in catalogPaths)
            {
                using JsonDocument catalogDocument = JsonDocument.Parse(File.ReadAllText(catalogPath));
                if (!TryGetArray(catalogDocument.RootElement, "types", out JsonElement types))
                {
                    continue;
                }

                foreach (JsonElement type in types.EnumerateArray())
                {
                    if (TryGetString(type, "id", out string id) && !string.IsNullOrWhiteSpace(id))
                    {
                        declaredTypeIds.Add(id);
                    }
                }
            }

            return declaredTypeIds;
        }

        private static bool TryGetEntityTeamId(JsonElement entity, Dictionary<string, int> templateTeams, out int teamId)
        {
            teamId = 0;
            if (entity.TryGetProperty("Overrides", out JsonElement overrides) &&
                overrides.TryGetProperty("Team", out JsonElement team) &&
                TryReadTeamId(team, out teamId))
            {
                return true;
            }

            if (TryGetString(entity, "Template", out string templateId) &&
                templateTeams.TryGetValue(templateId, out teamId))
            {
                return true;
            }

            return false;
        }

        private static bool TryReadTeamId(JsonElement team, out int teamId)
        {
            teamId = 0;
            return team.ValueKind == JsonValueKind.Number
                ? team.TryGetInt32(out teamId)
                : TryGetInt32(team, "Id", out teamId);
        }

        private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement array)
        {
            if (element.TryGetProperty(propertyName, out array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            array = default;
            return false;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
        {
            value = 0;
            return element.TryGetProperty(propertyName, out JsonElement property) &&
                   property.ValueKind == JsonValueKind.Number &&
                   property.TryGetInt32(out value);
        }

        private static bool TryGetString(JsonElement element, string propertyName, out string value)
        {
            value = string.Empty;
            if (!element.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString() ?? string.Empty;
            return true;
        }

        private static string? FindNearestModRoot(string path)
        {
            DirectoryInfo? directory = new FileInfo(path).Directory;
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "mod.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static string ToRepoRelativePath(string repoRoot, string path)
        {
            return Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
