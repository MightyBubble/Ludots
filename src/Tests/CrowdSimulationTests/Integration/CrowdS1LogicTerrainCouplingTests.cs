using Ludots.Core.Engine;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

public class CrowdS1LogicTerrainCouplingTests
{
    [Test]
    public void S1337_LogicTerrainStaysFlatMeterGrid()
    {
        string repoRoot = FindRepoRoot();
        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(CrowdMods(repoRoot), Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubView());
        engine.Start();
        engine.LoadMap("crowd_simulation_s1337");

        Assert.That(engine.LogicTerrain, Is.InstanceOf<FlatGridLogicTerrainField>());
        var flat = (FlatGridLogicTerrainField)engine.LogicTerrain;
        Assert.Multiple(() =>
        {
            Assert.That(flat.HorizontalStepCm, Is.EqualTo(100));
            Assert.That(flat.VerticalStepCm, Is.EqualTo(100));
            Assert.That(flat.WidthCells, Is.EqualTo(16000));
            Assert.That(flat.HeightCells, Is.EqualTo(16000));
        });

        var board = (GridBoard)engine.CurrentMapSession.GetBoard("default");
        Assert.That(board.GridCellSizeCm, Is.EqualTo(100));
        Assert.That(board.CoordinateConverter.GridCellSizeCm, Is.EqualTo(100));
        Assert.That(board.WorldSize.GridCellSizeCm, Is.EqualTo(100));
        Assert.That(engine.CrowdRuntime.TerrainTypeColors, Is.Not.Null);
        Assert.That(engine.CrowdRuntime.TerrainTypeColors!.Colors.Keys, Is.EquivalentTo(new[] { "water", "shore", "land", "mountain", "cliff" }));
    }

    private static List<string> CrowdMods(string repoRoot) =>
    [
        Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
        Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
        Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
        Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
    ];

    private sealed class StubView : Ludots.Core.Presentation.Camera.IViewController
    {
        public System.Numerics.Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
