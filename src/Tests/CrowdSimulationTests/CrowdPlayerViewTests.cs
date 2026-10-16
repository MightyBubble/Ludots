using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.EntityCollections;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using CrowdSimulationUiMod.UI;
using NUnit.Framework;
using CrowdSimulationTests.Parity;

namespace CrowdSimulationTests;

public sealed class CrowdPlayerViewTests
{
    [Test]
    public void PlayerFogScene_ClustersEnemiesAndLeavesHideFixtureAlone()
    {
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        string mod = Path.Combine(root, "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod", "assets", "CrowdSimulation");
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(mod, "regress", "fog-acceptance.json")))!.AsObject();
        var scene = JsonNode.Parse(File.ReadAllText(Path.Combine(mod, "player-fog-check.json")))!.AsObject();
        Assert.That(fixture["spawnCount"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(scene["spawnCount"]!.GetValue<int>(), Is.EqualTo(5));
        Assert.That(scene["enemyXCm"]!.GetValue<int>(), Is.EqualTo(fixture["enemyXCm"]!.GetValue<int>()));
        Assert.That(scene["viewerXCm"]!.GetValue<int>(), Is.EqualTo(fixture["viewerXCm"]!.GetValue<int>()));
        int span = (scene["spawnCount"]!.GetValue<int>() - 1) * scene["clusterSpacingCm"]!.GetValue<int>();
        int behindSpan = (scene["spawnCount"]!.GetValue<int>() - 1) * scene["behindClusterSpacingCm"]!.GetValue<int>();
        Assert.That(span, Is.LessThan(25000), "一排敌方必须落在同一格迷雾里，遮挡才是整排一起消失。");
        Assert.That(behindSpan, Is.LessThan(6250), "墙后一排必须落在同一格导航里，可见性才不会只剩一个。");
        // 顶栏大约盖住画面上方 230 行，状态卡盖住左侧。机位东移、南移，色环才落在这两块以外。
        Assert.That(scene["outsideLookXCm"]!.GetValue<int>() - scene["enemyXCm"]!.GetValue<int>(), Is.EqualTo(4000));
        Assert.That(scene["enemyYCm"]!.GetValue<int>() - scene["outsideLookYCm"]!.GetValue<int>(), Is.EqualTo(10000));
        Assert.That(scene["outsideDistanceCm"]!.GetValue<int>(), Is.EqualTo(28000));
        Assert.That(scene["wallSizeCm"]!.GetValue<int>(), Is.EqualTo(8000));
        Assert.That(scene["behindFromCellWestCm"]!.GetValue<int>(), Is.EqualTo(4000));
        Assert.That(scene["behindLookXCm"]!.GetValue<int>(), Is.EqualTo(529000));
        Assert.That(scene["behindDistanceCm"]!.GetValue<int>(), Is.EqualTo(31000));
        Assert.That(scene["template"]!.GetValue<string>(), Is.EqualTo(fixture["template"]!.GetValue<string>()));
        scene["unitType"] = 0;
        using var doc = System.Text.Json.JsonDocument.Parse(scene.ToJsonString());
        var rejected = Assert.Throws<InvalidOperationException>(() => CrowdFogCheckScene.Load(doc.RootElement, "player-fog-check.json"));
        Assert.That(rejected!.Message, Does.Contain("unitType"));
    }

    [Test]
    public void OrderMode_UsesPadAndSpreadThresholds()
    {
        var pad = Fix64.FromInt(10);
        var spread = Fix64.FromInt(100);
        Assert.That(Choose(0, 0, 100, 0, 50, 0, pad, spread, false, false), Is.EqualTo(CrowdOrderMode.Converge));
        Assert.That(Choose(0, 0, 100, 0, 200, 0, pad, spread, false, false), Is.EqualTo(CrowdOrderMode.Preserve));
        Assert.That(Choose(0, 0, 300, 0, 400, 0, pad, spread, false, false), Is.EqualTo(CrowdOrderMode.Formation));
        Assert.That(Choose(0, 0, 100, 0, 50, 0, pad, spread, true, false), Is.EqualTo(CrowdOrderMode.Formation));
        Assert.That(Choose(0, 0, 100, 0, 50, 0, pad, spread, false, true), Is.EqualTo(CrowdOrderMode.Formation));
    }

    [Test]
    public void FormationDrag_MidpointWidthAndFacing()
    {
        CrowdFormationDrag.FromLine(0, 0, 0, 100, out float midX, out float midY, out int width, out float faceX, out float faceY);
        Assert.That(midX, Is.EqualTo(0f).Within(0.01f));
        Assert.That(midY, Is.EqualTo(50f).Within(0.01f));
        Assert.That(width, Is.EqualTo(100));
        Assert.That(faceX, Is.EqualTo(1f).Within(0.001f));
        Assert.That(faceY, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void StatusCard_ClassifiesPlanningMovingArrivedAndPartial()
    {
        Assert.That(CrowdCommandStatusCard.Classify(0, 0, 0, 0), Is.EqualTo(CrowdCommandCardState.None));
        Assert.That(CrowdCommandStatusCard.Classify(3, 0, 0, 0), Is.EqualTo(CrowdCommandCardState.Planning));
        Assert.That(CrowdCommandStatusCard.Classify(1, 2, 0, 0), Is.EqualTo(CrowdCommandCardState.Moving));
        Assert.That(CrowdCommandStatusCard.Classify(0, 4, 0, 0), Is.EqualTo(CrowdCommandCardState.Moving));
        Assert.That(CrowdCommandStatusCard.Classify(0, 0, 4, 0), Is.EqualTo(CrowdCommandCardState.Arrived));
        Assert.That(CrowdCommandStatusCard.Classify(0, 1, 1, 0), Is.EqualTo(CrowdCommandCardState.Partial));
        Assert.That(CrowdCommandStatusCard.Classify(0, 0, 0, 2), Is.EqualTo(CrowdCommandCardState.Partial));
    }

    [Test]
    public void TeamSwitch_ClearsBothSelections()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var reps = InstallReps(session);
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 2, 1, 0, 0), Is.EqualTo(2));
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(500000), Fix64.FromInt(560000), 2, 2, 0, 0), Is.EqualTo(2));
        session.Presentation.CollectionApplier!.Apply(
            reps[1], session.Presentation.SelectedCollectionKeyId, CollectionWriteOp.Replace, new[] { session.Units.EntityAt(0) });
        session.Presentation.CollectionApplier.Apply(
            reps[2], session.Presentation.SelectedCollectionKeyId, CollectionWriteOp.Replace, new[] { session.Units.EntityAt(2) });
        CrowdPlayerSelection.ClearForSwitch(session, 1, 2);
        Assert.That(session.SelectedCollectionCount(1), Is.EqualTo(0));
        Assert.That(session.SelectedCollectionCount(2), Is.EqualTo(0));
    }

    [Test]
    public void LayerScope_HidesUnitsFromLaterOwnHandleCopies()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        const int tag = 3;
        session.Presentation.HiddenTemplateTagId = tag;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 3, 1, 0, 0), Is.EqualTo(3));
        var buffer = new uint[session.Units.Capacity];
        for (int i = 0; i < session.Units.Count; i++)
        {
            CrowdPlayerLayerScope.Apply(session.World, session.Units.EntityAt(i), tag, CrowdPlayerLayerScope.Included(false, true));
        }

        Assert.That(session.Units.CopyOwnHandles(1, buffer), Is.EqualTo(0));
        for (int i = 0; i < session.Units.Count; i++)
        {
            CrowdPlayerLayerScope.Apply(session.World, session.Units.EntityAt(i), tag, CrowdPlayerLayerScope.Included(true, true));
        }

        Assert.That(session.Units.CopyOwnHandles(1, buffer), Is.EqualTo(3));
    }

    [Test]
    public void FogPredicate_HidesUnseenEnemyAndUnknownStructure()
    {
        var (runtime, session) = S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        var fog = session.Fog!;
        int viewer = 1;
        int ally = 3;
        int enemy = 2;
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(487500), Fix64.FromInt(562500), 1, viewer, 0, 0), Is.EqualTo(1));
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(487500), Fix64.FromInt(562500), 1, ally, 0, 0), Is.EqualTo(1));
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(387500), Fix64.FromInt(562500), 1, enemy, 0, 0), Is.EqualTo(1));
        session.BlockOnDueReplies = true;
        Assert.That(session.Step(out _), Is.True);
        int viewerGroup = fog.GroupOf[session.Config.PairRules.IndexByPlayerId[viewer]];
        Assert.That(fog.GroupOf[session.Config.PairRules.IndexByPlayerId[ally]], Is.EqualTo(viewerGroup));
        Assert.That(CrowdPlayerVisibility.UnitShown(session, viewerGroup, 2, hideFog: false), Is.True);
        Assert.That(CrowdPlayerVisibility.UnitShown(session, viewerGroup, 2, hideFog: true), Is.False);
        Assert.That(CrowdPlayerVisibility.UnitShown(session, viewerGroup, 1, hideFog: true), Is.True);
        Assert.That(CrowdPlayerVisibility.StructureShown(session, viewerGroup, 909001, hideFog: false), Is.True);
        Assert.That(CrowdPlayerVisibility.StructureShown(session, viewerGroup, 909001, hideFog: true), Is.False);
        Assert.That(CrowdPlayerVisibility.PointShown(session, viewerGroup, 387500, 562500, hideFog: false), Is.True);
        Assert.That(CrowdPlayerVisibility.PointShown(session, viewerGroup, 387500, 562500, hideFog: true), Is.False);
    }

    [Test]
    public void Palette_RejectsMissingUnknownAndOutOfRange()
    {
        var valid = ViewJson();
        Assert.That(CrowdPlayerViewConfig.Load(valid, "player-view"), Is.Not.Null);
        var missing = (JsonObject)valid.DeepClone();
        missing.Remove("magicBox");
        Assert.Throws<InvalidOperationException>(() => CrowdPlayerViewConfig.Load(missing, "player-view"));
        var unknown = (JsonObject)valid.DeepClone();
        unknown["extra"] = 1;
        Assert.Throws<InvalidOperationException>(() => CrowdPlayerViewConfig.Load(unknown, "player-view"));
        var range = (JsonObject)valid.DeepClone();
        range["colors"]!["plan"]!["red"] = 256;
        Assert.Throws<InvalidOperationException>(() => CrowdPlayerViewConfig.Load(range, "player-view"));
        var missingLabel = (JsonObject)valid.DeepClone();
        missingLabel["labels"]!.AsObject().Remove("countPlanning");
        Assert.Throws<InvalidOperationException>(() => CrowdPlayerViewConfig.Load(missingLabel, "player-view"));
        var unknownLabel = (JsonObject)valid.DeepClone();
        unknownLabel["labels"]!.AsObject()["extra"] = "x";
        Assert.Throws<InvalidOperationException>(() => CrowdPlayerViewConfig.Load(unknownLabel, "player-view"));
    }

    [Test]
    public void ClickTemplate_UnknownKeyOutsideListAndMissingCatalog_AreRejected()
    {
        var oldKey = ViewJson();
        oldKey["clickUnitType"] = 0;
        var key = Assert.Throws<InvalidOperationException>(() => CrowdPlayerViewConfig.Load(oldKey, "player-view"));
        Assert.That(key!.Message, Does.Contain("clickUnitType"));

        var outside = ViewJson();
        outside["clickTemplate"] = "crowd_simulation.unit.navy.r100";
        var list = Assert.Throws<InvalidOperationException>(() => CrowdPlayerViewConfig.Load(outside, "player-view"));
        Assert.That(list!.Message, Does.Contain("clickTemplate"));

        var missing = ViewJson();
        missing["clickTemplate"] = "missing.unit";
        missing["clickTemplates"] = new JsonArray("missing.unit");
        var host = new CrowdPlayerViewHost(null, CrowdPlayerViewConfig.Load(missing, "player-view"));
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        var catalog = Assert.Throws<InvalidOperationException>(() => host.Bind(session));
        Assert.That(catalog!.Message, Does.Contain("missing.unit"));
    }

    [Test]
    public void StatusCard_LabelsEveryCount()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 4, 1, 0, 0), Is.EqualTo(4));
        var host = new CrowdPlayerViewHost(null, CrowdPlayerViewConfig.Load(ViewJson(), "player-view"));
        host.Bind(session);
        var routes = new RouteVisualBuffer();
        host.Draw(session, routes, new ScreenOverlayBuffer());
        var hud = default(CrowdPlayerHud);
        var units = new int[9];
        host.ReadPanel(ref hud, units);
        var labels = host.Labels;
        var texts = new System.Collections.Generic.List<string>
        {
            Write(b => CrowdHudFormat.TeamLine(b, in hud, units, labels)),
            Write(b => CrowdHudFormat.CardLine(b, in hud, labels)),
            Write(b => CrowdHudFormat.ModeLine(b, in hud, labels)),
            Write(b => CrowdHudFormat.ClickLine(b, in hud, labels)),
            Write(b => CrowdHudFormat.DeployLine(b, in hud, labels)),
        };

        Assert.That(texts, Does.Contain("idle · countPlanning 0 · countMoving 0 · arrived 0 · countOther 0"));
        Assert.That(texts, Does.Contain("clickSpawn unitType infantry.r100 · count 8"));
        Assert.That(texts, Has.Some.StartsWith("deploy 24 · radius infantry.r100-"));
        Assert.That(texts, Has.Some.Contains(" · layer foot"));
        Assert.That(texts, Has.Some.Contains("self"));
        Assert.That(string.Join("\n", texts), Does.Not.Contain("*"));
    }

    [Test]
    public void SteadyPresentationFrame_AllocatesNothing()
    {
        var (_, session) = S4DeployTruthTests.BuildSession("s1337");
        Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(480000), Fix64.FromInt(560000), 4, 1, 0, 0), Is.EqualTo(4));
        var host = new CrowdPlayerViewHost(null, CrowdPlayerViewConfig.Load(ViewJson(), "player-view"));
        host.Bind(session);
        var routes = new RouteVisualBuffer();
        var overlay = new ScreenOverlayBuffer();
        for (int i = 0; i < 32; i++)
        {
            routes.BeginFrame();
            overlay.Clear();
            host.Draw(session, routes, overlay);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            routes.BeginFrame();
            overlay.Clear();
            host.Draw(session, routes, overlay);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0L), $"稳态呈现分配了 {allocated} 字节");
    }

    [Test]
    public void AlphaHold_PinsInterpolationAtCurrent()
    {
        using var world = World.Create();
        var source = new AlphaSource(0.25f);
        var hold = new AlphaHold(false);
        var system = new PresentationFrameSetupSystem(world, new RealtimePacemaker(), () => source, () => hold);
        system.Initialize();
        system.Update(0.016f);
        Assert.That(ReadAlpha(world), Is.EqualTo(0.25f).Within(0.0001f));
        hold.Hold = true;
        system.Update(0.016f);
        Assert.That(ReadAlpha(world), Is.EqualTo(1f).Within(0.0001f));
    }

    private static string Write(Action<Ludots.UI.Runtime.UiTextBuffer> write)
    {
        var buffer = new Ludots.UI.Runtime.UiTextBuffer();
        write(buffer);
        return buffer.ToString();
    }

    private static CrowdOrderMode Choose(
        int minX, int minY, int maxX, int maxY, int clickX, int clickY, Fix64 pad, Fix64 spread, bool drag, bool autoOff)
        => CrowdOrderModeChooser.Choose(
            Fix64.FromInt(minX), Fix64.FromInt(minY), Fix64.FromInt(maxX), Fix64.FromInt(maxY),
            Fix64.FromInt(clickX), Fix64.FromInt(clickY), pad, spread, drag, autoOff);

    private static Entity[] InstallReps(CrowdSimSession session)
    {
        var applier = new CollectionApplier(session.World, session.Presentation.Collections);
        session.Presentation.CollectionApplier = applier;
        var reps = new Entity[session.Config.PairRules.PlayerCount + 1];
        reps[1] = session.World.Create(new Name { Value = "player-1" });
        reps[2] = session.World.Create(new Name { Value = "player-2" });
        session.Presentation.PlayerReps = reps;
        return reps;
    }

    private static float ReadAlpha(World world)
    {
        float alpha = -1f;
        var query = new QueryDescription().WithAll<PresentationFrameState>();
        world.Query(in query, (Entity _, ref PresentationFrameState state) => alpha = state.InterpolationAlpha);
        return alpha;
    }

    internal static JsonObject ViewJson()
    {
        var actions = new JsonObject();
        foreach (var name in new[]
        {
            "nextTeam", "selectAll", "clearSelection", "toggleLayer", "cycleLayer", "toggleFog",
            "sceneOutside", "sceneBehind", "toggleLos", "toggleRoute", "deployCountDown", "deployCountUp",
            "cycleClickTemplate", "cycleClickRadius", "toggleMagicBox", "cycleShape", "clickCountDown",
            "clickCountUp", "cycleRadiusRange", "boxSelectBegin", "secondaryHold", "formationCommit", "primaryTap",
        })
        {
            actions[name] = name;
        }

        var labels = new JsonObject();
        foreach (var name in new[]
        {
            "planning", "moving", "arrived", "partial", "idle", "magicOn", "magicOff", "formation", "converge",
            "preserve", "fogOn", "fogOff", "routeOn", "routeOff", "losOn", "losOff", "clickSpawn",
            "sceneOutside", "sceneBehind", "countPlanning", "countMoving", "countOther",
            "unitType", "radius", "count", "deploy", "layer", "self",
        })
        {
            labels[name] = name;
        }

        var colors = new JsonObject();
        foreach (var name in new[]
        {
            "plan", "ink", "paper", "selected", "terrainLow", "terrainHigh", "terrainShade", "goal", "ripple", "enemyMark",
        })
        {
            colors[name] = new JsonObject { ["red"] = 10, ["green"] = 20, ["blue"] = 30, ["alpha"] = 40 };
        }

        return new JsonObject
        {
            ["magicBox"] = true,
            ["fogHide"] = false,
            ["playerRoute"] = true,
            ["layersStartEnabled"] = true,
            ["deployCount"] = 24,
            ["deployCountStep"] = 4,
            ["deployCountMin"] = 1,
            ["deployCountMax"] = 500,
            ["clickCount"] = 8,
            ["clickCountStep"] = 1,
            ["clickCountMin"] = 1,
            ["clickCountMax"] = 64,
            ["clickTemplate"] = "crowd_simulation.unit.infantry.r100",
            ["clickTemplates"] = new JsonArray(
                "crowd_simulation.unit.infantry.r100",
                "crowd_simulation.unit.infantry.r200"),
            ["rippleMs"] = 700,
            ["rippleRadiusCm"] = 6000,
            ["dashCm"] = 8000,
            ["gapCm"] = 4000,
            ["maxDashes"] = 256,
            ["routeThicknessCm"] = 300,
            ["markerRadiusCm"] = 2000,
            ["crossArmCm"] = 3000,
            ["killRingScale"] = 2,
            ["killImpulse"] = 0,
            ["enemyMarkRadiusCm"] = 2400,
            ["hillshadeStrength"] = 1.2,
            ["maxBeliefMarkers"] = 4096,
            ["cameraId"] = "cam",
            ["seatId"] = "seat.0",
            ["boxingContextId"] = "boxing",
            ["fogSceneUri"] = "mod:asset",
            ["defaultShapeId"] = "box",
            ["shapeLabels"] = new JsonObject
            {
                ["box"] = "方阵",
                ["line"] = "横队",
                ["wedge"] = "楔形",
                ["loose"] = "松散",
            },
            ["actions"] = actions,
            ["labels"] = labels,
            ["colors"] = colors,
        };
    }

    private sealed class AlphaSource : IPresentationInterpolationSource
    {
        public AlphaSource(float alpha) => InterpolationAlpha = alpha;
        public float InterpolationAlpha { get; }
    }

    private sealed class AlphaHold : IPresentationAlphaHold
    {
        public AlphaHold(bool hold) => Hold = hold;
        public bool Hold { get; set; }
        public bool HoldAtCurrent => Hold;
    }
}
