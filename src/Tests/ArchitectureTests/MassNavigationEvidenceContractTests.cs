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

    private static void AssertCoordinate(JsonElement sample, string propertyName, float expectedX, float expectedY)
    {
        JsonElement point = sample.GetProperty(propertyName);
        Assert.That(point.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(point.GetProperty("x_cm").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(point.GetProperty("y_cm").ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(point.GetProperty("x_cm").GetSingle(), Is.EqualTo(expectedX));
        Assert.That(point.GetProperty("y_cm").GetSingle(), Is.EqualTo(expectedY));
    }
}
