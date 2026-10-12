using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.NavMesh.Config;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// maps.&lt;mapId&gt;.solver 是移动求解器的唯一声明。求解器板要么是 boards 里那一块，要么是 RootBoard。
/// </summary>
[TestFixture]
public sealed class SolverDeclarationTests
{
    [Test]
    public void MissingSolver_IsRejected()
    {
        JsonObject navmesh = TestDefaults.DefaultNavmeshJson();
        navmesh["maps"]!["crowd_simulation_demo"]!.AsObject().Remove("solver");
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.ParseNavmesh(navmesh))!;
        Assert.That(ex.Message, Does.Contain("solver"));
    }

    [Test]
    public void UnknownSolver_IsRejected()
    {
        JsonObject navmesh = TestDefaults.DefaultNavmeshJson();
        navmesh["maps"]!["crowd_simulation_demo"]!["solver"] = "flow-field";
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.ParseNavmesh(navmesh))!;
        Assert.That(ex.Message, Does.Contain("flow-field"));
        Assert.That(ex.Message, Does.Contain(NavSolverDeclaration.AllowedList));
    }

    [Test]
    public void TwoSolverBoards_AreRejected()
    {
        JsonObject navmesh = TestDefaults.DefaultNavmeshJson();
        navmesh["maps"]!["crowd_simulation_demo"]!["boards"] = JsonNode.Parse("""
            {
              "alpha": { "tileWorldWidthCm": 6400, "tileWorldHeightCm": 6400 },
              "beta": { "tileWorldWidthCm": 6400, "tileWorldHeightCm": 6400 }
            }
            """);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.ParseNavmesh(navmesh))!;
        Assert.That(ex.Message, Does.Contain("2"));
        Assert.That(ex.Message, Does.Contain("一块求解器板"));
    }

    [Test]
    public void EmptyBoardsObject_IsRejected()
    {
        JsonObject navmesh = TestDefaults.DefaultNavmeshJson();
        navmesh["maps"]!["crowd_simulation_demo"]!["boards"] = new JsonObject();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.ParseNavmesh(navmesh))!;
        Assert.That(ex.Message, Does.Contain("0"));
        Assert.That(ex.Message, Does.Contain("一块求解器板"));
    }

    [Test]
    public void UndeclaredBoard_UsesRootBoard_NotTheFirstBoard()
    {
        var map = TestDefaults.DemoMap();
        map.Boards.Insert(0, new BoardConfig { Name = "decoy", WidthCm = 100, HeightCm = 50 });
        map.Boards[1].Name = "solver";
        map.RootBoard = "solver";

        var runtime = TestDefaults.Assemble(map: map, pairRules: TestDefaults.DemoPairRules());
        Assert.That(runtime.NavCellsPerSide * runtime.NavCellSizeCm, Is.EqualTo(TestDefaults.WorldCm));
    }

    [Test]
    public void DeclaredBoard_IsUsed_WhenItIsNotTheFirstBoard()
    {
        JsonObject navmesh = TestDefaults.DefaultNavmeshJson();
        navmesh["maps"]!["crowd_simulation_demo"]!["boards"] = JsonNode.Parse("""
            { "solver": { "tileWorldWidthCm": 6400, "tileWorldHeightCm": 6400 } }
            """);
        var map = TestDefaults.DemoMap();
        map.Boards.Insert(0, new BoardConfig { Name = "decoy", WidthCm = 100, HeightCm = 50 });
        map.Boards[1].Name = "solver";
        map.RootBoard = "decoy";

        var runtime = TestDefaults.Assemble(
            map: map,
            pairRules: TestDefaults.DemoPairRules(),
            navmesh: TestDefaults.ParseNavmesh(navmesh));
        Assert.That(runtime.NavCellsPerSide * runtime.NavCellSizeCm, Is.EqualTo(TestDefaults.WorldCm));
    }

    [Test]
    public void MassNavigationAndCrowdSimulation_CannotBothApply()
    {
        NavMeshBakeConfig navmesh = TestDefaults.DemoNavmesh();
        Assert.That(
            NavSolverDeclaration.Is(navmesh, "crowd_simulation_demo", NavSolverDeclaration.CrowdSimulation),
            Is.True);
        Assert.That(
            NavSolverDeclaration.Is(navmesh, "crowd_simulation_demo", NavSolverDeclaration.MassNavigation),
            Is.False);

        JsonObject json = TestDefaults.DefaultNavmeshJson();
        json["maps"]!["crowd_simulation_demo"]!["solver"] = NavSolverDeclaration.MassNavigation;
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => TestDefaults.Assemble(navmesh: TestDefaults.ParseNavmesh(json), pairRules: TestDefaults.DemoPairRules()))!;
        Assert.That(ex.Message, Does.Contain(NavSolverDeclaration.MassNavigation));
        Assert.That(ex.Message, Does.Contain(NavSolverDeclaration.CrowdSimulation));
    }

    [Test]
    public void Autostart_IsARetiredKey()
    {
        var debug = JsonNode.Parse("""{ "session": { "autostart": false } }""")!.AsObject();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CrowdSimulationDebugConfig.Load(debug))!;
        Assert.That(ex.Message, Does.Contain("已删除 session.autostart"));
        Assert.That(ex.Message, Does.Contain("crowd-simulation"));
    }
}
