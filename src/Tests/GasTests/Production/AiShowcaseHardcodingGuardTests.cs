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
        "TeamManager.SetRelationship",
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
                ParticipantRelationshipTypeId: "UtilityAutocast.Participant",
                HostileRelationshipTypeId: null))
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
                    "mods/showcases/combat_stance/CombatStanceShowcaseMod/assets/Maps/combat_stance_showcase.json",
                    "mods/showcases/combat_stance/CombatStanceShowcaseMod/assets/Relationships/catalog.json"
                },
                MapFile: "mods/showcases/combat_stance/CombatStanceShowcaseMod/assets/Maps/combat_stance_showcase.json",
                ParticipantRelationshipTypeId: "CombatStance.Participant",
                HostileRelationshipTypeId: "CombatStance.Hostile"))
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

        AssertMapUsesParticipantRelationships(repoRoot, guardCase);
    }

    private static void AssertMapUsesParticipantRelationships(string repoRoot, ShowcaseGuardCase guardCase)
    {
        string mapPath = Path.Combine(repoRoot, guardCase.MapFile.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(mapPath));
        JsonElement root = document.RootElement;

        AssertNonEmptyArray(root, "Teams", guardCase.Name);
        AssertNonEmptyArray(root, "Players", guardCase.Name);
        Assert.That(root.TryGetProperty("ParticipantRelationships", out JsonElement relationships), Is.True, $"{guardCase.Name} map must use map-owned participant relationships.");
        AssertParticipantRelationshipArray(relationships, "Teams", guardCase);
        AssertParticipantRelationshipArray(relationships, "Players", guardCase);
        AssertParticipantRelationshipArray(relationships, "PlayerTeams", guardCase);
        AssertSemanticTeamRelationships(relationships, guardCase);
    }

    private static void AssertNonEmptyArray(JsonElement root, string propertyName, string showcaseName)
    {
        Assert.That(root.TryGetProperty(propertyName, out JsonElement value), Is.True, $"{showcaseName} map requires '{propertyName}'.");
        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Array), $"{showcaseName} map '{propertyName}' must be an array.");
        Assert.That(value.GetArrayLength(), Is.GreaterThan(0), $"{showcaseName} map '{propertyName}' must not be empty.");
    }

    private static void AssertParticipantRelationshipArray(JsonElement relationships, string propertyName, ShowcaseGuardCase guardCase)
    {
        AssertNonEmptyArray(relationships, propertyName, guardCase.Name);
        bool hasParticipantEntry = false;
        int index = 0;
        foreach (JsonElement entry in relationships.GetProperty(propertyName).EnumerateArray())
        {
            Assert.That(entry.TryGetProperty("TypeId", out JsonElement typeId), Is.True, $"{guardCase.Name} ParticipantRelationships.{propertyName}[{index}] requires TypeId.");
            if (!string.Equals(typeId.GetString(), guardCase.ParticipantRelationshipTypeId, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            hasParticipantEntry = true;

            if (string.Equals(propertyName, "Teams", StringComparison.Ordinal))
            {
                Assert.That(entry.TryGetProperty("Attitude", out JsonElement attitude), Is.True, $"{guardCase.Name} ParticipantRelationships.Teams[{index}] requires Attitude.");
                Assert.That(attitude.GetString(), Is.Not.Empty, $"{guardCase.Name} ParticipantRelationships.Teams[{index}].Attitude must be explicit.");
            }

            index++;
        }

        Assert.That(hasParticipantEntry, Is.True, $"{guardCase.Name} ParticipantRelationships.{propertyName} must include '{guardCase.ParticipantRelationshipTypeId}'.");
    }

    private static void AssertSemanticTeamRelationships(JsonElement relationships, ShowcaseGuardCase guardCase)
    {
        if (guardCase.HostileRelationshipTypeId == null)
        {
            return;
        }

        JsonElement teams = relationships.GetProperty("Teams");
        int index = 0;
        foreach (JsonElement entry in teams.EnumerateArray())
        {
            Assert.That(entry.TryGetProperty("TypeId", out JsonElement typeId), Is.True, $"{guardCase.Name} ParticipantRelationships.Teams[{index}] requires TypeId.");
            if (!string.Equals(typeId.GetString(), guardCase.HostileRelationshipTypeId, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            Assert.That(entry.TryGetProperty("Attitude", out JsonElement attitude), Is.True, $"{guardCase.Name} ParticipantRelationships.Teams[{index}] requires Attitude.");
            Assert.That(attitude.GetString(), Is.EqualTo("Hostile"), $"{guardCase.Name} semantic hostile relationship must declare hostile attitude.");
            return;
        }

        Assert.Fail($"{guardCase.Name} ParticipantRelationships.Teams must include semantic hostile relationship '{guardCase.HostileRelationshipTypeId}'.");
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
        string ParticipantRelationshipTypeId,
        string? HostileRelationshipTypeId);
}
