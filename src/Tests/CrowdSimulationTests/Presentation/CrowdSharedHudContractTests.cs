using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Client;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

/// <summary>
/// 共用界面不点名某一张演示的单位，按钮只在输入表里有动作时才出现。
/// 最小样板挂上这份界面进图，点选和生成用它自己的单位。
/// </summary>
[TestFixture]
public sealed class CrowdSharedHudContractTests
{
    private const string Minimal = "CrowdSimulationMinimalMod";
    private const string MapId = "crowd_minimal";
    private const string MinimalUnit = "crowd_minimal.unit";

    private static readonly string[] PlayerControls =
    {
        "cycleShape", "toggleLos", "toggleOptimisticTerrain", "toggleRoute", "sceneOutside", "sceneBehind",
    };

    private static readonly string[] ControlIds =
    {
        "crowd-formations", "crowd-los", "crowd-optimistic", "crowd-pv-route", "crowd-scene-outside", "crowd-scene-behind",
    };

    [Test]
    public void SharedFiles_DoNotNameDemoUnits_AndDeclareThePlayerControls()
    {
        string ui = Path.Combine(RepoRoot(), "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets");
        string viewText = File.ReadAllText(Path.Combine(ui, "CrowdSimulationPlayerView.json"));
        string debugText = File.ReadAllText(Path.Combine(ui, "CrowdSimulationDebug.json"));
        Assert.That(viewText, Does.Not.Contain("crowd_simulation.unit."));
        Assert.That(debugText, Does.Not.Contain("spawnTemplate"));
        var view = JsonNode.Parse(viewText)!.AsObject();
        Assert.That(view["clickTemplate"], Is.Null);
        Assert.That(view["clickTemplates"], Is.Null);

        var input = JsonNode.Parse(File.ReadAllText(Path.Combine(ui, "Input", "default_input.json")))!.AsObject();
        var declared = input["actions"]!.AsArray().Select(row => row!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (string key in PlayerControls)
        {
            string id = view["actions"]![key]!.GetValue<string>();
            Assert.That(declared, Does.Contain(id), key);
        }

        var runtime = Parity.S1SurfaceTruthTests.LoadRuntime("s1337");
        string[] ids = CrowdDeployment.ListTemplateIds(runtime);
        Assert.That(ids, Is.Not.Empty);
        Assert.That(ids[0], Is.EqualTo("crowd_simulation.unit.infantry.r100"));
        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void MinimalMap_WithSharedHud_Enters_AndControlsFollowDeclaredActions()
    {
        using GameEngine engine = StartMinimalWithHud(RepoRoot());
        engine.LoadMap(MapId);
        CrowdSimSession session = engine.GetService(CoreServiceKeys.CrowdSimulationSession)
            ?? throw new InvalidOperationException("没有群体会话。");
        string[] ids = CrowdDeployment.ListTemplateIds(session.Config);
        Assert.That(ids, Is.EqualTo(new[] { MinimalUnit }));

        CrowdPlayerViewHost player = CrowdHudSamplingTests.ShippedPlayerView(session);
        var panel = default(CrowdPlayerHud);
        Span<int> counts = stackalloc int[9];
        player.ReadPanel(ref panel, counts);
        Assert.That(panel.ClickTemplate, Is.EqualTo(MinimalUnit));

        var tools = new CrowdDebugTools
        {
            BoomRadiusCm = 12500,
            RingScale = 2,
            Impulse = 4,
            SpawnCount = 24,
            PlaceEntry = "building",
            SpeedStepsPermille = new[] { 500, 1000, 2000, 4000 },
        };
        tools.ResolveSpawn(session.Config);
        var (unitType, radius) = CrowdDeployment.LocateTemplate(session.Config, MinimalUnit);
        Assert.That(tools.ResolvedUnitType, Is.EqualTo(unitType));
        Assert.That(tools.ResolvedRadiusClass, Is.EqualTo(radius));

        var seats = new ClientLocalSeatRegistry();
        seats.Add(new ClientLocalSeat("seat.0"));
        engine.SetService(CoreServiceKeys.ClientLocalSeatRegistry, seats);
        engine.SetService(CoreServiceKeys.InputHandler, Handler(Array.Empty<string>()));

        var feed = CrowdHudAllocTests.DeployFeed(new S4DeployDemoRuntime(null), () => session);
        feed.UsePlayerView(player);
        feed.UseActions(new CrowdHudActions(
            CrowdHudAllocTests.DeployPresentation().Deploy!.Actions,
            Array.Empty<string>(),
            debugView: null,
            player.ViewConfig.Actions));

        using var ui = new CrowdHudTestHost(engine);
        ui.FrameAfterAllPeriods(feed);
        foreach (string id in ControlIds)
            Assert.That(ui.Find(id)!.HasClass("nf-hidden"), Is.True, id);

        engine.SetService(CoreServiceKeys.InputHandler, Handler(DeclaredIds(RepoRoot())));
        ui.FrameAfterAllPeriods(feed);
        foreach (string id in ControlIds)
            Assert.That(ui.Find(id)!.HasClass("nf-hidden"), Is.False, id);
        Assert.That(ui.Find("crowd-formations")!.Children, Is.Not.Empty);

        Assert.DoesNotThrow(() => ui.Click("crowd-los"));
        Assert.DoesNotThrow(() => ui.Click("crowd-optimistic"));
        Assert.DoesNotThrow(() => ui.Click("crowd-pv-route"));
        Assert.DoesNotThrow(() => ui.Click("crowd-dock-tab-tests"));
        ui.Frame(feed);
        Assert.That(ui.Visible("crowd-scene-outside"), Is.True);
        Assert.That(ui.Visible("crowd-scene-behind"), Is.True);
        Assert.DoesNotThrow(() => ui.Click("crowd-scene-outside"));
        Assert.DoesNotThrow(() => ui.Click("crowd-scene-behind"));
    }

    private static string[] DeclaredIds(string repo)
    {
        string path = Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationUiMod", "assets", "Input", "default_input.json");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        InputConfigRoot config = JsonSerializer.Deserialize<InputConfigRoot>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException("输入表为空。");
        return config.Actions.Select(action => action.Id).ToArray();
    }

    private static PlayerInputHandler Handler(IReadOnlyList<string> actionIds)
    {
        var config = new InputConfigRoot();
        for (int i = 0; i < actionIds.Count; i++)
        {
            config.Actions.Add(new InputActionDef { Id = actionIds[i], Name = actionIds[i], Type = InputActionType.Button });
        }

        return new PlayerInputHandler(new EmptyInputBackend(), config);
    }

    private static GameEngine StartMinimalWithHud(string repo)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(new List<string>
        {
            Path.Combine(repo, "mods", "LudotsCoreMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationStandardMod"),
            Path.Combine(repo, "mods", "capabilities", "navigation", "CrowdSimulationUiMod"),
            Path.Combine(repo, "mods", "showcases", "crowd_simulation", Minimal),
        }, Path.Combine(repo, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubView());
        engine.Start();
        return engine;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("找不到仓库根。");
    }

    private sealed class StubView : IViewController
    {
        public Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }
}
