using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using MassNavigationTotalWarEntryMod.Runtime;
using MassNavigationMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationTotalWarShowcaseContractTests
    {
        [Test]
        public void TotalWarConfig_AuthorsFormationAndSoldierMassNavAgents()
        {
            string modRoot = TotalWarModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "TotalWarShowcaseConfig.json"));
            JsonObject massNavConfig = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonObject agentProfiles = massNavConfig["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig must author agentProfiles.");
            JsonObject formationAgent = config["formationAgent"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar config must author formationAgent.");
            AssertAgentAuthoringReferencesProfileOnly(formationAgent, "formationAgent");
            string formationTemplateId = RequireString(formationAgent, "templateId");
            Assert.That(formationTemplateId, Is.EqualTo("mass_navigation_total_war_formation_agent"));
            string formationProfileId = RequireString(formationAgent, "profileId");
            Assert.That(formationProfileId, Is.EqualTo("formation"));
            Assert.That(config.ContainsKey("selection"), Is.False,
                "TotalWar config must not invent a private selection scope block; selection acquire uses game.json selection.targetFilter.");
            JsonObject gameConfig = ReadObject(Path.Combine(modRoot, "assets", "game.json"));
            JsonObject selection = gameConfig["selection"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar game.json must author selection.");
            JsonObject targetFilter = selection["targetFilter"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar game.json selection must author targetFilter.");
            Assert.That(RequireString(targetFilter, "relationFilter"), Is.EqualTo("Friendly"),
                "TotalWar player acquisition must use Core RelationshipFilter authoring, not a showcase-local selection policy.");
            JsonObject formationProfile = FindAgentProfileById(agentProfiles, formationProfileId);
            float formationSpeedCmPerSecond = formationProfile["speedCmPerSecond"]?.GetValue<float>()
                ?? throw new InvalidOperationException("MassNavigation formation agent profile speedCmPerSecond must be numeric.");

            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));
            JsonObject formationTemplate = FindObjectById(templates, formationTemplateId);
            JsonObject components = formationTemplate["components"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar formation agent template must author components.");
            Assert.That(components.ContainsKey("Name"), Is.True);
            Assert.That(components.ContainsKey("WorldPositionCm"), Is.True);
            Assert.That(components.ContainsKey("VisualHeightmapSampleState"), Is.True,
                "Formation outline height must follow the visual-heightmap SSOT through the agent visual transform.");
            Assert.That(components.ContainsKey("FacingDirection"), Is.True);
            Assert.That(components.ContainsKey("MassNavigationAgentTag"), Is.True);
            Assert.That(components.ContainsKey("MassNavigationControllable"), Is.True);
            Assert.That(components.ContainsKey("OrderBuffer"), Is.True);
            Assert.That(components.ContainsKey("SelectionSelectableTag"), Is.True);
            Assert.That(components.ContainsKey("SelectionSelectableState"), Is.True);
            Assert.That(components.ContainsKey("Team"), Is.False,
                "Formation template must not bake scene team; TotalWarShowcaseConfig teamId is applied at receipt binding.");
            Assert.That(components.ContainsKey("PlayerOwner"), Is.False,
                "Formation template must not bake scene ownership; TotalWarShowcaseConfig ownerPlayerId is applied at receipt binding.");
            Assert.That(components.ContainsKey("AttributeBuffer"), Is.True);
            Assert.That(components.ContainsKey("SpatialBounds"), Is.False,
                "Formation footprint is derived from TotalWarShowcaseConfig outline at receipt binding time, not authored in the template.");
            Assert.That(components.ContainsKey("SpatialFootprint2D"), Is.False,
                "Formation footprint vertices must not drift away from the configured outline.");

            JsonArray formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("TotalWar config must author formations.");
            Assert.That(formations.Count, Is.GreaterThan(0));
            string[] shapes = formations
                .Select(node => RequireString(node?.AsObject() ?? throw new InvalidOperationException("Formation must be an object."), "outline", "shape"))
                .ToArray();
            Assert.That(shapes, Does.Contain("Rectangle"));
            Assert.That(shapes, Does.Contain("Circle"));

            JsonObject soldierTargetSync = config["soldierTargetSync"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar config must author soldierTargetSync.");
            AssertPositive(soldierTargetSync, "targetChangeEpsilonCm");
            AssertPositive(soldierTargetSync, "facingChangeEpsilonRadians");
            Assert.That(config.ContainsKey("formationSync"), Is.False,
                "TotalWar config must not keep empty sync sections without runtime semantics.");
            Assert.That(soldierTargetSync.ContainsKey("orderPathLookaheadCm"), Is.False,
                "TotalWar carrier-mode soldier sync must not author stale order-path knobs.");
            Assert.That(soldierTargetSync.ContainsKey("orderPathAnchorUpdateEpsilonCm"), Is.False,
                "TotalWar carrier-mode soldier sync must not author stale order-path knobs.");
            JsonObject obstacleOverlay = config["obstacleOverlay"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar config must author obstacleOverlay.");
            AssertPositive(obstacleOverlay, "borderWidthCm");

            foreach (JsonObject formation in formations.Select(node => node?.AsObject() ?? throw new InvalidOperationException("Formation must be an object.")))
            {
                AssertPositive(formation, "ownerPlayerId");
                JsonObject formationOutline = formation["outline"]?.AsObject()
                    ?? throw new InvalidOperationException("Every formation must author outline.");
                if (RequireString(formationOutline, "shape") == "Circle")
                {
                    JsonObject circle = formationOutline["circle"]?.AsObject()
                        ?? throw new InvalidOperationException("Circle formation must author outline.circle.");
                    Assert.That(circle["footprintVertexCount"]?.GetValue<int>(), Is.EqualTo(SpatialFootprint2D.MaxVerticesPerPolygon));
                }

                JsonObject soldierAgent = formation["soldierAgent"]?.AsObject()
                    ?? throw new InvalidOperationException("Every formation must author soldierAgent.");
                AssertAgentAuthoringReferencesProfileOnly(soldierAgent, "soldierAgent");
                string soldierProfileId = RequireString(soldierAgent, "profileId");
                JsonObject soldierProfile = FindAgentProfileById(agentProfiles, soldierProfileId);
                float soldierSpeedCmPerSecond = soldierProfile["speedCmPerSecond"]?.GetValue<float>()
                    ?? throw new InvalidOperationException("Every formation soldier agent profile speedCmPerSecond must be numeric.");
                Assert.That(soldierSpeedCmPerSecond, Is.GreaterThan(formationSpeedCmPerSecond),
                    "TotalWar soldier MassNavigation agents must use agentProfiles configured faster than formation MassNavigation agents.");
                JsonObject soldierTemplate = FindObjectById(templates, RequireString(soldierAgent, "templateId"));
                JsonObject soldierComponents = soldierTemplate["components"]?.AsObject()
                    ?? throw new InvalidOperationException("TotalWar soldier template must author components.");
                Assert.That(soldierComponents.ContainsKey("MassNavigationAgentTag"), Is.True);
                Assert.That(soldierComponents.ContainsKey("Team"), Is.False,
                    "Soldier team is owned by the formation config and applied through spawn receipt binding; templates must not author a second team SSOT.");
                Assert.That(soldierComponents.ContainsKey("MassNavigationControllable"), Is.False);
                Assert.That(soldierComponents.ContainsKey("OrderBuffer"), Is.False);
                Assert.That(soldierComponents.ContainsKey("SelectionSelectableTag"), Is.False);
                Assert.That(soldierComponents.ContainsKey("SelectionSelectableState"), Is.False);
                Assert.That(soldierComponents.ContainsKey("AttributeBuffer"), Is.False);

                JsonObject slots = formation["slots"]?.AsObject()
                    ?? throw new InvalidOperationException("Every formation must author slots.");
                string slotLayout = RequireString(slots, "layout");
                Assert.That(slotLayout, Is.AnyOf("Grid", "Disc"));
                JsonObject slotShape = slots[slotLayout == "Grid" ? "grid" : "disc"]?.AsObject()
                    ?? throw new InvalidOperationException($"Formation {slotLayout} slots must author its shape-specific block.");

                JsonObject outline = formation["outline"]?.AsObject()
                    ?? throw new InvalidOperationException("Every formation must author outline.");
                Assert.That(outline["shape"]?.GetValueKind(), Is.EqualTo(System.Text.Json.JsonValueKind.String));
                string outlineShape = RequireString(outline, "shape");
                Assert.That(outlineShape, Is.AnyOf("Rectangle", "Circle"));
                JsonObject outlineShapeBlock = outline[outlineShape == "Rectangle" ? "rectangle" : "circle"]?.AsObject()
                    ?? throw new InvalidOperationException($"Formation {outlineShape} outline must author its shape-specific block.");
                Assert.That(
                    (slotLayout, outlineShape),
                    Is.AnyOf(("Grid", "Rectangle"), ("Disc", "Circle")),
                    "Formation slot layout and outline shape must describe the same gameplay shape.");
                AssertPositive(outline, "heightOffsetM", allowZero: true);
                AssertPositive(outline, "curveSampleCount");
                AssertPositive(outline, "emissionPositionEpsilonM");
                AssertPositive(outline, "emissionFacingEpsilonRadians");
                AssertPositive(outline, "frontIndicatorLineWidthCm");
                AssertColor(outline["fillColor"]?.AsArray(), "fillColor");
                AssertColor(outline["borderColor"]?.AsArray(), "borderColor");
                if (slotLayout == "Grid")
                {
                    Assert.That(slots.ContainsKey("disc"), Is.False);
                    Assert.That(outline.ContainsKey("circle"), Is.False);
                    AssertPositive(slotShape, "columns");
                    AssertPositive(slotShape, "rows");
                    AssertPositive(slotShape, "spacingXCm");
                    AssertPositive(slotShape, "spacingYCm");
                    AssertPositive(outlineShapeBlock, "widthCm");
                    AssertPositive(outlineShapeBlock, "depthCm");
                    AssertPositive(outlineShapeBlock, "edgeLineWidthCm");
                    float slotWidth = (slotShape["columns"]!.GetValue<float>() - 1f) * slotShape["spacingXCm"]!.GetValue<float>();
                    float slotDepth = (slotShape["rows"]!.GetValue<float>() - 1f) * slotShape["spacingYCm"]!.GetValue<float>();
                    Assert.That(outlineShapeBlock["widthCm"]!.GetValue<float>(), Is.GreaterThanOrEqualTo(slotWidth));
                    Assert.That(outlineShapeBlock["depthCm"]!.GetValue<float>(), Is.GreaterThanOrEqualTo(slotDepth));
                }
                else
                {
                    Assert.That(slots.ContainsKey("grid"), Is.False);
                    Assert.That(outline.ContainsKey("rectangle"), Is.False);
                    AssertPositive(slotShape, "count");
                    AssertPositive(slotShape, "ringSpacingCm");
                    AssertPositive(outlineShapeBlock, "radiusCm");
                    AssertPositive(outlineShapeBlock, "ringWidthCm");
                    float count = slotShape["count"]!.GetValue<float>();
                    float ringSpacing = slotShape["ringSpacingCm"]!.GetValue<float>();
                    float requiredRadius = MathF.Sqrt(count - 1f) * ringSpacing;
                    Assert.That(outlineShapeBlock["radiusCm"]!.GetValue<float>(), Is.GreaterThanOrEqualTo(requiredRadius));
                }
            }
        }

        [Test]
        public void TotalWarMassNavigationConfig_IsCompleteExplicitAndReplaceMerged()
        {
            string modRoot = TotalWarModRoot();
            JsonArray catalog = ReadArray(Path.Combine(modRoot, "assets", "Configs", "config_catalog.json"));
            JsonObject massNavEntry = FindObjectByPath(catalog, "MassNavigationConfig.json");
            JsonObject totalWarEntry = FindObjectByPath(catalog, "TotalWarShowcaseConfig.json");
            Assert.That(RequireString(massNavEntry, "Policy"), Is.EqualTo("Replace"),
                "TotalWar owns a complete MassNavigation config file; it must not rely on base-mod DeepObject field fill.");
            Assert.That(RequireString(totalWarEntry, "Policy"), Is.EqualTo("Replace"),
                "TotalWar showcase authoring is a complete scenario SSOT; it must not rely on DeepObject field fill.");

            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonObject showcaseConfig = ReadObject(Path.Combine(modRoot, "assets", "TotalWarShowcaseConfig.json"));
            Assert.That(showcaseConfig.ContainsKey("selection"), Is.False,
                "TotalWarShowcaseConfig must not invent a private selection scope block.");
            Assert.That(showcaseConfig["initialSelectionEntityCapacity"]?.GetValue<int>(), Is.GreaterThan(0),
                "TotalWarShowcaseConfig must explicitly author initial selection scratch capacity.");
            string[] required =
            {
                "mapId",
                "world",
                "solver",
                "presentation",
                "scenario",
                "scenarioRuntime",
                "cadence",
                "agentProfiles",
                "cameraProfiles",
                "minimap",
                "teamRelationships",
                "flow",
                "arrival",
                "avoidance",
                "semantics",
                "viewResidency",
            };
            foreach (string property in required)
            {
                Assert.That(config.ContainsKey(property), Is.True, $"TotalWar MassNavigationConfig must author '{property}'.");
            }

            Assert.That(RequireString(config, "mapId"), Is.EqualTo("mass_navigation_total_war"));
            JsonObject agentProfiles = config["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("agentProfiles must be authored.");
            JsonArray profiles = agentProfiles["profiles"]?.AsArray()
                ?? throw new InvalidOperationException("agentProfiles.profiles must be authored.");
            Assert.That(profiles.Select(node => node?["id"]?.GetValue<string>()).ToArray(),
                Is.EquivalentTo(new[] { "formation", "heavy", "light" }));
            JsonObject formationProfile = FindAgentProfileById(agentProfiles, "formation");
            Assert.That(formationProfile["bodyRadiusCm"]?.GetValue<float>(), Is.EqualTo(720f));
            Assert.That(formationProfile["speedCmPerSecond"]?.GetValue<float>(), Is.EqualTo(360f));
            JsonObject scenarioRuntime = config["scenarioRuntime"]?.AsObject()
                ?? throw new InvalidOperationException("scenarioRuntime must be authored.");
            Assert.That(scenarioRuntime["autoSpawnConfiguredScenario"]?.GetValue<bool>(), Is.False);
            Assert.That(scenarioRuntime["initialSelectionScratchCapacity"]?.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(scenarioRuntime["initialSelectedEntityCapacity"]?.GetValue<int>(), Is.GreaterThan(0));
            JsonObject panelControls = scenarioRuntime["panelControls"]?.AsObject()
                ?? throw new InvalidOperationException("scenarioRuntime.panelControls must be authored.");
            Assert.That(panelControls["maxAgentsPerTeam"]?.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(panelControls["totalAgentStep"]?.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(panelControls["totalAgentPresets"]?.AsArray().Count, Is.GreaterThan(0));
            Assert.That(panelControls["panelRefreshIntervalSeconds"]?.GetValue<float>(), Is.GreaterThan(0f));
            JsonObject scenario = config["scenario"]?.AsObject()
                ?? throw new InvalidOperationException("scenario must be authored.");
            Assert.That(scenario["agentsPerTeam"]?.GetValue<int>(), Is.EqualTo(0),
                "TotalWar runtime owns formation/soldier spawning; the shared MassNavigation config must not auto-author generic scenario agents.");
            Assert.That(scenario.ContainsKey("spawnLayout"), Is.False,
                "TotalWar does not use MassNavigation generic auto-spawn, so it must not author a generic spawn layout SSOT.");
            JsonArray scenarioTeams = scenario["teams"]?.AsArray()
                ?? throw new InvalidOperationException("scenario.teams must be authored.");
            Assert.That(scenarioTeams.Select(node => node?["id"]?.GetValue<int>()).ToArray(),
                Is.EquivalentTo(new[] { 1, 2 }));
            JsonObject presentation = config["presentation"]?.AsObject()
                ?? throw new InvalidOperationException("presentation must be authored.");
            JsonArray presentationTeams = presentation["teams"]?.AsArray()
                ?? throw new InvalidOperationException("presentation.teams must be authored.");
            Assert.That(presentationTeams.Count, Is.EqualTo(0),
                "TotalWar owns formation/soldier spawning through TotalWarShowcaseConfig; MassNavigation generic auto-spawn team mappings must stay empty to avoid a second template SSOT.");
            Assert.That(presentation.ContainsKey("blockerPerformerId"), Is.False);
            Assert.That(presentation.ContainsKey("hotspotPerformerId"), Is.False);
            Assert.That(presentation.ContainsKey("blockerTemplateId"), Is.False);
            Assert.That(presentation.ContainsKey("hotspotTemplateId"), Is.False);
            JsonArray requiredMeshAssetIds = presentation["requiredMeshAssetIds"]?.AsArray()
                ?? throw new InvalidOperationException("presentation.requiredMeshAssetIds must be authored.");
            string[] requiredMeshIds = requiredMeshAssetIds.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray();
            Assert.That(requiredMeshIds, Does.Contain("mass_navigation.agent.soldier"));
            Assert.That(requiredMeshIds, Does.Contain("mass_navigation.selection.marker"));
            Assert.That(requiredMeshIds, Does.Not.Contain("mass_navigation.blocker.rock"));
            Assert.That(requiredMeshIds, Does.Not.Contain("mass_navigation.hotspot.obelisk"));

            JsonObject solver = config["solver"]?.AsObject()
                ?? throw new InvalidOperationException("solver must be authored.");
            string[] solverProperties =
            {
                "fieldWidthCm",
                "fieldHeightCm",
                "flowCellSizeCm",
                "maxObstacleCount",
                "separationHashCellSizeCm",
                "separationHashMinSearchRadiusCells",
                "hardResolveHashCellSizeCm",
                "hardResolveHashMinSearchRadiusCells",
                "playAreaMinXCm",
                "playAreaMaxXCm",
                "playAreaMinYCm",
                "playAreaMaxYCm",
            };
            foreach (string property in solverProperties)
            {
                Assert.That(solver.ContainsKey(property), Is.True, $"TotalWar MassNavigationConfig must author solver.{property}.");
            }

            JsonObject world = config["world"]?.AsObject()
                ?? throw new InvalidOperationException("world must be authored.");
            Assert.That(solver["fieldWidthCm"]!.GetValue<int>(), Is.EqualTo(world["solverWindowWidthCm"]!.GetValue<int>()));
            Assert.That(solver["fieldHeightCm"]!.GetValue<int>(), Is.EqualTo(world["solverWindowHeightCm"]!.GetValue<int>()));

            JsonObject cameraProfiles = config["cameraProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("cameraProfiles must be authored.");
            Assert.That(RequireString(cameraProfiles, "tacticalProfileId"), Is.EqualTo("Camera.Profile.MassNavigationTactical"));
            Assert.That(RequireString(cameraProfiles, "strategicProfileId"), Is.EqualTo("Camera.Profile.MassNavigationStrategic"));
            JsonObject cameraRequestPolicy = cameraProfiles["requestPolicy"]?.AsObject()
                ?? throw new InvalidOperationException("cameraProfiles.requestPolicy must be authored.");
            Assert.That(cameraRequestPolicy.ContainsKey("blendDurationSeconds"), Is.True);
            Assert.That(cameraRequestPolicy.ContainsKey("resetRuntimeState"), Is.True);
            Assert.That(cameraRequestPolicy.ContainsKey("snapToFollowTargetWhenAvailable"), Is.True);
            Assert.That(cameraRequestPolicy.ContainsKey("strategicTargetXCm"), Is.True);
            Assert.That(cameraRequestPolicy.ContainsKey("strategicTargetYCm"), Is.True);

            JsonObject minimap = config["minimap"]?.AsObject()
                ?? throw new InvalidOperationException("minimap must be authored.");
            Assert.That(minimap.ContainsKey("visible"), Is.True);
            Assert.That(RequireString(minimap, "initialPreset"), Is.EqualTo("RtsFullMap"));
            Assert.That(minimap["followCameraHalfExtentCm"]?.GetValue<float>(), Is.GreaterThan(0f));
            Assert.That(minimap.ContainsKey("rotateWithCamera"), Is.True);

            JsonObject residency = config["viewResidency"]?.AsObject()
                ?? throw new InvalidOperationException("viewResidency must be authored.");
            Assert.That(RequireString(residency, "mode"), Is.EqualTo("Probe"));
            Assert.That(residency["retainSeconds"]?.GetValue<float>(), Is.EqualTo(12f));
            Assert.That(residency["radiusCm"]?.GetValue<int>(), Is.EqualTo(24000));
            Assert.That(RequireString(residency, "initialProbeId"), Is.EqualTo("battlefield_overview"));
            JsonArray probes = residency["cameraProbes"]?.AsArray()
                ?? throw new InvalidOperationException("viewResidency.cameraProbes must be authored.");
            Assert.That(probes.Select(node => node?["id"]?.GetValue<string>()).ToArray(),
                Is.EquivalentTo(new[] { "battlefield_overview", "left_flank", "right_flank" }));

            JsonObject avoidance = config["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("avoidance must be authored.");
            Assert.That(avoidance.ContainsKey("lightNavMass"), Is.False,
                "TotalWar agent navMass must be owned only by MassNavigationConfig.agentProfiles.");
            Assert.That(avoidance.ContainsKey("heavyNavMass"), Is.False,
                "TotalWar agent navMass must be owned only by MassNavigationConfig.agentProfiles.");
            Assert.That(avoidance.ContainsKey("lightVisualScale"), Is.False,
                "TotalWar agent visualScale must be owned only by MassNavigationConfig.agentProfiles.");
            Assert.That(avoidance.ContainsKey("heavyVisualScale"), Is.False,
                "TotalWar agent visualScale must be owned only by MassNavigationConfig.agentProfiles.");
            Assert.That(avoidance["dominantMassRatio"]?.GetValue<float>(), Is.GreaterThan(0f));
            Assert.That(avoidance["friendlyResponseScale"]?.GetValue<float>(), Is.GreaterThan(0f));
            JsonObject legacyAvoidanceConfig = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            legacyAvoidanceConfig["avoidance"]!["heavyVisualScale"] = 0.34f;
            System.Text.Json.JsonException legacyAvoidanceField = Assert.Throws<System.Text.Json.JsonException>(
                () => MassNavigationConfig.Load(legacyAvoidanceConfig))!;
            Assert.That(legacyAvoidanceField.Message, Does.Contain("heavyVisualScale"));

            JsonObject group = config["semantics"]?["group"]?.AsObject()
                ?? throw new InvalidOperationException("semantics.group must be authored.");
            string[] formationSemantics =
            {
                "formationLineSpacingCm",
                "formationSquareSpacingCm",
                "formationCircleSpacingCm",
                "formationCircleMinRadiusCm",
                "formationWedgeSpacingCm",
                "formationRotationEpsilonRadians",
            };
            foreach (string property in formationSemantics)
            {
                Assert.That(group.ContainsKey(property), Is.True, $"TotalWar MassNavigationConfig must author group.{property}.");
                AssertPositive(group, property, allowZero: property == "formationRotationEpsilonRadians");
            }
        }

        [Test]
        public void MassNavigationSolverConfig_IsRequiredAndUsedByRuntime()
        {
            JsonObject missingSolver = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "MassNavigationConfig.json"));
            missingSolver.Remove("solver");
            InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(missingSolver))!;
            Assert.That(missing.Message, Does.Contain("solver"));

            JsonObject invalidSolver = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "MassNavigationConfig.json"));
            invalidSolver["solver"]!["flowCellSizeCm"] = 96;
            InvalidOperationException invalid = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(invalidSolver))!;
            Assert.That(invalid.Message, Does.Contain("solver"));
            Assert.That(invalid.Message, Does.Contain("FlowCellSizeCm"));

            JsonObject configJson = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "MassNavigationConfig.json"));
            MassNavigationConfig config = MassNavigationConfig.Load(configJson);
            var simulation = new MassNavigationSimulationRuntime(config);
            MassNavigationSolverRuntimeConfigSnapshot solver = simulation.CaptureSolverRuntimeConfig();
            Assert.That(solver.FieldWidthCm, Is.EqualTo(config.Solver.FieldWidthCm));
            Assert.That(solver.FieldHeightCm, Is.EqualTo(config.Solver.FieldHeightCm));
            Assert.That(solver.FlowCellSizeCm, Is.EqualTo(config.Solver.FlowCellSizeCm));
            Assert.That(solver.MaxObstacleCount, Is.EqualTo(config.Solver.MaxObstacleCount));
            Assert.That(solver.ParallelWorkerCount, Is.EqualTo(config.Solver.ParallelWorkerCount));
            Assert.That(solver.SeparationHashCellSizeCm, Is.EqualTo(config.Solver.SeparationHashCellSizeCm));
            Assert.That(solver.HardResolveHashCellSizeCm, Is.EqualTo(config.Solver.HardResolveHashCellSizeCm));
            Assert.That(solver.PlayAreaMinXCm, Is.EqualTo(config.Solver.PlayAreaMinXCm));
            Assert.That(solver.PlayAreaMaxXCm, Is.EqualTo(config.Solver.PlayAreaMaxXCm));
        }

        [Test]
        public void TotalWarShowcaseConfig_RejectsMissingProfilesAndLegacyAgentRuntimeFields()
        {
            JsonObject config = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "TotalWarShowcaseConfig.json"));
            JsonObject massNavConfig = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "MassNavigationConfig.json"));
            TotalWarShowcaseConfig loaded = TotalWarShowcaseConfig.Load(config);
            MassNavigationConfig loadedMassNav = MassNavigationConfig.Load(massNavConfig);
            Assert.DoesNotThrow(() => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles));

            JsonObject formationAgent = config["formationAgent"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar config must author formationAgent.");
            formationAgent["profileId"] = "missing_formation_profile";
            loaded = TotalWarShowcaseConfig.Load(config);
            InvalidOperationException missingFormationProfile = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles))!;
            Assert.That(missingFormationProfile.Message, Does.Contain("formationAgent.profileId"));
            Assert.That(missingFormationProfile.Message, Does.Contain("missing_formation_profile"));

            config = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "TotalWarShowcaseConfig.json"));
            JsonArray formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("TotalWar config must author formations.");
            JsonObject firstFormation = formations[0]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar formation must be an object.");
            JsonObject soldierAgent = firstFormation["soldierAgent"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar formation must author soldierAgent.");
            soldierAgent["profileId"] = "missing_soldier_profile";
            loaded = TotalWarShowcaseConfig.Load(config);
            InvalidOperationException missingSoldierProfile = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles))!;
            Assert.That(missingSoldierProfile.Message, Does.Contain("soldierAgent.profileId"));
            Assert.That(missingSoldierProfile.Message, Does.Contain("missing_soldier_profile"));

            config = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "TotalWarShowcaseConfig.json"));
            formationAgent = config["formationAgent"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar config must author formationAgent.");
            formationAgent["navMass"] = 12f;
            System.Text.Json.JsonException legacyFormationField = Assert.Throws<System.Text.Json.JsonException>(
                () => TotalWarShowcaseConfig.Load(config))!;
            Assert.That(legacyFormationField.Message, Does.Contain("navMass"));

            config = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "TotalWarShowcaseConfig.json"));
            formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("TotalWar config must author formations.");
            firstFormation = formations[0]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar formation must be an object.");
            soldierAgent = firstFormation["soldierAgent"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar formation must author soldierAgent.");
            soldierAgent["speedCmPerSecond"] = 920f;
            System.Text.Json.JsonException legacySoldierField = Assert.Throws<System.Text.Json.JsonException>(
                () => TotalWarShowcaseConfig.Load(config))!;
            Assert.That(legacySoldierField.Message, Does.Contain("speedCmPerSecond"));

            config = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "TotalWarShowcaseConfig.json"));
            loaded = TotalWarShowcaseConfig.Load(config);
            massNavConfig = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject profiles = massNavConfig["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig must author agentProfiles.");
            JsonObject profile = FindAgentProfileById(profiles, "light");
            profile["speedCmPerSecond"] = FindAgentProfileById(profiles, "formation")["speedCmPerSecond"]?.GetValue<float>()
                ?? throw new InvalidOperationException("formation profile speed must be numeric.");
            loadedMassNav = MassNavigationConfig.Load(massNavConfig);
            InvalidOperationException equalSpeed = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles))!;
            Assert.That(equalSpeed.Message, Does.Contain("soldierAgent.profileId"));
            Assert.That(equalSpeed.Message, Does.Contain("formationAgent.profileId"));

            massNavConfig = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "MassNavigationConfig.json"));
            profiles = massNavConfig["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig must author agentProfiles.");
            profile = FindAgentProfileById(profiles, "light");
            profile["speedCmPerSecond"] = FindAgentProfileById(profiles, "formation")["speedCmPerSecond"]!.GetValue<float>() - 1f;
            loadedMassNav = MassNavigationConfig.Load(massNavConfig);
            InvalidOperationException slowerSoldier = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles))!;
            Assert.That(slowerSoldier.Message, Does.Contain("soldierAgent.profileId"));
            Assert.That(slowerSoldier.Message, Does.Contain("formationAgent.profileId"));
        }

        [Test]
        public void TeamRelationshipConfig_RejectsCaseAliases()
        {
            JsonObject config = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject relationships = config["teamRelationships"]?.AsObject()
                ?? throw new InvalidOperationException("teamRelationships must be authored.");
            relationships["defaultRelationship"] = "hostile";

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(ex.Message, Does.Contain("defaultRelationship"));

            Assert.That(TeamManager.TryParseRelationship("Hostile", out TeamRelationship parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(TeamRelationship.Hostile));
            Assert.That(TeamManager.TryParseRelationship("hostile", out _), Is.False);
            Assert.That(TeamManager.TryParseRelationship("HOSTILE", out _), Is.False);
        }

        [Test]
        public void TotalWarRuntime_UsesTemplateSpawnReceiptsAndPresentationLifecycle()
        {
            string runtimePath = Path.Combine(TotalWarModRoot(), "Runtime", "TotalWarShowcaseRuntime.cs");
            string source = File.ReadAllText(runtimePath);

            Assert.That(source, Does.Contain("RuntimeEntitySpawnQueue"));
            Assert.That(source, Does.Contain("TotalWarSpawnReceiptBinding.ForFormationAgent"));
            Assert.That(source, Does.Contain("RegisterSpawnedFormationAgent(GameEngine engine"));
            Assert.That(source, Does.Contain("RegisterSpawnedObstacleOverlay"));
            Assert.That(source, Does.Contain("DestroyShowcaseOwnedEntities"));
            Assert.That(source, Does.Contain("PresentationDestroyPending"));
            Assert.That(source, Does.Not.Contain("World.Create("),
                "TotalWar formation agents must be spawned through the runtime template spawn path.");
            Assert.That(source, Does.Not.Contain("World.Destroy("),
                "TotalWar formation agents must enter presentation destroy lifecycle instead of direct ECS destroy.");
        }

        [Test]
        public void TotalWarRuntime_CachesSlotOffsetsAndSyncsSoldierTargetsFromFormationOrders()
        {
            string source = File.ReadAllText(Path.Combine(TotalWarModRoot(), "Runtime", "TotalWarShowcaseRuntime.cs"));
            string syncBody = ExtractMethodBody(source, "private void SyncSoldierTargetsFromFormationAgents");

            Assert.That(syncBody, Does.Contain("targetSync.TargetChangeEpsilonCm"));
            Assert.That(syncBody, Does.Contain("targetSync.FacingChangeEpsilonRadians"));
            Assert.That(syncBody, Does.Not.Contain("IntervalTicks"),
                "Soldier slot orders must be derived from formation order/target changes, not a tick cadence chase loop.");
            Assert.That(syncBody, Does.Contain("formationWorldX"));
            Assert.That(syncBody, Does.Contain("LastCarrierCenterWorldXCm"),
                "Carrier snapshots must use world coordinates; solver local coordinates are rebased when the navigation window moves.");
            Assert.That(syncBody, Does.Contain("_formationPlans[formationIndex] = plan;"),
                "Carrier snapshots must be committed even when soldier slot target writes are skipped by the epsilon gate.");
            Assert.That(syncBody, Does.Contain("SyncCarriedAgentRangeToCarrier"),
                "Soldiers must inherit the formation agent's resolved MassNavigation displacement through the core carrier/member API.");
            Assert.That(syncBody, Does.Not.Contain("ApplyAgentWorldDisplacementRange"),
                "TotalWar must not directly drive raw solver displacement.");
            Assert.That(syncBody, Does.Contain("soldierAgentPlan.SlotOffsetXCm"));
            Assert.That(syncBody, Does.Contain("soldierAgentPlan.SlotOffsetYCm"));
            Assert.That(syncBody, Does.Not.Contain("TryGetGroupMemberOrderTarget"),
                "Soldier target sync must use the MassNavigation order-path anchor SSOT without repeating order target lookups in the showcase hot path.");
            Assert.That(syncBody, Does.Not.Contain("OrderPathLookaheadCm"),
                "Carrier-mode soldiers must not chase the formation order-path lookahead; the formation's resolved displacement carries them through macro obstacles.");
            Assert.That(syncBody, Does.Not.Contain("OrderPathAnchorUpdateEpsilonCm"));
            Assert.That(syncBody, Does.Not.Contain("TryUpdateGroupMemberFollowerAnchor"));
            Assert.That(syncBody, Does.Not.Contain("TryUpdateGroupMemberOrderPathAnchor"),
                "TotalWar soldiers must not consume the pure order-path anchor in carrier mode.");
            Assert.That(syncBody, Does.Not.Contain("ResolveOrderPathLookaheadAnchor"),
                "TotalWar must not recompute its own chase anchor.");
            Assert.That(syncBody, Does.Contain("ResolveCarriedAgentSlotTarget"),
                "Soldier slot targets must use MassNavigation's carrier/member slot projection before writing navigation targets.");
            Assert.That(syncBody, Does.Contain("ApplyCarriedAgentSlotTarget"),
                "Soldier target writes must go through the core carrier/member API.");
            Assert.That(syncBody, Does.Not.Contain("ResolveAgentGroupSlotTargetLocalCm"),
                "TotalWar must not call raw MassNavigation slot target projection helpers.");
            Assert.That(syncBody, Does.Not.Contain("SetAgentTargetLocalCm"),
                "TotalWar must not directly write raw solver targets.");
            Assert.That(syncBody, Does.Not.Contain("TryGetUnitTarget"),
                "Soldier target sync must not read raw solver unit targets; order slot targets are the MassNavigation SSOT.");
            Assert.That(syncBody, Does.Not.Contain("TryGetGroupDestination"),
                "Soldier target sync must not collapse all soldiers to a group destination.");
            Assert.That(syncBody, Does.Contain("ResolveFormationFacing"),
                "Soldier slot rotation must come from the formation entity's explicit FacingDirection.");
            Assert.That(syncBody, Does.Not.Contain("GetVelocityCmPerSecond"),
                "Soldier target sync must not infer formation facing from movement velocity.");
            Assert.That(syncBody, Does.Contain("ShouldWriteSoldierTarget"),
                "Soldier target sync must avoid re-writing unchanged per-soldier targets into navigation hot state.");
            Assert.That(syncBody, Does.Contain("resolvedTarget.WorldXCm"),
                "Target write de-duplication must compare world coordinates so solver-window rebases do not stale the cache.");
            Assert.That(syncBody, Does.Contain("resetRecovery: targetChanged"),
                "Soldier agents must only reset arrival recovery when formation-derived orders actually change.");
            Assert.That(syncBody, Does.Contain("if (facingChanged)"),
                "Soldier facing writes must be split from anchor-only target updates.");
            Assert.That(syncBody, Does.Not.Contain("ResolveSlotOffset"),
                "Soldier target sync must use BuildScenarioPlans cached slot offsets instead of reparsing formation layout every tick.");
            Assert.That(syncBody, Does.Not.Contain("ActiveConfig"),
                "Soldier target sync must be driven by explicit method inputs, not hidden runtime config lookups.");
            Assert.That(syncBody, Does.Not.Contain("MassFlow"),
                "TotalWar must use MassNavigationSimulationRuntime's public navigation API instead of piercing solver internals.");
        }

        [Test]
        public void TotalWarSoldierBinding_UsesMassFlowAgentProfileSsot()
        {
            string modRoot = TotalWarModRoot();
            string bindingSource = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarSpawnReceiptBindingSystem.cs"));
            string bindSoldier = ExtractMethodBody(bindingSource, "private void BindSoldier");

            Assert.That(bindSoldier, Does.Contain("RejectComponent<Team>"));
            Assert.That(bindSoldier, Does.Contain("_simulation.BindSpawnedAgent"));
            Assert.That(bindSoldier, Does.Contain("binding.MassNavAgentIndex"));
            Assert.That(bindSoldier, Does.Not.Contain("binding.TeamId"));
            Assert.That(bindSoldier, Does.Not.Contain("binding.NavMass"));
            Assert.That(bindSoldier, Does.Not.Contain("binding.VisualScale"));
            Assert.That(bindSoldier, Does.Not.Contain("binding.BodyRadiusCm"));
            Assert.That(bindSoldier, Does.Not.Contain("binding.SpeedCmPerSecond"));
            Assert.That(bindSoldier, Does.Not.Contain("new Team"));
            Assert.That(bindSoldier, Does.Not.Contain("RequireComponent<Team>"));
            Assert.That(bindSoldier, Does.Not.Contain("RequireTeam("));
        }

        [Test]
        public void TotalWarSpawnReceiptBinding_HasKindGuardedPayloadWithoutAgentProfileSentinels()
        {
            string source = File.ReadAllText(Path.Combine(
                TotalWarModRoot(),
                "Runtime",
                "TotalWarSpawnReceiptRuntime.cs"));

            Assert.That(source, Does.Contain("SoldierSpawnReceiptPayload"));
            Assert.That(source, Does.Contain("FormationAgentSpawnReceiptPayload"));
            Assert.That(source, Does.Contain("ObstacleOverlaySpawnReceiptPayload"));
            Assert.That(source, Does.Contain("RequireKind"));
            Assert.That(source, Does.Not.Contain("TeamId"));
            Assert.That(source, Does.Not.Contain("Heavy"));
            Assert.That(source, Does.Not.Contain("NavMass"));
            Assert.That(source, Does.Not.Contain("VisualScale"));
            Assert.That(source, Does.Not.Contain("BodyRadiusCm"));
            Assert.That(source, Does.Not.Contain("SpeedCmPerSecond"));
            Assert.That(source, Does.Not.Contain("obstacleRadiusCm: 0f"));
            Assert.That(source, Does.Not.Contain("massNavAgentIndex: 0"));
            Assert.That(source, Does.Not.Contain("slotIndex: 0"));
        }

        [Test]
        public void TotalWarObstacleOverlayPlans_AreBuiltAfterMassNavigationLoadsObstacleSsot()
        {
            string runtimeSource = File.ReadAllText(Path.Combine(TotalWarModRoot(), "Runtime", "TotalWarShowcaseRuntime.cs"));
            string spawnBody = ExtractMethodBody(runtimeSource, "private void SpawnScenario");

            int resetIndex = spawnBody.IndexOf("simulation.ResetRuntimeState(engine.World, _agentSeeds)", StringComparison.Ordinal);
            int obstaclePlanIndex = spawnBody.IndexOf("BuildObstacleOverlayPlans(simulation)", StringComparison.Ordinal);
            int enqueueIndex = spawnBody.IndexOf("EnqueueObstacleOverlaySpawns(engine", StringComparison.Ordinal);

            Assert.That(resetIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(obstaclePlanIndex, Is.GreaterThan(resetIndex),
                "Obstacle overlays must be planned from MassNavigation after ResetRuntimeState has loaded WorldConfig.Obstacles.");
            Assert.That(enqueueIndex, Is.GreaterThan(obstaclePlanIndex));
        }

        [Test]
        public void TotalWarObstacleOverlayTemplate_DoesNotAuthorConfigOwnedValues()
        {
            string modRoot = TotalWarModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "TotalWarShowcaseConfig.json"));
            JsonObject obstacleOverlay = config["obstacleOverlay"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar config must author obstacleOverlay.");

            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));
            JsonObject overlayTemplate = FindObjectById(templates, RequireString(obstacleOverlay, "templateId"));
            JsonObject components = overlayTemplate["components"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar obstacle overlay template must author components.");

            Assert.That(components.ContainsKey("TotalWarObstacleOverlay"), Is.False,
                "Obstacle overlay values come from TotalWarShowcaseConfig and MassNavigation obstacle radius.");

            string bindingSource = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarSpawnReceiptBindingSystem.cs"));
            string bindObstacle = ExtractMethodBody(bindingSource, "private void BindObstacleOverlay");
            Assert.That(bindObstacle, Does.Contain("must not author component"));
            Assert.That(bindObstacle, Does.Contain("ActiveConfig.ObstacleOverlay.ToComponent"));
        }

        [Test]
        public void TotalWarObstacleOverlayPresentation_UsesConfiguredWidthAndFormalStableId()
        {
            string source = File.ReadAllText(Path.Combine(
                TotalWarModRoot(),
                "Runtime",
                "TotalWarObstacleOverlayPresentationSystem.cs"));

            Assert.That(source, Does.Contain("PerformerBehaviorRuntimeUtility.ComposeVisualStableId"));
            Assert.That(source, Does.Contain("AssetKind.GroundOverlay"));
            Assert.That(source, Does.Contain("overlay.BorderWidthCm"));
            Assert.That(source, Does.Not.Contain("OverlayStableIdOffset"));
            Assert.That(source, Does.Not.Contain("MathF.Max"));
            Assert.That(source, Does.Not.Contain("0.08f"));
        }

        [Test]
        public void TotalWarFormationOutlines_UseRoadSplinesSampledFromVisualHeightmap()
        {
            string outlineSource = File.ReadAllText(Path.Combine(
                TotalWarModRoot(),
                "Runtime",
                "TotalWarFormationOutlinePresentationSystem.cs"));

            Assert.That(outlineSource, Does.Contain("RoadSplineBuffer"));
            Assert.That(outlineSource, Does.Contain("TryAddLine"));
            Assert.That(outlineSource, Does.Contain("ProjectToGround"));
            Assert.That(outlineSource, Does.Contain("CurveSampleCount"));
            Assert.That(outlineSource, Does.Contain("EmissionPositionEpsilonM"));
            Assert.That(outlineSource, Does.Contain("EmissionFacingEpsilonRadians"));
            Assert.That(outlineSource, Does.Contain("NormalizeAngleRadians"));
            Assert.That(outlineSource, Does.Not.Contain("CenterX.Equals(other.CenterX)"),
                "Formation outline dirty checks must use configured epsilon instead of exact transform floats.");
            Assert.That(outlineSource, Does.Not.Contain("OutlineCurveSamples"));
            Assert.That(outlineSource, Does.Contain("OutlineEmissionState"),
                "Formation outlines must cache emitted state so static formations do not resample terrain and upsert splines every frame.");
            Assert.That(outlineSource, Does.Not.Contain("GroundOverlayBuffer"),
                "Formation outlines must not be long flat GroundOverlay shapes; they need per-segment visual-heightmap samples.");
            Assert.That(outlineSource, Does.Not.Contain("GroundOverlayShape"));
        }

        [Test]
        public void TotalWarPresentationHotPathCollections_UseConfigDerivedCapacities()
        {
            string modRoot = TotalWarModRoot();
            string configSource = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarShowcaseConfig.cs"));
            string runtimeSource = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarShowcaseRuntime.cs"));
            string componentsSource = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarFormationComponents.cs"));
            string outlineSource = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarFormationOutlinePresentationSystem.cs"));
            string obstacleSource = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarObstacleOverlayPresentationSystem.cs"));

            Assert.That(configSource, Does.Contain("FormationOutlineOwnerCapacity => Formations.Length"));
            Assert.That(configSource, Does.Contain("FormationOutlineSplineCapacity"));
            Assert.That(componentsSource, Does.Contain("TotalWarFormationOutlineSegments"));
            Assert.That(configSource, Does.Contain("TotalWarFormationOutlineSegments.CountSplineSegments"));
            Assert.That(outlineSource, Does.Contain("TotalWarFormationOutlineSegments.CountSplineSegments"));
            Assert.That(outlineSource, Does.Not.Contain("TotalWarFormationOutlineShape.Rectangle => 4"));
            Assert.That(outlineSource, Does.Not.Contain("TotalWarFormationOutlineShape.Circle => 1"));
            Assert.That(runtimeSource, Does.Contain("new TotalWarFormationOutlinePresentationSystem(engine, this, config)"));
            Assert.That(runtimeSource, Does.Contain("new TotalWarObstacleOverlayPresentationSystem(engine, this, simulation.WorldConfig.Obstacles.Length)"));
            Assert.That(configSource, Does.Contain("InitialSelectionEntityCapacity { get; set; }"));
            Assert.That(runtimeSource, Does.Contain("config.InitialSelectionEntityCapacity"));
            Assert.That(runtimeSource, Does.Not.Contain("config.InitialSelectionScratchCapacity"));
            Assert.That(runtimeSource, Does.Not.Contain("new Entity[1]"));
            Assert.That(runtimeSource, Does.Contain("new HashSet<int>(_formationPlans.Length)"));
            Assert.That(runtimeSource, Does.Contain("simulation.Config.Scenario.Teams"));
            Assert.That(runtimeSource, Does.Not.Contain("new HashSet<int>()"));
            Assert.That(runtimeSource, Does.Not.Contain("new SortedSet<int>"));
            Assert.That(runtimeSource, Does.Not.Contain("SortedSet<int>"));

            Assert.That(outlineSource, Does.Contain("new List<int>(_stableIdCapacity)"));
            Assert.That(outlineSource, Does.Contain("new HashSet<int>(_stableIdCapacity)"));
            Assert.That(outlineSource, Does.Contain("new HashSet<int>(_ownerCapacity)"));
            Assert.That(outlineSource, Does.Contain("new Dictionary<int, OutlineEmissionState>(_ownerCapacity)"));
            Assert.That(outlineSource, Does.Contain("TrackStableId"));
            Assert.That(outlineSource, Does.Contain("RequireEmissionStateCapacity"));
            Assert.That(outlineSource, Does.Contain("foreach (ref var chunk in _engine.World.Query(in FormationOutlineQuery))"));
            Assert.That(outlineSource, Does.Contain("PublishFormationOutlineCountIfChanged"));
            Assert.That(outlineSource, Does.Contain("_lastPublishedFormationOutlineCount"));
            Assert.That(outlineSource, Does.Not.Contain("_engine.World.Query(\r\n            in FormationOutlineQuery,"));
            Assert.That(outlineSource, Does.Not.Contain("private readonly List<int> _currentStableIds = new();"));
            Assert.That(outlineSource, Does.Not.Contain("private readonly HashSet<int> _currentStableIdSet = new();"));
            Assert.That(outlineSource, Does.Not.Contain("private readonly Dictionary<int, OutlineEmissionState> _emittedStateByOwnerStableId = new();"));
            Assert.That(outlineSource, Does.Not.Contain("AddRange(_currentStableIds)"));

            Assert.That(obstacleSource, Does.Contain("new List<int>(_overlayCapacity)"));
            Assert.That(obstacleSource, Does.Contain("new HashSet<int>(_overlayCapacity)"));
            Assert.That(obstacleSource, Does.Contain("new Dictionary<int, ObstacleOverlayEmissionState>(_overlayCapacity)"));
            Assert.That(obstacleSource, Does.Contain("TrackStableId"));
            Assert.That(obstacleSource, Does.Contain("RequireEmissionStateCapacity"));
            Assert.That(obstacleSource, Does.Contain("foreach (ref var chunk in _engine.World.Query(in ObstacleOverlayQuery))"));
            Assert.That(obstacleSource, Does.Not.Contain("_engine.World.Query(\r\n            in ObstacleOverlayQuery,"));
            Assert.That(obstacleSource, Does.Not.Contain("private readonly List<int> _currentStableIds = new();"));
            Assert.That(obstacleSource, Does.Not.Contain("private readonly HashSet<int> _currentStableIdSet = new();"));
            Assert.That(obstacleSource, Does.Not.Contain("private readonly Dictionary<int, ObstacleOverlayEmissionState> _emittedStateByStableId = new();"));
            Assert.That(obstacleSource, Does.Not.Contain("AddRange(_currentStableIds)"));
        }

        [Test]
        public void MassFlowCrowdCost_UsesAgentLayerWorldsInsteadOfTeamWideLayerUnion()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassFlowSimulationState.cs"));

            Assert.That(source, Does.Contain("FlowRuntimeState"));
            Assert.That(source, Does.Contain("_flowRuntimeIndices"));
            Assert.That(source, Does.Contain("ResolveFlowStateIndex"));
            Assert.That(source, Does.Contain("CanFlowStateObserveAgent"));
            Assert.That(source, Does.Contain("flowState.Flow"));
            Assert.That(source, Does.Not.Contain("LayerInteractionMask"),
                "Flow crowd cost must be keyed by each agent world's layer pair, not a team-wide layer union.");
        }

        [Test]
        public void TotalWarOutlinePresentation_IgnoresDestroyPendingFormationAgents()
        {
            string source = File.ReadAllText(Path.Combine(
                TotalWarModRoot(),
                "Runtime",
                "TotalWarFormationOutlinePresentationSystem.cs"));

            Assert.That(source, Does.Contain("WithNone<PresentationDestroyPending>"),
                "Formation outlines must not render entities that are already in presentation destroy lifecycle.");
        }

        [Test]
        public void GroundOverlayAssetIds_DoNotAcceptCaseAliases()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Core", "Engine", "GameEngine.cs"));
            string body = ExtractMethodBody(source, "private static int ResolveGroundOverlayShapeId");

            Assert.That(body, Does.Contain("ignoreCase: false"));
            Assert.That(body, Does.Not.Contain("ignoreCase: true"));
        }

        [Test]
        public void MassNavigationOrderIngestion_ConsumesOnlyControllableAgents()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Systems",
                "MassNavigationOrderIngestionSystem.cs"));

            Assert.That(source, Does.Contain("MassNavigationControllable"),
                "OrderIngestion must make controllability a MassNavigation contract, not a showcase template accident.");
            Assert.That(source, Does.Contain("ResolveControllableAgent"),
                "Order completion must fail fast when a move order references an unbound controllable agent slot.");
            Assert.That(source, Does.Contain("TryGetControllableEntity"),
                "Order completion must resolve sparse controllable slots through AgentState instead of reading the list directly.");
            Assert.That(source, Does.Not.Contain("ControllableAgentSlots["),
                "OrderIngestion must not treat the exposed sparse slot list as an indexing API.");
            Assert.That(source, Does.Not.Contain("TotalWar"),
                "OrderIngestion must stay generic and not know the TotalWar showcase.");
        }

        [Test]
        public void MassNavigationOrderIngestion_PreallocatesCommandBucketsFromConfiguredRuntimeCapacity()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Systems",
                "MassNavigationOrderIngestionSystem.cs"));

            Assert.That(source, Does.Contain("capacity.OrderIngestionTokenCapacity"),
                "OrderIngestion token storage must use the explicit runtimeCapacity token capacity.");
            Assert.That(source, Does.Contain("capacity.OrderIngestionMemberCapacity"),
                "OrderIngestion member storage must use the explicit runtimeCapacity member capacity.");
            Assert.That(source, Does.Contain("new Dictionary<int, int>(_orderTokenCapacity)"));
            Assert.That(source, Does.Contain("new HashSet<int>(_orderTokenCapacity)"));
            Assert.That(source, Does.Contain("new List<int>(_orderTokenCapacity)"));
            Assert.That(source, Does.Contain("new List<OrderBucket>(_orderTokenCapacity)"));
            Assert.That(source, Does.Contain("_buckets.Add(new OrderBucket(_bucketMemberCapacity))"));
            Assert.That(source, Does.Contain("new List<int>(memberCapacity)"));
            Assert.That(source, Does.Contain("foreach (ref var chunk in _engine.World.Query(in Query))"));
            Assert.That(source, Does.Contain("scenarioRuntime.runtimeCapacity.orderIngestionTokenCapacity"));
            Assert.That(source, Does.Contain("scenarioRuntime.runtimeCapacity.orderIngestionMemberCapacity"));
            Assert.That(source, Does.Not.Contain("InitialSelectedEntityCapacity"),
                "OrderIngestion must not use selection capacity as a hidden alias for order token/member capacity.");
            Assert.That(source, Does.Not.Contain("_engine.World.Query(in Query,"),
                "OrderIngestion hot path must use chunk/span iteration instead of query lambdas.");
            Assert.That(source, Does.Not.Contain("private readonly Dictionary<int, int> _bucketIndexByToken = new();"),
                "OrderIngestion must not rely on Dictionary first-use resizing during large selection move frames.");
            Assert.That(source, Does.Not.Contain("public List<int> Members { get; } = new();"),
                "Order bucket member storage must be preallocated from explicit configuration.");
        }

        [Test]
        public void MassNavigationMetadataSync_UsesScenarioTeamOrderAsSsot()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Systems",
                "MassNavigationAgentMetadataSyncSystem.cs"));

            Assert.That(source, Does.Contain("simulation.Config.Scenario.Teams"));
            Assert.That(source, Does.Contain("_configuredTeamIds"));
            Assert.That(source, Does.Contain("ConfigureScenarioTeams(_configuredTeamIds)"));
            Assert.That(source, Does.Contain("MissingEntityLayerQuery"));
            Assert.That(source, Does.Contain("foreach (ref var chunk in _engine.World.Query(in Query))"));
            Assert.That(source, Does.Not.Contain("Array.Sort"));
            Assert.That(source, Does.Not.Contain("_teamScratch"));
            Assert.That(source, Does.Not.Contain("_engine.World.Query(in Query,"));
        }

        [Test]
        public void TotalWarMapUnload_DestroysAllTrackedMassNavigationAgents()
        {
            string source = File.ReadAllText(Path.Combine(
                TotalWarModRoot(),
                "Runtime",
                "TotalWarShowcaseRuntime.cs"));
            string unloadBody = ExtractMethodBody(source, "public Task HandleMapUnloadedAsync");

            Assert.That(unloadBody, Does.Contain("simulation.ResetRuntimeState(engine.World)"));
            Assert.That(unloadBody, Does.Not.Contain("DestroyFormationAgents"),
                "Map unload must destroy the complete tracked MassNavigation agent set, including soldiers.");
        }

        [Test]
        public void MassNavigationVisualScale_IsNavigationProfileMetadataNotPerformerSizeSsot()
        {
            string flowSource = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassFlowSimulationState.cs"));
            string performerSource = File.ReadAllText(Path.Combine(
                TotalWarModRoot(),
                "assets",
                "Presentation",
                "performers.json"));

            Assert.That(flowSource, Does.Contain("_visualScales"));
            Assert.That(flowSource, Does.Not.Contain("AgentBodyRadiusCm * _visualScales"));
            Assert.That(performerSource, Does.Contain("\"localScale\""),
                "Performer size is currently authored by performer definitions; MassNavigation visualScale remains solver/profile metadata.");
        }

        [Test]
        public void TotalWarSystems_AreGatedByShowcaseMapNotMassNavigationConfig()
        {
            string modRoot = TotalWarModRoot();
            string receiptSystem = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarSpawnReceiptBindingSystem.cs"));
            string formationSystem = File.ReadAllText(Path.Combine(modRoot, "Runtime", "TotalWarFormationRuntimeSystem.cs"));

            Assert.That(receiptSystem, Does.Contain("_runtime.IsCurrentShowcaseMap(_engine)"));
            Assert.That(formationSystem, Does.Contain("_runtime.IsCurrentShowcaseMap(_engine)"));
            Assert.That(receiptSystem, Does.Not.Contain("MassNavigationIds.IsCurrentNavigationMap"));
            Assert.That(formationSystem, Does.Not.Contain("MassNavigationIds.IsCurrentNavigationMap"));
        }

        [Test]
        public void MassNavigationAgentState_DestroyTrackedUsesPresentationLifecycleOnly()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState();
            Entity performerRoot = world.Create();
            Entity agent = world.Create(
                new MassNavigationAgentTag(),
                new MassNavigationControllable(),
                new MassNavigationAgentIndex { Value = 0 },
                new MassNavigationAgentProfile { Heavy = false, NavMass = 1f, VisualScale = 0.2f, BodyRadiusCm = 20f },
                new PresentationStableId { Value = 1001 },
                new PresentationDestroyEventPublished(),
                new PresentationOwnerHasPerformerPayload { Count = 1, RootCount = 1, SingleRootPerformer = performerRoot });

            state.RegisterAgentAtIndex(agent, agentIndex: 0, controllable: true);
            state.DestroyTracked(world);

            Assert.That(world.IsAlive(agent), Is.True);
            Assert.That(world.Has<PresentationDestroyPending>(agent), Is.True);
            Assert.That(world.Has<PresentationDestroyEventPublished>(agent), Is.False);
            Assert.That(world.Has<MassNavigationAgentTag>(agent), Is.False);
            Assert.That(world.Has<MassNavigationControllable>(agent), Is.False);
            Assert.That(world.Has<MassNavigationAgentIndex>(agent), Is.False);
            Assert.That(world.Has<MassNavigationAgentProfile>(agent), Is.False);
            Assert.That(state.TotalAgents, Is.EqualTo(0));
            Assert.That(state.ControllableAgentCount, Is.EqualTo(0));
            Assert.That(state.ControllableAgentSlotCount, Is.EqualTo(0));
        }

        [Test]
        public void MassNavigationAgentState_DestroyTrackedFailsWithoutPresentationStableId()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState();
            Entity agent = world.Create(new MassNavigationAgentTag());

            state.RegisterAgentAtIndex(agent, agentIndex: 0, controllable: true);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => state.DestroyTracked(world))!;
            Assert.That(ex.Message, Does.Contain("without PresentationStableId"));
            Assert.That(world.IsAlive(agent), Is.True);
        }

        [Test]
        public void MassNavigationAgentState_RegisterAgentAtIndexHandlesSparseResizeAndRejectsDuplicates()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState();
            Entity first = world.Create();
            Entity second = world.Create();

            state.RegisterAgentAtIndex(first, agentIndex: 5, controllable: true);

            Assert.That(state.TotalAgents, Is.EqualTo(6));
            Assert.That(state.ControllableAgentCount, Is.EqualTo(1));
            Assert.That(state.ControllableAgentSlotCount, Is.EqualTo(6));
            Assert.That(state.AllAgents[0], Is.EqualTo(Entity.Null));
            Assert.That(state.AllAgents[5], Is.EqualTo(first));
            Assert.That(state.ControllableAgentSlots[5], Is.EqualTo(first));
            Assert.That(state.TryGetControllableIndex(first, out int index), Is.True);
            Assert.That(index, Is.EqualTo(5));

            Assert.Throws<InvalidOperationException>(() => state.RegisterAgentAtIndex(second, agentIndex: 5, controllable: true));
        }

        [Test]
        public void MassNavigationAgentState_RegisterAgentAtIndexRejectsInvalidInputWithoutStateMutation()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState();
            Entity agent = world.Create();

            Assert.Throws<InvalidOperationException>(() => state.RegisterAgentAtIndex(agent, agentIndex: -1, controllable: true));

            Assert.That(state.SpawnedEntities, Is.Empty);
            Assert.That(state.AllAgents, Is.Empty);
            Assert.That(state.ControllableAgentSlots, Is.Empty);
            Assert.That(state.TryGetControllableIndex(agent, out _), Is.False);
        }

        [Test]
        public void MassFlowRuntimeProfile_RejectsBelowSemanticMinimumInsteadOfClamping()
        {
            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 100f,
                    localPositionYCm: 100f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer),
            };
            var obstacles = new[]
            {
                new MassNavigationObstacleConfig { LocalXCm = 5_000f, LocalYCm = 5_000f, RadiusCm = 100f },
            };

            flow.Reset(seeds, obstacles);

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitRuntimeProfile(0, 1, 0f, 1f, 20f, 800f, layer));
            Assert.That(flow.GetNavMass(0), Is.EqualTo(1f));

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitRuntimeProfile(0, 1, 1f, 0f, 20f, 800f, layer));
            Assert.That(flow.GetVisualScale(0), Is.EqualTo(1f));

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitRuntimeProfile(0, 1, 1f, 1f, 0f, 800f, layer));
            Assert.That(flow.GetBodyRadiusCm(0), Is.EqualTo(20f));
        }

        [Test]
        public void MassFlowUnitTargetApis_RejectOutOfRangeAgentIndex()
        {
            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 100f,
                    localPositionYCm: 100f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer),
            };

            var obstacles = new[]
            {
                new MassNavigationObstacleConfig { LocalXCm = 5_000f, LocalYCm = 5_000f, RadiusCm = 100f },
            };

            flow.Reset(seeds, obstacles);

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitTarget(flow.UnitCount, 100f, 100f));
            Assert.Throws<InvalidOperationException>(() => flow.ReleaseUnitToTeamTarget(flow.UnitCount));
            Assert.Throws<InvalidOperationException>(() => flow.HoldUnitAtCurrentPosition(flow.UnitCount));
        }

        [Test]
        public void MassFlowExternalDisplacementRange_CarriesPositionAndUnitTarget()
        {
            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 100f,
                    localPositionYCm: 100f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer),
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 200f,
                    localPositionYCm: 200f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer),
            };
            flow.Reset(seeds, new[]
            {
                new MassNavigationObstacleConfig { LocalXCm = 5_000f, LocalYCm = 5_000f, RadiusCm = 100f },
            });

            Assert.That(flow.SetUnitTarget(1, 400f, 500f), Is.True);
            flow.ApplyExternalDisplacementRange(startIndex: 1, count: 1, deltaXCm: 30f, deltaYCm: -20f);

            Assert.That(flow.GetPositionX(0), Is.EqualTo(100f));
            Assert.That(flow.GetPositionY(0), Is.EqualTo(100f));
            Assert.That(flow.GetPositionX(1), Is.EqualTo(230f));
            Assert.That(flow.GetPositionY(1), Is.EqualTo(180f));
            Assert.That(flow.TryGetUnitTarget(1, out float targetX, out float targetY), Is.True);
            Assert.That(targetX, Is.EqualTo(430f));
            Assert.That(targetY, Is.EqualTo(480f));
            Assert.That(flow.PendingEntitySyncCount, Is.EqualTo(2),
                "External displacement must mark carried agents dirty without clearing existing pending target-sync dirtiness.");
        }

        [Test]
        public void MassNavigationOrderPathAnchor_AdvancesAlongAuthoredOrderPathNotCurrentOffsetChase()
        {
            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 1000f,
                    localPositionYCm: 1000f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer),
            };
            flow.Reset(seeds, new[]
            {
                new MassNavigationObstacleConfig { Id = "anchor-test-obstacle", LocalXCm = 9000f, LocalYCm = 9000f, RadiusCm = 100f },
            });

            var world = World.Create();
            try
            {
                Entity agent = world.Create(new FacingDirection { AngleRad = 0f });
                var agentState = new MassNavigationAgentState();
                agentState.RegisterAgentAtIndex(agent, agentIndex: 0, controllable: true);
                var runtime = CreateTestNavGroupRuntime(agentCapacity: 1, groupMemberCapacity: 1);

                runtime.UpsertOrderMoveCommand(
                    flow,
                    agentState,
                    orderToken: 1,
                    memberIndices: new[] { 0 },
                    teamId: 1,
                    destinationWorldCm: new Vector2(5000f, 1000f),
                    formationMode: MassNavigationFormationMode.None,
                    rotationRadians: 0f);

                var positions = (float[])typeof(MassFlowSimulationState)
                    .GetField("_positionsCm", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(flow)!;
                positions[1] = 1800f;

                Assert.That(runtime.TryUpdateGroupMemberOrderPathAnchor(
                        flow,
                        unitIndex: 0,
                        lookaheadCm: 500f,
                        updateEpsilonCm: 1f,
                        out float anchorWorldX,
                        out float anchorWorldY,
                        out _),
                    Is.True);

                Assert.That(anchorWorldX, Is.EqualTo(1500f).Within(1f));
                Assert.That(anchorWorldY, Is.EqualTo(1000f).Within(1f),
                    "Order path anchors must stay on the authored order path; lateral avoidance drift must not become a follower chase target.");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void MassNavigationFollowerAnchor_IncludesPassiveDisplacementFromAuthoredOrderPath()
        {
            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 1000f,
                    localPositionYCm: 1000f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer),
            };
            flow.Reset(seeds, new[]
            {
                new MassNavigationObstacleConfig { Id = "follower-anchor-test-obstacle", LocalXCm = 9000f, LocalYCm = 9000f, RadiusCm = 100f },
            });

            var world = World.Create();
            try
            {
                Entity agent = world.Create(new FacingDirection { AngleRad = 0f });
                var agentState = new MassNavigationAgentState();
                agentState.RegisterAgentAtIndex(agent, agentIndex: 0, controllable: true);
                var runtime = CreateTestNavGroupRuntime(agentCapacity: 1, groupMemberCapacity: 1);

                runtime.UpsertOrderMoveCommand(
                    flow,
                    agentState,
                    orderToken: 1,
                    memberIndices: new[] { 0 },
                    teamId: 1,
                    destinationWorldCm: new Vector2(5000f, 1000f),
                    formationMode: MassNavigationFormationMode.None,
                    rotationRadians: 0f);

                var positions = (float[])typeof(MassFlowSimulationState)
                    .GetField("_positionsCm", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(flow)!;
                positions[0] = 1800f;
                positions[1] = 1220f;

                Assert.That(runtime.TryUpdateGroupMemberOrderPathAnchor(
                        flow,
                        unitIndex: 0,
                        lookaheadCm: 500f,
                        updateEpsilonCm: 1f,
                        out float pathAnchorWorldX,
                        out float pathAnchorWorldY,
                        out _),
                    Is.True);
                Assert.That(runtime.TryUpdateGroupMemberFollowerAnchor(
                        flow,
                        unitIndex: 0,
                        lookaheadCm: 500f,
                        updateEpsilonCm: 1f,
                        out float followerAnchorWorldX,
                        out float followerAnchorWorldY,
                        out _),
                    Is.True);

                Assert.That(pathAnchorWorldX, Is.EqualTo(2300f).Within(1f));
                Assert.That(pathAnchorWorldY, Is.EqualTo(1000f).Within(1f));
                Assert.That(followerAnchorWorldX, Is.EqualTo(pathAnchorWorldX).Within(1f));
                Assert.That(followerAnchorWorldY, Is.EqualTo(1220f).Within(1f),
                    "Follower anchors must add passive avoidance displacement back onto the authored order-path lookahead.");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void MassNavigationOrderMove_RotationChangeRefreshesOrderSlotTargets()
        {
            using MassNavigationGroupRuntimeFixture fixture = CreateGroupRuntimeFixture(
                new Vector2(1000f, 1000f),
                new Vector2(1180f, 1000f));

            fixture.Runtime.UpsertOrderMoveCommand(
                fixture.Flow,
                fixture.AgentState,
                orderToken: 11,
                memberIndices: new[] { 0, 1 },
                teamId: 1,
                destinationWorldCm: new Vector2(3000f, 3000f),
                formationMode: MassNavigationFormationMode.Line,
                rotationRadians: 0f);
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(0, out float beforeX, out float beforeY), Is.True);

            fixture.Runtime.UpsertOrderMoveCommand(
                fixture.Flow,
                fixture.AgentState,
                orderToken: 11,
                memberIndices: new[] { 0, 1 },
                teamId: 1,
                destinationWorldCm: new Vector2(3000f, 3000f),
                formationMode: MassNavigationFormationMode.Line,
                rotationRadians: MathF.PI * 0.5f);
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(0, out float afterX, out float afterY), Is.True);

            Assert.That((afterX - beforeX) * (afterX - beforeX) + (afterY - beforeY) * (afterY - beforeY),
                Is.GreaterThan(1f),
                "MassNavigation order groups must treat explicit rotation as slot-layout input, not ignore it behind a same-destination early return.");
        }

        [Test]
        public void MassNavigationGroupMemberRemoval_RewritesRemainingMemberOrderTargetsAndAnchors()
        {
            using MassNavigationGroupRuntimeFixture fixture = CreateGroupRuntimeFixture(
                new Vector2(1000f, 1000f),
                new Vector2(1180f, 1000f),
                new Vector2(1360f, 1000f),
                new Vector2(7000f, 7000f));

            fixture.Runtime.UpsertOrderMoveCommand(
                fixture.Flow,
                fixture.AgentState,
                orderToken: 21,
                memberIndices: new[] { 0, 1, 2 },
                teamId: 1,
                destinationWorldCm: new Vector2(3000f, 3000f),
                formationMode: MassNavigationFormationMode.Line,
                rotationRadians: 0f);
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(2, out float beforeX, out float beforeY), Is.True);
            Assert.That(fixture.Runtime.TryUpdateGroupMemberOrderPathAnchor(
                    fixture.Flow,
                    unitIndex: 2,
                    lookaheadCm: 500f,
                    updateEpsilonCm: 1f,
                    out _,
                    out _,
                    out int beforeAnchorRevision),
                Is.True);

            fixture.Runtime.UpsertOrderMoveCommand(
                fixture.Flow,
                fixture.AgentState,
                orderToken: 22,
                memberIndices: new[] { 1, 3 },
                teamId: 1,
                destinationWorldCm: new Vector2(6000f, 3000f),
                formationMode: MassNavigationFormationMode.Line,
                rotationRadians: 0f);
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(2, out float afterX, out float afterY), Is.True);
            Assert.That(fixture.Runtime.TryUpdateGroupMemberOrderPathAnchor(
                    fixture.Flow,
                    unitIndex: 2,
                    lookaheadCm: 500f,
                    updateEpsilonCm: 1f,
                    out _,
                    out _,
                    out int afterAnchorRevision),
                Is.True);

            Assert.That((afterX - beforeX) * (afterX - beforeX) + (afterY - beforeY) * (afterY - beforeY),
                Is.GreaterThan(1f),
                "Removing a member from an order group must immediately rewrite the surviving slot targets.");
            Assert.That(afterAnchorRevision, Is.GreaterThan(beforeAnchorRevision),
                "Rewritten order targets must invalidate and advance order-path anchors instead of retaining stale copied anchors.");
        }

        [Test]
        public void MassFlowHardResolve_SeparatesLargeAgentsByConfiguredBodyRadius()
        {
            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 4_500f,
                    localPositionYCm: 5_000f,
                    heavy: true,
                    navMass: 12f,
                    visualScale: 1f,
                    bodyRadiusCm: 720f,
                    speedCmPerSecond: 360f,
                    layer),
                new MassNavigationAgentSeed(
                    teamId: 2,
                    localPositionXCm: 5_500f,
                    localPositionYCm: 5_000f,
                    heavy: true,
                    navMass: 12f,
                    visualScale: 1f,
                    bodyRadiusCm: 720f,
                    speedCmPerSecond: 360f,
                    layer),
            };

            TeamManager.LoadConfig(new TeamConfig
            {
                DefaultRelationship = "Hostile",
                Relationships = new List<RelationshipEntry>(),
            });
            flow.Reset(seeds, new[]
            {
                new MassNavigationObstacleConfig { Id = "far_contract_obstacle", LocalXCm = 9_000f, LocalYCm = 9_000f, RadiusCm = 100f },
            });
            flow.Step(
                dt: 0f,
                world: World.Create(),
                navGroupRuntime: CreateTestNavGroupRuntime(agentCapacity: seeds.Length, groupMemberCapacity: seeds.Length),
                runHardResolve: true,
                hardResolveCandidateThresholdAgents: 1);

            float dx = flow.GetPositionX(0) - flow.GetPositionX(1);
            float dy = flow.GetPositionY(0) - flow.GetPositionY(1);
            float distance = MathF.Sqrt((dx * dx) + (dy * dy));
            Assert.That(distance, Is.GreaterThanOrEqualTo(1_439f),
                "Large formation agents must use configured bodyRadiusCm in hard resolve instead of the old small-agent hash neighborhood.");
        }

        [Test]
        public void MassFlowResolveUnitNavigableTarget_UsesUnitBodyRadiusBeforeSetUnitTarget()
        {
            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 4_000f,
                    localPositionYCm: 5_000f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 120f,
                    speedCmPerSecond: 800f,
                    layer),
            };

            flow.Reset(seeds, new[]
            {
                new MassNavigationObstacleConfig { Id = "target-projection-obstacle", LocalXCm = 5_000f, LocalYCm = 5_000f, RadiusCm = 200f },
            });

            Vector2 resolved = flow.ResolveUnitNavigableTarget(
                index: 0,
                xCm: 5_000f,
                yCm: 5_000f,
                hintX: 1f,
                hintY: 0f,
                minimumClearanceCm: 50f);
            Assert.That(flow.SetUnitTarget(0, resolved.X, resolved.Y, resetRecovery: true), Is.True);

            float dx = resolved.X - flow.GetObstacleX(0);
            float dy = resolved.Y - flow.GetObstacleY(0);
            float distance = MathF.Sqrt((dx * dx) + (dy * dy));
            Assert.That(distance, Is.GreaterThanOrEqualTo(flow.GetObstacleRadius(0) + flow.GetBodyRadiusCm(0) - 0.5f),
                "MassFlow target writes that represent agent slots must resolve through the unit's authored body radius before SetUnitTarget.");
        }

        [Test]
        public void RuntimeSpawnReceiptQueue_CanDrainPendingTotalWarReceiptsBeforeReset()
        {
            var channels = new RuntimeEntitySpawnReceiptChannelRegistry();
            int totalWarChannel = channels.Register("massNavigation.totalWar.runtimeSpawnReceipts");
            int otherChannel = channels.Register("some.other.runtimeSpawnReceipts");
            var queue = new RuntimeEntitySpawnReceiptQueue();

            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = otherChannel,
                ReceiptId = 1,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "other_template",
                MapId = new Ludots.Core.Map.MapId("other_map"),
            }), Is.True);
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = totalWarChannel,
                ReceiptId = 11,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_total_war_formation_agent",
                MapId = new Ludots.Core.Map.MapId("mass_navigation_total_war"),
            }), Is.True);
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = totalWarChannel,
                ReceiptId = 12,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_total_war_soldier_azure_light",
                MapId = new Ludots.Core.Map.MapId("mass_navigation_total_war"),
            }), Is.True);

            int drained = 0;
            while (queue.TryDequeueForChannel(totalWarChannel, out _))
            {
                drained++;
            }

            Assert.That(drained, Is.EqualTo(2));
            Assert.That(queue.CountForChannel(totalWarChannel), Is.EqualTo(0));
            Assert.That(queue.Count, Is.EqualTo(1), "Draining a showcase receipt channel must not consume unrelated receipt channels.");
            Assert.That(queue.TryDequeueForChannel(otherChannel, out RuntimeEntitySpawnReceipt other), Is.True);
            Assert.That(other.TemplateId, Is.EqualTo("other_template"));
        }

        [Test]
        public void TotalWarRuntime_ResetDrainsOwnReceiptChannelWithoutTouchingOtherChannels()
        {
            var runtime = new TotalWarShowcaseRuntime();
            var engine = new Ludots.Core.Engine.GameEngine();
            var spawnQueue = new RuntimeEntitySpawnQueue();
            var receipts = new RuntimeEntitySpawnReceiptQueue();
            var channels = new RuntimeEntitySpawnReceiptChannelRegistry();
            engine.SetService(CoreServiceKeys.RuntimeEntitySpawnQueue, spawnQueue);
            engine.SetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue, receipts);
            engine.SetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry, channels);

            JsonObject configJson = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "TotalWarShowcaseConfig.json"));
            TotalWarShowcaseConfig config = TotalWarShowcaseConfig.Load(configJson);
            int totalWarChannel = runtime.ResolveReceiptChannelId(engine, config);
            int otherChannel = channels.Register("other.runtimeSpawnReceipts");
            Assert.That(spawnQueue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_total_war_formation_agent",
                EmitReceipt = 1,
                ReceiptChannelId = totalWarChannel,
                ReceiptId = 11,
            }), Is.True);
            Assert.That(spawnQueue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "unrelated_template",
                EmitReceipt = 1,
                ReceiptChannelId = otherChannel,
                ReceiptId = 12,
            }), Is.True);
            Assert.That(receipts.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = totalWarChannel,
                ReceiptId = 1,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_total_war_formation_agent",
                MapId = new MapId(config.MapId),
            }), Is.True);
            Assert.That(receipts.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = otherChannel,
                ReceiptId = 2,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "unrelated_template",
                MapId = new MapId("other_map"),
            }), Is.True);

            runtime.ResetSpawnReceiptsForTests(engine, config);

            Assert.That(spawnQueue.CountForReceiptChannel(totalWarChannel), Is.EqualTo(0));
            Assert.That(spawnQueue.CountForReceiptChannel(otherChannel), Is.EqualTo(1));
            Assert.That(receipts.CountForChannel(totalWarChannel), Is.EqualTo(0));
            Assert.That(receipts.CountForChannel(otherChannel), Is.EqualTo(1));
        }

        [Test]
        public void RuntimeEntitySpawnQueue_RemovesOnlyMatchingReceiptChannel()
        {
            var queue = new RuntimeEntitySpawnQueue();
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "first",
                EmitReceipt = 1,
                ReceiptChannelId = 10,
            }), Is.True);
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "second",
                EmitReceipt = 1,
                ReceiptChannelId = 20,
            }), Is.True);
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "third",
                EmitReceipt = 1,
                ReceiptChannelId = 10,
            }), Is.True);

            Assert.That(queue.RemoveForReceiptChannel(10), Is.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out RuntimeEntitySpawnRequest remaining), Is.True);
            Assert.That(remaining.TemplateId, Is.EqualTo("second"));
            Assert.That(remaining.ReceiptChannelId, Is.EqualTo(20));
        }

        [Test]
        public void RuntimeTemplateSpawnCaches_UseExactTemplateKeys()
        {
            string[] files =
            {
                Path.Combine(FindRepoRoot(), "src", "Core", "Gameplay", "Spawning", "RuntimeEntitySpawnSystem.cs"),
                Path.Combine(FindRepoRoot(), "src", "Core", "Config", "TemplateEntityBatchSpawner.cs"),
            };

            foreach (string file in files)
            {
                string source = File.ReadAllText(file);
                Assert.That(source, Does.Contain("StringComparer.Ordinal"));
                Assert.That(source, Does.Not.Contain("StringComparer.OrdinalIgnoreCase"),
                    $"{Path.GetFileName(file)} must not permit case aliases for template ids.");
            }
        }

        [Test]
        public void RuntimeEntitySpawnSystem_RejectsTemplateCaseAlias()
        {
            string templateJson = """
[
  {
    "id": "mass_navigation_exact_template",
    "components": {
      "Name": { "Value": "Exact Template" },
      "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
      "FacingDirection": { "AngleRad": 0.0 },
      "AttributeBuffer": { "base": {} },
      "GameplayTagContainer": {},
      "TagCountContainer": {}
    }
  }
]
""";

            using TempTemplatePipeline temp = TempTemplatePipeline.Create(templateJson);
            var templates = new DataRegistry<EntityTemplate>(temp.Pipeline);
            templates.Load("Entities/templates.json", temp.Catalog);
            using var world = World.Create();
            var requests = new RuntimeEntitySpawnQueue(capacity: 4);
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                new EntityTemplateKeyRegistry(),
                new Ludots.Core.Presentation.PresentationStableIdAllocator());

            Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "Mass_Navigation_Exact_Template",
            }), Is.True);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("Mass_Navigation_Exact_Template"));
        }

        [Test]
        public void MassNavigationFormationRuntime_UsesConfiguredSemanticSpacing()
        {
            var semantics = new MassNavigationGroupSemantics
            {
                FormationLineSpacingCm = 240f,
                FormationSquareSpacingCm = 120f,
                FormationCircleSpacingCm = 300f,
                FormationCircleMinRadiusCm = 450f,
                FormationWedgeSpacingCm = 260f,
                FormationRotationEpsilonRadians = 0f,
            };
            semantics.Validate();
            var runtime = new MassNavigationFormationRuntime(semantics);
            float[] baseX = new float[4];
            float[] baseY = new float[4];
            float[] offsetX = new float[4];
            float[] offsetY = new float[4];

            runtime.BuildOffsets(baseX, baseY, offsetX, offsetY, 4, MassNavigationFormationMode.Square, 0f);

            Assert.That(baseX, Is.EqualTo(new[] { -60f, 60f, -60f, 60f }));
            Assert.That(baseY, Is.EqualTo(new[] { -60f, -60f, 60f, 60f }));

            runtime.BuildOffsets(baseX, baseY, offsetX, offsetY, 3, MassNavigationFormationMode.Line, 0f);

            Assert.That(baseX.Take(3).ToArray(), Is.EqualTo(new[] { -240f, 0f, 240f }));
            Assert.That(baseY.Take(3).ToArray(), Is.EqualTo(new[] { 0f, 0f, 0f }));
        }

        [Test]
        public void RoadSplineBuffer_TransientFormationOutlinesDoNotAccumulate()
        {
            var buffer = new RoadSplineBuffer(capacity: 8);
            var start = new System.Numerics.Vector3(1f, 0f, 1f);
            var end = new System.Numerics.Vector3(2f, 0f, 1f);

            for (int frame = 0; frame < 4; frame++)
            {
                Assert.That(buffer.TryAddLine(0, in start, in end, 0.1f, Vector4.One, Vector4.One, 0.1f), Is.True);
                Assert.That(buffer.TryAddLine(0, in start, in end, 0.1f, Vector4.One, Vector4.One, 0.1f), Is.True);
                Assert.That(buffer.Count, Is.EqualTo(2));
                buffer.ClearTransient();
                Assert.That(buffer.Count, Is.EqualTo(0));
            }
        }

        [Test]
        public void TotalWarPlayable_PlayerSelectionCancelMarkersMoveOutlinesAndCulling_WorkThroughFormalRuntimeChains()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should spawn formation and soldier agents, bind receipts, and seed the authored formation selection.");

            Assert.That(simulation.AgentState.TotalAgents, Is.EqualTo(TotalWarAcceptance.ExpectedTotalAgents));
            Assert.That(simulation.AgentState.ControllableAgentCount, Is.EqualTo(TotalWarAcceptance.ExpectedTotalFormations));
            Assert.That(simulation.AgentState.ControllableAgentSlotCount, Is.EqualTo(TotalWarAcceptance.ExpectedTotalFormations));
            AssertFormationAgentsDoNotOverlap(engine, simulation);
            Assert.That(SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext).Length,
                Is.EqualTo(TotalWarAcceptance.ExpectedInitialSelection));
            AssertInitialSelectionTargetsFormationAgents(engine);
            Assert.That(CountSelectionMarkerPerformers(engine), Is.EqualTo(TotalWarAcceptance.ExpectedInitialSelection),
                "Initial selection markers must be created by performer rules from SelectionMemberAdded events.");

            AssertFormationOutlines(engine);
            AssertObstacleOverlays(engine, simulation);
            AssertCullingProbeAndDebugDraw(engine);

            Entity[] initialSelection = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            DragSelect(engine, GetInputBackend(engine), ProjectEntitiesDragRect(engine, initialSelection));
            TickUntil(
                engine,
                () => simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection &&
                      CountSelectionMarkerPerformers(engine) == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: BuildSelectionDiagnostics(engine));

            LeftClick(engine, GetInputBackend(engine), WorldToScreen(engine, TotalWarAcceptance.EmptyGroundWorldCm));
            TickUntil(
                engine,
                () => simulation.SelectedCount == 0 &&
                      SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext).Length == 0 &&
                      CountSelectionMarkerPerformers(engine) == 0,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Empty ground click should clear LivePrimary selection and destroy scoped marker performers.");

            DragSelect(engine, GetInputBackend(engine), ProjectEntitiesDragRect(engine, initialSelection));
            TickUntil(
                engine,
                () => simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection &&
                      CountSelectionMarkerPerformers(engine) == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: BuildSelectionDiagnostics(engine));

            int rejectsBeforeMove = simulation.CommandRejectsTotal;
            Vector2 moveTargetScreen = WorldToScreen(engine, TotalWarAcceptance.MoveTargetWorldCm);
            AssertOutsideMinimapInteractiveRegion(engine, moveTargetScreen);
            WorldCmInt2 expectedMoveTarget = ResolveGroundWorldCm(engine, moveTargetScreen);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => simulation.LastCommandSelectionCount == TotalWarAcceptance.ExpectedInitialSelection &&
                      simulation.CommandRejectsTotal == rejectsBeforeMove &&
                      CountActiveMoveOrders(engine, simulation) > 0,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Right-click command should flow through PlayerInputHandler, MassNavigationLocalCommandInputSystem, and OrderBuffer.");

            Assert.That(simulation.HasCommandFocus, Is.True);
            Assert.That(simulation.CommandFocusXCm, Is.EqualTo(expectedMoveTarget.X).Within(1f));
            Assert.That(simulation.CommandFocusYCm, Is.EqualTo(expectedMoveTarget.Y).Within(1f));
        }

        [Test]
        public void TotalWarPlayable_MoveOrdersPreserveFacingAndRotateOrdersDriveSoldierSlots()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be fully spawned and selected before movement/facing verification.");

            Entity formation = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext)[0];
            Assert.That(engine.World.TryGet(formation, out MassNavigationAgentIndex formationAgentIndex), Is.True);
            Assert.That(engine.World.TryGet(formation, out TotalWarFormationAgent formationAgent), Is.True);
            float initialFacing = engine.World.Get<FacingDirection>(formation).AngleRad;
            int soldierAgentIndex = FindFirstSoldierAgentIndex(engine, formationAgent.FormationIndex);
            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(soldierAgentIndex, out float soldierTargetBeforeX, out float soldierTargetBeforeY), Is.True);
            Vector2 soldierBefore = simulation.GetAgentLocalPositionCm(soldierAgentIndex);

            Vector2 moveTargetScreen = WorldToScreen(engine, TotalWarAcceptance.MoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => CountActiveMoveOrders(engine, simulation) > 0,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Right-click move should submit a MassNavigation order before facing verification.");

            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(
                    formationAgentIndex.Value,
                    out float formationOrderWorldX,
                    out float formationOrderWorldY),
                Is.True);
            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(soldierAgentIndex, out float soldierTargetAfterOrderX, out float soldierTargetAfterOrderY), Is.True);
            Vector2 formationLocal = simulation.GetAgentLocalPositionCm(formationAgentIndex.Value);
            float currentFormationDx = soldierTargetAfterOrderX - formationLocal.X;
            float currentFormationDy = soldierTargetAfterOrderY - formationLocal.Y;
            Assert.That((currentFormationDx * currentFormationDx) + (currentFormationDy * currentFormationDy),
                Is.LessThan(2_500_000f),
                "Carrier-mode soldier targets must stay near the formation's current resolved MassNavigation center, not jump toward the final formation order path.");

            Tick(engine, TotalWarAcceptance.FrameBudgetForInteraction);
            float facingAfterMove = engine.World.Get<FacingDirection>(formation).AngleRad;
            Assert.That(facingAfterMove, Is.EqualTo(initialFacing).Within(0.0001f),
                "Moving a formation must not implicitly rotate it toward the destination.");
            Vector2 soldierAfterMove = simulation.GetAgentLocalPositionCm(soldierAgentIndex);
            float soldierMoveDeltaX = soldierAfterMove.X - soldierBefore.X;
            float soldierMoveDeltaY = soldierAfterMove.Y - soldierBefore.Y;
            Assert.That((soldierMoveDeltaX * soldierMoveDeltaX) + (soldierMoveDeltaY * soldierMoveDeltaY), Is.GreaterThan(1f),
                "Soldier MassNavigation agents must actually move after a formation move order, not just receive stale slot targets.");
            AssertMassFlowEntityPositionSynced(engine, simulation, formationAgentIndex.Value);
            AssertMassFlowEntityPositionSynced(engine, simulation, soldierAgentIndex);

            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(TotalWarAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(TotalWarAcceptance.RotateRightActionId);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInputRelease);
            TickUntil(
                engine,
                () => MathF.Abs(NormalizeAngleRadians(engine.World.Get<FacingDirection>(formation).AngleRad - initialFacing)) > 0.0001f,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Explicit rotate input should change the selected formation FacingDirection.");

            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(soldierAgentIndex, out float soldierTargetAfterX, out float soldierTargetAfterY), Is.True);
            float targetDeltaX = soldierTargetAfterX - soldierTargetBeforeX;
            float targetDeltaY = soldierTargetAfterY - soldierTargetBeforeY;
            Assert.That((targetDeltaX * targetDeltaX) + (targetDeltaY * targetDeltaY), Is.GreaterThan(1f),
                "Soldier slot targets must follow explicit formation facing changes.");
            Assert.That(float.IsFinite(formationOrderWorldX), Is.True);
            Assert.That(float.IsFinite(formationOrderWorldY), Is.True);
        }

        [Test]
        public void TotalWarPlayable_NonLocalPlayerOwnerFormationSelectionRejectsRightClickMoveOrder()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be fully spawned before non-local formation command verification.");

            AssertLocalPlayerOwnerFormations(engine);
            int localPlayerId = ResolveLocalPlayerOwnerId(engine);
            Entity enemyFormation = FindNonLocalPlayerOwnerFormation(engine, localPlayerId);
            Assert.That(engine.World.TryGet(enemyFormation, out MassNavigationAgentIndex enemyAgentIndex), Is.True);
            Assert.That(engine.World.TryGet(enemyFormation, out PlayerOwner enemyOwner), Is.True);
            Assert.That(enemyOwner.PlayerId, Is.Not.EqualTo(localPlayerId));
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(
                    enemyAgentIndex.Value,
                    out float _,
                    out float _),
                Is.False);

            SelectFormations(engine, new[] { enemyFormation });
            TickUntil(
                engine,
                () => simulation.SelectedCount == 1,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored selection should contain the non-local formation agent.");

            int rejectsBeforeMove = simulation.CommandRejectsTotal;
            Vector2 moveTargetScreen = WorldToScreen(engine, TotalWarAcceptance.MoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => simulation.CommandRejectsTotal == rejectsBeforeMove + 1,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Right-clicking a selected non-local formation must be rejected at the MassNavigation command boundary.");

            Assert.That(CountActiveMoveOrders(engine, simulation), Is.EqualTo(0));
            Assert.That(engine.World.TryGet(enemyFormation, out enemyOwner), Is.True);
            Assert.That(enemyOwner.PlayerId, Is.Not.EqualTo(localPlayerId));
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(
                    enemyAgentIndex.Value,
                    out float _,
                    out float _),
                Is.False);

            float enemyFacingBeforeRotate = engine.World.Get<FacingDirection>(enemyFormation).AngleRad;
            int rejectsBeforeRotate = simulation.CommandRejectsTotal;
            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(TotalWarAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(TotalWarAcceptance.RotateRightActionId);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInputRelease);

            Assert.That(
                NormalizeAngleRadians(engine.World.Get<FacingDirection>(enemyFormation).AngleRad - enemyFacingBeforeRotate),
                Is.EqualTo(0f).Within(0.0001f),
                "Q/E rotation must use the same local PlayerOwner command boundary as right-click move orders.");
            Assert.That(simulation.CommandRejectsTotal, Is.GreaterThanOrEqualTo(rejectsBeforeRotate + 1));
        }

        [Test]
        public void TotalWarPlayable_BoxSelectionOnlySelectsLocalCommandableFormations()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be fully spawned before box selection ownership verification.");

            AssertFormationSelectionCandidateFacts(engine);
            Entity[] formations = CaptureFormationAgents(engine, TotalWarAcceptance.ExpectedTotalFormations);
            DragSelect(engine, GetInputBackend(engine), ProjectEntitiesDragRect(engine, formations));
            int selectorTeamId = ResolveSelectionOwnerTeamId(engine);
            TickUntil(
                engine,
                () => simulation.SelectedCount == CountFriendlyTeamFormations(engine, selectorTeamId),
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Player box selection should include only formations accepted by the configured Friendly relationship filter.");

            Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            Assert.That(selected.Length, Is.EqualTo(CountFriendlyTeamFormations(engine, selectorTeamId)));
            for (int i = 0; i < selected.Length; i++)
            {
                Entity entity = selected[i];
                Assert.That(engine.World.Has<TotalWarFormationAgent>(entity), Is.True);
                Assert.That(engine.World.TryGet(entity, out Team team), Is.True);
                Assert.That(RelationshipFilterUtil.Passes(RelationshipFilter.Friendly, selectorTeamId, team.Id), Is.True);
            }
        }

        [Test]
        public void TotalWarPlayable_MixedLocalAndNonLocalSelectionRejectsRotateForWholeSelection()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be fully spawned before mixed selection rotate verification.");

            int localPlayerId = ResolveLocalPlayerOwnerId(engine);
            Entity localFormation = FindLocalPlayerOwnerFormation(engine, localPlayerId);
            Entity enemyFormation = FindNonLocalPlayerOwnerFormation(engine, localPlayerId);
            float localFacingBefore = engine.World.Get<FacingDirection>(localFormation).AngleRad;
            float enemyFacingBefore = engine.World.Get<FacingDirection>(enemyFormation).AngleRad;
            SelectFormations(engine, new[] { localFormation, enemyFormation });
            TickUntil(
                engine,
                () => simulation.SelectedCount == 2,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored mixed selection should enter MassNavigation's selected snapshot.");

            int rejectsBeforeRotate = simulation.CommandRejectsTotal;
            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(TotalWarAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(TotalWarAcceptance.RotateRightActionId);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInputRelease);

            Assert.That(
                NormalizeAngleRadians(engine.World.Get<FacingDirection>(localFormation).AngleRad - localFacingBefore),
                Is.EqualTo(0f).Within(0.0001f),
                "Mixed local/non-local selection must not partially rotate local formations.");
            Assert.That(
                NormalizeAngleRadians(engine.World.Get<FacingDirection>(enemyFormation).AngleRad - enemyFacingBefore),
                Is.EqualTo(0f).Within(0.0001f),
                "Mixed local/non-local selection must not rotate enemy formations.");
            Assert.That(simulation.CommandRejectsTotal, Is.GreaterThanOrEqualTo(rejectsBeforeRotate + 1));
        }

        [Test]
        public void TotalWarPlayable_SolverWindowRebaseDoesNotCarrySoldiersAwayFromFormation()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be ready before solver-window rebase verification.");

            Entity formation = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext)[0];
            Assert.That(engine.World.TryGet(formation, out MassNavigationAgentIndex formationAgentIndex), Is.True);
            Assert.That(engine.World.TryGet(formation, out TotalWarFormationAgent formationAgent), Is.True);
            int soldierAgentIndex = FindFirstSoldierAgentIndex(engine, formationAgent.FormationIndex);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInteraction);

            Vector2 beforeOffset = AgentWorldOffset(simulation, soldierAgentIndex, formationAgentIndex.Value);
            simulation.FocusSimulationWindow(TotalWarAcceptance.SolverWindowRebaseFocusWorldCm);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInteraction);
            Vector2 afterOffset = AgentWorldOffset(simulation, soldierAgentIndex, formationAgentIndex.Value);

            Assert.That(simulation.SolverWindowMovesTotal, Is.GreaterThan(0));
            Assert.That(Vector2.DistanceSquared(afterOffset, beforeOffset),
                Is.LessThan(TotalWarAcceptance.SoldierFormationOffsetRebaseToleranceSq),
                "Moving the solver window must not be interpreted as formation displacement by Total War soldier carrier sync.");
            AssertMassFlowEntityPositionSynced(engine, simulation, formationAgentIndex.Value);
            AssertMassFlowEntityPositionSynced(engine, simulation, soldierAgentIndex);
        }

        [Test]
        public void TotalWarPlayable_MultipleFormationMoveOrdersPreserveRelativeFormationSpacing()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be fully spawned before multi-formation order verification.");

            Entity[] formations = CaptureFormationAgents(engine, expectedCount: 3);
            float initialMinDistanceSq = MinPairDistanceSq(engine, simulation, formations, useOrderTargets: false);
            SelectFormations(engine, formations);
            TickUntil(
                engine,
                () => simulation.SelectedCount == formations.Length,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored selection should contain the chosen formation agents.");

            Vector2 moveTargetScreen = WorldToScreen(engine, TotalWarAcceptance.MultiFormationMoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => CountActiveMoveOrders(engine, simulation) == formations.Length,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Multi-formation right-click should submit one shared MassNavigation order per selected formation.");

            float orderMinDistanceSq = MinPairDistanceSq(engine, simulation, formations, useOrderTargets: true);
            Assert.That(
                orderMinDistanceSq,
                Is.GreaterThanOrEqualTo(initialMinDistanceSq * TotalWarAcceptance.MultiFormationSpacingRetentionRatio),
                "Multiple formation agents must translate their current relative shape to the move target instead of being repacked into a compact fallback layout.");
        }

        [Test]
        public void TotalWarPlayable_ResetClearsSelectedMarkersAndDestroysTrackedAgentsThroughPresentationLifecycle()
        {
            using GameEngine engine = CreatePlayableTotalWarEngine();
            engine.LoadMap("mass_navigation_total_war");
            Tick(engine, TotalWarAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalAgents &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection &&
                      CountSelectionMarkerPerformers(engine) == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be fully spawned and selected before reset.");

            Entity[] previousAgents = CaptureTrackedAgents(simulation);
            Entity[] previousObstacleOverlays = CaptureObstacleOverlays(engine, simulation.NavigationObstacleCount);

            simulation.RequestSceneReset();
            TickUntil(
                engine,
                () => simulation.SceneResetCount > 0 &&
                      simulation.SelectedCount == 0 &&
                      SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext).Length == 0 &&
                      CountSelectionMarkerPerformers(engine) == 0 &&
                      CountTrackedAgentRuntimeTags(engine) == 0,
                maxFrames: TotalWarAcceptance.FrameBudgetForInteraction,
                failureMessage: "Scene reset should clear selection, remove scoped marker performers, and strip runtime agent tags before respawn.");

            TickUntil(
                engine,
                () => CountAlive(engine, previousAgents) == 0 &&
                      CountAlive(engine, previousObstacleOverlays) == 0 &&
                      CountPresentationDestroyPending(engine) == 0 &&
                      CountSelectionMarkerPerformers(engine) == 0,
                maxFrames: TotalWarAcceptance.FrameBudgetForPresentationDestroy,
                failureMessage: "Presentation lifecycle should finalize previously tracked soldiers, obstacle overlays, and scoped markers after reset.");
        }

        [Test]
        public void SelectionMarkerRules_CreateAndDestroyScopedPerformersThroughSelectionEvents()
        {
            var world = World.Create();
            try
            {
                var selectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var selection = new SelectionRuntime(
                    world,
                    new SelectionRuntimeConfig
                    {
                        TargetFilter = new SelectionTargetFilterConfig { RelationFilter = "All" },
                    },
                    selectionKeys);
                var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                var commands = new PerformerCommandBuffer();
                var definitions = new PerformerDefinitionRegistry();
                int markerDefId = definitions.Register("test_selection_marker", new PerformerDefinition());
                int agentDefId = definitions.Register("test_agent", new PerformerDefinition
                {
                    Rules = new[]
                    {
                        new PerformerRule
                        {
                            Event = new EventFilter
                            {
                                Kind = PresentationEventKind.SelectionMemberAdded,
                                KeyId = selectionKeys.Register(SelectionSetKeys.LivePrimary),
                            },
                            Command = new PerformerCommand
                            {
                                CommandKind = PerformerCommandKind.CreatePerformer,
                                PerformerDefinitionId = markerDefId,
                                ScopeSource = PerformerCommandScopeSource.SourceStableId,
                                AnchorKind = PresentationAnchorKind.Entity,
                            },
                        },
                        new PerformerRule
                        {
                            Event = new EventFilter
                            {
                                Kind = PresentationEventKind.SelectionMemberRemoved,
                                KeyId = selectionKeys.Register(SelectionSetKeys.LivePrimary),
                            },
                            Command = new PerformerCommand
                            {
                                CommandKind = PerformerCommandKind.DestroyScopedPerformer,
                                PerformerDefinitionId = markerDefId,
                                ScopeSource = PerformerCommandScopeSource.SourceStableId,
                            },
                        },
                    },
                });

                var runtime = new PerformerEntityRuntime(world);
                using var selectionEvents = new SelectionPresentationEventSystem(world, selection, events);
                using var rules = new PerformerRuleSystem(
                    world,
                    events,
                    commands,
                    definitions,
                    runtime,
                    new Ludots.Core.GraphRuntime.GraphProgramRegistry(),
                    new Ludots.Core.NodeLibraries.GASGraph.Host.GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null),
                    new System.Collections.Generic.Dictionary<string, object>());

                Entity owner = world.Create();
                Entity unit = world.Create(
                    new PresentationStableId { Value = 42 },
                    VisualTransform.Default,
                    new CullState { IsVisible = true });
                Entity rootPerformer = runtime.CreateHierarchy(
                    definitions,
                    agentDefId,
                    unit,
                    scopeId: 42,
                    PresentationAnchorKind.Entity,
                    System.Numerics.Vector3.Zero,
                    stableId: 1001,
                    parent: Entity.Null,
                    definitions.Get(agentDefId));

                Assert.That(selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, new[] { unit }), Is.True);
                selectionEvents.Update(0.016f);
                rules.Update(0.016f);
                Assert.That(commands.Count, Is.EqualTo(1));
                PerformerCommand create = commands.GetSpan()[0];
                Assert.That(create.CommandKind, Is.EqualTo(PerformerCommandKind.CreatePerformer));
                Assert.That(create.PerformerDefinitionId, Is.EqualTo(markerDefId));
                Assert.That(create.Source, Is.EqualTo(unit));
                Assert.That(create.ParentEntity, Is.EqualTo(rootPerformer));
                Assert.That(create.ScopeTag, Is.EqualTo(42));
                commands.Clear();

                Assert.That(selection.ClearSelection(owner, SelectionSetKeys.LivePrimary), Is.True);
                selectionEvents.Update(0.016f);
                rules.Update(0.016f);
                Assert.That(commands.Count, Is.EqualTo(1));
                PerformerCommand destroy = commands.GetSpan()[0];
                Assert.That(destroy.CommandKind, Is.EqualTo(PerformerCommandKind.DestroyScopedPerformer));
                Assert.That(destroy.PerformerDefinitionId, Is.EqualTo(markerDefId));
                Assert.That(destroy.Source, Is.EqualTo(unit));
                Assert.That(destroy.ScopeTag, Is.EqualTo(42));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void MassNavigationRuntime_UnloadGatesByMapAndClearsCullingOverride()
        {
            var runtime = new MassNavigationMod.Runtime.MassNavigationRuntime(new NullModContext());
            var engine = new Ludots.Core.Engine.GameEngine();
            var focus = new CameraCullingFocusOverride
            {
                Enabled = true,
                SourceId = "battlefield_overview",
            };
            engine.SetService(CoreServiceKeys.CameraCullingFocusOverride, focus);
            engine.InitializeWithConfigPipeline(
                new System.Collections.Generic.List<string>
                {
                    Path.Combine(FindRepoRoot(), "mods", "LudotsCoreMod"),
                    Path.Combine(FindRepoRoot(), "mods", "CoreInputMod"),
                    Path.Combine(FindRepoRoot(), "mods", "capabilities", "camera", "CameraProfilesMod"),
                    Path.Combine(FindRepoRoot(), "mods", "capabilities", "navigation", "MassNavigationMod")
                },
                Path.Combine(FindRepoRoot(), "assets"));

            var unrelated = engine.CreateContext();
            unrelated.Set(CoreServiceKeys.MapId, new MapId("unrelated_map"));
            runtime.HandleMapUnloadedAsync(unrelated).GetAwaiter().GetResult();

            Assert.That(focus.Enabled, Is.True);

            var massNav = engine.CreateContext();
            massNav.Set(CoreServiceKeys.MapId, new MapId("mass_navigation"));
            runtime.HandleMapUnloadedAsync(massNav).GetAwaiter().GetResult();

            Assert.That(focus.Enabled, Is.False);
            Assert.That(focus.SourceId, Is.EqualTo(string.Empty));
            engine.Dispose();
        }

        [Test]
        public void MassNavigationRuntime_SuspendClearsCullingOverrideWithoutResettingScenario()
        {
            var runtime = new MassNavigationMod.Runtime.MassNavigationRuntime(new NullModContext());
            var engine = new Ludots.Core.Engine.GameEngine();
            var focus = new CameraCullingFocusOverride
            {
                Enabled = true,
                SourceId = "battlefield_overview",
            };
            engine.SetService(CoreServiceKeys.CameraCullingFocusOverride, focus);
            engine.InitializeWithConfigPipeline(MassNavigationDependencyPaths(), Path.Combine(FindRepoRoot(), "assets"));
            var simulation = new MassNavigationSimulationRuntime(MassNavigationConfig.Load(ReadObject(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "assets",
                "MassNavigationConfig.json"))));
            simulation.MarkScenarioSpawned();
            engine.SetService(MassNavigationMod.MassNavigationKeys.SimulationRuntime, simulation);
            Assert.That(simulation.ScenarioSpawnCount, Is.EqualTo(1));

            var unrelated = engine.CreateContext();
            unrelated.Set(CoreServiceKeys.MapId, new MapId("unrelated_map"));
            runtime.HandleMapSuspendedAsync(unrelated).GetAwaiter().GetResult();

            Assert.That(focus.Enabled, Is.True);
            Assert.That(simulation.ScenarioSpawnCount, Is.EqualTo(1));

            var massNav = engine.CreateContext();
            massNav.Set(CoreServiceKeys.MapId, new MapId("mass_navigation"));
            runtime.HandleMapSuspendedAsync(massNav).GetAwaiter().GetResult();

            Assert.That(focus.Enabled, Is.False);
            Assert.That(focus.SourceId, Is.EqualTo(string.Empty));
            Assert.That(simulation.ScenarioSpawnCount, Is.EqualTo(1),
                "MapSuspended must release global presentation ownership without treating the MassNavigation map as unloaded.");
            engine.Dispose();
        }

        [Test]
        public void MassNavigationControlSystem_ResetRemovesOwnPendingSpawnRequests()
        {
            var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(MassNavigationDependencyPaths(), Path.Combine(FindRepoRoot(), "assets"));
            var spawnQueue = new RuntimeEntitySpawnQueue();
            var channels = new RuntimeEntitySpawnReceiptChannelRegistry();
            engine.SetService(CoreServiceKeys.RuntimeEntitySpawnQueue, spawnQueue);
            engine.SetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry, channels);
            var simulation = new MassNavigationSimulationRuntime(MassNavigationConfig.Load(ReadObject(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "assets",
                "MassNavigationConfig.json"))));
            int massNavChannel = channels.Register(MassNavigationMod.MassNavigationIds.RuntimeSpawnReceiptChannelKey);
            int otherChannel = channels.Register("other.runtimeSpawnReceipts");
            Assert.That(spawnQueue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_total_war_soldier_azure_light",
                EmitReceipt = 1,
                ReceiptChannelId = massNavChannel,
            }), Is.True);
            Assert.That(spawnQueue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "other_template",
                EmitReceipt = 1,
                ReceiptChannelId = otherChannel,
            }), Is.True);

            var control = new MassNavigationMod.Systems.MassNavigationControlSystem(engine, simulation);
            InvokePrivate(control, "ResetRuntimeState");

            Assert.That(spawnQueue.CountForReceiptChannel(massNavChannel), Is.EqualTo(0));
            Assert.That(spawnQueue.CountForReceiptChannel(otherChannel), Is.EqualTo(1));
            engine.Dispose();
        }

        [Test]
        public void MassNavigationCameraRequests_UseVirtualCameraProfilesAsPoseSsot()
        {
            string runtimeSource = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassNavigationRuntime.cs"));

            Assert.That(runtimeSource, Does.Not.Contain("MassNavigationTacticalCameraDistanceCm"));
            Assert.That(runtimeSource, Does.Not.Contain("MassNavigationStrategicCameraDistanceCm"));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestCameraJump"), Does.Not.Contain("DistanceCm ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestTacticalCameraReset"), Does.Not.Contain("DistanceCm ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestStrategicCameraReset"), Does.Not.Contain("DistanceCm ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestCameraJump"), Does.Not.Contain("Pitch ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestTacticalCameraReset"), Does.Not.Contain("Pitch ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestStrategicCameraReset"), Does.Not.Contain("Pitch ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestCameraJump"), Does.Not.Contain("Yaw ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestTacticalCameraReset"), Does.Not.Contain("Yaw ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestStrategicCameraReset"), Does.Not.Contain("Yaw ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestCameraJump"), Does.Not.Contain("FovYDeg ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestTacticalCameraReset"), Does.Not.Contain("FovYDeg ="));
            Assert.That(ExtractMethodBody(runtimeSource, "RequestStrategicCameraReset"), Does.Not.Contain("FovYDeg ="));
        }

        [Test]
        public void MassNavigationPanel_HidesGenericAgentCountControlsForFormationOwnedScenario()
        {
            string panelSource = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "UI",
                "MassNavigationPanelController.cs"));

            Assert.That(panelSource, Does.Contain("AutoSpawnConfiguredScenario"));
            Assert.That(panelSource, Does.Contain("Externally-authored scenarios use their own authored agent config for unit counts."));
        }

        [Test]
        public void MassNavigationPanel_UsesConfiguredPresentationCadenceAndMountsOnMap()
        {
            string repoRoot = FindRepoRoot();
            string runtimeSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassNavigationRuntime.cs"));
            string systemSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Systems",
                "MassNavigationPanelPresentationSystem.cs"));
            string controllerSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "UI",
                "MassNavigationPanelController.cs"));

            Assert.That(runtimeSource, Does.Contain("new MassNavigationPanelPresentationSystem("));
            Assert.That(runtimeSource, Does.Contain("PanelRefreshIntervalSeconds"));
            Assert.That(systemSource, Does.Contain("_refreshIntervalSeconds"));
            Assert.That(controllerSource, Does.Contain("PanelRefreshIntervalSeconds"));
            Assert.That(controllerSource, Does.Not.Contain("TimeSpan.TicksPerSecond / 4"));
            Assert.That(systemSource, Does.Not.Contain("PanelRefreshIntervalSeconds = 0.25f"));

            string refreshBody = ExtractMethodBody(runtimeSource, "public void RefreshPanel");
            Assert.That(refreshBody, Does.Contain("_panelController.MountOrSync(engine, simulation)"));
            Assert.That(refreshBody, Does.Not.Contain("ClearPanelIfOwned(engine)"));
            string updateBody = ExtractMethodBody(systemSource, "public void Update");
            int resetIndex = updateBody.IndexOf("_refreshAccumulatorSeconds = _refreshIntervalSeconds;", StringComparison.Ordinal);
            int returnIndex = updateBody.IndexOf("return;", resetIndex, StringComparison.Ordinal);
            Assert.That(resetIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(returnIndex, Is.GreaterThan(resetIndex));
            string nonNavigationMapBranch = updateBody.Substring(resetIndex, returnIndex - resetIndex);
            Assert.That(nonNavigationMapBranch, Does.Not.Contain("_runtime.RefreshPanel(_engine)"));
        }

        [Test]
        public void MassNavigationAndTotalWarSources_DoNotReintroduceFallbackAliasOrPrototypeNames()
        {
            string repoRoot = FindRepoRoot();
            string[] roots =
            {
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"),
                Path.Combine(repoRoot, "mods", "showcases", "mass_navigation_total_war_entry"),
            };

            string[] forbidden =
            {
                "fallback",
                "alias",
                "WebParity",
                "webParity",
                "MassNavigationWeb",
                "OrdinalIgnoreCase",
                "StringComparer.OrdinalIgnoreCase",
                "PropertyNameCaseInsensitive = true",
                "?? default",
            };

            foreach (string path in roots.SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                         .Where(path => !PathHasSegment(path, "bin") && !PathHasSegment(path, "obj"))
                         .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal)))
            {
                string source = File.ReadAllText(path);
                foreach (string token in forbidden)
                {
                    Assert.That(source, Does.Not.Contain(token), $"{path} must not contain forbidden token '{token}'.");
                }
            }
        }

        [Test]
        public void TotalWarRaylibLaunchGraph_DoesNotLoadPrototypeShowcaseMods()
        {
            string repoRoot = FindRepoRoot();
            string launchGraphPath = Path.Combine(
                repoRoot,
                "src",
                "Apps",
                "Raylib",
                "Ludots.App.Raylib",
                "raylib.mass-navigation-total-war.launch.graph.json");

            JsonObject launchGraph = ReadObject(launchGraphPath);
            JsonArray orderedModIds = launchGraph["orderedModIds"]?.AsArray()
                ?? throw new InvalidOperationException("TotalWar Raylib launch graph must author orderedModIds.");
            string[] ids = orderedModIds.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray();

            Assert.That(ids, Does.Contain("MassNavigationMod"));
            Assert.That(ids, Does.Contain("MassNavigationTotalWarEntryMod"));
            Assert.That(ids, Does.Not.Contain("PerformerBlacksmithShowcaseMod"));
            Assert.That(ids.Any(id => id.Contains("Blacksmith", StringComparison.Ordinal)), Is.False);
            Assert.That(ids.Any(id => id.Contains("WebParity", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void MassNavigationPostMovementSystems_UseExplicitRequiredAnchors()
        {
            string repoRoot = FindRepoRoot();
            string engineSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "Engine", "GameEngine.cs"));
            string runtimeSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassNavigationRuntime.cs"));

            Assert.That(engineSource, Does.Contain("InsertSystemBeforeRequired"));
            Assert.That(runtimeSource, Does.Contain("InsertSystemBeforeRequired<MassNavigationFormationSystem>"));
            Assert.That(runtimeSource, Does.Contain("InsertSystemBeforeRequired<MassNavigationOrderIngestionSystem>"));
            Assert.That(runtimeSource, Does.Not.Contain("CommandApply"));
            Assert.That(runtimeSource.IndexOf("new MassNavigationFormationSystem", StringComparison.Ordinal),
                Is.LessThan(runtimeSource.IndexOf("InsertSystemBeforeRequired<MassNavigationFormationSystem>", StringComparison.Ordinal)));
        }

        [Test]
        public void MassNavigationRuntimeBoundaries_UseExplicitAgentTermsAndKindSpecificReceipts()
        {
            string repoRoot = FindRepoRoot();
            string agentStateSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassNavigationAgentState.cs"));
            string receiptSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassNavigationSpawnReceiptKind.cs"));
            string bootstrapSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Systems",
                "MassNavigationScenarioBootstrap.cs"));
            string orderIngestionSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Systems",
                "MassNavigationOrderIngestionSystem.cs"));
            string moveOrderArgsSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassNavigationMoveOrderArgs.cs"));

            Assert.That(agentStateSource, Does.Contain("ControllableAgentCount"));
            Assert.That(agentStateSource, Does.Contain("ControllableAgentSlotCount"));
            Assert.That(agentStateSource, Does.Not.Contain("ControllableCount"));
            Assert.That(receiptSource, Does.Contain("ForAgent"));
            Assert.That(receiptSource, Does.Contain("ForBlocker"));
            Assert.That(receiptSource, Does.Contain("ForWorldMarker"));
            Assert.That(receiptSource, Does.Not.Contain("?? string.Empty"));
            Assert.That(bootstrapSource, Does.Contain("SpawnConfiguredScenario"));
            Assert.That(bootstrapSource, Does.Not.Contain("SpawnDefaultScenario"));
            Assert.That(orderIngestionSource, Does.Contain("MassNavigationMoveOrderArgs.Decode"));
            Assert.That(orderIngestionSource, Does.Not.Contain(".Args.I0"));
            Assert.That(orderIngestionSource, Does.Not.Contain(".Args.F0"));
            Assert.That(moveOrderArgsSource, Does.Contain("DecodeFormationMode"));
            Assert.That(orderIngestionSource, Does.Not.Contain(": MassNavigationFormationMode.None"));
        }

        [Test]
        public void MassNavigationGroupRuntime_ExposesOrderSlotTargetsAndDoesNotCollapseNoneFormationOrders()
        {
            string repoRoot = FindRepoRoot();
            string groupRuntimeSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassNavigationGroupRuntime.cs"));
            string orderUpsertBody = ExtractMethodBody(groupRuntimeSource, "public int UpsertOrderMoveCommand");

            Assert.That(groupRuntimeSource, Does.Contain("TryGetGroupMemberOrderTarget"));
            Assert.That(groupRuntimeSource, Does.Contain("TryUpdateGroupMemberOrderPathAnchor"));
            Assert.That(groupRuntimeSource, Does.Not.Contain("TryGetGroupMemberTarget"));
            Assert.That(groupRuntimeSource, Does.Not.Contain("TryGetGroupDestination"));
            Assert.That(orderUpsertBody, Does.Contain("bool singleMemberOrder = memberIndices.Length == 1"));
            Assert.That(orderUpsertBody, Does.Not.Contain("formationMode == MassNavigationFormationMode.None || memberIndices.Length == 1"));
            Assert.That(groupRuntimeSource, Does.Contain("BuildCurrentRelativeOffsets"));
            Assert.That(groupRuntimeSource, Does.Contain("_groupIdsByAgentIndex"));
            Assert.That(groupRuntimeSource, Does.Not.Contain("_groupIdsByControllableIndex"));
            Assert.That(groupRuntimeSource, Does.Not.Contain("EnsureMembershipCapacity(agentState.ControllableAgentSlotCount)"));
            Assert.That(groupRuntimeSource, Does.Contain("EnsureMembershipCapacityForMembers(memberIndices[..assignedCount])"));
            Assert.That(groupRuntimeSource, Does.Contain("EnsureMembershipCapacityForMembers(memberIndices[..memberCount])"));
            Assert.That(groupRuntimeSource, Does.Not.Contain("ResolveLayoutMode"));
            Assert.That(groupRuntimeSource, Does.Not.Contain("? MassNavigationFormationMode.Square"));
            Assert.That(groupRuntimeSource, Does.Contain("ResolveBodyRadiusSpacingScale"));
        }

        [Test]
        public void MassFlowNeighborSearch_UsesLayerScopedBodyRadiusNotGlobalLargestAgent()
        {
            string repoRoot = FindRepoRoot();
            string flowSource = File.ReadAllText(Path.Combine(
                repoRoot,
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "Runtime",
                "MassFlowSimulationState.cs"));
            string separationBody = ExtractMethodBody(flowSource, "private int ResolveSeparationHashSearchRadiusCells");
            string hardResolveBody = ExtractMethodBody(flowSource, "private int ResolveHardResolveHashSearchRadiusCells");

            Assert.That(flowSource, Does.Contain("_maxInteractingBodyRadiiCm"));
            Assert.That(flowSource, Does.Contain("_separationHashSearchRadiusCellsByAgent"));
            Assert.That(flowSource, Does.Contain("_hardResolveHashSearchRadiusCellsByAgent"));
            Assert.That(flowSource, Does.Contain("RecomputeMaxInteractingBodyRadiiCm"));
            Assert.That(flowSource, Does.Contain("TrailingZeroCount"));
            Assert.That(flowSource, Does.Contain("flowObstacleNeighborRadiusCells"));
            Assert.That(flowSource, Does.Not.Contain("oy = -2"));
            Assert.That(flowSource, Does.Not.Contain("ox = -2"));
            Assert.That(flowSource, Does.Not.Contain("_hardResolveCandidates[j] = 1"),
                "Parallel steering workers must only write candidate flags owned by their own unit index.");
            Assert.That(separationBody, Does.Contain("ResolveMaxInteractingBodyRadiusCm(selfUnitIndex)"));
            Assert.That(hardResolveBody, Does.Contain("ResolveMaxInteractingBodyRadiusCm(selfUnitIndex)"));
            Assert.That(flowSource, Does.Contain("int separationHashSearchRadius = _separationHashSearchRadiusCellsByAgent[i]"));
            Assert.That(flowSource, Does.Contain("int hardResolveSearchRadius = _hardResolveHashSearchRadiusCellsByAgent[i]"));
            Assert.That(separationBody, Does.Not.Contain("_maxBodyRadiusCm * 2f"));
            Assert.That(hardResolveBody, Does.Not.Contain("+ _maxBodyRadiusCm +"));
        }

        private static bool PathHasSegment(string path, string segment)
        {
            return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => string.Equals(part, segment, StringComparison.Ordinal));
        }

        private static List<string> MassNavigationDependencyPaths()
        {
            string repoRoot = FindRepoRoot();
            string modsRoot = Path.Combine(repoRoot, "mods");
            return new List<string>
            {
                Path.Combine(modsRoot, "LudotsCoreMod"),
                Path.Combine(modsRoot, "CoreInputMod"),
                Path.Combine(modsRoot, "capabilities", "camera", "CameraProfilesMod"),
                Path.Combine(modsRoot, "capabilities", "navigation", "MassNavigationMod"),
            };
        }

        private static List<string> TotalWarDependencyPaths()
        {
            List<string> paths = MassNavigationDependencyPaths();
            paths.Add(Path.Combine(
                FindRepoRoot(),
                "mods",
                "showcases",
                "mass_navigation_total_war_entry",
                "MassNavigationTotalWarEntryMod"));
            return paths;
        }

        private static GameEngine CreatePlayableTotalWarEngine()
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(TotalWarDependencyPaths(), Path.Combine(FindRepoRoot(), "assets"));
            InstallPlayableInput(engine);

            var focusOverride = new CameraCullingFocusOverride();
            HeadlessPresentationTestHost.Install(engine, focusOverride);

            var mapping = new TotalWarWorldScreenMapping(
                TotalWarAcceptance.ScreenCenter,
                TotalWarAcceptance.PixelsPerCm);
            engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)mapping);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)mapping);
            engine.GlobalContext[TotalWarAcceptance.WorldScreenMappingKey] = mapping;

            var renderCameraDebug = new RenderCameraDebugState();
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

            engine.Start();
            return engine;
        }

        private static void InstallPlayableInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[TotalWarAcceptance.InputBackendKey] = backend;
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[TotalWarAcceptance.InputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Total War playable test input backend is missing.");
        }

        private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine)
        {
            return engine.GetService(MassNavigationMod.MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigationSimulationRuntime is missing.");
        }

        private static void Tick(GameEngine engine, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(TotalWarAcceptance.FrameSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames, string failureMessage)
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine);
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static Vector2 WorldToScreen(GameEngine engine, Vector2 worldCm)
        {
            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector is missing.");
            return projector.WorldToScreen(new Vector3(worldCm.X / 100f, 0f, worldCm.Y / 100f));
        }

        private static WorldCmInt2 ResolveGroundWorldCm(GameEngine engine, Vector2 screen)
        {
            if (!Ludots.Core.Input.Runtime.AuthoritativeGroundPointerHelper.TryResolveFromScreen(
                    engine.GlobalContext,
                    screen,
                    out WorldCmInt2 worldCm))
            {
                throw new InvalidOperationException($"Could not resolve screen point {screen} to Total War ground.");
            }

            return worldCm;
        }

        private static void AssertOutsideMinimapInteractiveRegion(GameEngine engine, Vector2 screenPosition)
        {
            var minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("MinimapRuntime is missing.");
            Assert.That(
                minimap.ContainsInteractiveRegion(screenPosition),
                Is.False,
                "This acceptance path verifies a normal ground right-click; the screen point must not be consumed by minimap command input.");
        }

        private static ScreenRect ProjectCurrentSelectionDragRect(GameEngine engine)
        {
            Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            Assert.That(selected.Length, Is.EqualTo(TotalWarAcceptance.ExpectedInitialSelection));
            return ProjectEntitiesDragRect(engine, selected);
        }

        private static ScreenRect ProjectEntitiesDragRect(GameEngine engine, ReadOnlySpan<Entity> entities)
        {
            IScreenProjector projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector is missing.");
            bool hasPoint = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                Assert.That(engine.World.IsAlive(entity), Is.True, $"Projected entity {entity.Id} should still be alive.");
                if (!SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out ScreenRect bounds))
                {
                    throw new InvalidOperationException($"Could not project Total War entity {entity.Id}.");
                }

                if (!hasPoint)
                {
                    minX = bounds.MinX;
                    minY = bounds.MinY;
                    maxX = bounds.MaxX;
                    maxY = bounds.MaxY;
                    hasPoint = true;
                }
                else
                {
                    minX = MathF.Min(minX, bounds.MinX);
                    minY = MathF.Min(minY, bounds.MinY);
                    maxX = MathF.Max(maxX, bounds.MaxX);
                    maxY = MathF.Max(maxY, bounds.MaxY);
                }
            }

            if (!hasPoint)
            {
                throw new InvalidOperationException("Entity set has no projectable Total War entities.");
            }

            return new ScreenRect(
                minX - TotalWarAcceptance.SelectionDragPaddingPixels,
                minY - TotalWarAcceptance.SelectionDragPaddingPixels,
                maxX + TotalWarAcceptance.SelectionDragPaddingPixels,
                maxY + TotalWarAcceptance.SelectionDragPaddingPixels);
        }

        private static void DragSelect(GameEngine engine, TestInputBackend backend, in ScreenRect rect)
        {
            DragSelect(engine, backend, new Vector2(rect.MinX, rect.MinY), new Vector2(rect.MaxX, rect.MaxY));
        }

        private static void DragSelect(GameEngine engine, TestInputBackend backend, Vector2 start, Vector2 end)
        {
            backend.SetMousePosition(start);
            Tick(engine);
            backend.SetButton(TotalWarAcceptance.LeftMousePath, true);
            Tick(engine);
            backend.SetMousePosition(end);
            Tick(engine);
            backend.SetButton(TotalWarAcceptance.LeftMousePath, false);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInputRelease);
        }

        private static void LeftClick(GameEngine engine, TestInputBackend backend, Vector2 position)
        {
            backend.SetMousePosition(position);
            Tick(engine);
            backend.SetButton(TotalWarAcceptance.LeftMousePath, true);
            Tick(engine);
            backend.SetButton(TotalWarAcceptance.LeftMousePath, false);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInputRelease);
        }

        private static void RightClick(GameEngine engine, TestInputBackend backend, Vector2 position)
        {
            backend.SetMousePosition(position);
            Tick(engine);
            backend.SetButton(TotalWarAcceptance.RightMousePath, true);
            Tick(engine);
            backend.SetButton(TotalWarAcceptance.RightMousePath, false);
            Tick(engine, TotalWarAcceptance.FrameBudgetForInputRelease);
        }

        private static int CountSelectionMarkerPerformers(GameEngine engine)
        {
            PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry is missing.");
            int formation = definitions.GetId("mass_navigation_total_war_formation_selection_marker");
            int count = 0;
            var query = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in query, (ref PerformerState state) =>
            {
                if (state.DefId == formation)
                {
                    count++;
                }
            });

            return count;
        }

        private static int CountTrackedAgentRuntimeTags(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<MassNavigationAgentTag>();
            engine.World.Query(in query, (Entity _) => count++);
            return count;
        }

        private static int FindFirstSoldierAgentIndex(GameEngine engine, int formationIndex)
        {
            int agentIndex = -1;
            var query = new QueryDescription().WithAll<TotalWarFormationSoldier, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref TotalWarFormationSoldier soldier, ref MassNavigationAgentIndex index) =>
            {
                if (agentIndex < 0 && soldier.FormationIndex == formationIndex)
                {
                    agentIndex = index.Value;
                }
            });

            if (agentIndex < 0)
            {
                throw new InvalidOperationException($"No Total War soldier was bound for formation index {formationIndex}.");
            }

            return agentIndex;
        }

        private static Entity[] CaptureFormationAgents(GameEngine engine, int expectedCount)
        {
            var formations = new List<(int FormationIndex, Entity Entity)>(expectedCount);
            var query = new QueryDescription().WithAll<TotalWarFormationAgent>();
            engine.World.Query(in query, (Entity entity, ref TotalWarFormationAgent formation) =>
            {
                formations.Add((formation.FormationIndex, entity));
            });

            formations.Sort(static (left, right) => left.FormationIndex.CompareTo(right.FormationIndex));
            Assert.That(formations.Count, Is.GreaterThanOrEqualTo(expectedCount));
            Entity[] result = new Entity[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                result[i] = formations[i].Entity;
            }

            return result;
        }

        private static Entity FindNonLocalPlayerOwnerFormation(GameEngine engine, int localPlayerId)
        {
            Entity result = Entity.Null;
            int formationIndex = int.MaxValue;
            int formationsWithoutOwner = 0;
            var query = new QueryDescription().WithAll<TotalWarFormationAgent>();
            engine.World.Query(in query, (Entity entity, ref TotalWarFormationAgent formation) =>
            {
                if (!engine.World.TryGet(entity, out PlayerOwner owner))
                {
                    formationsWithoutOwner++;
                    return;
                }

                if (owner.PlayerId == localPlayerId || formation.FormationIndex >= formationIndex)
                {
                    return;
                }

                formationIndex = formation.FormationIndex;
                result = entity;
            });

            Assert.That(result, Is.Not.EqualTo(Entity.Null),
                $"Total War command authorization test requires at least one non-local owner formation; formations without PlayerOwner={formationsWithoutOwner}.");
            return result;
        }

        private static Entity FindLocalPlayerOwnerFormation(GameEngine engine, int localPlayerId)
        {
            Entity result = Entity.Null;
            int formationIndex = int.MaxValue;
            int formationsWithoutOwner = 0;
            var query = new QueryDescription().WithAll<TotalWarFormationAgent>();
            engine.World.Query(in query, (Entity entity, ref TotalWarFormationAgent formation) =>
            {
                if (!engine.World.TryGet(entity, out PlayerOwner owner))
                {
                    formationsWithoutOwner++;
                    return;
                }

                if (owner.PlayerId != localPlayerId || formation.FormationIndex >= formationIndex)
                {
                    return;
                }

                formationIndex = formation.FormationIndex;
                result = entity;
            });

            Assert.That(result, Is.Not.EqualTo(Entity.Null),
                $"Total War command authorization test requires at least one local owner formation; formations without PlayerOwner={formationsWithoutOwner}.");
            return result;
        }

        private static int CountFriendlyTeamFormations(GameEngine engine, int selectorTeamId)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<TotalWarFormationAgent, Team>();
            engine.World.Query(in query, (ref TotalWarFormationAgent _, ref Team team) =>
            {
                if (RelationshipFilterUtil.Passes(RelationshipFilter.Friendly, selectorTeamId, team.Id))
                {
                    count++;
                }
            });

            return count;
        }

        private static void AssertFormationSelectionCandidateFacts(GameEngine engine)
        {
            int selectorTeamId = ResolveSelectionOwnerTeamId(engine);
            int friendlyFormationCount = 0;
            int rejectedFormationCount = 0;
            int formationsWithoutTeam = 0;
            var query = new QueryDescription().WithAll<TotalWarFormationAgent, SelectionSelectableState>();
            engine.World.Query(in query, (Entity entity, ref TotalWarFormationAgent _, ref SelectionSelectableState selectable) =>
            {
                Assert.That(selectable.Enabled, Is.True,
                    "Total War formation candidates stay generally selectable; Core relationship filtering gates player acquisition.");

                if (!engine.World.TryGet(entity, out Team team))
                {
                    formationsWithoutTeam++;
                    return;
                }

                if (RelationshipFilterUtil.Passes(RelationshipFilter.Friendly, selectorTeamId, team.Id))
                {
                    friendlyFormationCount++;
                }
                else
                {
                    rejectedFormationCount++;
                }
            });

            Assert.That(formationsWithoutTeam, Is.EqualTo(0));
            Assert.That(friendlyFormationCount, Is.GreaterThan(0));
            Assert.That(rejectedFormationCount, Is.GreaterThan(0));
        }

        private static void AssertLocalPlayerOwnerFormations(GameEngine engine)
        {
            int localPlayerId = ResolveLocalPlayerOwnerId(engine);
            int localFormationCount = 0;
            var query = new QueryDescription().WithAll<TotalWarFormationAgent, PlayerOwner>();
            engine.World.Query(in query, (ref TotalWarFormationAgent _, ref PlayerOwner owner) =>
            {
                if (owner.PlayerId != localPlayerId)
                {
                    return;
                }

                localFormationCount++;
            });

            Assert.That(localFormationCount, Is.GreaterThan(0),
                "Total War command authorization test requires at least one local owner formation.");
        }

        private static void SelectFormations(GameEngine engine, ReadOnlySpan<Entity> formations)
        {
            SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");
            Entity owner = ResolveLocalPlayerEntity(engine);
            Assert.That(selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, formations), Is.True);
            if (!SelectionContextRuntime.TrySetCurrentView(
                    engine.World,
                    engine.GlobalContext,
                    selection,
                    owner,
                    SelectionViewKeys.Primary,
                    owner,
                    SelectionSetKeys.LivePrimary,
                    out _))
            {
                throw new InvalidOperationException("Could not bind LivePrimary as current Total War selection view.");
            }

            Tick(engine);
        }

        private static Entity ResolveLocalPlayerEntity(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
                localObj is not Entity local ||
                !engine.World.IsAlive(local))
            {
                throw new InvalidOperationException("LocalPlayerEntity is missing.");
            }

            return local;
        }

        private static int ResolveLocalPlayerOwnerId(GameEngine engine)
        {
            Entity local = ResolveLocalPlayerEntity(engine);
            Assert.That(engine.World.TryGet(local, out PlayerOwner owner), Is.True);
            return owner.PlayerId;
        }

        private static int ResolveSelectionOwnerTeamId(GameEngine engine)
        {
            Entity local = ResolveLocalPlayerEntity(engine);
            Assert.That(engine.World.TryGet(local, out Team team), Is.True);
            return team.Id;
        }

        private static float MinPairDistanceSq(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            ReadOnlySpan<Entity> formations,
            bool useOrderTargets)
        {
            float minDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < formations.Length; i++)
            {
                Vector2 left = ResolveFormationPoint(engine, simulation, formations[i], useOrderTargets);
                for (int j = i + 1; j < formations.Length; j++)
                {
                    Vector2 right = ResolveFormationPoint(engine, simulation, formations[j], useOrderTargets);
                    float dx = left.X - right.X;
                    float dy = left.Y - right.Y;
                    minDistanceSq = MathF.Min(minDistanceSq, (dx * dx) + (dy * dy));
                }
            }

            Assert.That(float.IsFinite(minDistanceSq), Is.True);
            return minDistanceSq;
        }

        private static Vector2 ResolveFormationPoint(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            Entity formation,
            bool useOrderTarget)
        {
            Assert.That(engine.World.TryGet(formation, out MassNavigationAgentIndex agentIndex), Is.True);
            if (useOrderTarget)
            {
                Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(
                        agentIndex.Value,
                        out float targetWorldX,
                        out float targetWorldY),
                    Is.True);
                return new Vector2(targetWorldX, targetWorldY);
            }

            return simulation.GetAgentWorldPositionCm(agentIndex.Value);
        }

        private static Vector2 AgentWorldOffset(
            MassNavigationSimulationRuntime simulation,
            int agentIndex,
            int anchorAgentIndex)
        {
            return AgentWorldPosition(simulation, agentIndex) - AgentWorldPosition(simulation, anchorAgentIndex);
        }

        private static Vector2 AgentWorldPosition(MassNavigationSimulationRuntime simulation, int agentIndex)
        {
            return simulation.GetAgentWorldPositionCm(agentIndex);
        }

        private static void AssertMassFlowEntityPositionSynced(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            int agentIndex)
        {
            simulation.SyncAgentEntitiesNow(engine.World);
            Assert.That(simulation.AgentState.TryGetAgentEntity(agentIndex, out Entity entity), Is.True);
            Assert.That(engine.World.TryGet(entity, out WorldPositionCm worldPosition), Is.True);
            Vector2 expectedWorld = simulation.GetAgentWorldPositionCm(agentIndex);
            int expectedWorldX = (int)MathF.Round(expectedWorld.X);
            int expectedWorldY = (int)MathF.Round(expectedWorld.Y);
            Assert.That(worldPosition.Value.X.RawValue, Is.EqualTo(Fix64.FromInt(expectedWorldX).RawValue));
            Assert.That(worldPosition.Value.Y.RawValue, Is.EqualTo(Fix64.FromInt(expectedWorldY).RawValue));
        }

        private static Entity[] CaptureTrackedAgents(MassNavigationSimulationRuntime simulation)
        {
            IReadOnlyList<Entity> agents = simulation.AgentState.AllAgents;
            Assert.That(agents.Count, Is.EqualTo(TotalWarAcceptance.ExpectedTotalAgents));
            var snapshot = new Entity[agents.Count];
            for (int i = 0; i < agents.Count; i++)
            {
                snapshot[i] = agents[i];
                Assert.That(snapshot[i], Is.Not.EqualTo(Entity.Null), $"Total War tracked agent {i} must be bound before reset.");
            }

            return snapshot;
        }

        private static Entity[] CaptureObstacleOverlays(GameEngine engine, int expectedCount)
        {
            var overlays = new List<Entity>(expectedCount);
            var query = new QueryDescription().WithAll<TotalWarObstacleOverlay>();
            engine.World.Query(in query, (Entity entity, ref TotalWarObstacleOverlay _) =>
            {
                overlays.Add(entity);
            });

            Assert.That(overlays.Count, Is.EqualTo(expectedCount));
            return overlays.ToArray();
        }

        private static int CountAlive(GameEngine engine, ReadOnlySpan<Entity> entities)
        {
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (engine.World.IsAlive(entities[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountPresentationDestroyPending(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<PresentationDestroyPending>();
            engine.World.Query(in query, (Entity _) => count++);
            return count;
        }

        private static int CountActiveMoveOrders(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            int count = 0;
            int moveOrderTypeId = ResolveMassNavigationMoveOrderTypeId(engine);
            ReadOnlySpan<Entity> selected = simulation.SelectedEntities;
            for (int i = 0; i < selected.Length; i++)
            {
                Entity entity = selected[i];
                if (!engine.World.IsAlive(entity) ||
                    !engine.World.Has<OrderBuffer>(entity))
                {
                    continue;
                }

                ref readonly OrderBuffer orders = ref engine.World.Get<OrderBuffer>(entity);
                if (orders.HasActive &&
                    orders.ActiveOrder.Order.OrderTypeId == moveOrderTypeId &&
                    orders.ActiveOrder.Order.Args.Spatial.Kind == OrderSpatialKind.WorldCm)
                {
                    count++;
                }
            }

            return count;
        }

        private static float NormalizeAngleRadians(float angle)
        {
            while (angle > MathF.PI)
            {
                angle -= MathF.Tau;
            }

            while (angle < -MathF.PI)
            {
                angle += MathF.Tau;
            }

            return angle;
        }

        private static void AssertInitialSelectionTargetsFormationAgents(GameEngine engine)
        {
            Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            Assert.That(selected.Length, Is.EqualTo(TotalWarAcceptance.ExpectedInitialSelection));
            for (int i = 0; i < selected.Length; i++)
            {
                Entity entity = selected[i];
                Assert.That(engine.World.Get<Name>(entity).Value, Is.EqualTo("MassNavigation.TotalWar.FormationAgent"));
                Assert.That(engine.World.Has<OrderBuffer>(entity), Is.True);
                Assert.That(engine.World.Has<AttributeBuffer>(entity), Is.True);
                Assert.That(engine.World.Has<MassNavigationControllable>(entity), Is.True);
            }
        }

        private static void AssertFormationAgentsDoNotOverlap(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            int[] agentIndices = new int[TotalWarAcceptance.ExpectedTotalFormations];
            int count = 0;
            var query = new QueryDescription().WithAll<TotalWarFormationAgent, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref TotalWarFormationAgent _, ref MassNavigationAgentIndex agentIndex) =>
            {
                if (count >= agentIndices.Length)
                {
                    throw new InvalidOperationException("Total War playable test found more formation agents than expected.");
                }

                agentIndices[count++] = agentIndex.Value;
            });

            Assert.That(count, Is.EqualTo(TotalWarAcceptance.ExpectedTotalFormations));
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    int left = agentIndices[i];
                    int right = agentIndices[j];
                    Vector2 leftPosition = simulation.GetAgentLocalPositionCm(left);
                    Vector2 rightPosition = simulation.GetAgentLocalPositionCm(right);
                    float dx = leftPosition.X - rightPosition.X;
                    float dy = leftPosition.Y - rightPosition.Y;
                    float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                    float required = simulation.GetAgentBodyRadiusCm(left) + simulation.GetAgentBodyRadiusCm(right);
                    Assert.That(distance, Is.GreaterThanOrEqualTo(required - 1f),
                        $"Formation agents {left} and {right} must not overlap in MassNavigation.");
                }
            }
        }

        private static int ResolveMassNavigationMoveOrderTypeId(GameEngine engine)
        {
            OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("OrderTypeRegistry is missing.");
            if (!registry.TryGetId(MassNavigationMod.Runtime.MassNavigationOrderKeys.Move, out int id))
            {
                throw new InvalidOperationException("massNavigationMove order type is not registered.");
            }

            return id;
        }

        private static void AssertFormationOutlines(GameEngine engine)
        {
            RoadSplineBuffer splines = engine.GetService(CoreServiceKeys.RoadSplineBuffer)
                ?? throw new InvalidOperationException("RoadSplineBuffer is missing.");
            Assert.That(splines.Count, Is.EqualTo(TotalWarAcceptance.ExpectedOutlineSplineSegments));
            Assert.That(engine.GlobalContext.TryGetValue(TotalWarShowcaseContextKeys.FormationOutlineCount, out object? outlineCount), Is.True);
            Assert.That(outlineCount, Is.EqualTo(TotalWarAcceptance.ExpectedOutlineSplineSegments));

            ReadOnlySpan<float> p0Y = splines.P0Y;
            ReadOnlySpan<float> p3Y = splines.P3Y;
            bool hasTerrainHeightChange = false;
            for (int i = 0; i < splines.Count; i++)
            {
                if (MathF.Abs(p0Y[i] - p3Y[i]) > 0.0001f)
                {
                    hasTerrainHeightChange = true;
                    break;
                }
            }

            Assert.That(hasTerrainHeightChange, Is.True,
                "Formation outline spline segments should use per-endpoint visual heightmap samples instead of a single flat overlay height.");
        }

        private static void AssertObstacleOverlays(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            JsonObject config = ReadObject(Path.Combine(TotalWarModRoot(), "assets", "TotalWarShowcaseConfig.json"));
            JsonObject obstacleOverlay = config["obstacleOverlay"]?.AsObject()
                ?? throw new InvalidOperationException("Total War showcase config requires obstacleOverlay.");
            float expectedBorderWidthM = WorldUnits.CmToM(obstacleOverlay["borderWidthCm"]?.GetValue<float>()
                ?? throw new InvalidOperationException("Total War obstacleOverlay.borderWidthCm must be numeric."));
            GroundOverlayBuffer overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer is missing.");
            ReadOnlySpan<GroundOverlayItem> items = overlays.GetSpan();
            int ringCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Shape == GroundOverlayShape.Ring)
                {
                    ringCount++;
                    Assert.That(items[i].BorderWidth, Is.EqualTo(expectedBorderWidthM).Within(0.0001f));
                }
            }

            Assert.That(ringCount, Is.GreaterThanOrEqualTo(simulation.NavigationObstacleCount));
        }

        private static void AssertCullingProbeAndDebugDraw(GameEngine engine)
        {
            CameraCullingFocusOverride focus = engine.GetService(CoreServiceKeys.CameraCullingFocusOverride)
                ?? throw new InvalidOperationException("CameraCullingFocusOverride is missing.");
            Assert.That(focus.Enabled, Is.True);
            Assert.That(focus.SourceId, Is.EqualTo("battlefield_overview"));

            CameraCullingDebugState culling = engine.GetService(CoreServiceKeys.CameraCullingDebugState)
                ?? throw new InvalidOperationException("CameraCullingDebugState is missing.");
            Assert.That(culling.VisibleEntityCount, Is.GreaterThan(0));

            RenderCameraDebugState renderDebug = engine.GetService(CoreServiceKeys.RenderCameraDebugState)
                ?? throw new InvalidOperationException("RenderCameraDebugState is missing.");
            DebugDrawCommandBuffer debugDraw = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer)
                ?? throw new InvalidOperationException("DebugDrawCommandBuffer is missing.");
            debugDraw.Clear();
            renderDebug.Enabled = true;
            renderDebug.DrawLogicalCullingDebug = true;
            Tick(engine);
            Assert.That(debugDraw.Boxes.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(debugDraw.Circles.Count, Is.GreaterThanOrEqualTo(3));
        }

        private static string BuildSelectionDiagnostics(GameEngine engine)
        {
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            return $"selection={simulation.SelectedCount} markers={CountSelectionMarkerPerformers(engine)} agents={simulation.AgentState.TotalAgents}";
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(target.GetType().FullName, methodName);
            method.Invoke(target, Array.Empty<object>());
        }

        private static void AssertPositive(JsonObject obj, string propertyName, bool allowZero = false)
        {
            float value = obj[propertyName]?.GetValue<float>()
                ?? throw new InvalidOperationException($"JSON object requires numeric '{propertyName}'.");
            Assert.That(value, allowZero ? Is.GreaterThanOrEqualTo(0f) : Is.GreaterThan(0f), propertyName);
        }

        private static void AssertColor(JsonArray? values, string label)
        {
            Assert.That(values, Is.Not.Null, $"{label} must be authored.");
            Assert.That(values!.Count, Is.EqualTo(4), $"{label} must contain rgba.");
            for (int i = 0; i < values.Count; i++)
            {
                float channel = values[i]?.GetValue<float>()
                    ?? throw new InvalidOperationException($"{label}[{i}] must be numeric.");
                Assert.That(channel, Is.InRange(0f, 1f), $"{label}[{i}]");
            }
        }

        private static JsonObject FindObjectById(JsonArray array, string id)
        {
            return array
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => string.Equals(obj?["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"JSON object id '{id}' not found.");
        }

        private static JsonObject FindObjectByPath(JsonArray array, string path)
        {
            return array
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => string.Equals(obj?["Path"]?.GetValue<string>(), path, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"JSON object Path '{path}' not found.");
        }

        private static JsonObject FindAgentProfileById(JsonObject agentProfiles, string id)
        {
            JsonArray profiles = agentProfiles["profiles"]?.AsArray()
                ?? throw new InvalidOperationException("agentProfiles.profiles must be authored.");
            return FindObjectById(profiles, id);
        }

        private static void AssertAgentAuthoringReferencesProfileOnly(JsonObject agentAuthoring, string label)
        {
            Assert.That(RequireString(agentAuthoring, "templateId"), Is.Not.Empty);
            Assert.That(RequireString(agentAuthoring, "profileId"), Is.Not.Empty);
            string[] forbiddenProfileFields =
            {
                "heavy",
                "navMass",
                "visualScale",
                "bodyRadiusCm",
                "speedCmPerSecond",
            };

            foreach (string field in forbiddenProfileFields)
            {
                Assert.That(agentAuthoring.ContainsKey(field), Is.False,
                    $"{label}.{field} must be owned by MassNavigationConfig.agentProfiles.");
            }
        }

        private static string RequireString(JsonObject obj, string propertyName)
        {
            string value = obj[propertyName]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"JSON object requires non-empty '{propertyName}'.");
            }

            return value;
        }

        private static string RequireString(JsonObject obj, string objectName, string propertyName)
        {
            JsonObject nested = obj[objectName]?.AsObject()
                ?? throw new InvalidOperationException($"JSON object requires '{objectName}'.");
            return RequireString(nested, propertyName);
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            int methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
            if (methodIndex < 0)
            {
                throw new InvalidOperationException($"Method '{methodName}' not found.");
            }

            int bodyStart = source.IndexOf('{', methodIndex);
            if (bodyStart < 0)
            {
                throw new InvalidOperationException($"Method '{methodName}' body not found.");
            }

            int depth = 0;
            for (int i = bodyStart; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(bodyStart, i - bodyStart + 1);
                    }
                }
            }

            throw new InvalidOperationException($"Method '{methodName}' body was not closed.");
        }

        private static JsonArray ReadArray(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return JsonNode.Parse(stream)?.AsArray()
                ?? throw new InvalidOperationException($"Expected JSON array at {path}.");
        }

        private static JsonObject ReadObject(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return JsonNode.Parse(stream)?.AsObject()
                ?? throw new InvalidOperationException($"Expected JSON object at {path}.");
        }

        private static MassNavigationGroupRuntimeFixture CreateGroupRuntimeFixture(params Vector2[] localPositions)
        {
            if (localPositions.Length <= 0)
            {
                throw new InvalidOperationException("MassNavigation group runtime fixture requires at least one position.");
            }

            var flow = new MassFlowSimulationState(CreateTestSolverConfig());
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var seeds = new MassNavigationAgentSeed[localPositions.Length];
            for (int i = 0; i < localPositions.Length; i++)
            {
                seeds[i] = new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: localPositions[i].X,
                    localPositionYCm: localPositions[i].Y,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1f,
                    bodyRadiusCm: 20f,
                    speedCmPerSecond: 800f,
                    layer);
            }

            flow.Reset(seeds, new[]
            {
                new MassNavigationObstacleConfig { Id = "group-fixture-obstacle", LocalXCm = 9000f, LocalYCm = 9000f, RadiusCm = 100f },
            });

            var world = World.Create();
            var agentState = new MassNavigationAgentState();
            for (int i = 0; i < localPositions.Length; i++)
            {
                Entity entity = world.Create(new FacingDirection { AngleRad = 0f });
                agentState.RegisterAgentAtIndex(entity, i, controllable: true);
            }

            var runtime = CreateTestNavGroupRuntime(
                agentCapacity: localPositions.Length,
                groupMemberCapacity: localPositions.Length);
            return new MassNavigationGroupRuntimeFixture(world, flow, agentState, runtime);
        }

        private static MassNavigationGroupRuntime CreateTestNavGroupRuntime(
            int agentCapacity = 16,
            int groupMemberCapacity = 16)
        {
            return new MassNavigationGroupRuntime(
                new MassNavigationFormationRuntime(new MassNavigationGroupSemantics()),
                CreateRuntimeCapacity(agentCapacity, groupMemberCapacity));
        }

        private static MassNavigationRuntimeCapacityConfig CreateRuntimeCapacity(
            int agentCapacity,
            int groupMemberCapacity)
        {
            return new MassNavigationRuntimeCapacityConfig
            {
                NavigationGroupCapacity = 8,
                GroupMembershipAgentCapacity = agentCapacity,
                SelectionMemberScratchCapacity = groupMemberCapacity,
                GroupMemberCapacity = groupMemberCapacity,
                OrderIngestionTokenCapacity = 8,
                OrderIngestionMemberCapacity = groupMemberCapacity,
                LoadedChunkCapacity = 16,
                MetadataTeamCapacity = 4,
            };
        }

        private static string TotalWarModRoot()
        {
            return Path.Combine(FindRepoRoot(), "mods", "showcases", "mass_navigation_total_war_entry", "MassNavigationTotalWarEntryMod");
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private static MassFlowSolverConfig CreateTestSolverConfig()
        {
            return new MassFlowSolverConfig
            {
                FieldWidthCm = 10_000,
                FieldHeightCm = 10_000,
                FlowCellSizeCm = 100,
                MaxObstacleCount = 64,
                ParallelWorkerCount = 1,
                SeparationHashCellSizeCm = 100,
                SeparationHashMinSearchRadiusCells = 2,
                HardResolveHashCellSizeCm = 50,
                HardResolveHashMinSearchRadiusCells = 1,
                PlayAreaMinXCm = 50f,
                PlayAreaMaxXCm = 9_950f,
                PlayAreaMinYCm = 50f,
                PlayAreaMaxYCm = 9_950f,
            };
        }

        private sealed class MassNavigationGroupRuntimeFixture : IDisposable
        {
            public MassNavigationGroupRuntimeFixture(
                World world,
                MassFlowSimulationState flow,
                MassNavigationAgentState agentState,
                MassNavigationGroupRuntime runtime)
            {
                World = world;
                Flow = flow;
                AgentState = agentState;
                Runtime = runtime;
            }

            public World World { get; }
            public MassFlowSimulationState Flow { get; }
            public MassNavigationAgentState AgentState { get; }
            public MassNavigationGroupRuntime Runtime { get; }

            public void Dispose()
            {
                World.Destroy(World);
            }
        }

        private sealed class NullModContext : Ludots.Core.Modding.IModContext
        {
            private readonly Ludots.Core.Modding.VirtualFileSystem _vfs = new();
            private readonly FunctionRegistry _functionRegistry = new();
            private readonly Ludots.Core.Engine.SystemFactoryRegistry _systemFactoryRegistry = new();
            private readonly TriggerDecoratorRegistry _triggerDecorators = new();
            private readonly Ludots.Core.Diagnostics.LogChannel _logChannel =
                Ludots.Core.Diagnostics.Log.GetOrCreateModChannel("MassNavigationTotalWarShowcaseContractTests");

            public string ModId => "MassNavigationTotalWarShowcaseContractTests";
            public Ludots.Core.Modding.IVirtualFileSystem VFS => _vfs;
            public FunctionRegistry FunctionRegistry => _functionRegistry;
            public Ludots.Core.Engine.SystemFactoryRegistry SystemFactoryRegistry => _systemFactoryRegistry;
            public TriggerDecoratorRegistry TriggerDecorators => _triggerDecorators;
            public Ludots.Core.Diagnostics.LogChannel LogChannel => _logChannel;

            public void Log(string message) { }
            public void Log(Ludots.Core.Diagnostics.LogLevel level, string message) { }
            public Stream GetResource(string uri) => VFS.GetStream(uri);
            public void OnEvent(EventKey eventKey, Func<ScriptContext, System.Threading.Tasks.Task> handler) { }
        }

        private static class TotalWarAcceptance
        {
            public const string InputBackendKey = "Tests.TotalWar.InputBackend";
            public const string WorldScreenMappingKey = "Tests.TotalWar.WorldScreenMapping";
            public const string LeftMousePath = "<Mouse>/LeftButton";
            public const string RightMousePath = "<Mouse>/RightButton";
            public const string RotateRightActionId = "MassNavigation_RotateRight";
            public const float FrameSeconds = 1f / 20f;
            public const float PixelsPerCm = 0.08f;
            public const float HeadlessRayOriginHeightM = 2000f;
            public const float SoldierFormationOffsetRebaseToleranceSq = 40_000f;
            public const int FrameBudgetForMapEntry = 4;
            public const int FrameBudgetForScenarioReady = 220;
            public const int FrameBudgetForInteraction = 40;
            public const int FrameBudgetForInputRelease = 2;
            public const int FrameBudgetForPresentationDestroy = 80;
            public const int ExpectedTotalSoldiers = 1280;
            public const int ExpectedTotalFormations = 6;
            public const int ExpectedTotalAgents = ExpectedTotalSoldiers + ExpectedTotalFormations;
            public const int ExpectedInitialSelection = 1;
            public const int ExpectedOutlineSplineSegments = 576;
            public const float MultiFormationSpacingRetentionRatio = 0.8f;
            public const float SelectionDragPaddingPixels = 24f;
            public static readonly Vector2 ScreenCenter = new(960f, 540f);
            public static readonly Vector2 EmptyGroundWorldCm = new(18000f, 18000f);
            public static readonly Vector2 MoveTargetWorldCm = new(-3600f, -400f);
            public static readonly Vector2 MultiFormationMoveTargetWorldCm = new(3200f, 200f);
            public static readonly Vector2 SolverWindowRebaseFocusWorldCm = new(1_200_000f, 400_000f);
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class TotalWarWorldScreenMapping : IScreenProjector, IScreenRayProvider
        {
            private readonly Vector2 _screenCenter;
            private readonly float _pixelsPerCm;

            public TotalWarWorldScreenMapping(Vector2 screenCenter, float pixelsPerCm)
            {
                if (!(pixelsPerCm > 0f))
                {
                    throw new ArgumentOutOfRangeException(nameof(pixelsPerCm));
                }

                _screenCenter = screenCenter;
                _pixelsPerCm = pixelsPerCm;
            }

            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(
                    _screenCenter.X + (worldPosition.X * 100f * _pixelsPerCm),
                    _screenCenter.Y + (worldPosition.Z * 100f * _pixelsPerCm));
            }

            public ScreenRay GetRay(Vector2 screenPosition)
            {
                float worldXCm = (screenPosition.X - _screenCenter.X) / _pixelsPerCm;
                float worldYCm = (screenPosition.Y - _screenCenter.Y) / _pixelsPerCm;
                return new ScreenRay(
                    new Vector3(worldXCm / 100f, TotalWarAcceptance.HeadlessRayOriginHeightM, worldYCm / 100f),
                    -Vector3.UnitY);
            }
        }

        private sealed class TempTemplatePipeline : IDisposable
        {
            private TempTemplatePipeline(string root, ConfigPipeline pipeline, ConfigCatalog catalog)
            {
                Root = root;
                Pipeline = pipeline;
                Catalog = catalog;
            }

            public string Root { get; }
            public ConfigPipeline Pipeline { get; }
            public ConfigCatalog Catalog { get; }

            public static TempTemplatePipeline Create(string templatesJson)
            {
                string root = Path.Combine(Path.GetTempPath(), "ludots-total-war-template-" + Guid.NewGuid().ToString("N"));
                string entityDir = Path.Combine(root, "Entities");
                Directory.CreateDirectory(entityDir);
                File.WriteAllText(Path.Combine(entityDir, "templates.json"), templatesJson);
                string configDir = Path.Combine(root, "Configs");
                Directory.CreateDirectory(configDir);
                File.WriteAllText(
                    Path.Combine(configDir, "config_catalog.json"),
                    "[{ \"Path\": \"Entities/templates.json\", \"Policy\": \"ArrayById\", \"IdField\": \"id\" }]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var triggerManager = new TriggerManager();
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), triggerManager);
                var pipeline = new ConfigPipeline(vfs, modLoader);
                return new TempTemplatePipeline(root, pipeline, ConfigCatalogLoader.Load(pipeline));
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
        }
    }
}
