using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Tool;
using NUnit.Framework;

namespace CrowdSimulationTests.Authoring;

/// <summary>
/// mod init --template crowd-data 写出的骨架，跑完三条 nav 命令就能进图：会话建起来，两名玩家的单位都生成并朝对方基地走。
/// 工具按仓库根找 Mod，所以骨架放在临时仓库根里，依赖的三个 Mod 拷进去；进图时依赖用仓库里的原件。
/// </summary>
[Category("ci-gate")]
[Category("crowd-authoring")]
[TestFixture]
public sealed class CrowdModScaffoldTests
{
    private const string ModId = "ScaffoldProbeCrowdMod";
    private const string MapId = "scaffold_probe";
    private static readonly CrowdModScaffoldIds Ids = CrowdModScaffoldIds.Parse("land,water", "foot");

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "crowd-scaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Scaffold_AfterNavCommands_LoadsAndUnitsWalkTowardTheOtherBase()
    {
        string repo = RepoRoot();
        CopyTree(Path.Combine(repo, "assets"), Path.Combine(_root, "assets"), "*");
        string core = Path.Combine(repo, "mods", "LudotsCoreMod");
        CopyTree(Path.Combine(core, "assets"), Path.Combine(_root, "mods", "LudotsCoreMod", "assets"), "*.json");
        CopyFile(core, Path.Combine(_root, "mods", "LudotsCoreMod"), "mod.json");
        CopyFile(core, Path.Combine(_root, "mods", "LudotsCoreMod"), Path.Combine("bin", "net9.0", "LudotsCoreMod.dll"));
        foreach (string capability in new[] { "CrowdSimulationMod", "CrowdSimulationStandardMod" })
        {
            CopyTree(Path.Combine(repo, "mods", "capabilities", "navigation", capability), Path.Combine(_root, "mods", capability), "*");
        }

        string modDir = Path.Combine(_root, "mods", ModId);
        CrowdModScaffold.Write(modDir, ModId, MapId, Ids);
        var water = new[] { CrowdNavSurfaceAuthoring.CrowdTerrainRect.Parse("water:120,20,135,40") };
        CrowdNavSurfaceAuthoring.CrowdAuthoringStart start =
            CrowdNavSurfaceAuthoring.WriteInitialTerrain(_root, ModId, MapId, "land", water, overwrite: false);
        CrowdNavSurfaceAuthoring.WriteFlatHeightmap(_root, ModId, MapId, 100, overwrite: false);
        string navSurface = Path.Combine(modDir, "assets", "terrain", MapId + ".navsurface");
        NavSurfaceAsset baked = CrowdNavSurfaceAuthoring.BakeAuthoredFile(_root, ModId, MapId, navSurface, overwrite: false);
        Assert.That(start.NavCellsPerSide, Is.EqualTo(256));
        Assert.That(baked.TerrainTypeIds, Is.EqualTo(new[] { "land", "water" }));

        using GameEngine engine = StartEngine(repo, modDir);
        CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)
            ?? throw new InvalidOperationException("没有群体会话。");
        int guard = 0;
        while (session.TickCount < 150 && guard++ < 600) engine.Tick(1f / 30f);

        Assert.That(session.Units.Count, Is.EqualTo(2 * CrowdModScaffold.UnitsPerPlayer));
        double meanX1 = 0, meanX2 = 0;
        for (int i = 0; i < session.Units.Count; i++)
        {
            Entity entity = session.Units.EntityAt(i);
            Fix64Vec2 p = session.World.Get<WorldPositionCm>(entity).Value;
            var state = session.World.Get<CrowdSimulationUnitState>(entity);
            int player = session.Groups.Groups[state.GroupId]!.Player;
            if (player == 0) meanX1 += p.X.ToDouble();
            else meanX2 += p.X.ToDouble();
        }

        meanX1 /= CrowdModScaffold.UnitsPerPlayer;
        meanX2 /= CrowdModScaffold.UnitsPerPlayer;
        TestContext.Out.WriteLine($"tick={session.TickCount} meanX1={meanX1:F0} meanX2={meanX2:F0}");
        Assert.That(meanX1, Is.GreaterThan(CrowdModScaffold.Player1BaseXCm + 20000), "玩家 1 没朝东走。");
        Assert.That(meanX2, Is.LessThan(CrowdModScaffold.Player2BaseXCm - 20000), "玩家 2 没朝西走。");
    }

    [Test]
    public void Scaffold_RefusesExistingDirectoryAndMissingMapId()
    {
        string modDir = Path.Combine(_root, ModId);
        Assert.Throws<InvalidOperationException>(() => CrowdModScaffold.Write(modDir, ModId, "", Ids));
        Directory.CreateDirectory(modDir);
        Assert.Throws<IOException>(() => CrowdModScaffold.Write(modDir, ModId, MapId, Ids));
    }

    [TestCase(null, "foot", "--terrainTypes")]
    [TestCase("land,", "foot", "空 id")]
    [TestCase("land,land", "foot", "重复")]
    [TestCase("land", " ", "--navProfile")]
    public void ScaffoldIds_MissingOrMalformed_AreRejected(string? terrainTypes, string? navProfile, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CrowdModScaffoldIds.Parse(terrainTypes, navProfile));
        Assert.That(ex!.Message, Does.Contain(expected));
    }

    private static GameEngine StartEngine(string repo, string modDir)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(new List<string>
        {
            Path.Combine(repo, "mods", "LudotsCoreMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
            modDir,
        }, Path.Combine(repo, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubView());
        engine.Start();
        engine.LoadMap(MapId);
        return engine;
    }

    private static void CopyTree(string source, string target, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(source, pattern, SearchOption.AllDirectories))
        {
            string dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
    }

    private static void CopyFile(string sourceDir, string targetDir, string relative)
    {
        string dest = Path.Combine(targetDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(Path.Combine(sourceDir, relative), dest);
    }

    private sealed class StubView : Ludots.Core.Presentation.Camera.IViewController
    {
        public System.Numerics.Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
