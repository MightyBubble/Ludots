using System.Diagnostics;
using System.Text.Json;
using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class MassNavigationEvidenceContractTests
{
    [Test]
    public void AnchorSample_SerializesReadableCentimeterCoordinates()
    {
        var sample = new MassNavigationAnchorEvidenceSample(
            AgentIndex: 7,
            TeamId: 3,
            OwnerEntityId: 11,
            PerformerStableId: 13,
            SolverWorldCm: new MassNavigationEvidencePoint(100.5f, 200.25f),
            EcsWorldCm: new MassNavigationEvidencePoint(101.5f, 201.25f),
            VisualWorldCm: new MassNavigationEvidencePoint(102.5f, 202.25f),
            PerformerWorldCm: new MassNavigationEvidencePoint(103.5f, 203.25f),
            OwnerVisible: true);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(sample));
        JsonElement root = document.RootElement;
        AssertCoordinate(root, "solver_world_cm", 100.5f, 200.25f);
        AssertCoordinate(root, "ecs_world_cm", 101.5f, 201.25f);
        AssertCoordinate(root, "visual_world_cm", 102.5f, 202.25f);
        AssertCoordinate(root, "performer_world_cm", 103.5f, 203.25f);
    }

    [Test]
    public void LargeWorldReport_DescribesOrderQueueSharedBatchBeforeOrderBufferActivation()
    {
        string repoRoot = FindRepoRoot();
        string recorder = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Tools",
            "Ludots.Launcher.Evidence",
            "LauncherEvidenceRecorder.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(recorder, Does.Contain("OrderQueue shared batch"));
            Assert.That(recorder, Does.Contain("formal OrderBuffer activation"));
            Assert.That(recorder, Does.Not.Contain("submit a `massNavigationMove` order through OrderBufferSystem"));
            Assert.That(recorder, Does.Not.Contain("Submit massNavigationMove through OrderBuffer"));
        });
    }

    [Test]
    public void LargeWorldAcceptance_RequiresUnitMovementAndSecondCommandEvidence()
    {
        string repoRoot = FindRepoRoot();
        string recorder = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Tools",
            "Ludots.Launcher.Evidence",
            "LauncherEvidenceRecorder.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(recorder, Does.Contain("\"002_settled_before_crossing\""));
            Assert.That(recorder, Does.Contain("\"003_crossing_order\""));
            Assert.That(recorder, Does.Contain("CountMovedMassNavigationSamples"));
            Assert.That(recorder, Does.Contain("First massNavigationMove"));
            Assert.That(recorder, Does.Contain("Second massNavigationMove"));
            Assert.That(recorder, Does.Contain("movement:"));
        });
    }

    [Test]
    public void LargeWorldAcceptance_AggregatesStageFailuresAcrossFullTimeline()
    {
        string repoRoot = FindRepoRoot();
        string recorder = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Tools",
            "Ludots.Launcher.Evidence",
            "LauncherEvidenceRecorder.cs"));

        string script = File.ReadAllText(Path.Combine(
            repoRoot,
            "scripts",
            "acceptance",
            "run-mass-navigation-large-world-uat.ps1"));

        Assert.Multiple(() =>
        {
            Assert.That(recorder, Does.Contain("SummarizeMassNavigationStageFailures(timeline)"));
            Assert.That(recorder, Does.Contain("Transform-stage failures across timeline"));
            Assert.That(recorder, Does.Contain("transform_failure_count = stageFailures.TransformFailureCount"));
            Assert.That(recorder, Does.Not.Contain("AddAcceptanceCheck(boot.TransformFailureCount == 0"));
            Assert.That(recorder, Does.Not.Contain("chain:{boot.PayloadFailureCount}/{boot.TransformFailureCount}"));
            Assert.That(script, Does.Contain("stageFailureFields"));
            Assert.That(script, Does.Contain("stage failure field must be zero across the full timeline"));
        });
    }

    [Test]
    public void LargeWorldSummary_RequiresFormalPerformanceFields()
    {
        string repoRoot = FindRepoRoot();
        string recorder = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Tools",
            "Ludots.Launcher.Evidence",
            "LauncherEvidenceRecorder.cs"));

        string script = File.ReadAllText(Path.Combine(
            repoRoot,
            "scripts",
            "acceptance",
            "run-mass-navigation-large-world-uat.ps1"));

        string[] fields =
        [
            "max_frame_ms",
            "max_mass_navigation_ms",
            "max_mass_navigation_prepare_ms",
            "max_mass_navigation_steer_ms",
            "max_mass_navigation_resolve_ms",
            "max_mass_navigation_crowd_step_ms",
            "max_mass_navigation_sync_ms",
        ];

        Assert.Multiple(() =>
        {
            foreach (string field in fields)
            {
                Assert.That(recorder, Does.Contain(field));
                Assert.That(script, Does.Contain(field));
            }

            Assert.That(recorder, Does.Contain("mass_navigation_prepare_ms"));
            Assert.That(recorder, Does.Contain("mass_navigation_steer_ms"));
            Assert.That(recorder, Does.Contain("mass_navigation_resolve_ms"));
            Assert.That(recorder, Does.Contain("mass_navigation_sync_ms"));
            Assert.That(recorder, Does.Contain("HasValidTimingFields"));
            Assert.That(recorder, Does.Contain("frame/massNavigation/prepare/steer/resolve/crowd/sync"));
            Assert.That(recorder, Does.Contain("MaxMassNavigationCrowdStepMs:F3}/{timing.MaxMassNavigationSyncMs:F3"));
            Assert.That(script, Does.Contain("performanceFields"));
            Assert.That(script, Does.Contain("visible crowd count is missing or zero"));
        });
    }

    [Test]
    public void LargeWorldEvidenceWorkflow_PublishesHeadShaBoundPortableArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string workflow = File.ReadAllText(Path.Combine(
            repoRoot,
            ".github",
            "workflows",
            "mass-navigation-10k-evidence.yml"));
        string script = File.ReadAllText(Path.Combine(
            repoRoot,
            "scripts",
            "acceptance",
            "run-mass-navigation-large-world-uat.ps1"));
        string recorder = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Tools",
            "Ludots.Launcher.Evidence",
            "LauncherEvidenceRecorder.cs"));
        int massNavigationReportStart = recorder.IndexOf(
            "private static string BuildMassNavigationBattleReport(",
            StringComparison.Ordinal);
        int massNavigationReportEnd = recorder.IndexOf(
            "private static string BuildMassNavigationTraceJsonl(",
            massNavigationReportStart,
            StringComparison.Ordinal);
        Assert.That(massNavigationReportStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(massNavigationReportEnd, Is.GreaterThan(massNavigationReportStart));
        string massNavigationReport = recorder[massNavigationReportStart..massNavigationReportEnd];

        Assert.Multiple(() =>
        {
            Assert.That(workflow, Does.Contain("pull_request:"));
            Assert.That(workflow, Does.Contain("github.event.pull_request.head.sha || github.sha"));
            Assert.That(workflow, Does.Contain("$env:RUNNER_TEMP"));
            Assert.That(workflow, Does.Contain("$env:GITHUB_ENV"));
            Assert.That(workflow, Does.Not.Contain("${{ runner.temp }}"));
            Assert.That(workflow, Does.Contain("Pin evidence SDK outside the worktree"));
            Assert.That(workflow, Does.Contain("Split-Path $env:GITHUB_WORKSPACE -Parent"));
            Assert.That(workflow, Does.Contain("--sdk-version 9.0.100"));
            Assert.That(workflow, Does.Contain("--output $sdkSelectionRoot"));
            Assert.That(workflow, Does.Contain("-Build always"));
            Assert.That(workflow, Does.Contain("actions/upload-artifact@v4"));
            Assert.That(workflow, Does.Contain("mass-navigation-10k-${{ matrix.adapter }}-${{ env.EVIDENCE_SOURCE_SHA }}"));
            Assert.That(workflow, Does.Contain("workflow_run_url"));
            Assert.That(workflow, Does.Contain("portability_outcome"));
            Assert.That(workflow, Does.Contain("assert-portable-evidence.ps1"));
            Assert.That(workflow, Does.Contain("- raylib"));
            Assert.That(workflow, Does.Contain("- web"));
            Assert.That(script, Does.Contain("output_dir = $runName"));
            Assert.That(script, Does.Contain("summary = \"$runName/summary.json\""));
            Assert.That(script, Does.Contain("ConvertTo-PortableEvidenceLog"));
            Assert.That(script, Does.Contain("Get-EvidenceAbsolutePathViolations"));
            Assert.That(script, Does.Contain("ValidateSet(\"auto\", \"always\", \"never\")"));
            Assert.That(massNavigationReport, Does.Contain("BuildPortableCommandText(request)"));
            Assert.That(massNavigationReport, Does.Not.Contain("request.CommandText"));
            Assert.That(script, Does.Not.Contain("output_dir = $runDir"));
            Assert.That(script, Does.Not.Contain("summary = $summaryFile"));
        });
    }

    [Test]
    public void PortableEvidenceValidator_ScansActualBundleContentForMachinePaths()
    {
        string repoRoot = FindRepoRoot();
        string validator = Path.Combine(
            repoRoot,
            "scripts",
            "acceptance",
            "assert-portable-evidence.ps1");
        string artifactRoot = Path.Combine(
            Path.GetTempPath(),
            $"ludots-portable-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactRoot);

        try
        {
            string report = Path.Combine(artifactRoot, "battle-report.md");
            string log = Path.Combine(artifactRoot, "run.log");
            File.WriteAllText(
                report,
                "- Launch command: `scripts/run-mod-launcher.cmd cli launch sample --record <artifact-root>`\n");
            File.WriteAllText(log, "recording=run-0001\nsummary=run-0001/summary.json\n");

            (int validExitCode, string validOutput) = RunPowerShellScript(validator, artifactRoot);
            Assert.That(validExitCode, Is.Zero, validOutput);

            File.AppendAllText(
                report,
                "windows=C:\\Users\\runneradmin\\AppData\\Local\\Temp\\evidence\n" +
                "unix=/home/runner/work/Ludots/evidence\n");
            (int invalidExitCode, string invalidOutput) = RunPowerShellScript(validator, artifactRoot);

            Assert.Multiple(() =>
            {
                Assert.That(invalidExitCode, Is.Not.Zero);
                Assert.That(invalidOutput, Does.Contain("battle-report.md"));
                Assert.That(invalidOutput, Does.Contain("machine-absolute path"));
            });
        }
        finally
        {
            Directory.Delete(artifactRoot, recursive: true);
        }
    }

    private static void AssertCoordinate(JsonElement sample, string propertyName, float expectedX, float expectedY)
    {
        JsonElement point = sample.GetProperty(propertyName);
        Assert.That(point.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(point.GetProperty("x_cm").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(point.GetProperty("y_cm").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(point.GetProperty("x_cm").GetSingle(), Is.EqualTo(expectedX));
        Assert.That(point.GetProperty("y_cm").GetSingle(), Is.EqualTo(expectedY));
    }

    private static (int ExitCode, string Output) RunPowerShellScript(string scriptPath, string artifactRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-Root");
        startInfo.ArgumentList.Add(artifactRoot);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell portability validator.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput + standardError);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "Core")) &&
                Directory.Exists(Path.Combine(current.FullName, "src", "Tools")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Ludots repository root.");
    }
}
