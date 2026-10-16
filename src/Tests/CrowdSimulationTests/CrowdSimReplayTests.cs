using Arch.Core;
using Ludots.Core.CrowdSimulation.Diagnostics;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

/// <summary>同一份日志回放到新会话，改过的命令要报出分区名。</summary>
public sealed class CrowdSimReplayTests
{
    [Test]
    public void Replay_RecordedRun_MatchesChecksumAndPartitions()
    {
        var (_, live) = S4DeployTruthTests.BuildSession("s7");
        var basis = live.Config.Deploy.Bases[0];
        SubmitSpawn(live, basis.PlayerId, basis.XCm, basis.YCm);
        Assert.That(live.Step(out _), Is.True);
        Assert.That(live.Step(out _), Is.True);
        Assert.That(live.Units.Count, Is.GreaterThan(0));

        var capture = CrowdSimReplay.Capture(live);
        var report = CrowdSimReplay.Replay(Fresh(live), capture);
        Assert.That(report.Passed, Is.True, report.Error);
        Assert.That(report.Checksum, Is.EqualTo(capture.Checksum));
        Assert.That(report.Expected, Is.EqualTo(capture.Checksum));
        Assert.That(report.Tick, Is.EqualTo(capture.Tick));
        Assert.That(report.Commands, Is.EqualTo(1));
        Assert.That(report.Differing, Is.Empty);
    }

    [Test]
    public void Replay_TamperedCommand_NamesPosition()
    {
        var (_, live) = S4DeployTruthTests.BuildSession("s7");
        var bases = live.Config.Deploy.Bases;
        var origin = bases[0];
        SubmitSpawn(live, origin.PlayerId, origin.XCm, origin.YCm);
        Assert.That(live.Step(out _), Is.True);
        Assert.That(live.Step(out _), Is.True);

        var capture = CrowdSimReplay.Capture(live);
        var shifted = bases.Count > 1 ? bases[bases.Count - 1] : origin;
        int x = bases.Count > 1 ? shifted.XCm : origin.XCm + live.Config.NavCellSizeCm * 10;
        int y = bases.Count > 1 ? shifted.YCm : origin.YCm;
        var command = capture.Commands[0];
        var tampered = capture.WithCommands(new[]
        {
            command with { Cmd = command.Cmd with { XCm = x, YCm = y } },
        });
        var report = CrowdSimReplay.Replay(Fresh(live), tampered);
        Assert.That(report.Passed, Is.False);
        Assert.That(report.Differing, Does.Contain("units.x"));
        Assert.That(report.Commands, Is.EqualTo(1));
    }

    private static void SubmitSpawn(CrowdSimSession session, int player, int xCm, int yCm)
    {
        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.SpawnAt,
            Player = player,
            XCm = xCm,
            YCm = yCm,
            Count = 4,
            UnitType = 0,
            RIdx = 0,
        }, CrowdSimCommands.Exec);
    }

    private static CrowdSimSession Fresh(CrowdSimSession live)
    {
        var session = new CrowdSimSession(
            live.Config, World.Create(), live.Navs, live.NavByLayerRadius, live.Presentation);
        session.Structures = live.Structures;
        session.RebakeSources = live.RebakeSources;
        session.NavTileCache = live.NavTileCache;
        return session;
    }
}
