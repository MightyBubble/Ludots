using System;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// 视线打开时不透明格已分配、高度已注入。建筑挡住的格在视线开时不可见，视线关时仍可见。
/// </summary>
public sealed class CrowdFogLineOfSightTests
{
    private const int SpawnXCm = 480000;
    private const int SpawnYCm = 560000;
    private const int Ticks = 24;

    [Test]
    public void LineOfSightTrue_RunsTicks_AndBuildingHidesCellVisibleWhenOff()
    {
        using var off = Open(lineOfSight: false);
        using var on = Open(lineOfSight: true);
        var placed = PlaceWallBesideUnit(off.Session);
        PlaceWall(on.Session, placed.WallXCm, placed.WallYCm);
        CrowdDeployment.SpawnAt(on.Session, Fix64.FromInt(SpawnXCm), Fix64.FromInt(SpawnYCm), 1, 1, 0, 0);

        off.Session.Advance(Ticks);
        on.Session.Advance(Ticks);

        var fogOff = off.Session.Fog!;
        var fogOn = on.Session.Fog!;
        int behind = Behind(fogOff, placed.ObserverX, placed.ObserverY);
        Assert.That(Visible(fogOff, 0, behind), Is.True, "视线关闭时，墙后的格应在视盘里");
        Assert.That(Visible(fogOn, 0, behind), Is.False, "视线打开时，墙挡住墙后的格");
    }

    [Test]
    public void S1337_RunHide_MatchesTheSceneAsset()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337", lineOfSight: false);
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(60), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        var scene = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root,
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/fog-acceptance.json")))!.AsObject();
        Ludots.Core.CrowdSimulation.Diagnostics.CrowdFogAcceptance.RunHide(session, scene);
    }

    [Test]
    public void S1337_RunTerrain_MatchesTheSceneAsset()
    {
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        var scene = JsonNode.Parse(File.ReadAllText(Path.Combine(
            root,
            "mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/fog-acceptance.json")))!.AsObject();
        RunTerrain(scene, optimistic: false);
        RunTerrain(scene, optimistic: true);
    }

    private static void RunTerrain(JsonObject scene, bool optimistic)
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337", lineOfSight: false);
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(60), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        Ludots.Core.CrowdSimulation.Diagnostics.CrowdFogAcceptance.RunTerrain(session, scene, optimistic);
    }

    [Test]
    public void FogSightOn_RunsTicks_AndBuildingHidesCell()
    {
        using var harness = Open(lineOfSight: false);
        var session = harness.Session;
        var placed = PlaceWallBesideUnit(session);
        session.Advance(Ticks);
        var fog = session.Fog!;
        int behind = Behind(fog, placed.ObserverX, placed.ObserverY);
        Assert.That(fog.Los, Is.False);
        Assert.That(Visible(fog, 0, behind), Is.True);

        session.Commands.Submit(session, new JsonObject { ["type"] = "fogSight", ["on"] = true }, CrowdSimCommands.Exec);
        Assert.That(fog.Los, Is.True);
        session.Advance(Ticks);
        Assert.That(Visible(fog, 0, behind), Is.False);
    }

    private readonly record struct Harness(PathQueryService Service, CrowdSimSession Session) : IDisposable
    {
        public void Dispose() => Service.Dispose();
    }

    private readonly record struct Placed(int ObserverX, int ObserverY, int WallXCm, int WallYCm);

    private static Harness Open(bool lineOfSight)
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s7", lineOfSight);
        var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        TestDefaults.EnableFog(session);
        session.BlockOnDueReplies = true;
        return new Harness(service, session);
    }

    private static Placed PlaceWallBesideUnit(CrowdSimSession session)
    {
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(SpawnXCm), Fix64.FromInt(SpawnYCm), 1, 1, 0, 0), Is.EqualTo(1));
        var pos = session.World.Get<WorldPositionCm>(session.Units.EntityAt(0)).Value;
        var fog = session.Fog!;
        int side = fog.FogCellsPerSide;
        int cellCm = fog.FogCellSizeCm;
        int fx = (int)(pos.X / Fix64.FromInt(cellCm)).ToLong();
        int fy = (int)(pos.Y / Fix64.FromInt(cellCm)).ToLong();
        Assert.That(fx + 2, Is.LessThan(side));
        Assert.That(fy, Is.GreaterThanOrEqualTo(0).And.LessThan(side));
        int wallX = (fx + 1) * cellCm + cellCm / 2;
        int wallY = fy * cellCm + cellCm / 2;
        PlaceWall(session, wallX, wallY);
        return new Placed(fx, fy, wallX, wallY);
    }

    private static void PlaceWall(CrowdSimSession session, int wallXCm, int wallYCm)
    {
        int cellCm = session.Fog!.FogCellSizeCm;
        session.Commands.Submit(session, new JsonObject
        {
            ["type"] = "placeStructure",
            ["template"] = "building",
            ["xCm"] = wallXCm,
            ["yCm"] = wallYCm,
            ["sizeCm"] = cellCm,
        }, CrowdSimCommands.Exec);
    }

    private static int Behind(CrowdFog fog, int observerX, int observerY)
        => observerY * fog.FogCellsPerSide + observerX + 2;

    private static bool Visible(CrowdFog fog, int group, int cell)
        => fog.View(group)[cell] == 2;
}
