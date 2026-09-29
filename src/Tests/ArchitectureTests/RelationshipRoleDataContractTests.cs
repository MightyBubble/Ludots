using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class RelationshipRoleDataContractTests
    {
        private static readonly Regex ReservedRelationshipNameLiteral =
            new("\"(Owns|Controls|MemberOf)\"", RegexOptions.Compiled);

        private static readonly string[] RequiredRoles = { "Ownership", "Membership", "ControlGrant" };

        [Test]
        public void CoreSource_DoesNotNameRelationshipTypesThatCatalogRolesBind()
        {
            string repoRoot = FindRepoRoot();
            string coreRoot = Path.Combine(repoRoot, "src", "Core");
            var failures = new List<string>();

            foreach (string file in Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                         .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                int lineNumber = 0;
                foreach (string line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (ReservedRelationshipNameLiteral.IsMatch(line))
                    {
                        failures.Add($"{Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}:{lineNumber}: {line.Trim()}");
                    }
                }
            }

            Assert.That(
                failures,
                Is.Empty,
                "Core must resolve ownership / membership / control-grant relationship types through RelationshipRoleBindings " +
                "(the \"role\" field in Relationships/catalog.json), not by type name:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void DefaultRelationshipCatalog_BindsEveryRequiredRoleExactlyOnce()
        {
            string repoRoot = FindRepoRoot();
            string catalogPath = Path.Combine(repoRoot, "assets", "Relationships", "catalog.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));

            var roleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JsonElement type in document.RootElement.GetProperty("types").EnumerateArray())
            {
                if (type.TryGetProperty("role", out JsonElement role) && role.ValueKind == JsonValueKind.String)
                {
                    string roleName = role.GetString()!;
                    roleCounts[roleName] = roleCounts.TryGetValue(roleName, out int count) ? count + 1 : 1;
                }
            }

            for (int i = 0; i < RequiredRoles.Length; i++)
            {
                Assert.That(
                    roleCounts.TryGetValue(RequiredRoles[i], out int count) ? count : 0,
                    Is.EqualTo(1),
                    $"assets/Relationships/catalog.json must bind role '{RequiredRoles[i]}' to exactly one type.");
            }
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
    }
}
