using System;
using System.IO;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class MassNavigationEvidenceContractTests
{
    [Test]
    public void EvidenceRecorder_UsesProcessWideMemoryMetricsAndAuditableTimingModes()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Tools",
            "Ludots.Launcher.Evidence",
            "LauncherEvidenceRecorder.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("GC.GetTotalAllocatedBytes"));
            Assert.That(source, Does.Contain("GC.GetGCMemoryInfo"));
            Assert.That(source, Does.Contain("Process.GetCurrentProcess"));
            Assert.That(source, Does.Contain("MassNavigationSteadyTimingEnabledEnvironmentVariable"));
            Assert.That(source, Does.Contain("defaultValue: false"));
            Assert.That(source, Does.Contain("simulation.Telemetry.SetTimingEnabled(timingEnabled)"));
            Assert.That(source, Does.Contain("timings.SystemBreakdownEnabled = false"));
            Assert.That(source, Does.Contain("simulation.AgentState.TotalAgents"));
            Assert.That(source, Does.Contain("submittedOrderCount > 0"));
            Assert.That(source, Does.Contain("movedCommandActorCount > 0"));
            Assert.That(source, Does.Not.Contain("afterOrder.ActiveOrderGroups > 0 || afterOrder.ActiveGroups > 0"));
            Assert.That(source, Does.Contain("afterOrder.ScreenHudBarCount == boot.AgentCount"));
            Assert.That(source, Does.Contain("afterOrder.ScreenHudTextCount == boot.AgentCount"));
            Assert.That(source, Does.Contain("afterOrder.ScreenHudDroppedTotal == 0"));
            Assert.That(source, Does.Contain("steady_state_duration_seconds"));
            Assert.That(source, Does.Contain("steady_timing_enabled_requested"));
            Assert.That(source, Does.Contain("steady_timing_disabled"));
            Assert.That(source, Does.Contain("steady_presentation_timing_disabled"));
            Assert.That(source, Does.Contain("git_commit_sha"));
            Assert.That(source, Does.Contain("source_worktree_dirty"));
            Assert.That(source, Does.Contain("source_worktree_sha256"));
            Assert.That(source, Does.Contain("build_mode"));
            Assert.That(source, Does.Contain("runtime_config_sha256"));
            Assert.That(source, Does.Contain("resolved_capability_profile_sha256"));
            Assert.That(source, Does.Contain("scenario_random_seed"));
            Assert.That(source, Does.Contain("steady_flow_cadence_count"));
            Assert.That(source, Does.Contain("steady_flow_publication_count"));
            Assert.That(source, Does.Contain("steady_flow_state_growth_events"));
            Assert.That(source, Does.Contain("steady_total_allocated_bytes"));
            Assert.That(source, Does.Contain("steady_working_set_growth_bytes"));
            Assert.That(source, Does.Contain("steady_capacity_growth_events"));
            Assert.That(source, Does.Contain("steady_p95_tick_ms"));
            Assert.That(source, Does.Contain("steady_p99_tick_ms"));
            Assert.That(source, Does.Contain("steady_slowest_ticks"));
            Assert.That(source, Does.Contain("simulation.NavGroupRuntime.PeakActiveGroupCount"));
            Assert.That(source, Does.Contain("simulation.PeakOrderIngestionMemberCount"));
            Assert.That(source, Does.Not.Contain("capacity_navigation_group_peak = timeline.Max"));
        });
    }

    [Test]
    public void LauncherNeverMode_ValidatesExistingAppInsteadOfBuildingDuringMeasurement()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Tools",
            "Ludots.Launcher.Backend",
            "LauncherService.cs"));
        string wrapper = File.ReadAllText(Path.Combine(repoRoot, "scripts", "run-mod-launcher.ps1"));
        string host = File.ReadAllText(Path.Combine(repoRoot, "scripts", "dotnet-host.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("await PrepareAppAsync(resolveResult.Plan)"));
            Assert.That(source, Does.Contain("App build mode is never, but the app assembly is missing"));
            Assert.That(source, Does.Contain("!buildNever && plannedEntries.Any"));
            Assert.That(source, Does.Contain("Build mode is never, but the mod assembly is missing"));
            Assert.That(source, Does.Contain("Build mode is never, but the host browser runtime provider package is missing or invalid"));
            Assert.That(wrapper, Does.Contain("-NoBuild:$noBuild"));
            Assert.That(wrapper, Does.Contain("$cliArgs[$index + 1] -ieq \"never\""));
            Assert.That(host, Does.Contain("NoBuild requested, but project output dll not found"));
        });
    }

    [Test]
    public void SoakScript_LaunchesCapabilityStandard10kAndReadsOnlyRecorderFields()
    {
        string repoRoot = FindRepoRoot();
        string script = File.ReadAllText(Path.Combine(
            repoRoot,
            "scripts",
            "acceptance",
            "run-mass-navigation-large-world-uat.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("$capability_standard_mass_navigation_large_world_10k"));
            Assert.That(script, Does.Contain("steady_state_duration_seconds"));
            Assert.That(script, Does.Contain("steady_total_allocated_bytes"));
            Assert.That(script, Does.Contain("steady_working_set_growth_bytes"));
            Assert.That(script, Does.Contain("steady_capacity_growth_events"));
            Assert.That(script, Does.Contain("steady_timing_enabled_requested"));
            Assert.That(script, Does.Contain("LUDOTS_MASS_NAV_STEADY_TIMING_ENABLED"));
            Assert.That(script, Does.Contain("MassNavigationTimingEnabled"));
            Assert.That(script, Does.Contain("$ErrorActionPreference = \"Continue\""));
            Assert.That(script, Does.Not.Contain("first_command_advance_cm"));
            Assert.That(script, Does.Not.Contain("second_command_advance_cm"));
            Assert.That(script, Does.Not.Contain("empty_world_command_advance_cm"));
            Assert.That(script, Does.Not.Contain("multi_team_min_advance_cm"));
            Assert.That(script, Does.Not.Contain("edge_inside_min_advance_cm"));
        });
    }

    [Test]
    public void FoundationScenario_RemainsAnExact10kAcceptanceInput()
    {
        string repoRoot = FindRepoRoot();
        JsonArray profiles = JsonNode.Parse(File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "assets",
                "MassNavigationConfig.json")))?.AsArray()
            ?? throw new InvalidOperationException("MassNavigationConfig.json must contain an ArrayById profile array.");
        JsonObject config = profiles
            .OfType<JsonObject>()
            .Single(profile => string.Equals(profile["id"]?.GetValue<string>(), "mass_navigation", StringComparison.Ordinal));

        JsonObject sceneAuthoring = config["sceneAuthoring"]?.AsObject()
            ?? throw new InvalidOperationException("MassNavigation profile sceneAuthoring is required for the 10K acceptance profile.");
        JsonObject scenario = sceneAuthoring["scenario"]?.AsObject()
            ?? throw new InvalidOperationException("MassNavigation profile sceneAuthoring.scenario is required.");
        int agentsPerTeam = scenario["agentsPerTeam"]?.GetValue<int>()
            ?? throw new InvalidOperationException("MassNavigationConfig.scenario.agentsPerTeam is required.");
        int teamCount = scenario["teams"]?.AsArray().Count
            ?? throw new InvalidOperationException("MassNavigationConfig.scenario.teams is required.");

        Assert.That(checked(agentsPerTeam * teamCount), Is.EqualTo(10_000));
    }

    [Test]
    public void FormalAuthoringDocs_UseMapProfileBindingAndCurrentConfigOwners()
    {
        string repoRoot = FindRepoRoot();
        string guide = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "reference", "map-scale-authoring-guide.md"));
        string starter = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "reference", "map-scale-authoring-starter.html"));
        string userBook = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "reference", "mass-navigation-user-book.md"));

        Assert.Multiple(() =>
        {
            Assert.That(guide, Does.Contain("Metadata.massNavigation.profileId"));
            Assert.That(guide, Does.Contain("streaming.radiusCm"));
            Assert.That(guide, Does.Not.Contain("\"solverWindowWidthCm\""));
            Assert.That(guide, Does.Not.Contain("\"solverWindowHeightCm\""));
            Assert.That(guide, Does.Not.Contain("\"streamingRadiusCm\""));
            Assert.That(starter, Does.Contain("profileId: mapId"));
            Assert.That(starter, Does.Contain("return JSON.stringify([{"));
            Assert.That(starter, Does.Not.Contain("solverWindowWidthCm:"));
            Assert.That(starter, Does.Not.Contain("solverWindowHeightCm:"));
            Assert.That(starter, Does.Not.Contain("streamingRadiusCm:"));
            Assert.That(userBook, Does.Contain("ConfigPipeline `ArrayById` profile catalog"));
        });
    }

    [TestCase("mods/capabilities/navigation/MassNavigationMod/assets/game.json")]
    [TestCase("mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod/assets/game.json")]
    public void MassNavigation10kPresentationCapacities_AreBoundedToMeasuredScenarioScale(string relativePath)
    {
        string repoRoot = FindRepoRoot();
        JsonObject game = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, relativePath)))?.AsObject()
            ?? throw new InvalidOperationException($"{relativePath} must contain an object.");
        JsonObject presentation = game["presentation"]?.AsObject()
            ?? throw new InvalidOperationException($"{relativePath} presentation config is required.");

        Assert.Multiple(() =>
        {
            AssertCapacity(presentation, "performerInstanceCapacity", minimum: 30_009, maximum: 32_768);
            AssertCapacity(presentation, "performerCommandCapacity", minimum: 30_009, maximum: 65_536);
            AssertCapacity(presentation, "presentationRequestCapacity", minimum: 30_009, maximum: 65_536);
            AssertCapacity(presentation, "worldHudCapacity", minimum: 20_000, maximum: 32_768);
            AssertCapacity(presentation, "screenHudCapacity", minimum: 20_000, maximum: 32_768);
            AssertCapacity(presentation, "minimapMarkerCapacity", minimum: 10_009, maximum: 16_384);
            AssertCapacity(presentation, "runtimeEntitySpawnQueueCapacity", minimum: 10_009, maximum: 16_384);
            AssertCapacity(presentation, "runtimeEntitySpawnReceiptQueueCapacity", minimum: 10_009, maximum: 16_384);
        });
    }

    private static void AssertCapacity(JsonObject presentation, string fieldName, int minimum, int maximum)
    {
        int value = presentation[fieldName]?.GetValue<int>()
            ?? throw new InvalidOperationException($"presentation.{fieldName} is required.");
        Assert.That(value, Is.InRange(minimum, maximum), $"presentation.{fieldName} must fit the measured 10K workload without retaining the old 128K/256K blanket capacity.");
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }
}
