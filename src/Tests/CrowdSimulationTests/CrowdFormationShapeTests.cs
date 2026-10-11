using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public sealed class CrowdFormationShapeTests
{
    [Test]
    public void UnknownShape_Throws_AndListsKnownIds()
    {
        var shapes = TestDefaults.Assemble().Formations;
        Assert.That(shapes[0].Id, Is.EqualTo("box"));

        var known = CrowdFormations.ShapeOf(shapes, "line");
        Assert.That(known.Id, Is.EqualTo("line"));

        var ex = Assert.Throws<InvalidOperationException>(() => CrowdFormations.ShapeOf(shapes, "aaa"));
        Assert.That(ex!.Message, Does.Contain("未知阵型"));
        Assert.That(ex.Message, Does.Contain("\"aaa\""));
        Assert.That(ex.Message, Does.Contain("box"));
        Assert.That(ex.Message, Does.Contain("line"));
        Assert.That(ex.Message, Does.Contain("loose"));
        Assert.That(ex.Message, Does.Contain("wedge"));
    }

    [Test]
    public void UnknownShapeOrder_IsRejectedBeforeEnteringTheLog()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        var order = CrowdSimCommand.Parse(JsonNode.Parse(
            """{"type":"order","player":1,"xCm":620000,"yCm":620000,"shape":"aaa"}""")!);

        var live = Assert.Throws<InvalidOperationException>(
            () => session.Commands.Submit(session, order, CrowdSimCommands.Exec));
        Assert.That(live!.Message, Does.Contain("未知阵型"));

        var scripted = Assert.Throws<InvalidOperationException>(
            () => session.Commands.Schedule(session, new[] { new CrowdCommand(3, order) }));
        Assert.That(scripted!.Message, Does.Contain("未知阵型"));

        Assert.That(session.Commands.Log, Is.Empty);
        Assert.That(session.Commands.PendingCount, Is.Zero);
    }

    [Test]
    public void OmittedShape_DefaultsToBox_AndMissingBoxThrows()
    {
        var parsed = CrowdSimCommand.Parse(JsonNode.Parse(
            """{"type":"order","player":1,"xCm":1,"yCm":2}""")!);
        Assert.That(parsed.ShapeId, Is.EqualTo("box"));

        var withoutBox = TestDefaults.Assemble().Formations.Where(shape => shape.Id != "box").ToArray();
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdFormations.ShapeOf(withoutBox, parsed.ShapeId));
        Assert.That(ex!.Message, Does.Contain("未知阵型"));
        Assert.That(ex.Message, Does.Contain("\"box\""));
        Assert.That(ex.Message, Does.Contain("line"));
        Assert.That(ex.Message, Does.Not.Contain("box,"));
    }
}
