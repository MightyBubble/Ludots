using System;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// Separation / Calm 按稠密下标存活。删非末行时，末行单位的推挤必须搬进空洞。
/// </summary>
public sealed class SwapRemovePushStateTests
{
    [Test]
    public void RemoveDense_CarriesSurvivorSeparationAndCalm()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        session.Commands.Schedule(session, new[]
        {
            new CrowdCommand(0, CrowdSimCommand.Parse((JsonObject)JsonNode.Parse("""{"type":"spawn","count":4}""")!)),
        });
        session.Advance(1);
        Assert.That(session.Units.Count, Is.GreaterThanOrEqualTo(2));

        CrowdMovementKernel kernel = session.Movement!;
        int last = session.Units.Count - 1;
        var survivor = session.Units.EntityAt(last);
        var removed = session.Units.EntityAt(0);
        int survivorGroup = session.World.Get<CrowdSimulationUnitState>(survivor).GroupId;
        int removedGroup = session.World.Get<CrowdSimulationUnitState>(removed).GroupId;
        int survivorGroupCount = session.Groups.TryGet(survivorGroup, out var survivorGroupRow) ? survivorGroupRow.Count : -1;
        kernel.Separation[0] = new Fix64Vec2(Fix64.FromInt(3), Fix64.FromInt(4));
        kernel.Calm[0] = 1;
        kernel.Separation[last] = new Fix64Vec2(Fix64.FromInt(9), Fix64.FromInt(8));
        kernel.Calm[last] = 0;

        uint removedHandle = session.Units.HandleAt(0);
        session.RemoveDense(0);

        Assert.That(session.Units.EntityAt(0), Is.EqualTo(survivor));
        Assert.That(session.Units.Count, Is.EqualTo(last));
        Assert.That(kernel.Separation[0].X, Is.EqualTo(Fix64.FromInt(9)));
        Assert.That(kernel.Separation[0].Y, Is.EqualTo(Fix64.FromInt(8)));
        Assert.That(kernel.Calm[0], Is.EqualTo(0));
        Assert.That(session.Groups.TryGet(survivorGroup, out var after), Is.True);
        int expectedCount = survivorGroup == removedGroup ? survivorGroupCount - 1 : survivorGroupCount;
        Assert.That(after.Count, Is.EqualTo(expectedCount));
        Assert.That(session.Units.HandleAt(0), Is.Not.EqualTo(removedHandle));
    }
}
