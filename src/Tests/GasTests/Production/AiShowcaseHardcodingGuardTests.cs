using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[TestFixture]
public sealed class AiShowcaseHardcodingGuardTests
{
    private static readonly string[] ForbiddenBehaviorSnippets =
    {
        "World.Add(",
        "world.Add(",
        ".Add<UtilityAiAgent",
        ".Add<CombatStanceState",
        ".Add<ActuatorReadiness",
        ".Add<AimGate",
        "new UtilityAiAgent",
        "new CombatStanceState",
        "new ActuatorReadiness",
        "new AimGate",
        "new UtilityAiTargetPriority",
        "RelationshipRuntime(",
        "EnsureLink("
    };

    public static IEnumerable<TestCaseData> ShowcaseCases()
    {
        yield return new TestCaseData(new ShowcaseGuardCase(
                Name: "UtilityAutocastShowcaseMod",
                Root: "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod",
                AllowedSourceFiles: new[]
                {
                    "UtilityAutocastShowcaseModEntry.cs",
                    "Triggers/PrintUtilityAutocastTraceOnMapLoadedTrigger.cs"
                },
                RequiredDataFiles: new[]
                {
                    "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/decision_makers.json",
                    "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/decisions.json",
                    "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/target_filters.json",
                    "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/GAS/abilities.json",
                    "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/Entities/templates.json",
                    "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/Maps/utility_autocast_showcase.json"
                },
                MapFile: "mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/Maps/utility_autocast_showcase.json",
                HostileRelationshipTypeId: "Hostile"))
            .SetName("UtilityAutocastShowcase_CSharpDoesNotHardcodeBehavior");

        yield return new TestCaseData(new ShowcaseGuardCase(
                Name: "CombatStanceShowcaseMod",
                Root: "mods/showcases/combat_stance/CombatStanceShowcaseMod",
                AllowedSourceFiles: new[]
                {
                    "CombatStanceShowcaseModEntry.cs",
                    "Runtime/CombatStanceShowcaseConfig.cs",
                    "Triggers/InstallCombatStanceShowcaseOrdersTrigger.cs"
                },
                RequiredDataFiles: new[]
                {
                    "mods/CombatStanceBehaviorMod/assets/CombatStance/behavior.json",
                    "mods/showcases/combat_stance/CombatStanceShowcaseMod/assets/CombatStanceShowcase/scenario.json",
                    "mods/showcases/combat_stance/CombatStanceShowcaseMod/assets/Entities/templates.json",
                    "mods/showcases/combat_stance/CombatStanceShowcaseMod/assets/Maps/combat_stance_showcase.json"
                },
                MapFile: "mods/showcases/combat_stance/CombatStanceShowcaseMod/assets/Maps/combat_stance_showcase.json",
                HostileRelationshipTypeId: "Hostile"))
            .SetName("CombatStanceShowcase_CSharpDoesNotHardcodeBehavior");
    }

    [TestCaseSource(nameof(ShowcaseCases))]
    public void ShowcaseCSharp_OnlyUsesWhitelistedShellsAndAssets(ShowcaseGuardCase guardCase)
    {
        string repoRoot = FindRepoRoot();
        string root = Path.Combine(repoRoot, guardCase.Root.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(root, Does.Exist);

        var allowed = new HashSet<string>(guardCase.AllowedSourceFiles, StringComparer.Ordinal);
        foreach (string sourceFile in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Normalize(Path.GetRelativePath(root, sourceFile));
            if (IsBuildOutput(relative))
            {
                continue;
            }

            Assert.That(
                allowed.Contains(relative),
                Is.True,
                $"{guardCase.Name} contains non-whitelisted C# source '{relative}'. Showcase behavior must stay in assets.");

            string text = File.ReadAllText(sourceFile);
            for (int i = 0; i < ForbiddenBehaviorSnippets.Length; i++)
            {
                Assert.That(
                    text,
                    Does.Not.Contain(ForbiddenBehaviorSnippets[i]),
                    $"{guardCase.Name} source '{relative}' must not hardcode behavior with '{ForbiddenBehaviorSnippets[i]}'.");
            }
        }

        for (int i = 0; i < guardCase.RequiredDataFiles.Length; i++)
        {
            string file = Path.Combine(repoRoot, guardCase.RequiredDataFiles[i].Replace('/', Path.DirectorySeparatorChar));
            Assert.That(file, Does.Exist, $"{guardCase.Name} requires behavior data file '{guardCase.RequiredDataFiles[i]}'.");
            Assert.That(new FileInfo(file).Length, Is.GreaterThan(2), $"{guardCase.Name} behavior data file '{guardCase.RequiredDataFiles[i]}' must not be empty.");
        }

        AssertMapAuthorsHostileTeamEdge(repoRoot, guardCase);
    }

    private static void AssertMapAuthorsHostileTeamEdge(string repoRoot, ShowcaseGuardCase guardCase)
    {
        string mapPath = Path.Combine(repoRoot, guardCase.MapFile.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(mapPath));
        JsonElement root = document.RootElement;

        AssertNonEmptyArray(root, "Teams", guardCase.Name);
        AssertNonEmptyArray(root, "Players", guardCase.Name);
        var teamRepresentatives = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement team in root.GetProperty("Teams").EnumerateArray())
        {
            teamRepresentatives.Add(team.GetProperty("RepresentativeInstanceId").GetString()!);
        }

        foreach (JsonElement entity in root.GetProperty("Entities").EnumerateArray())
        {
            if (!entity.TryGetProperty("InstanceId", out JsonElement owner) ||
                !teamRepresentatives.Contains(owner.GetString()!) ||
                !entity.TryGetProperty("Relations", out JsonElement relations))
            {
                continue;
            }

            foreach (JsonElement relation in relations.EnumerateArray())
            {
                if (string.Equals(relation.GetProperty("Type").GetString(), guardCase.HostileRelationshipTypeId, StringComparison.Ordinal) &&
                    teamRepresentatives.Contains(relation.GetProperty("To").GetString()!))
                {
                    return;
                }
            }
        }

        Assert.Fail($"{guardCase.Name} map must author a '{guardCase.HostileRelationshipTypeId}' relation between team representatives.");
    }

    private static void AssertNonEmptyArray(JsonElement root, string propertyName, string showcaseName)
    {
        Assert.That(root.TryGetProperty(propertyName, out JsonElement value), Is.True, $"{showcaseName} map requires '{propertyName}'.");
        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Array), $"{showcaseName} map '{propertyName}' must be an array.");
        Assert.That(value.GetArrayLength(), Is.GreaterThan(0), $"{showcaseName} map '{propertyName}' must not be empty.");
    }

    private static bool IsBuildOutput(string relativePath)
    {
        return relativePath.StartsWith("obj/", StringComparison.Ordinal) ||
               relativePath.StartsWith("bin/", StringComparison.Ordinal);
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    public sealed record ShowcaseGuardCase(
        string Name,
        string Root,
        string[] AllowedSourceFiles,
        string[] RequiredDataFiles,
        string MapFile,
        string HostileRelationshipTypeId);
}
