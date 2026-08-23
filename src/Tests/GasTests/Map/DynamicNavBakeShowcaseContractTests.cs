using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DynamicNavBakeShowcaseMod;
using DynamicNavBakeShowcaseMod.Runtime;
using DynamicNavBakeShowcaseMod.UI;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Map;

[TestFixture]
public sealed class DynamicNavBakeShowcaseContractTests
{
    private static readonly ShowcaseSceneContract[] ShowcaseScenes =
    {
        new(
            DynamicNavBakeShowcaseIds.RtsMapId,
            "NavBakeDynamicRtsShowcaseMod",
            "mods/showcases/nav_bake/NavBakeDynamicRtsShowcaseMod",
            isHex: false,
            isOpenWorld: false),
        new(
            DynamicNavBakeShowcaseIds.RtsHexMapId,
            "NavBakeDynamicRtsHexShowcaseMod",
            "mods/showcases/nav_bake/NavBakeDynamicRtsHexShowcaseMod",
            isHex: true,
            isOpenWorld: false),
        new(
            DynamicNavBakeShowcaseIds.OpenWorldMapId,
            "NavBakeOpenWorld64x64ShowcaseMod",
            "mods/showcases/nav_bake/NavBakeOpenWorld64x64ShowcaseMod",
            isHex: false,
            isOpenWorld: true),
        new(
            DynamicNavBakeShowcaseIds.OpenWorldHexMapId,
            "NavBakeOpenWorld64x64HexShowcaseMod",
            "mods/showcases/nav_bake/NavBakeOpenWorld64x64HexShowcaseMod",
            isHex: true,
            isOpenWorld: true),
    };

    private static IEnumerable<TestCaseData> ShowcaseSceneCases()
    {
        foreach (ShowcaseSceneContract scene in ShowcaseScenes)
        {
            yield return new TestCaseData(scene).SetName($"ShowcaseMap_AuthoringContract_{scene.MapId}");
        }
    }

    private static IEnumerable<TestCaseData> PathingSceneCases()
    {
        foreach (ShowcaseSceneContract scene in ShowcaseScenes)
        {
            yield return new TestCaseData(scene).SetName($"ShowcaseScene_Pathing_{scene.MapId}");
        }
    }

    [Test]
    public void PanelController_MountOrSync_WithUiRootButMissingSurfaceHost_ThrowsPathSpecificError()
    {
        using var engine = new GameEngine();
        var uiRoot = new UIRoot(new SkiaUiRenderer());
        uiRoot.Resize(1280f, 720f);
        engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

        var runtime = new DynamicNavBakeShowcaseRuntime();
        var controller = new DynamicNavBakeShowcasePanelController(runtime);

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => controller.MountOrSync(uiRoot, engine, DynamicNavBakeShowcasePanelState.Empty));
        Assert.That(ex!.Message, Does.Contain("UiSurfaceHost"));
        Assert.That(ex.Message, Does.Contain("UIRoot").IgnoreCase);
    }

    [Test]
    public void ShowcaseConfig_RequiresBenchmarkSectionWithValidatedGates()
    {
        DynamicNavBakeShowcaseConfig rts = LoadSceneConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseConfig open = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        foreach (ShowcaseSceneContract scene in ShowcaseScenes)
        {
            DynamicNavBakeShowcaseConfig config = LoadSceneConfig(scene.MapId);
            Assert.That(config.Benchmark, Is.Not.Null, $"{scene.MapId} must declare benchmark gates.");
            Assert.That(config.Benchmark.SampleWindowCount, Is.GreaterThan(0));
            Assert.That(config.Benchmark.PeakResidentTileCountMax, Is.EqualTo(config.ResidentWidthChunks * config.ResidentHeightChunks));
            Assert.That(config.Benchmark.DeterminismWorkerCounts, Does.Contain(1));
        }

        Assert.That(rts.Benchmark.SampleWindowCount, Is.GreaterThan(0));
        Assert.That(rts.Benchmark.FixedStepBudgetMs, Is.EqualTo(4.0));
        Assert.That(rts.Benchmark.DirtyPublishP95RatioMax, Is.EqualTo(1.15));
        Assert.That(rts.Benchmark.SteadyStateThroughputRatioMin, Is.EqualTo(0.85));
        Assert.That(open.Benchmark.FixedStepBudgetMs, Is.EqualTo(4.0));
    }

    // Feature: Dirty-wall timing comparison stays fair between the small RTS yard and the open world
    // Given both showcases author the same P95 sample window and boundary-margin contract
    // When a player compares rebuild cost after parking-wall teleports
    // Then neither scene may under-sample P95 or park walls where halo reaches the RTS map edge
    [Test]
    public void ShowcaseConfigs_AuthoredDirtyComparisonFairness_UsesMinimumP95WindowAndBoundaryMargin()
    {
        foreach (ShowcaseSceneContract scene in ShowcaseScenes)
        {
            DynamicNavBakeShowcaseConfig config = LoadSceneConfig(scene.MapId);
            Assert.That(
                config.Benchmark.SampleWindowCount,
                Is.GreaterThanOrEqualTo(DynamicNavBakeShowcaseBenchmarkConfig.MinimumP95SampleWindowCount),
                $"{scene.MapId} must keep enough samples for P95 comparison.");
            Assert.That(
                config.Benchmark.DirtyComparisonBoundaryMarginChunks,
                Is.GreaterThanOrEqualTo(scene.IsHex ? 1 : 2),
                $"{scene.MapId} must park pooled walls away from the resident boundary halo.");
        }
    }

    [Test]
    public void ShowcaseConfig_SampleWindowBelowMinimumP95_ThrowsNamingSampleWindowAndMinimum()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["benchmark"]!["sampleWindowCount"] = 19;

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("sampleWindowCount"));
        Assert.That(ex.Message, Does.Contain("MinimumP95SampleWindowCount"));
    }

    [TestCaseSource(nameof(ShowcaseSceneCases))]
    public void ShowcaseMap_AuthoringContract_MatchesSharedConfig(ShowcaseSceneContract scene)
    {
        string repoRoot = FindRepoRoot();
        AssertShowcaseRegistry(repoRoot, scene.MapId);
        string entryModRoot = Path.Combine(repoRoot, scene.ModRelativePath);
        string sharedAssetsRoot = Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets");
        JsonObject map = ReadJsonObject(Path.Combine(sharedAssetsRoot, "Maps", $"{scene.MapId}.json"));
        JsonObject config = ReadJsonObject(Path.Combine(
            sharedAssetsRoot,
            "Showcases",
            "DynamicNavBake",
            $"{scene.MapId}.json"));
        JsonObject game = ReadJsonObject(Path.Combine(entryModRoot, "assets", "game.json"));
        JsonObject sharedGame = ReadJsonObject(Path.Combine(sharedAssetsRoot, "game.json"));
        JsonArray catalog = ReadJsonArray(Path.Combine(sharedAssetsRoot, "Configs", "config_catalog.json"));
        JsonObject pathing = ReadJsonObject(Path.Combine(sharedAssetsRoot, "Configs", "Navigation", "pathing.json"));
        JsonObject inputOrderMappings = ReadJsonObject(Path.Combine(sharedAssetsRoot, "Input", "input_order_mappings.json"));
        DynamicNavBakeShowcaseConfig loaded = DynamicNavBakeShowcaseConfig.Load(config);

        Assert.That(game["startupMapId"]!.GetValue<string>(), Is.EqualTo(scene.MapId));
        AssertDefaultGameplayContext(game, $"{scene.ModRelativePath}/assets/game.json");
        AssertDefaultGameplayContext(sharedGame, "DynamicNavBakeShowcaseMod/assets/game.json");
        Assert.That(
            game["targetFps"]!.GetValue<int>(),
            Is.EqualTo(60),
            "NavBake scene game.json must explicitly set targetFps=60 so Raylib FixedSteps keep pace with the auto timeline.");
        Assert.That(map["Id"]!.GetValue<string>(), Is.EqualTo(scene.MapId));
        Assert.That(config["mapId"]!.GetValue<string>(), Is.EqualTo(scene.MapId));
        Assert.That(config["squad"]!["profileId"]!.GetValue<string>(), Is.EqualTo("light"));
        Assert.That(map["Tags"]!.AsArray().ToJsonString(), Does.Contain(MapTags.FeatureNavMeshOn.Name));
        Assert.That(map["DefaultCamera"]!["VirtualCameraId"]!.GetValue<string>(), Is.EqualTo("Camera.Profile.Tactical"));
        Assert.That(map["DefaultCamera"]!["TargetXCm"]!.GetValue<int>(), Is.EqualTo(loaded.CameraTargetXCm));
        Assert.That(map["DefaultCamera"]!["TargetYCm"]!.GetValue<int>(), Is.EqualTo(loaded.CameraTargetYCm));
        AssertBoardContract(sharedAssetsRoot, map, loaded, scene);
        AssertNavMeshConfigContract(sharedAssetsRoot, catalog, loaded, scene);
        AssertReliefGroundAssetCoversMap(sharedAssetsRoot, map, config);
        AssertMassNavigationRightClickMapping(inputOrderMappings);
        AssertCommandIntentRoutesMassNavigationMove(sharedAssetsRoot);
        AssertCatalogDeclaresPath(catalog, $"Showcases/DynamicNavBake/{scene.MapId}.json", "Replace");
        AssertCatalogDeclaresPath(catalog, $"Navigation/navmesh.{scene.MapId}.json", "Replace");
        AssertCatalogDeclaresPath(catalog, "Navigation/pathing.json", "DeepObject");
        AssertCatalogDeclaresPath(catalog, "Input/command_intent_profiles.json", "DeepObject");
        AssertCatalogDeclaresPath(catalog, "Input/input_order_mappings.json", "DeepObject");
        AssertBuildingFootprintMatchesGateConfig(sharedAssetsRoot, config);
        AssertHumanoidMapsToLightPreferMesh(pathing);

        Assert.That(loaded.WidthChunks, Is.GreaterThan(0));
        Assert.That(loaded.ResidentWidthChunks, Is.EqualTo(scene.ExpectedResidentChunks));
        Assert.That(loaded.ResidentHeightChunks, Is.EqualTo(scene.ExpectedResidentChunks));
        Assert.That(loaded.RaylibAutoTimeline.CameraTargetToleranceCm, Is.EqualTo(scene.IsHex ? 500 : 250));
        Assert.That(loaded.RaylibAutoTimeline.PlayerFraming, Is.Not.Null);
        Assert.That(loaded.RaylibAutoTimeline.PlayerFraming.MinSquadMembersOnScreen, Is.GreaterThan(0));
        Assert.That(loaded.RaylibAutoTimeline.PlayerFraming.AspectRatio, Is.EqualTo(1.7777778f).Within(0.0001f));
    }

    // Feature: A new player can control the Dynamic NavBake lab with ordinary RTS input
    // Given either showcase map is opened from its launcher preset
    // When the player uses WASD, drags a selection box, aims at the ground, and right-clicks
    // Then the formal gameplay context, ground surface, and MassNavigation move mapping are all mounted
    [Test]
    public void ShowcasePlayerInput_UsesFormalRtsControlPathWithoutMissingGroundOrLocalOrderSource()
    {
        string repoRoot = FindRepoRoot();
        string modRoot = Path.Combine(repoRoot, "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod");
        string entrySource = File.ReadAllText(Path.Combine(modRoot, "DynamicNavBakeShowcaseModEntry.cs"));
        string systemSource = File.ReadAllText(Path.Combine(modRoot, "Systems", "DynamicNavBakeShowcaseLocalOrderSourceSystem.cs"));

        Assert.That(entrySource, Does.Contain("DynamicNavBakeShowcaseLocalOrderSourceSystem"));
        Assert.That(entrySource, Does.Contain("SystemGroup.InputCollection"));
        Assert.That(systemSource, Does.Contain("LocalOrderSourceHelper"));
        Assert.That(systemSource, Does.Contain("TryCreateMapping"));
        Assert.That(systemSource, Does.Contain("TrySetLocalPlayer"));
    }

    [Test]
    public void SharedMod_AutoCaptureCamera_IsLockedOrbitWithoutUserInput()
    {
        string repoRoot = FindRepoRoot();
        JsonArray array = ReadJsonArray(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Configs/Camera/virtual_cameras.json"));
        Assert.That(array.Count, Is.EqualTo(1));
        JsonObject camera = array[0]!.AsObject();
        Assert.That(camera["id"]!.GetValue<string>(), Is.EqualTo(DynamicNavBakeShowcaseIds.AutoCaptureCameraId));
        Assert.That(camera["panMode"]!.GetValue<string>(), Is.EqualTo("None"));
        Assert.That(camera["allowUserInput"]!.GetValue<bool>(), Is.False);

        JsonArray catalog = ReadJsonArray(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Configs/config_catalog.json"));
        AssertCatalogDeclaresPath(catalog, "Camera/virtual_cameras.json", "ArrayById");
        AssertCatalogDeclaresPath(catalog, "Presentation/performers.json", "ArrayById");
    }

    // Feature: Player sees the sealed gate wall, the local march line, and the open-world corridor ribbon
    // Given the shared Dynamic NavBake presentation catalog
    // When performers.json is authored for wall mesh + local/corridor WorldSpline rules
    // Then bootstrap/create/set/destroy rules are complete and colors/widths stay visibly distinct
    [Test]
    public void SharedMod_Performers_RegisterWallMeshAndDistinctPathWorldSplineRules()
    {
        string repoRoot = FindRepoRoot();
        JsonArray performers = ReadJsonArray(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Presentation/performers.json"));

        JsonObject? wallBootstrap = FindPerformer(performers, "dynamic_nav_bake_wall_segment_bootstrap");
        JsonObject? wallVisual = FindPerformer(performers, "dynamic_nav_bake_wall_segment");
        JsonObject? localPath = FindPerformer(performers, "dynamic_nav_bake.local_path");
        JsonObject? corridorPath = FindPerformer(performers, "dynamic_nav_bake.corridor_path");
        JsonObject? worldFacts = FindPerformer(performers, "dynamic_nav_bake.world_fact_rules");
        Assert.That(wallBootstrap, Is.Not.Null);
        Assert.That(wallVisual, Is.Not.Null);
        Assert.That(localPath, Is.Not.Null);
        Assert.That(corridorPath, Is.Not.Null);
        Assert.That(worldFacts, Is.Not.Null);

        AssertBootstrapHasSpawnAndDestroy(wallBootstrap!, "dynamic_nav_bake_wall_segment");
        AssertMeshIsMovableEveryFrame(wallVisual!);
        AssertWorldSplineAssetHasFillWidthBorder(localPath!);
        AssertWorldSplineAssetHasFillWidthBorder(corridorPath!);
        AssertWorldFactRulesComplete(worldFacts!, "dynamic_nav_bake.local_path");
        AssertWorldFactRulesComplete(worldFacts!, "dynamic_nav_bake.corridor_path");

        float localWidth = localPath!["paramDefaults"]!.AsArray()
            .First(node => node!["paramKey"]!.GetValue<string>() == "worldSpline.width")!
            ["floatValue"]!.GetValue<float>();
        float corridorWidth = corridorPath!["paramDefaults"]!.AsArray()
            .First(node => node!["paramKey"]!.GetValue<string>() == "worldSpline.width")!
            ["floatValue"]!.GetValue<float>();
        Assert.That(corridorWidth, Is.GreaterThan(localWidth),
            "Corridor ribbon must read thicker than the local march line.");
    }

    [Test]
    public void OpenWorld_AutoCaptureMinimapRect_IsRequiredAndFitsCapture()
    {
        DynamicNavBakeShowcaseConfig open = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        Assert.That(open.OpenWorld, Is.Not.Null);
        Assert.That(open.OpenWorld!.AutoCaptureMinimapRect, Is.Not.Null);
        DynamicNavBakeShowcaseMinimapRectConfig rect = open.OpenWorld.AutoCaptureMinimapRect;
        DynamicNavBakeShowcasePlayerFramingConfig framing = open.RaylibAutoTimeline.PlayerFraming;
        Assert.That(rect.Width, Is.EqualTo(300));
        Assert.That(rect.Height, Is.EqualTo(420));
        Assert.That(rect.X + rect.Width, Is.LessThanOrEqualTo(framing.CaptureWidthPx));
        Assert.That(rect.Y + rect.Height, Is.LessThanOrEqualTo(framing.CaptureHeightPx));
        Assert.That(rect.Y, Is.LessThanOrEqualTo(framing.SafeInsetTopPx),
            "Auto-capture minimap rect must sit in the top-right capture corner, not mid-screen.");
        Assert.That(rect.X, Is.GreaterThanOrEqualTo(framing.CaptureWidthPx - rect.Width - framing.SafeInsetRightPx - 8),
            "Auto-capture minimap rect must sit in the top-right capture corner.");
        Assert.That(framing.SafeInsetRightPx, Is.EqualTo(340),
            "Open-world safe inset must keep framing clear of the x=956 auto-capture minimap.");
        Assert.That(open.RaylibAutoTimeline.PlayerFraming.MaxDistanceCm, Is.LessThanOrEqualTo(22000f));
        Assert.That(open.RaylibAutoTimeline.PlayerFraming.MinProjectedSquadSpanPx, Is.GreaterThan(0f));
        Assert.That(open.RaylibAutoTimeline.PlayerFraming.PathLookaheadCm, Is.GreaterThan(0f));
        Assert.That(open.Presentation.LocalPathWidthMeters, Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(open.Presentation.CorridorPathWidthMeters, Is.EqualTo(0.45f).Within(0.0001f));
    }

    [Test]
    public void ShowcaseConfig_RequiresPresentationSectionWithStrictPositiveWidths()
    {
        DynamicNavBakeShowcaseConfig rts = LoadSceneConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseConfig open = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        AssertPresentationReadable(rts.Presentation, expectedLocal: 0.55f, expectedCorridor: 0.35f);
        AssertPresentationReadable(open.Presentation, expectedLocal: 0.75f, expectedCorridor: 0.45f);
    }

    [Test]
    public void ShowcaseConfig_PresentationNavMeshStyle_IsRequiredAndMapsToCoreStyle()
    {
        DynamicNavBakeShowcaseConfig rts = LoadSceneConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseConfig open = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        AssertPresentationNavMeshStyle(rts.Presentation);
        AssertPresentationNavMeshStyle(open.Presentation);

        Ludots.Core.Presentation.Navigation.NavMeshPresentationStyle style = rts.Presentation.ToNavMeshStyle();
        Assert.That(style.FillColor.Red, Is.EqualTo(rts.Presentation.FillColor[0]).Within(0.0001f));
        Assert.That(style.PendingColor.Alpha, Is.EqualTo(rts.Presentation.PendingColor[3]).Within(0.0001f));
        Assert.That(style.RebuildingColor.Alpha, Is.EqualTo(rts.Presentation.RebuildingColor[3]).Within(0.0001f));
        Assert.That(style.CommittedColor.Alpha, Is.EqualTo(rts.Presentation.CommittedColor[3]).Within(0.0001f));
        Assert.That(style.DrawTileStateIndication, Is.True);
        Assert.That(style.HeightOffsetMeters, Is.EqualTo(rts.Presentation.HeightOffsetMeters).Within(0.0001f));
    }

    [Test]
    public void ShowcaseConfig_PresentationMissingNavMeshStyleProperty_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["presentation"]!.AsObject().Remove("rebuildingColor");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("rebuildingColor"));
    }

    [Test]
    public void ShowcaseConfig_PresentationInvalidColorChannel_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["presentation"]!["fillColor"] = new JsonArray(0.1, 0.2, 0.3, 1.5);

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("fillColor"));
    }

    [Test]
    public void CorePresentation_StaticContract_OwnsNavMeshLifecycle_AndShowcaseOnlyConfiguresIt()
    {
        string repoRoot = FindRepoRoot();
        string modEntrySource = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/DynamicNavBakeShowcaseModEntry.cs"));
        string runtimeSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/Runtime/DynamicNavBakeShowcaseRuntime.cs"));
        string engineSource = File.ReadAllText(Path.Combine(repoRoot, "src/Core/Engine/GameEngine.cs"));

        Assert.That(engineSource, Does.Contain("new NavMeshPresentationState()"));
        Assert.That(engineSource, Does.Contain("new NavMeshPresentationBuffer("));
        Assert.That(engineSource, Does.Contain("presentationConfig.NavMeshTileCapacity"));
        Assert.That(engineSource, Does.Contain("presentationConfig.NavMeshTileStateCapacity"));
        Assert.That(engineSource, Does.Contain("SetService(CoreServiceKeys.NavMeshPresentationState, navMeshPresentationState)"));
        Assert.That(engineSource, Does.Contain("SetService(CoreServiceKeys.NavMeshPresentationBuffer, navMeshPresentationBuffer)"));
        Assert.That(engineSource, Does.Contain("RegisterPresentationSystem(new NavMeshPresentationSystem("));

        Assert.That(modEntrySource, Does.Contain("NavMeshPresentationCapabilityValidator.Require"));
        Assert.That(modEntrySource, Does.Contain("Core-owned NavMeshPresentationState"));
        Assert.That(modEntrySource, Does.Contain("Core-owned NavMeshPresentationBuffer"));
        Assert.That(modEntrySource, Does.Contain("InsertPresentationSystemBefore<PerformerRuleSystem>(showcasePresentation)"));
        Assert.That(modEntrySource, Does.Contain("UnregisterPresentationSystem(registeredPresentation)"));
        Assert.That(modEntrySource, Does.Not.Contain("new NavMeshPresentationBuffer"));
        Assert.That(modEntrySource, Does.Not.Contain("new NavMeshPresentationSystem"));
        Assert.That(modEntrySource, Does.Not.Contain("RemoveService(CoreServiceKeys.NavMeshPresentationBuffer)"));
        Assert.That(modEntrySource, Does.Not.Contain("RemoveService(CoreServiceKeys.NavMeshPresentationState)"));

        Assert.That(runtimeSource, Does.Contain("engine.GetService(CoreServiceKeys.NavMeshPresentationState)"));
        Assert.That(runtimeSource, Does.Contain("presentation.NavMeshLayer"));
        Assert.That(runtimeSource, Does.Contain("presentation.NavMeshProfile"));
        Assert.That(runtimeSource, Does.Not.Contain("new NavMeshPresentationState"));
        Assert.That(runtimeSource, Does.Not.Contain("new NavMeshPresentationBuffer"));
    }

    [Test]
    public void ShowcaseConfig_MissingPresentation_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw.Remove("presentation");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("presentation"));
    }

    [Test]
    public void ShowcaseConfig_PresentationNonPositiveWidth_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["presentation"]!["localPathWidthMeters"] = 0;

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("localPathWidthMeters"));
    }

    [Test]
    public void ShowcaseConfig_PresentationMissingProperty_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["presentation"]!.AsObject().Remove("corridorPathBorderWidthMeters");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("corridorPathBorderWidthMeters"));
    }

    private static void AssertPresentationReadable(
        DynamicNavBakeShowcasePresentationConfig presentation,
        float expectedLocal,
        float expectedCorridor)
    {
        Assert.That(presentation.PathOverlayY, Is.GreaterThan(0f));
        Assert.That(presentation.LocalPathWidthMeters, Is.EqualTo(expectedLocal).Within(0.0001f));
        Assert.That(presentation.CorridorPathWidthMeters, Is.EqualTo(expectedCorridor).Within(0.0001f));
        Assert.That(presentation.LocalPathBorderWidthMeters, Is.GreaterThan(0f));
        Assert.That(presentation.CorridorPathBorderWidthMeters, Is.GreaterThan(0f));
        AssertPresentationNavMeshStyle(presentation);
    }

    private static void AssertPresentationNavMeshStyle(DynamicNavBakeShowcasePresentationConfig presentation)
    {
        Assert.That(presentation.FillColor, Has.Length.EqualTo(4));
        Assert.That(presentation.EdgeColor, Has.Length.EqualTo(4));
        Assert.That(presentation.TileBoundsColor, Has.Length.EqualTo(4));
        Assert.That(presentation.PendingColor, Has.Length.EqualTo(4));
        Assert.That(presentation.RebuildingColor, Has.Length.EqualTo(4));
        Assert.That(presentation.CommittedColor, Has.Length.EqualTo(4));
        Assert.That(float.IsFinite(presentation.HeightOffsetMeters), Is.True);
        Assert.That(presentation.DrawFill, Is.True);
        Assert.That(presentation.DrawEdges, Is.True);
        Assert.That(presentation.DrawTileBounds, Is.True);
        Assert.That(presentation.DrawTileStateIndication, Is.True);
        Ludots.Core.Presentation.Navigation.NavMeshPresentationStyle style = presentation.ToNavMeshStyle();
        Assert.That(style.DrawTileStateIndication, Is.True);
        Assert.That(style.HeightOffsetMeters, Is.EqualTo(presentation.HeightOffsetMeters).Within(0.0001f));
    }

    [Test]
    public void ShowcaseConfig_MissingMinProjectedSquadSpanPx_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["raylibAutoTimeline"]!["playerFraming"]!.AsObject().Remove("minProjectedSquadSpanPx");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("minProjectedSquadSpanPx"));
    }

    [Test]
    public void ShowcaseConfig_MissingPathLookaheadCm_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["raylibAutoTimeline"]!["playerFraming"]!.AsObject().Remove("pathLookaheadCm");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("pathLookaheadCm"));
    }

    [Test]
    public void OpenWorld_MissingAutoCaptureMinimapRect_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json"));
        raw["openWorld"]!.AsObject().Remove("autoCaptureMinimapRect");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("autoCaptureMinimapRect"));
    }

    [Test]
    public void OpenWorld_AutoCaptureMinimapRectOutsideCapture_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json"));
        raw["openWorld"]!["autoCaptureMinimapRect"]!["x"] = 1200;
        raw["openWorld"]!["autoCaptureMinimapRect"]!["width"] = 300;

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("autoCaptureMinimapRect"));
        Assert.That(ex.Message, Does.Contain("capture").IgnoreCase);
    }

    private static JsonObject? FindPerformer(JsonArray performers, string id)
    {
        foreach (JsonNode? node in performers)
        {
            if (node is JsonObject obj &&
                string.Equals(obj["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
            {
                return obj;
            }
        }

        return null;
    }

    private static void AssertBootstrapHasSpawnAndDestroy(JsonObject bootstrap, string templateKey)
    {
        JsonArray rules = bootstrap["rules"]!.AsArray();
        bool spawned = false;
        bool destroyed = false;
        foreach (JsonNode? node in rules)
        {
            JsonObject rule = node!.AsObject();
            string kind = rule["event"]!["kind"]!.GetValue<string>();
            string key = rule["event"]!["key"]!.GetValue<string>();
            if (kind == "EntitySpawned" && key == templateKey)
            {
                spawned = true;
                Assert.That(rule["command"]!["kind"]!.GetValue<string>(), Is.EqualTo("CreatePerformer"));
            }

            if (kind == "EntityDestroyed" && key == templateKey)
            {
                destroyed = true;
                Assert.That(rule["command"]!["kind"]!.GetValue<string>(), Is.EqualTo("DestroyPerformerScope"));
            }
        }

        Assert.That(spawned, Is.True);
        Assert.That(destroyed, Is.True);
    }

    private static void AssertMeshIsMovableEveryFrame(JsonObject performer)
    {
        JsonArray behaviors = performer["behaviors"]!.AsArray();
        bool bodyOk = false;
        bool groundingOk = false;
        foreach (JsonNode? node in behaviors)
        {
            JsonObject behavior = node!.AsObject();
            string slot = behavior["slot"]!.GetValue<string>();
            if (slot == "body")
            {
                JsonObject binding = behavior["assetBinding"]!.AsObject();
                Assert.That(binding["assetId"]!.GetValue<string>(), Is.EqualTo("cube"));
                Assert.That(binding["materialId"]!.GetValue<string>(), Is.EqualTo("default_surface"));
                Assert.That(binding["mobility"]!.GetValue<string>(), Is.EqualTo("Movable"));
                bodyOk = true;
            }

            if (slot == "grounding")
            {
                Assert.That(behavior["grounding"]!["updatePolicy"]!.GetValue<string>(), Is.EqualTo("EveryFrame"));
                groundingOk = true;
            }
        }

        Assert.That(bodyOk, Is.True);
        Assert.That(groundingOk, Is.True);
    }

    private static void AssertWorldSplineAssetHasFillWidthBorder(JsonObject performer)
    {
        JsonArray defaults = performer["paramDefaults"]!.AsArray();
        bool fill = false;
        bool border = false;
        bool width = false;
        bool borderWidth = false;
        foreach (JsonNode? node in defaults)
        {
            string key = node!["paramKey"]!.GetValue<string>();
            fill |= key == "worldSpline.fill";
            border |= key == "worldSpline.border";
            width |= key == "worldSpline.width";
            borderWidth |= key == "worldSpline.border.width";
        }

        Assert.That(fill, Is.True);
        Assert.That(border, Is.True);
        Assert.That(width, Is.True);
        Assert.That(borderWidth, Is.True);
    }

    private static void AssertWorldFactRulesComplete(JsonObject worldFacts, string splineKey)
    {
        JsonArray rules = worldFacts["rules"]!.AsArray();
        bool create = false;
        bool setP0 = false;
        bool setP3 = false;
        bool setWidth = false;
        bool setBorderWidth = false;
        bool destroy = false;
        foreach (JsonNode? node in rules)
        {
            JsonObject rule = node!.AsObject();
            if (!string.Equals(rule["event"]!["key"]!.GetValue<string>(), splineKey, StringComparison.Ordinal))
            {
                continue;
            }

            string eventKind = rule["event"]!["kind"]!.GetValue<string>();
            string commandKind = rule["command"]!["kind"]!.GetValue<string>();
            string? paramKey = rule["command"]!["paramKey"]?.GetValue<string>();
            if (eventKind == "WorldSplineUpdated" && commandKind == "CreatePerformer")
            {
                create = true;
            }

            if (eventKind == "WorldSplineUpdated" && commandKind == "SetParam" && paramKey == "worldSpline.p0")
            {
                setP0 = true;
            }

            if (eventKind == "WorldSplineUpdated" && commandKind == "SetParam" && paramKey == "worldSpline.p3")
            {
                setP3 = true;
            }

            if (eventKind == "WorldSplineUpdated" && commandKind == "SetParam" && paramKey == "worldSpline.width")
            {
                setWidth = true;
            }

            if (eventKind == "WorldSplineUpdated" && commandKind == "SetParam" && paramKey == "worldSpline.border.width")
            {
                setBorderWidth = true;
            }

            if (eventKind == "WorldSplineEnded" && commandKind == "DestroyScopedPerformer")
            {
                destroy = true;
            }
        }

        Assert.That(create, Is.True, $"{splineKey} Create missing");
        Assert.That(setP0, Is.True, $"{splineKey} p0 missing");
        Assert.That(setP3, Is.True, $"{splineKey} p3 missing");
        Assert.That(setWidth, Is.True, $"{splineKey} width missing");
        Assert.That(setBorderWidth, Is.True, $"{splineKey} border width missing");
        Assert.That(destroy, Is.True, $"{splineKey} Destroy missing");
    }

    [TestCaseSource(nameof(PathingSceneCases))]
    public void ShowcaseScene_PathingConfigPipeline_ResolvesHumanoidToLightPreferMesh(
        ShowcaseSceneContract scene)
    {
        string repoRoot = FindRepoRoot();
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", Path.Combine(repoRoot, "assets"));
        vfs.Mount(
            "DynamicNavBakeShowcaseMod",
            Path.Combine(repoRoot, "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod"));
        vfs.Mount(scene.ModId, Path.Combine(repoRoot, scene.ModRelativePath));
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        modLoader.LoadedModIds.Add("DynamicNavBakeShowcaseMod");
        modLoader.LoadedModIds.Add(scene.ModId);
        var pipeline = new ConfigPipeline(vfs, modLoader);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        PathingConfig pathing = new PathingConfigLoader(pipeline).Load(catalog);

        PathingAgentTypeConfig? humanoid = null;
        for (int i = 0; i < pathing.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig agent = pathing.AgentTypes[i];
            if (string.Equals(agent.Id, "Humanoid", StringComparison.Ordinal))
            {
                humanoid = agent;
                break;
            }
        }

        Assert.That(humanoid, Is.Not.Null, "Merged PathingConfig must keep Humanoid agent type.");
        Assert.That(humanoid!.ProfileId, Is.EqualTo("light"));
        Assert.That(humanoid.Selection.Mode, Is.EqualTo(PathSelectionMode.PreferMesh));
    }

    [Test]
    public void RtsShowcase_AuthoredCoordinates_KeepOpenMarchOffCenterChunkSeams()
    {
        DynamicNavBakeShowcaseConfig config = LoadSceneConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        Assert.That(config.WidthChunks, Is.EqualTo(8));
        Assert.That(config.HeightChunks, Is.EqualTo(8));
        Assert.That(config.CameraTargetXCm, Is.EqualTo(0));
        Assert.That(config.CameraTargetYCm, Is.EqualTo(0));
        Assert.That(config.Gate.CenterXCm, Is.EqualTo(0));
        Assert.That(config.Gate.CenterYCm, Is.EqualTo(0));
        // Near-center corridor: LayeredSpan open flat emits flat-grid-baseline-v2 (editor SSOT).
        // Inset from x=0 avoids FindNearestPoly dual-tile seam snapping.
        Assert.That(config.Squad.CenterXCm, Is.EqualTo(100));
        Assert.That(config.Squad.CenterYCm, Is.EqualTo(-2000));
        Assert.That(config.Goal.XCm, Is.EqualTo(100));
        Assert.That(config.Goal.YCm, Is.EqualTo(3600));
        Assert.That(config.SideRouteWest.XCm, Is.EqualTo(-4800));
        Assert.That(config.SideRouteWest.YCm, Is.EqualTo(0));
        Assert.That(config.SideRouteEast.XCm, Is.EqualTo(4800));
        Assert.That(config.SideRouteEast.YCm, Is.EqualTo(0));
        Assert.That(config.Parking.XCm, Is.EqualTo(-11200));
        Assert.That(config.Parking.YCm, Is.EqualTo(-11200));
    }

    // Feature: Wall-pool parking keeps dirty work on equivalent interior tiles
    // Given both scenes share the same authored parking point in resident-local chunk 2
    // When a wall teleports between parking and the gate
    // Then open-world and RTS dirty the same local neighborhood away from the RTS map edge
    [Test]
    public void ShowcaseParking_MatchesSafeInteriorCoordinatesAwayFromRtsWorldBoundary()
    {
        DynamicNavBakeShowcaseConfig rts = LoadSceneConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseConfig open = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        Assert.That(rts.Parking.XCm, Is.EqualTo(-11200));
        Assert.That(rts.Parking.YCm, Is.EqualTo(-11200));
        Assert.That(open.Parking.XCm, Is.EqualTo(rts.Parking.XCm),
            "Open-world parking must match RTS parking so wall teleports dirty equivalent resident tiles.");
        Assert.That(open.Parking.YCm, Is.EqualTo(rts.Parking.YCm));
        Assert.That(open.OpenWorld!.InitialHotspotIndex, Is.EqualTo(1));
        Assert.That(open.OpenWorld.Hotspots[1].Id, Is.EqualTo("central_gate"));
    }

    [Test]
    public void OpenWorld_ParkingOutsideConfiguredDirtyComparisonInset_ThrowsNamingParkingAndMargin()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json"));
        // Former corner parking sits inside the resident window but outside the margin-2 inset.
        raw["parking"]!["xCm"] = -24000;
        raw["parking"]!["yCm"] = -24000;

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("parking"));
        Assert.That(ex.Message, Does.Contain("dirtyComparisonBoundaryMarginChunks"));
    }

    [Test]
    public void RtsShowcase_FlatGridTriangleSnapshot_IsOneHundredTwentyEight()
    {
        DynamicNavBakeShowcaseConfig config = LoadSceneConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        Assert.That(config.WidthChunks, Is.EqualTo(8));
        Assert.That(config.HeightChunks, Is.EqualTo(8));
    }

    [Test]
    public void OpenWorldShowcase_FlatGridTriangleSnapshot_IsEightThousandOneHundredNinetyTwo()
    {
        DynamicNavBakeShowcaseConfig config = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        Assert.That(config.WidthChunks, Is.EqualTo(64));
        Assert.That(config.HeightChunks, Is.EqualTo(64));
        Assert.That(config.OpenWorld, Is.Not.Null);
        Assert.That(config.OpenWorld!.Hotspots.Length, Is.GreaterThan(0));
    }

    [Test]
    public void OpenWorld_HotspotWallCenters_AreExplicitDistinctAndInsideAuthoredResidentWindows()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json"));
        JsonArray hotspots = raw["openWorld"]!["hotspots"]!.AsArray();
        Assert.That(hotspots.Count, Is.EqualTo(3));
        for (int i = 0; i < hotspots.Count; i++)
        {
            JsonObject hotspot = hotspots[i]!.AsObject();
            Assert.That(
                hotspot.ContainsKey("wallCenterXCm"),
                Is.True,
                $"openWorld.hotspots[{i}] must author explicit wallCenterXCm (no infer/fallback).");
            Assert.That(
                hotspot.ContainsKey("wallCenterYCm"),
                Is.True,
                $"openWorld.hotspots[{i}] must author explicit wallCenterYCm (no infer/fallback).");
        }

        DynamicNavBakeShowcaseConfig config = DynamicNavBakeShowcaseConfig.Load(raw);
        Assert.That(config.OpenWorld!.Hotspots.Length, Is.EqualTo(3));
        Assert.That(config.OpenWorld.Hotspots[0].Id, Is.EqualTo("west_pass"));
        Assert.That(config.OpenWorld.Hotspots[0].WallCenterXCm, Is.EqualTo(-140800));
        Assert.That(config.OpenWorld.Hotspots[0].WallCenterYCm, Is.EqualTo(0));
        Assert.That(config.OpenWorld.Hotspots[1].Id, Is.EqualTo("central_gate"));
        Assert.That(config.OpenWorld.Hotspots[1].WallCenterXCm, Is.EqualTo(0));
        Assert.That(config.OpenWorld.Hotspots[1].WallCenterYCm, Is.EqualTo(0));
        Assert.That(config.OpenWorld.Hotspots[2].Id, Is.EqualTo("east_reach"));
        Assert.That(config.OpenWorld.Hotspots[2].WallCenterXCm, Is.EqualTo(140800));
        Assert.That(config.OpenWorld.Hotspots[2].WallCenterYCm, Is.EqualTo(0));

        Assert.That(config.OpenWorld.Hotspots[0].WallCenterXCm, Is.Not.EqualTo(config.OpenWorld.Hotspots[2].WallCenterXCm));
        Assert.That(config.OpenWorld.Hotspots[0].WallCenterXCm, Is.Not.EqualTo(0));
        Assert.That(config.OpenWorld.Hotspots[2].WallCenterXCm, Is.Not.EqualTo(0));
    }

    [Test]
    public void ShowcaseConfig_RequiresRaylibAutoTimelineSectionWithStrictMonotonicFrames()
    {
        DynamicNavBakeShowcaseConfig rts = LoadSceneConfig(DynamicNavBakeShowcaseIds.RtsMapId);
        DynamicNavBakeShowcaseConfig open = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        AssertAuthoredRaylibAutoTimelineSharedPrefix(rts.RaylibAutoTimeline);
        AssertAuthoredRaylibAutoTimelineSharedPrefix(open.RaylibAutoTimeline);

        Assert.That(rts.RaylibAutoTimeline.FinalScreenshotFrame, Is.EqualTo(3000));
        Assert.That(rts.RaylibAutoTimeline.AutoExitFrame, Is.EqualTo(3030));
        Assert.That(
            rts.RaylibAutoTimeline.ResolvedFinalCaptureCompletionMode,
            Is.EqualTo(DynamicNavBakeShowcaseFinalCaptureCompletionMode.Arrival));
        Assert.That(rts.RaylibAutoTimeline.FinalArrivalMemberToleranceCm, Is.EqualTo(300));
        Assert.That(rts.RaylibAutoTimeline.FinalArrivalRequiredStableFixedTicks, Is.EqualTo(2));

        Assert.That(open.RaylibAutoTimeline.FinalScreenshotFrame, Is.EqualTo(720));
        Assert.That(open.RaylibAutoTimeline.AutoExitFrame, Is.EqualTo(750));
        Assert.That(
            open.RaylibAutoTimeline.ResolvedFinalCaptureCompletionMode,
            Is.EqualTo(DynamicNavBakeShowcaseFinalCaptureCompletionMode.RouteReady));
        Assert.That(open.RaylibAutoTimeline.FinalArrivalMemberToleranceCm, Is.EqualTo(300));
        Assert.That(open.RaylibAutoTimeline.FinalArrivalRequiredStableFixedTicks, Is.EqualTo(2));
    }

    [Test]
    public void ShowcaseConfig_MissingRaylibAutoTimeline_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw.Remove("raylibAutoTimeline");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("raylibAutoTimeline"));
    }

    [Test]
    public void ShowcaseConfig_RaylibAutoTimelineNonMonotonic_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["raylibAutoTimeline"]!["initialScreenshotFrame"] = 300;
        raw["raylibAutoTimeline"]!["dynamicActionFrame"] = 240;

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("raylibAutoTimeline"));
        Assert.That(ex.Message, Does.Contain("strictly increasing").IgnoreCase);
    }

    [Test]
    public void ShowcaseConfig_RaylibAutoTimelineMissingProperty_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["raylibAutoTimeline"]!.AsObject().Remove("autoExitFrame");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("autoExitFrame"));
    }

    private static void AssertAuthoredRaylibAutoTimelineSharedPrefix(DynamicNavBakeShowcaseRaylibAutoTimelineConfig timeline)
    {
        Assert.That(timeline, Is.Not.Null);
        Assert.That(timeline.AlgorithmRequestEarliestFrame, Is.EqualTo(30));
        Assert.That(timeline.AlgorithmCommitDeadlineFrame, Is.EqualTo(180));
        Assert.That(timeline.InitialScreenshotFrame, Is.EqualTo(240));
        Assert.That(timeline.DynamicActionFrame, Is.EqualTo(300));
        Assert.That(timeline.DynamicCommitDeadlineFrame, Is.EqualTo(450));
        Assert.That(timeline.DynamicScreenshotFrame, Is.EqualTo(480));
        Assert.That(timeline.FinalActionFrame, Is.EqualTo(540));
        Assert.That(timeline.FinalCommitDeadlineFrame, Is.EqualTo(690));
        Assert.That(timeline.CameraTargetToleranceCm, Is.EqualTo(250));
        Assert.That(timeline.RequiredQuiescentFixedTicks, Is.GreaterThanOrEqualTo(2));
        Assert.That(timeline.FinalArrivalMemberToleranceCm, Is.GreaterThan(0));
        Assert.That(timeline.FinalArrivalRequiredStableFixedTicks, Is.GreaterThanOrEqualTo(2));
        Assert.That(timeline.PlayerFraming, Is.Not.Null);
        Assert.That(timeline.PlayerFraming.MinSquadMembersOnScreen, Is.GreaterThan(0));
        Assert.That(timeline.PlayerFraming.MinProjectedSquadSpanPx, Is.GreaterThan(0f));
        Assert.That(timeline.PlayerFraming.PathLookaheadCm, Is.GreaterThan(0f));
        Assert.That(timeline.PlayerFraming.MarginCm, Is.GreaterThanOrEqualTo(0f));
        Assert.That(timeline.PlayerFraming.MaxDistanceCm, Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinDistanceCm));
        Assert.That(timeline.PlayerFraming.BaseDistanceCm, Is.GreaterThanOrEqualTo(timeline.PlayerFraming.MinDistanceCm));
        Assert.That(timeline.PlayerFraming.BaseDistanceCm, Is.LessThanOrEqualTo(timeline.PlayerFraming.MaxDistanceCm));
    }

    [Test]
    public void ShowcaseConfig_RaylibAutoTimelineMissingPlayerFraming_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json"));
        raw["raylibAutoTimeline"]!.AsObject().Remove("playerFraming");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("playerFraming"));
    }

    [Test]
    public void OpenWorld_HotspotMissingWallCenter_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json"));
        JsonObject hotspot = raw["openWorld"]!["hotspots"]![0]!.AsObject();
        hotspot.Remove("wallCenterXCm");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("openWorld.hotspots[0].wallCenterXCm"));
    }

    [Test]
    public void OpenWorld_HotspotWallCenterOutsideResidentWindow_ThrowsPathSpecificError()
    {
        string repoRoot = FindRepoRoot();
        JsonObject raw = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json"));
        // West resident window cannot cover the far-east authored wall center.
        raw["openWorld"]!["hotspots"]![0]!["wallCenterXCm"] = 140800;

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => DynamicNavBakeShowcaseConfig.Load(raw));
        Assert.That(ex!.Message, Does.Contain("openWorld.hotspots[0]"));
        Assert.That(ex.Message, Does.Contain("resident").IgnoreCase);
    }

    [Test]
    public void OpenWorld_CoarseGraph_UsesCenteredGridOriginAndNearestWestNode()
    {
        DynamicNavBakeShowcaseConfig config = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        int originXcm = config.WorldOriginXCm;
        int originZcm = config.WorldOriginZCm;
        Assert.That(originXcm, Is.LessThan(0));
        Assert.That(originZcm, Is.LessThan(0));

        var grid = new NavTriangleSurfaceTileGrid(
            originXcm,
            originZcm,
            config.SurfaceTileWidthCm,
            config.SurfaceTileHeightCm,
            config.WidthChunks,
            config.HeightChunks,
            haloPaddingCm: 200);
        var board = new NodeGraphBoard(
            new BoardId("graph"),
            "graph",
            new BoardConfig
            {
                WidthInMacroTiles = config.WidthInMacroTiles,
                HeightInMacroTiles = config.HeightInMacroTiles,
                GridCellSizeCm = config.GridCellSizeCm,
                ChunkSizeCells = config.ChunkSizeCells,
                LoadedChunkCapacity = Math.Max(64, config.WidthChunks * config.HeightChunks)
            });

        DynamicNavBakeShowcaseCoarseGraphBootstrap.CoarseGraphState state =
            DynamicNavBakeShowcaseCoarseGraphBootstrap.BuildAndInstall(board, config, grid);

        Assert.That(state.NodeCount, Is.EqualTo(checked(config.WidthChunks * config.HeightChunks)));
        int halfTileX = config.SurfaceTileWidthCm / 2;
        int halfTileZ = config.SurfaceTileHeightCm / 2;
        int firstCenterX = originXcm + halfTileX;
        int firstCenterZ = originZcm + halfTileZ;
        int lastCenterX = originXcm + (config.WidthChunks - 1) * config.SurfaceTileWidthCm + halfTileX;
        int lastCenterZ = originZcm + (config.HeightChunks - 1) * config.SurfaceTileHeightCm + halfTileZ;
        Assert.That(state.FullView.Graph.PosXcm[0], Is.EqualTo(firstCenterX));
        Assert.That(state.FullView.Graph.PosYcm[0], Is.EqualTo(firstCenterZ));
        Assert.That(firstCenterX, Is.LessThan(0), "Node 0 center must carry nonzero centered-world grid origin.");
        Assert.That(firstCenterZ, Is.LessThan(0), "Node 0 center must carry nonzero centered-world grid origin.");
        int last = state.NodeCount - 1;
        Assert.That(state.FullView.Graph.PosXcm[last], Is.EqualTo(lastCenterX));
        Assert.That(state.FullView.Graph.PosYcm[last], Is.EqualTo(lastCenterZ));
        Assert.That(lastCenterX, Is.GreaterThan(0));
        Assert.That(lastCenterZ, Is.GreaterThan(0));

        // Interior of a negative-west tile (avoid exact half-open tile boundaries where two centers tie).
        const int westXcm = -5000;
        const int westZcm = 100;
        int nearest = DynamicNavBakeShowcaseCoarseGraphBootstrap.FindNearestNodeId(state, westXcm, westZcm);
        int expectedChunkX = (westXcm - originXcm) / config.SurfaceTileWidthCm;
        int expectedChunkZ = (westZcm - originZcm) / config.SurfaceTileHeightCm;
        Assert.That(expectedChunkX, Is.EqualTo(31));
        Assert.That(expectedChunkZ, Is.EqualTo(32));
        Assert.That(
            state.FullView.Graph.PosXcm[nearest],
            Is.EqualTo(originXcm + expectedChunkX * config.SurfaceTileWidthCm + halfTileX));
        Assert.That(
            state.FullView.Graph.PosYcm[nearest],
            Is.EqualTo(originZcm + expectedChunkZ * config.SurfaceTileHeightCm + halfTileZ));
    }

    [Test]
    public void OpenWorldHex_CoarseGraph_UsesPositiveHexSurfaceGridAndNearestNode()
    {
        DynamicNavBakeShowcaseConfig config = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldHexMapId);
        Assert.That(config.WorldOriginXCm, Is.EqualTo(0));
        Assert.That(config.WorldOriginZCm, Is.EqualTo(0));
        Assert.That(config.SurfaceTileWidthCm, Is.EqualTo(22176));
        Assert.That(config.SurfaceTileHeightCm, Is.EqualTo(19200));

        var grid = new NavTriangleSurfaceTileGrid(
            config.WorldOriginXCm,
            config.WorldOriginZCm,
            config.SurfaceTileWidthCm,
            config.SurfaceTileHeightCm,
            config.WidthChunks,
            config.HeightChunks,
            haloPaddingCm: 384);
        var board = new NodeGraphBoard(
            new BoardId("graph"),
            "graph",
            new BoardConfig
            {
                WidthInMacroTiles = config.WidthInMacroTiles,
                HeightInMacroTiles = config.HeightInMacroTiles,
                GridCellSizeCm = config.GridCellSizeCm,
                ChunkSizeCells = config.ChunkSizeCells,
                LoadedChunkCapacity = Math.Max(64, config.WidthChunks * config.HeightChunks)
            });

        DynamicNavBakeShowcaseCoarseGraphBootstrap.CoarseGraphState state =
            DynamicNavBakeShowcaseCoarseGraphBootstrap.BuildAndInstall(board, config, grid);

        Assert.That(state.NodeCount, Is.EqualTo(16384));
        Assert.That(state.FullView.Graph.PosXcm[0], Is.EqualTo(config.SurfaceTileWidthCm / 2));
        Assert.That(state.FullView.Graph.PosYcm[0], Is.EqualTo(config.SurfaceTileHeightCm / 2));

        int probeXcm = config.OpenWorld!.Hotspots[1].WallCenterXCm + 1000;
        int probeZcm = config.OpenWorld.Hotspots[1].WallCenterYCm + 1000;
        int nearest = DynamicNavBakeShowcaseCoarseGraphBootstrap.FindNearestNodeId(state, probeXcm, probeZcm);
        int expectedChunkX = (probeXcm - config.WorldOriginXCm) / config.SurfaceTileWidthCm;
        int expectedChunkZ = (probeZcm - config.WorldOriginZCm) / config.SurfaceTileHeightCm;
        Assert.That(expectedChunkX, Is.EqualTo(64));
        Assert.That(expectedChunkZ, Is.EqualTo(64));
        Assert.That(
            state.FullView.Graph.PosXcm[nearest],
            Is.EqualTo(config.WorldOriginXCm + expectedChunkX * config.SurfaceTileWidthCm + config.SurfaceTileWidthCm / 2));
        Assert.That(
            state.FullView.Graph.PosYcm[nearest],
            Is.EqualTo(config.WorldOriginZCm + expectedChunkZ * config.SurfaceTileHeightCm + config.SurfaceTileHeightCm / 2));
    }

    [Test]
    public void OpenWorld_NegativeWorldPoint_MapsToResidentWindowAndCommittedBoundsCarryGridOrigin()
    {
        DynamicNavBakeShowcaseConfig config = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldMapId);
        int originXcm = config.WorldOriginXCm;
        int originZcm = config.WorldOriginZCm;
        var grid = new NavTriangleSurfaceTileGrid(
            originXcm,
            originZcm,
            config.SurfaceTileWidthCm,
            config.SurfaceTileHeightCm,
            config.WidthChunks,
            config.HeightChunks,
            haloPaddingCm: 200);

        const int westXcm = -6400;
        const int westZcm = 0;
        DynamicNavBakeShowcaseCoarseGraphBootstrap.ComputeResidentOriginForWorldPoint(
            grid,
            westXcm,
            westZcm,
            config.ResidentWidthChunks,
            config.ResidentHeightChunks,
            out int originChunkX,
            out int originChunkZ);

        int pointChunkX = (westXcm - originXcm) / config.SurfaceTileWidthCm;
        int pointChunkZ = (westZcm - originZcm) / config.SurfaceTileHeightCm;
        Assert.That(originChunkX, Is.EqualTo(pointChunkX - config.ResidentWidthChunks / 2));
        Assert.That(originChunkZ, Is.EqualTo(pointChunkZ - config.ResidentHeightChunks / 2));

        var tiles = new NavBakeTileCoord[checked(config.ResidentWidthChunks * config.ResidentHeightChunks)];
        int index = 0;
        for (int dz = 0; dz < config.ResidentHeightChunks; dz++)
        {
            for (int dx = 0; dx < config.ResidentWidthChunks; dx++)
            {
                tiles[index++] = new NavBakeTileCoord(originChunkX + dx, originChunkZ + dz);
            }
        }

        DynamicNavBakeShowcaseCoarseGraphBootstrap.ResolveWindowWorldBounds(
            grid,
            tiles,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ);
        Assert.That(minX, Is.EqualTo(originXcm + originChunkX * config.SurfaceTileWidthCm));
        Assert.That(minZ, Is.EqualTo(originZcm + originChunkZ * config.SurfaceTileHeightCm));
        Assert.That(maxX, Is.EqualTo(originXcm + (originChunkX + config.ResidentWidthChunks) * config.SurfaceTileWidthCm));
        Assert.That(maxZ, Is.EqualTo(originZcm + (originChunkZ + config.ResidentHeightChunks) * config.SurfaceTileHeightCm));
        Assert.That(minX, Is.LessThan(0));
        Assert.That(westXcm, Is.GreaterThanOrEqualTo(minX));
        Assert.That(westXcm, Is.LessThan(maxX));
        Assert.That(minZ, Is.LessThanOrEqualTo(westZcm));
        Assert.That(westZcm, Is.LessThan(maxZ));
    }

    [Test]
    public void OpenWorldHex_PositiveWorldPoint_MapsToResidentWindowAndCommittedBoundsCarryHexSurfaceGrid()
    {
        DynamicNavBakeShowcaseConfig config = LoadSceneConfig(DynamicNavBakeShowcaseIds.OpenWorldHexMapId);
        var grid = new NavTriangleSurfaceTileGrid(
            config.WorldOriginXCm,
            config.WorldOriginZCm,
            config.SurfaceTileWidthCm,
            config.SurfaceTileHeightCm,
            config.WidthChunks,
            config.HeightChunks,
            haloPaddingCm: 384);

        int focusXcm = config.OpenWorld!.Hotspots[1].WallCenterXCm + 1000;
        int focusZcm = config.OpenWorld.Hotspots[1].WallCenterYCm + 1000;
        DynamicNavBakeShowcaseCoarseGraphBootstrap.ComputeResidentOriginForWorldPoint(
            grid,
            focusXcm,
            focusZcm,
            config.ResidentWidthChunks,
            config.ResidentHeightChunks,
            out int originChunkX,
            out int originChunkZ);

        int pointChunkX = (focusXcm - config.WorldOriginXCm) / config.SurfaceTileWidthCm;
        int pointChunkZ = (focusZcm - config.WorldOriginZCm) / config.SurfaceTileHeightCm;
        Assert.That(originChunkX, Is.EqualTo(pointChunkX - config.ResidentWidthChunks / 2));
        Assert.That(originChunkZ, Is.EqualTo(pointChunkZ - config.ResidentHeightChunks / 2));

        var tiles = new NavBakeTileCoord[checked(config.ResidentWidthChunks * config.ResidentHeightChunks)];
        int index = 0;
        for (int dz = 0; dz < config.ResidentHeightChunks; dz++)
        {
            for (int dx = 0; dx < config.ResidentWidthChunks; dx++)
            {
                tiles[index++] = new NavBakeTileCoord(originChunkX + dx, originChunkZ + dz);
            }
        }

        DynamicNavBakeShowcaseCoarseGraphBootstrap.ResolveWindowWorldBounds(
            grid,
            tiles,
            out int minX,
            out int minZ,
            out int maxX,
            out int maxZ);
        Assert.That(minX, Is.EqualTo(config.WorldOriginXCm + originChunkX * config.SurfaceTileWidthCm));
        Assert.That(minZ, Is.EqualTo(config.WorldOriginZCm + originChunkZ * config.SurfaceTileHeightCm));
        Assert.That(maxX, Is.EqualTo(config.WorldOriginXCm + (originChunkX + config.ResidentWidthChunks) * config.SurfaceTileWidthCm));
        Assert.That(maxZ, Is.EqualTo(config.WorldOriginZCm + (originChunkZ + config.ResidentHeightChunks) * config.SurfaceTileHeightCm));
        Assert.That(minX, Is.GreaterThanOrEqualTo(0));
        Assert.That(minZ, Is.GreaterThanOrEqualTo(0));
        Assert.That(focusXcm, Is.GreaterThanOrEqualTo(minX));
        Assert.That(focusXcm, Is.LessThan(maxX));
        Assert.That(focusZcm, Is.GreaterThanOrEqualTo(minZ));
        Assert.That(focusZcm, Is.LessThan(maxZ));
    }

    private static DynamicNavBakeShowcaseConfig LoadSceneConfig(string mapId)
    {
        string repoRoot = FindRepoRoot();
        string configRelative = mapId switch
        {
            DynamicNavBakeShowcaseIds.RtsMapId => "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts.json",
            DynamicNavBakeShowcaseIds.RtsHexMapId => "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_dynamic_rts_hex.json",
            DynamicNavBakeShowcaseIds.OpenWorldMapId => "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64.json",
            DynamicNavBakeShowcaseIds.OpenWorldHexMapId => "mods/showcases/nav_bake/DynamicNavBakeShowcaseMod/assets/Showcases/DynamicNavBake/nav_bake_open_world_64x64_hex.json",
            _ => throw new ArgumentOutOfRangeException(nameof(mapId))
        };
        JsonObject config = ReadJsonObject(Path.Combine(repoRoot, configRelative));
        return DynamicNavBakeShowcaseConfig.Load(config);
    }

    private static void AssertShowcaseRegistry(string repoRoot, string mapId)
    {
        JsonObject registry = ReadJsonObject(Path.Combine(repoRoot, "showcase.registry.json"));
        JsonArray showcases = registry["showcases"]!.AsArray();
        bool found = false;
        foreach (JsonNode? node in showcases)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            if (string.Equals(entry["binding"]?.GetValue<string>(), mapId, StringComparison.Ordinal) &&
                string.Equals(entry["status"]?.GetValue<string>(), "active", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        }

        Assert.That(found, Is.True, $"showcase.registry.json must contain active T1 entry for binding '{mapId}'.");
    }

    private static void AssertCatalogDeclaresPath(JsonArray catalog, string path, string policy)
    {
        bool found = false;
        foreach (JsonNode? node in catalog)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            if (string.Equals(entry["Path"]?.GetValue<string>(), path, StringComparison.Ordinal) &&
                string.Equals(entry["Policy"]?.GetValue<string>(), policy, StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }

        Assert.That(found, Is.True, $"config_catalog.json must declare '{path}' with policy '{policy}'.");
    }

    private static void AssertHumanoidMapsToLightPreferMesh(JsonObject pathing)
    {
        JsonArray agentTypes = pathing["agentTypes"]!.AsArray();
        JsonObject? humanoid = null;
        foreach (JsonNode? node in agentTypes)
        {
            if (node is not JsonObject agent)
            {
                continue;
            }

            if (string.Equals(agent["id"]?.GetValue<string>(), "Humanoid", StringComparison.Ordinal))
            {
                humanoid = agent;
                break;
            }
        }

        Assert.That(humanoid, Is.Not.Null, "Scene Navigation/pathing.json must declare Humanoid.");
        Assert.That(humanoid!["profileId"]!.GetValue<string>(), Is.EqualTo("light"));
        Assert.That(humanoid["selection"]!["mode"]!.GetValue<string>(), Is.EqualTo("PreferMesh"));
    }

    private static void AssertDefaultGameplayContext(JsonObject game, string source)
    {
        JsonArray contexts = game["startupInputContexts"]?.AsArray()
            ?? throw new InvalidOperationException($"{source} must declare startupInputContexts.");
        Assert.That(
            contexts.Select(node => node!.GetValue<string>()),
            Is.EqualTo(new[] { "Default_Gameplay" }),
            $"{source} must activate the merged Core/Camera/CoreInput gameplay context used by WASD and RTS pointer actions.");
    }

    private static void AssertReliefGroundAssetCoversMap(
        string sharedAssetsRoot,
        JsonObject map,
        JsonObject config)
    {
        string assetUri = map["VisualHeightmapAsset"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Dynamic NavBake map must declare VisualHeightmapAsset for authoritative ground aiming.");
        const string assetPrefix = "assets/";
        Assert.That(assetUri, Does.StartWith(assetPrefix));
        Assert.That(
            assetUri,
            Does.Contain("relief"),
            "Dynamic NavBake must ship a non-flat visual heightmap so players can see ground height influence.");
        string relativeAssetPath = assetUri[assetPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        string fullAssetPath = Path.Combine(sharedAssetsRoot, relativeAssetPath);
        Assert.That(File.Exists(fullAssetPath), Is.True, $"Dynamic NavBake ground asset is missing: {fullAssetPath}");

        using FileStream stream = File.OpenRead(fullAssetPath);
        VisualHeightmapAsset asset = VisualHeightmapBinary.Read(stream);
        DynamicNavBakeShowcaseConfig scene = DynamicNavBakeShowcaseConfig.Load(config);
        Assert.That(asset.Bounds.Left, Is.LessThanOrEqualTo(scene.WorldOriginXCm));
        Assert.That(asset.Bounds.Right, Is.GreaterThanOrEqualTo(scene.WorldMaxXCm));
        Assert.That(asset.Bounds.Top, Is.LessThanOrEqualTo(scene.WorldOriginZCm));
        Assert.That(asset.Bounds.Bottom, Is.GreaterThanOrEqualTo(scene.WorldMaxZCm));
        if (assetUri.Contains("hex_relief", StringComparison.Ordinal))
        {
            Assert.That(asset.Bounds.Left, Is.EqualTo(0));
            Assert.That(asset.Bounds.Top, Is.EqualTo(0));
            Assert.That(asset.SampleColumns, Is.EqualTo(65));
            Assert.That(asset.SampleRows, Is.EqualTo(65));
        }

        Assert.That(asset.HeightSamplesCm, Is.Not.Empty);
        Assert.That(
            asset.HeightSamplesCm.Any(heightCm => heightCm != 0),
            Is.True,
            "Dynamic NavBake visual heightmap must contain non-zero samples so terrain relief is visible.");
        Assert.That(
            asset.HeightSamplesCm.Max(heightCm => heightCm),
            Is.GreaterThanOrEqualTo(400),
            "Dynamic NavBake relief should reach at least 4 m so players can notice height changes from the tactical camera.");
    }

    private static void AssertBoardContract(
        string sharedAssetsRoot,
        JsonObject map,
        DynamicNavBakeShowcaseConfig config,
        ShowcaseSceneContract scene)
    {
        JsonObject defaultBoard = map["Boards"]!.AsArray()
            .OfType<JsonObject>()
            .Single(board => string.Equals(board["Name"]?.GetValue<string>(), "default", StringComparison.Ordinal));

        Assert.That(defaultBoard["WidthInMacroTiles"]!.GetValue<int>(), Is.EqualTo(config.WidthInMacroTiles));
        Assert.That(defaultBoard["HeightInMacroTiles"]!.GetValue<int>(), Is.EqualTo(config.HeightInMacroTiles));
        Assert.That(defaultBoard["GridCellSizeCm"]!.GetValue<int>(), Is.EqualTo(config.GridCellSizeCm));

        if (scene.IsHex)
        {
            Assert.That(defaultBoard["SpatialType"]!.GetValue<string>(), Is.EqualTo("HexGrid"));
            Assert.That(defaultBoard["HexEdgeLengthCm"]!.GetValue<int>(), Is.EqualTo(400));
            Assert.That(defaultBoard["ChunkSizeCells"]!.GetValue<int>(), Is.EqualTo(64));
            Assert.That(config.ChunkSizeCells, Is.EqualTo(32));
            Assert.That(config.WorldOriginXCm, Is.EqualTo(0));
            Assert.That(config.WorldOriginZCm, Is.EqualTo(0));
            Assert.That(config.SurfaceTileWidthCm, Is.EqualTo(22176));
            Assert.That(config.SurfaceTileHeightCm, Is.EqualTo(19200));
            Assert.That(config.TerrainEditCellSizeCm, Is.EqualTo(96));
            string dataFile = defaultBoard["DataFile"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"{scene.MapId} HexGrid board must declare a .vtxm DataFile.");
            Assert.That(dataFile, Does.EndWith(".vtxm"));
            string dataFilePath = Path.Combine(sharedAssetsRoot, dataFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(dataFilePath), Is.True, $"{scene.MapId} must ship real HexGrid VertexMap data at {dataFilePath}.");
            Assert.That(
                map["VisualHeightmapAsset"]!.GetValue<string>(),
                Is.EqualTo("assets/terrain/dynamic_nav_bake_hex_relief.vhtm"));
            return;
        }

        Assert.That(defaultBoard["SpatialType"]!.GetValue<string>(), Is.EqualTo("Grid"));
        Assert.That(defaultBoard["ChunkSizeCells"]!.GetValue<int>(), Is.EqualTo(config.ChunkSizeCells));
        Assert.That(defaultBoard.ContainsKey("HexEdgeLengthCm"), Is.False);
        Assert.That(config.WorldOriginXCm, Is.EqualTo(checked(-config.WorldWidthCm / 2)));
        Assert.That(config.WorldOriginZCm, Is.EqualTo(checked(-config.WorldHeightCm / 2)));
        Assert.That(config.SurfaceTileWidthCm, Is.EqualTo(config.ChunkSizeCm));
        Assert.That(config.SurfaceTileHeightCm, Is.EqualTo(config.ChunkSizeCm));
        Assert.That(config.TerrainEditCellSizeCm, Is.EqualTo(config.GridCellSizeCm));
    }

    private static void AssertNavMeshConfigContract(
        string sharedAssetsRoot,
        JsonArray catalog,
        DynamicNavBakeShowcaseConfig config,
        ShowcaseSceneContract scene)
    {
        string navMeshPath = $"Navigation/navmesh.{scene.MapId}.json";
        AssertCatalogDeclaresPath(catalog, navMeshPath, "Replace");
        JsonObject navMesh = ReadJsonObject(Path.Combine(
            sharedAssetsRoot,
            "Configs",
            navMeshPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.That(navMesh["mode"]!.GetValue<string>(), Is.EqualTo("runtime-incremental"));
        Assert.That(navMesh["algorithm"]!.GetValue<string>(), Is.EqualTo("layered-span"));
        JsonObject runtime = navMesh["runtimeIncremental"]!.AsObject();
        Assert.That(runtime["initialResidentWidthChunks"]!.GetValue<int>(), Is.EqualTo(config.ResidentWidthChunks));
        Assert.That(runtime["initialResidentHeightChunks"]!.GetValue<int>(), Is.EqualTo(config.ResidentHeightChunks));
        Assert.That(
            runtime["residentTileCapacity"]!.GetValue<int>(),
            Is.EqualTo(checked(config.ResidentWidthChunks * config.ResidentHeightChunks)));
        Assert.That(
            runtime["dirtyTileCapacity"]!.GetValue<int>(),
            Is.GreaterThanOrEqualTo(checked(config.ResidentWidthChunks * config.ResidentHeightChunks)));

        JsonObject layeredSpan = navMesh["layeredSpan"]!.AsObject();
        Assert.That(layeredSpan["rasterCellSizeCm"]!.GetValue<int>(), Is.EqualTo(config.TerrainEditCellSizeCm));
        JsonObject triangleSurface = navMesh["triangleSurface"]!.AsObject();
        int haloPaddingCm = triangleSurface["haloPaddingCm"]!.GetValue<int>();
        Assert.That(haloPaddingCm % config.TerrainEditCellSizeCm, Is.EqualTo(0));
        Assert.That(config.SurfaceTileWidthCm % config.TerrainEditCellSizeCm, Is.EqualTo(0));
        Assert.That(config.SurfaceTileHeightCm % config.TerrainEditCellSizeCm, Is.EqualTo(0));
        if (scene.IsHex)
        {
            Assert.That(triangleSurface["tileSubdivisionsX"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(triangleSurface["tileSubdivisionsZ"]!.GetValue<int>(), Is.EqualTo(2));
        }
        else
        {
            Assert.That(triangleSurface.ContainsKey("tileSubdivisionsX"), Is.False);
            Assert.That(triangleSurface.ContainsKey("tileSubdivisionsZ"), Is.False);
        }
    }

    private static void AssertMassNavigationRightClickMapping(JsonObject inputOrderMappings)
    {
        JsonObject? command = inputOrderMappings["mappings"]!.AsArray()
            .OfType<JsonObject>()
            .SingleOrDefault(mapping => mapping["actionId"]?.GetValue<string>() == "Command");
        Assert.That(command, Is.Not.Null, "Dynamic NavBake must map the CoreInput right-click Command action.");
        Assert.That(command!["actorCollectionKey"]!.GetValue<string>(), Is.EqualTo("collection.command.source"));
        Assert.That(command["trigger"]!.GetValue<string>(), Is.EqualTo("PressedThisFrame"));
        Assert.That(command["orderTypeKey"]!.GetValue<string>(), Is.EqualTo("massNavigationMove"));
        Assert.That(command["targetType"]!.GetValue<string>(), Is.EqualTo("Position"));
        Assert.That(command["requireTarget"]!.GetValue<bool>(), Is.True);
    }

    private static void AssertCommandIntentRoutesMassNavigationMove(string sharedAssetsRoot)
    {
        JsonObject profiles = ReadJsonObject(Path.Combine(sharedAssetsRoot, "Input", "command_intent_profiles.json"));
        JsonObject? commandDefault = profiles["profiles"]!.AsArray()
            .OfType<JsonObject>()
            .SingleOrDefault(profile => profile["id"]?.GetValue<string>() == "intent.command.default");
        Assert.That(commandDefault, Is.Not.Null, "Dynamic NavBake must override intent.command.default for right-click routing.");
        JsonObject[] rules = commandDefault!["rules"]!.AsArray().OfType<JsonObject>().ToArray();
        Assert.That(rules.Length, Is.GreaterThanOrEqualTo(2), "Dynamic NavBake must cover both ground and entity Command targets.");
        foreach (JsonObject rule in rules)
        {
            Assert.That(
                rule["route"]!["orderTypeKey"]!.GetValue<string>(),
                Is.EqualTo("massNavigationMove"),
                "Right-click Command must route to massNavigationMove; moveTo is a no-op on mass-nav agents.");
        }

        Assert.That(
            rules.Any(rule => rule["target"]?["hasEntity"]?.GetValue<bool>() == false),
            Is.True,
            "Dynamic NavBake command intent must route ground Command targets.");
        Assert.That(
            rules.Any(rule => rule["target"]?["hasEntity"]?.GetValue<bool>() == true),
            Is.True,
            "Dynamic NavBake command intent must route entity Command targets.");
    }

    private static void AssertBuildingFootprintMatchesGateConfig(string sharedAssetsRoot, JsonObject config)
    {
        DynamicNavBakeShowcaseConfig scene = DynamicNavBakeShowcaseConfig.Load(config);
        JsonArray templates = ReadJsonArray(Path.Combine(sharedAssetsRoot, "Entities", "templates.json"));
        JsonObject? wall = templates
            .OfType<JsonObject>()
            .SingleOrDefault(template => template["id"]?.GetValue<string>() == scene.Gate.WallTemplateId);
        Assert.That(wall, Is.Not.Null, $"Missing wall template '{scene.Gate.WallTemplateId}'.");
        JsonObject intent = wall!["components"]!["ManifestationObstacleIntent2D"]!.AsObject();
        Assert.That(intent["shape"]!.GetValue<string>(), Is.EqualTo("Circle"));
        Assert.That(intent["navRadiusCm"]!.GetValue<int>(), Is.EqualTo(scene.Gate.NavRadiusCm));
        Assert.That(intent["radiusCm"]!.GetValue<int>(), Is.EqualTo(scene.Gate.NavRadiusCm));
        Assert.That(intent["navMaxYcm"]!.GetValue<int>(), Is.GreaterThan(intent["navMinYcm"]!.GetValue<int>()));
        Assert.That(scene.Gate.NavMaxYcm, Is.GreaterThan(scene.Gate.NavMinYcm));
    }

    private static JsonObject ReadJsonObject(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static JsonArray ReadJsonArray(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))!.AsArray();
    }

    public sealed class ShowcaseSceneContract
    {
        public ShowcaseSceneContract(
            string mapId,
            string modId,
            string modRelativePath,
            bool isHex,
            bool isOpenWorld)
        {
            MapId = mapId;
            ModId = modId;
            ModRelativePath = modRelativePath;
            IsHex = isHex;
            IsOpenWorld = isOpenWorld;
        }

        public string MapId { get; }

        public string ModId { get; }

        public string ModRelativePath { get; }

        public bool IsHex { get; }

        public bool IsOpenWorld { get; }

        public int ExpectedResidentChunks => IsHex && IsOpenWorld ? 4 : 8;

        public override string ToString() => MapId;
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "assets")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }
}
