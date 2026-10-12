using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.EntityCollections;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 选中只进集合仓。指令日志里的 order 自带提交当时的成员句柄；
/// 参考脚本里的 select 与无成员 order 在入日志前译成这份成员。
/// </summary>
public sealed class CrowdSelectionContractTests
{
    [Test]
    public void Selection_DoesNotEnterTheLog_AndOrdersKeepSubmittedMembers()
    {
        var script = new JsonArray
        {
            Row(0, """{"type":"spawnAt","player":1,"xCm":480000,"yCm":560000,"count":6,"unitType":0,"rIdx":0}"""),
            Row(1, """{"type":"selectAll","player":1}"""),
            Row(2, """{"type":"order","player":1,"xCm":800000,"yCm":800000,"shape":"box"}"""),
            Row(3, """{"type":"clearSelection"}"""),
            Row(4, """{"type":"order","player":1,"xCm":500000,"yCm":500000,"shape":"box"}"""),
        };
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        session.ReferenceScript = CrowdReferenceScript.Load(script, session.Units.Capacity);
        var first = new List<ulong>();
        session.Advance(6, first);
        var log = session.Commands.Log.Select(e => new CrowdCommand(e.Tick, e.Cmd)).ToArray();
        Assert.That(log.Select(e => e.Cmd.Kind), Is.EqualTo(new[]
        {
            CrowdSimCommandKind.SpawnAt,
            CrowdSimCommandKind.Order,
            CrowdSimCommandKind.Order,
        }));
        Assert.That(log[1].Cmd.Members, Has.Length.EqualTo(6));
        Assert.That(log[2].Cmd.Members, Has.Length.EqualTo(6));
        Assert.That(log[2].Cmd.Members, Is.EqualTo(log[1].Cmd.Members));

        var (_, replay) = Parity.S4DeployTruthTests.BuildSession("s1337");
        replay.Commands.Schedule(replay, log);
        var second = new List<ulong>();
        replay.Advance(6, second);
        Assert.That(second, Is.EqualTo(first).AsCollection);
        Assert.That(replay.Commands.Log.Any(e =>
            e.Cmd.Kind == CrowdSimCommandKind.Order && (e.Cmd.Members == null || e.Cmd.Members.Length == 0)), Is.False);

        var rep = session.World.Create(new Name { Value = "player-1" });
        var applier = new CollectionApplier(session.World, session.Presentation.Collections);
        session.Presentation.CollectionApplier = applier;
        session.Presentation.RosterCollectionKeyId = session.Presentation.Collections.KeyRegistry.Register(
            CrowdSimPresentationWiring.RosterCollectionKey);
        var reps = new Entity[session.Config.PairRules.PlayerCount + 1];
        reps[1] = rep;
        session.Presentation.PlayerReps = reps;
        session.ReferenceScript = null;
        session.Reset();

        Assert.Throws<InvalidOperationException>(() => CrowdSimCommand.Parse(JsonNode.Parse(
            """{"type":"select","player":1,"x0Cm":0,"y0Cm":0,"x1Cm":1,"y1Cm":1}""")!));

        int spawned = CrowdDeployment.SpawnAt(
            session, Fix64.FromInt(480000), Fix64.FromInt(560000), 4, 1, 0, 0);
        Assert.That(spawned, Is.EqualTo(4));
        Assert.That(RosterCount(session, rep), Is.EqualTo(4));

        applier.Apply(
            rep,
            session.Presentation.SelectedCollectionKeyId,
            CollectionWriteOp.Replace,
            new[] { session.Units.EntityAt(0) });
        Assert.That(session.SelectedCollectionCount(1), Is.EqualTo(1));
        Assert.That(session.Commands.Log, Is.Empty);

        var frozen = new[] { session.Units.HandleAt(0), session.Units.HandleAt(1) };
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = 1,
            XCm = 800000,
            YCm = 800000,
            ShapeId = "box",
            Members = frozen,
        }, CrowdSimCommands.Exec);
        applier.Apply(
            rep,
            session.Presentation.SelectedCollectionKeyId,
            CollectionWriteOp.Replace,
            new[] { session.Units.EntityAt(3) });
        Assert.That(session.SelectedCollectionCount(1), Is.EqualTo(1));
        Assert.That(session.Commands.Log[0].Cmd.Members, Is.EqualTo(frozen));

        var empty = Assert.Throws<InvalidOperationException>(() => session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = 1,
            XCm = 1,
            YCm = 1,
            ShapeId = "box",
            Members = Array.Empty<uint>(),
        }, CrowdSimCommands.Exec));
        Assert.That(empty!.Message, Is.EqualTo("order: 成员集为空。"));
        Assert.That(session.Commands.Log, Has.Count.EqualTo(1));
    }

    [Test]
    public void Roster_ExcludesHiddenTemplateKeys()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        var rep = session.World.Create(new Name { Value = "player-1" });
        session.Presentation.CollectionApplier = new CollectionApplier(session.World, session.Presentation.Collections);
        session.Presentation.RosterCollectionKeyId = session.Presentation.Collections.KeyRegistry.Register(
            CrowdSimPresentationWiring.RosterCollectionKey);
        var reps = new Entity[session.Config.PairRules.PlayerCount + 1];
        reps[1] = rep;
        session.Presentation.PlayerReps = reps;

        int hiddenKey = session.Presentation.TemplatesByUnitTypeRadius[0][0].TemplateKeyId;
        session.Presentation.HiddenTemplateKeyIds.Add(hiddenKey);
        int hidden = CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 3, 1, 0, 0);
        Assert.That(hidden, Is.EqualTo(3));
        var buffer = new uint[session.Units.Capacity];
        Assert.That(session.Units.CopyOwnHandles(1, buffer), Is.EqualTo(0));
        Assert.That(RosterCount(session, rep), Is.EqualTo(0));

        int otherType = -1;
        int otherRadius = 0;
        var rows = session.Presentation.TemplatesByUnitTypeRadius;
        for (int t = 0; t < rows.Count && otherType < 0; t++)
        {
            for (int r = 0; r < rows[t].Count; r++)
            {
                if (rows[t][r].TemplateKeyId == hiddenKey) continue;
                otherType = t;
                otherRadius = r;
                break;
            }
        }

        if (otherType < 0) return;
        int shown = CrowdDeployment.SpawnAt(
            session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, otherType, otherRadius);
        Assert.That(shown, Is.EqualTo(2));
        Assert.That(session.Units.CopyOwnHandles(1, buffer), Is.EqualTo(2));
        Assert.That(RosterCount(session, rep), Is.EqualTo(2));
    }

    private static int RosterCount(CrowdSimSession session, Entity rep)
    {
        if (!session.Presentation.Collections.TryGet(rep, session.Presentation.RosterCollectionKeyId, out var handle)) return 0;
        return session.Presentation.Collections.TryGetView(handle, out var view) ? view.Count : 0;
    }

    private static JsonObject Row(int tick, string cmd) => new()
    {
        ["tick"] = tick,
        ["cmd"] = JsonNode.Parse(cmd),
    };
}
