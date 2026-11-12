using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
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
            Row(0, """{"type":"spawnAt","player":1,"xCm":480000,"yCm":560000,"count":6,"template":"crowd_simulation.unit.infantry.r100"}"""),
            Row(1, """{"type":"selectAll","player":1}"""),
            Row(2, """{"type":"order","player":1,"xCm":800000,"yCm":800000,"shape":"box"}"""),
            Row(3, """{"type":"clearSelection"}"""),
            Row(4, """{"type":"order","player":1,"xCm":500000,"yCm":500000,"shape":"box"}"""),
        };
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        var reference = CrowdReferenceScript.Load(script, session.Units.Capacity);
        var first = new List<ulong>();
        reference.Advance(session, 6, first);
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
        var reps = new Entity[session.Config.PairRules.PlayerCount + 1];
        reps[1] = rep;
        session.Presentation.PlayerReps = reps;
        session.Reset();

        Assert.Throws<InvalidOperationException>(() => CrowdSimCommand.Parse(JsonNode.Parse(
            """{"type":"select","player":1,"x0Cm":0,"y0Cm":0,"x1Cm":1,"y1Cm":1}""")!));

        int spawned = CrowdDeployment.SpawnAt(
            session, Fix64.FromInt(480000), Fix64.FromInt(560000), 4, 1, 0, 0);
        Assert.That(spawned, Is.EqualTo(4));

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
    public void HiddenTemplateTag_DropsTaggedUnitsFromOwnHandles()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        const int hiddenTag = 3;
        session.Presentation.HiddenTemplateTagId = hiddenTag;
        int hidden = CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 3, 1, 0, 0);
        Assert.That(hidden, Is.EqualTo(3));
        for (int i = 0; i < session.Units.Count; i++)
        {
            Tag(session, session.Units.EntityAt(i), hiddenTag);
        }

        var buffer = new uint[session.Units.Capacity];
        Assert.That(session.Units.CopyOwnHandles(1, buffer), Is.EqualTo(0));

        int shown = CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, 0, 0);
        Assert.That(shown, Is.EqualTo(2));
        Assert.That(session.Units.CopyOwnHandles(1, buffer), Is.EqualTo(2));
    }

    [Test]
    public void Clear_EmptiesSelectedCollection()
    {
        var (_, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        var rep = session.World.Create(new Name { Value = "player-1" });
        var applier = new CollectionApplier(session.World, session.Presentation.Collections);
        session.Presentation.CollectionApplier = applier;
        var reps = new Entity[session.Config.PairRules.PlayerCount + 1];
        reps[1] = rep;
        session.Presentation.PlayerReps = reps;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 3, 1, 0, 0), Is.EqualTo(3));
        applier.Apply(
            rep,
            session.Presentation.SelectedCollectionKeyId,
            CollectionWriteOp.Replace,
            new[] { session.Units.EntityAt(0), session.Units.EntityAt(1) });
        Assert.That(session.SelectedCollectionCount(1), Is.EqualTo(2));
        session.Units.Clear();
        Assert.That(session.SelectedCollectionCount(1), Is.EqualTo(0));
        Assert.That(session.Units.Count, Is.EqualTo(0));
    }

    private static void Tag(CrowdSimSession session, Entity entity, int tagId)
    {
        if (!session.World.Has<GameplayTagContainer>(entity))
        {
            session.World.Add(entity, new GameplayTagContainer());
        }

        ref GameplayTagContainer tags = ref session.World.Get<GameplayTagContainer>(entity);
        tags.AddTag(tagId);
    }

    private static JsonObject Row(int tick, string cmd) => new()
    {
        ["tick"] = tick,
        ["cmd"] = JsonNode.Parse(cmd),
    };
}
