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

    private static void AssertCoordinate(JsonElement sample, string propertyName, float expectedX, float expectedY)
    {
        JsonElement point = sample.GetProperty(propertyName);
        Assert.That(point.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(point.GetProperty("x_cm").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(point.GetProperty("y_cm").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(point.GetProperty("x_cm").GetSingle(), Is.EqualTo(expectedX));
        Assert.That(point.GetProperty("y_cm").GetSingle(), Is.EqualTo(expectedY));
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
