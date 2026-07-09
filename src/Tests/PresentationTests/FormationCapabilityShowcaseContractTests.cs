using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.MassNavigation.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using FormationCapabilityShowcaseMod.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class FormationCapabilityShowcaseContractTests
    {
        private const string MassNavigationAgentLayerName = "massNavigation.agent";
        private const int TestMassNavigationMoveOrderTypeId = 37;

        [Test]
        public void FormationCapabilityConfig_AuthorsFormationAndSoldierMassNavAgents()
        {
            string modRoot = FormationCapabilityModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "FormationCapabilityShowcaseConfig.json"));
            JsonObject massNavConfig = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonObject agentProfiles = massNavConfig["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig must author agentProfiles.");
            JsonObject formationAgent = config["formationAgent"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author formationAgent.");
            AssertAgentAuthoringReferencesProfileOnly(formationAgent, "formationAgent");
            string formationTemplateId = RequireString(formationAgent, "templateId");
            Assert.That(formationTemplateId, Is.EqualTo("formation_capability_showcase_formation_agent"));
            string formationProfileId = RequireString(formationAgent, "profileId");
            Assert.That(formationProfileId, Is.EqualTo("formation"));
            Assert.That(config.ContainsKey("selection"), Is.False,
                "FormationCapability config must not invent a private selection scope block; command-source acquire uses game.json commandSource.targetFilter.");
            JsonObject gameConfig = ReadObject(Path.Combine(modRoot, "assets", "game.json"));
            Assert.That(gameConfig["startupLocalPlayerId"]?.GetValue<int>(), Is.EqualTo(1),
                "FormationCapability must publish LocalPlayerEntity through the formal startup player + map participant binding path before command-source mutation.");
            JsonObject commandSource = gameConfig["commandSource"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability game.json must author commandSource.");
            JsonObject targetFilter = commandSource["targetFilter"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability game.json commandSource must author targetFilter.");
            Assert.That(RequireString(targetFilter, "relationFilter"), Is.EqualTo("Friendly"),
                "FormationCapability player acquisition must use Core RelationshipFilter authoring, not a showcase-local selection policy.");
            JsonObject mapConfig = ReadObject(Path.Combine(modRoot, "assets", "Maps", "formation_capability_showcase.json"));
            JsonObject localPlayerEntity = mapConfig["Entities"]?.AsArray()
                .Select(node => node?.AsObject())
                .FirstOrDefault(node => string.Equals(node?["InstanceId"]?.GetValue<string>(), "formation.local_player", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("FormationCapability map must author a local player representative entity.");
            Assert.That(localPlayerEntity["Template"]?.GetValue<string>(), Is.EqualTo("mass_navigation_local_player"));
            JsonObject localPlayerBinding = mapConfig["Players"]?.AsArray()
                .Select(node => node?.AsObject())
                .FirstOrDefault(node => node?["PlayerId"]?.GetValue<int>() == 1)
                ?? throw new InvalidOperationException("FormationCapability map must bind Player 1.");
            Assert.That(localPlayerBinding["RepresentativeInstanceId"]?.GetValue<string>(), Is.EqualTo("formation.local_player"));
            JsonObject formationProfile = FindAgentProfileById(agentProfiles, formationProfileId);
            float formationSpeedCmPerSecond = formationProfile["speedCmPerSecond"]?.GetValue<float>()
                ?? throw new InvalidOperationException("MassNavigation formation agent profile speedCmPerSecond must be numeric.");

            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));
            JsonObject formationTemplate = FindObjectById(templates, formationTemplateId);
            JsonObject components = formationTemplate["components"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability formation agent template must author components.");
            Assert.That(components.ContainsKey("Name"), Is.True);
            Assert.That(components.ContainsKey("WorldPositionCm"), Is.True);
            Assert.That(components.ContainsKey("VisualHeightmapSampleState"), Is.True,
                "Formation outline height must follow the visual-heightmap SSOT through the agent visual transform.");
            Assert.That(components.ContainsKey("FacingDirection"), Is.True);
            Assert.That(components.ContainsKey("MassNavigationAgent"), Is.True);
            Assert.That(components.ContainsKey("OrderBuffer"), Is.True);
            Assert.That(components.ContainsKey("CommandSourceSelectableTag"), Is.True);
            Assert.That(components.ContainsKey("CommandSourceSelectableState"), Is.True);
            Assert.That(components.ContainsKey("Team"), Is.False,
                "Formation template must not bake scene team; FormationCapabilityShowcaseConfig teamId is applied by the generic runtime spawn request.");
            Assert.That(components.ContainsKey("PlayerOwner"), Is.False,
                "Formation template must not bake scene ownership; FormationCapabilityShowcaseConfig ownerPlayerId is applied by the generic runtime spawn request.");
            Assert.That(components.ContainsKey("AttributeBuffer"), Is.True);
            Assert.That(components.ContainsKey("SpatialBounds"), Is.False,
                "Formation footprint is derived from FormationCapabilityShowcaseConfig outline during scenario binding, not authored in the template.");
            Assert.That(components.ContainsKey("SpatialFootprint2D"), Is.False,
                "Formation footprint vertices must not drift away from the configured outline.");
            Assert.That(components.ContainsKey("MassNavigationFormationAnchor"), Is.False,
                "Formation identity is per spawned formation and must be applied by runtime component patch, not an empty template placeholder.");
            Assert.That(components.ContainsKey("MassNavigationFollowerLocomotion"), Is.True,
                "Follower sync tuning belongs to component authoring, not a showcase-only runtime config block.");

            JsonArray formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("FormationCapability config must author formations.");
            Assert.That(formations.Count, Is.GreaterThan(0));
            string[] shapes = formations
                .Select(node => RequireString(node?.AsObject() ?? throw new InvalidOperationException("Formation must be an object."), "outline", "shape"))
                .ToArray();
            Assert.That(shapes, Does.Contain("Rectangle"));
            Assert.That(shapes, Does.Contain("Circle"));

            Assert.That(config.ContainsKey("soldierTargetSync"), Is.False,
                "Follower sync tuning must be authored through MassNavigationFollowerLocomotion on formation templates.");
            Assert.That(config.ContainsKey("formationSync"), Is.False,
                "FormationCapability config must not keep empty sync sections without runtime semantics.");
            JsonObject obstacleOverlay = config["obstacleOverlay"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author obstacleOverlay.");
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
                    "FormationCapability soldier MassNavigation agents must use agentProfiles configured faster than formation MassNavigation agents.");
                JsonObject soldierTemplate = FindObjectById(templates, RequireString(soldierAgent, "templateId"));
                JsonObject soldierComponents = soldierTemplate["components"]?.AsObject()
                    ?? throw new InvalidOperationException("FormationCapability soldier template must author components.");
                Assert.That(soldierComponents.ContainsKey("MassNavigationAgent"), Is.True);
                Assert.That(soldierComponents.ContainsKey("MassNavigationFormationFollower"), Is.False,
                    "Soldier slot binding is per spawned soldier and must be applied by runtime component patch, not an empty template placeholder.");
                Assert.That(soldierComponents.ContainsKey("Team"), Is.False,
                    "Soldier team is owned by the formation config and applied by the generic runtime spawn request; templates must not author a second team SSOT.");
                Assert.That(soldierComponents.ContainsKey("OrderBuffer"), Is.False);
                Assert.That(soldierComponents.ContainsKey("CommandSourceSelectableTag"), Is.False);
                Assert.That(soldierComponents.ContainsKey("CommandSourceSelectableState"), Is.False);
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
        public void FormationCapabilityMassNavigationConfig_IsDeepObjectOverrideAndMergesWithBaseConfig()
        {
            string modRoot = FormationCapabilityModRoot();
            JsonArray catalog = ReadArray(Path.Combine(modRoot, "assets", "Configs", "config_catalog.json"));
            JsonObject massNavEntry = FindObjectByPath(catalog, "MassNavigationConfig.json");
            JsonObject formationCapabilityEntry = FindObjectByPath(catalog, "FormationCapabilityShowcaseConfig.json");
            Assert.That(RequireString(massNavEntry, "Policy"), Is.EqualTo("DeepObject"),
                "Showcase MassNavigation config must be a focused override merged through ConfigPipeline.");
            Assert.That(RequireString(formationCapabilityEntry, "Policy"), Is.EqualTo("Replace"),
                "FormationCapability showcase authoring is a complete scenario SSOT; it must not rely on DeepObject field fill.");

            JsonObject overrideConfig = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            Assert.That(overrideConfig.ContainsKey("cadence"), Is.False,
                "Showcase MassNavigationConfig must not duplicate base cadence defaults.");
            JsonObject config = LoadMergedFormationCapabilityMassNavigationConfigObject();
            JsonObject showcaseConfig = ReadObject(Path.Combine(modRoot, "assets", "FormationCapabilityShowcaseConfig.json"));
            Assert.That(showcaseConfig.ContainsKey("selection"), Is.False,
                "FormationCapabilityShowcaseConfig must not invent a private selection scope block.");
            Assert.That(showcaseConfig["initialCommandSourceEntityCapacity"]?.GetValue<int>(), Is.GreaterThan(0),
                "FormationCapabilityShowcaseConfig must explicitly author initial command actor scratch capacity.");
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
                "teamRelationships",
                "flow",
                "arrival",
                "avoidance",
                "semantics",
                "streaming",
            };
            foreach (string property in required)
            {
                Assert.That(config.ContainsKey(property), Is.True, $"FormationCapability MassNavigationConfig must author '{property}'.");
            }

            string[] forbidden =
            {
                "cameraProfiles",
                "minimap",
                "viewResidency",
            };
            foreach (string property in forbidden)
            {
                Assert.That(config.ContainsKey(property), Is.False,
                    $"MassNavigationConfig must stay data-only for MassNavigation and must not own '{property}'.");
            }

            Assert.That(RequireString(config, "mapId"), Is.EqualTo("formation_capability_showcase"));
            JsonObject agentProfiles = config["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("agentProfiles must be authored.");
            JsonArray profiles = agentProfiles["profiles"]?.AsArray()
                ?? throw new InvalidOperationException("agentProfiles.profiles must be authored.");
            Assert.That(profiles.Select(node => node?["id"]?.GetValue<string>()).ToArray(),
                Is.EquivalentTo(new[] { "formation", "heavy", "light" }));
            JsonObject formationProfile = FindAgentProfileById(agentProfiles, "formation");
            Assert.That(formationProfile.ContainsKey("bodyRadiusCm"), Is.False,
                "MassNavigationConfig.agentProfiles no longer owns geometry; radius comes from Navigation/agent_profiles.json.");
            AgentProfileRegistry geometryProfiles = LoadFormationCapabilityAgentProfiles();
            AgentProfileConfig formationGeometry = geometryProfiles.Require("formation", "FormationCapability formation geometry");
            Assert.That(formationGeometry.RadiusCm, Is.EqualTo(720f));
            Assert.That(formationGeometry.Mass, Is.EqualTo(12f));
            Assert.That(formationProfile["speedCmPerSecond"]?.GetValue<float>(), Is.EqualTo(360f));
            JsonObject scenarioRuntime = config["scenarioRuntime"]?.AsObject()
                ?? throw new InvalidOperationException("scenarioRuntime must be authored.");
            Assert.That(scenarioRuntime["autoSpawnConfiguredScenario"]?.GetValue<bool>(), Is.False);
            Assert.That(scenarioRuntime["initialCommandActorScratchCapacity"]?.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(scenarioRuntime["initialCommandActorSnapshotCapacity"]?.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(scenarioRuntime.ContainsKey("panel"), Is.False);
            Assert.That(scenarioRuntime.ContainsKey("panelControls"), Is.False);
            JsonObject scenario = config["scenario"]?.AsObject()
                ?? throw new InvalidOperationException("scenario must be authored.");
            Assert.That(scenario["agentsPerTeam"]?.GetValue<int>(), Is.EqualTo(0),
                "FormationCapability runtime owns formation/soldier spawning; the shared MassNavigation config must not auto-author generic scenario agents.");
            Assert.That(scenario["spawnLayout"], Is.Null,
                "FormationCapability does not use MassNavigation generic auto-spawn, so the DeepObject override must disable the base spawn layout.");
            JsonArray scenarioTeams = scenario["teams"]?.AsArray()
                ?? throw new InvalidOperationException("scenario.teams must be authored.");
            Assert.That(scenarioTeams.Select(node => node?["id"]?.GetValue<int>()).ToArray(),
                Is.EquivalentTo(new[] { 1, 2 }));
            JsonObject presentation = config["presentation"]?.AsObject()
                ?? throw new InvalidOperationException("presentation must be authored.");
            JsonArray presentationTeams = presentation["teams"]?.AsArray()
                ?? throw new InvalidOperationException("presentation.teams must be authored.");
            Assert.That(presentationTeams.Count, Is.EqualTo(0),
                "FormationCapability owns formation/soldier spawning through FormationCapabilityShowcaseConfig; MassNavigation generic auto-spawn team mappings must stay empty to avoid a second template SSOT.");
            Assert.That(presentation["blockerPerformerId"], Is.Null);
            Assert.That(presentation["hotspotPerformerId"], Is.Null);
            Assert.That(presentation["blockerTemplateId"]?.GetValue<string>(), Is.EqualTo("mass_navigation_blocker"),
                "Configured obstacles seed core-owned MassNavigationBlocker entities through the runtime template spawn path even when agents are externally authored.");
            Assert.That(presentation["hotspotTemplateId"], Is.Null);
            JsonArray requiredMeshAssetIds = presentation["requiredMeshAssetIds"]?.AsArray()
                ?? throw new InvalidOperationException("presentation.requiredMeshAssetIds must be authored.");
            string[] requiredMeshIds = requiredMeshAssetIds.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray();
            Assert.That(requiredMeshIds, Does.Contain("mass_navigation.agent.soldier"));
            Assert.That(requiredMeshIds, Does.Contain("mass_navigation.command.marker"));
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
                Assert.That(solver.ContainsKey(property), Is.True, $"FormationCapability MassNavigationConfig must author solver.{property}.");
            }

            JsonObject world = config["world"]?.AsObject()
                ?? throw new InvalidOperationException("world must be authored.");
            Assert.That(solver["fieldWidthCm"]!.GetValue<int>(), Is.EqualTo(world["solverWindowWidthCm"]!.GetValue<int>()));
            Assert.That(solver["fieldHeightCm"]!.GetValue<int>(), Is.EqualTo(world["solverWindowHeightCm"]!.GetValue<int>()));

            JsonObject streaming = config["streaming"]?.AsObject()
                ?? throw new InvalidOperationException("streaming must be authored.");
            Assert.That(streaming["retainSeconds"]?.GetValue<float>(), Is.EqualTo(12f));
            Assert.That(streaming["radiusCm"]?.GetValue<int>(), Is.EqualTo(24000));

            JsonObject avoidance = config["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("avoidance must be authored.");
            Assert.That(avoidance.ContainsKey("lightNavMass"), Is.False,
                "FormationCapability agent mass must be owned only by Navigation/agent_profiles.json.");
            Assert.That(avoidance.ContainsKey("heavyNavMass"), Is.False,
                "FormationCapability agent mass must be owned only by Navigation/agent_profiles.json.");
            Assert.That(avoidance.ContainsKey("lightVisualScale"), Is.False,
                "FormationCapability agent visualScale must be owned only by MassNavigationConfig.agentProfiles.");
            Assert.That(avoidance.ContainsKey("heavyVisualScale"), Is.False,
                "FormationCapability agent visualScale must be owned only by MassNavigationConfig.agentProfiles.");
            Assert.That(avoidance["dominantMassRatio"]?.GetValue<float>(), Is.GreaterThan(0f));
            Assert.That(avoidance["friendlyResponseScale"]?.GetValue<float>(), Is.GreaterThan(0f));
            JsonObject legacyAvoidanceConfig = LoadMergedFormationCapabilityMassNavigationConfigObject();
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
                Assert.That(group.ContainsKey(property), Is.True, $"FormationCapability MassNavigationConfig must author group.{property}.");
                AssertPositive(group, property, allowZero: property == "formationRotationEpsilonRadians");
            }
        }

        [Test]
        public void MassNavigationSolverConfig_IsRequiredAndUsedByRuntime()
        {
            JsonObject missingSolver = LoadMergedFormationCapabilityMassNavigationConfigObject();
            missingSolver.Remove("solver");
            InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(missingSolver))!;
            Assert.That(missing.Message, Does.Contain("solver"));

            JsonObject invalidSolver = LoadMergedFormationCapabilityMassNavigationConfigObject();
            invalidSolver["solver"]!["flowCellSizeCm"] = 96;
            InvalidOperationException invalid = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(invalidSolver))!;
            Assert.That(invalid.Message, Does.Contain("solver"));
            Assert.That(invalid.Message, Does.Contain("FlowCellSizeCm"));

            JsonObject configJson = LoadMergedFormationCapabilityMassNavigationConfigObject();
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
        public void FormationCapabilityShowcaseConfig_RejectsMissingProfilesAndLegacyAgentRuntimeFields()
        {
            JsonObject config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            JsonObject massNavConfig = LoadMergedFormationCapabilityMassNavigationConfigObject();
            FormationCapabilityShowcaseConfig loaded = FormationCapabilityShowcaseConfig.Load(config);
            MassNavigationConfig loadedMassNav = MassNavigationConfig.Load(massNavConfig);
            AgentProfileRegistry geometryProfiles = LoadFormationCapabilityAgentProfiles();
            Assert.DoesNotThrow(() => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles, geometryProfiles));

            JsonObject formationAgent = config["formationAgent"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author formationAgent.");
            formationAgent["profileId"] = "missing_formation_profile";
            loaded = FormationCapabilityShowcaseConfig.Load(config);
            InvalidOperationException missingFormationProfile = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles, geometryProfiles))!;
            Assert.That(missingFormationProfile.Message, Does.Contain("formationAgent.profileId"));
            Assert.That(missingFormationProfile.Message, Does.Contain("missing_formation_profile"));

            config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            JsonArray formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("FormationCapability config must author formations.");
            JsonObject firstFormation = formations[0]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability formation must be an object.");
            JsonObject soldierAgent = firstFormation["soldierAgent"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability formation must author soldierAgent.");
            soldierAgent["profileId"] = "missing_soldier_profile";
            loaded = FormationCapabilityShowcaseConfig.Load(config);
            InvalidOperationException missingSoldierProfile = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles, geometryProfiles))!;
            Assert.That(missingSoldierProfile.Message, Does.Contain("soldierAgent.profileId"));
            Assert.That(missingSoldierProfile.Message, Does.Contain("missing_soldier_profile"));

            config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            formationAgent = config["formationAgent"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author formationAgent.");
            formationAgent["navMass"] = 12f;
            System.Text.Json.JsonException legacyFormationField = Assert.Throws<System.Text.Json.JsonException>(
                () => FormationCapabilityShowcaseConfig.Load(config))!;
            Assert.That(legacyFormationField.Message, Does.Contain("navMass"));

            config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("FormationCapability config must author formations.");
            firstFormation = formations[0]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability formation must be an object.");
            soldierAgent = firstFormation["soldierAgent"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability formation must author soldierAgent.");
            soldierAgent["speedCmPerSecond"] = 920f;
            System.Text.Json.JsonException legacySoldierField = Assert.Throws<System.Text.Json.JsonException>(
                () => FormationCapabilityShowcaseConfig.Load(config))!;
            Assert.That(legacySoldierField.Message, Does.Contain("speedCmPerSecond"));

            config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            loaded = FormationCapabilityShowcaseConfig.Load(config);
            massNavConfig = LoadMergedFormationCapabilityMassNavigationConfigObject();
            JsonObject profiles = massNavConfig["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig must author agentProfiles.");
            JsonObject profile = FindAgentProfileById(profiles, "light");
            profile["speedCmPerSecond"] = FindAgentProfileById(profiles, "formation")["speedCmPerSecond"]?.GetValue<float>()
                ?? throw new InvalidOperationException("formation profile speed must be numeric.");
            loadedMassNav = MassNavigationConfig.Load(massNavConfig);
            InvalidOperationException equalSpeed = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles, geometryProfiles))!;
            Assert.That(equalSpeed.Message, Does.Contain("soldierAgent.profileId"));
            Assert.That(equalSpeed.Message, Does.Contain("formationAgent.profileId"));

            massNavConfig = LoadMergedFormationCapabilityMassNavigationConfigObject();
            profiles = massNavConfig["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig must author agentProfiles.");
            profile = FindAgentProfileById(profiles, "light");
            profile["speedCmPerSecond"] = FindAgentProfileById(profiles, "formation")["speedCmPerSecond"]!.GetValue<float>() - 1f;
            loadedMassNav = MassNavigationConfig.Load(massNavConfig);
            InvalidOperationException slowerSoldier = Assert.Throws<InvalidOperationException>(
                () => loaded.ValidateAgentProfileReferences(loadedMassNav.AgentProfiles, geometryProfiles))!;
            Assert.That(slowerSoldier.Message, Does.Contain("soldierAgent.profileId"));
            Assert.That(slowerSoldier.Message, Does.Contain("formationAgent.profileId"));
        }

        [Test]
        public void TeamRelationshipConfig_RejectsCaseAliases()
        {
            JsonObject config = LoadMergedFormationCapabilityMassNavigationConfigObject();
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
        public void FormationCapabilityRuntime_UsesComponentAuthoredRuntimeBindingAndPresentationLifecycle()
        {
            AssertPublicMethod(typeof(FormationCapabilityShowcaseRuntime), nameof(FormationCapabilityShowcaseRuntime.BindComponentAuthoredScenarioEntities));
            AssertPublicMethod(typeof(FormationCapabilityShowcaseRuntime), nameof(FormationCapabilityShowcaseRuntime.HandleMapUnloadedAsync));

            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: 180,
                "Formation Capability scenario should bind runtime-spawned formation agents.");

            Entity[] agents = CaptureTrackedAgents(simulation);
            Assert.That(agents.Any(entity => engine.World.Has<MassNavigationFormationAnchor>(entity)), Is.True);
            Assert.That(agents.Any(entity => engine.World.Has<MassNavigationFormationFollower>(entity)), Is.True);
        }

        [Test]
        public void FormationCapabilityRuntime_DelegatesSoldierSlotTargetsToCoreMassNavigationFormationFollowerSystem()
        {
            AssertPublicMethod(typeof(MassNavigationFormationFollowerSystem), nameof(MassNavigationFormationFollowerSystem.GetSyncStateCountForTests));

            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability soldiers should be runtime-bound to Core MassNavigationFormationFollower sidecars.");

            int soldierFollowers = 0;
            var query = new QueryDescription().WithAll<
                FormationCapabilityShowcaseFormationSoldier,
                MassNavigationFormationFollower,
                MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationSoldier _, ref MassNavigationFormationFollower _, ref MassNavigationAgentIndex _) =>
            {
                soldierFollowers++;
            });

            Assert.That(soldierFollowers, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalSoldiers));
        }

        [Test]
        public void MassNavigationFormationFollowerSystem_ResetRebuildDoesNotApplyPreviousCarrierDelta()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                int formationId = MassNavigationFormationRegistry.Register("tests.massNavigation.reset.rebuild");
                Entity firstAnchor = CreateFormationAnchor(engine.World, formationId, slotCount: 1);
                Entity firstFollower = CreateFormationFollower(engine.World, formationId, slotIndex: 0, localOffsetXCm: 100f, localOffsetYCm: 0f);
                simulation.RebuildFromAuthoredAgents(
                    engine.World,
                    new[] { firstAnchor, firstFollower },
                    new[]
                    {
                        CreateAgentSeed(simulation, worldXCm: 1000f, worldYCm: 1000f),
                        CreateAgentSeed(simulation, worldXCm: 1100f, worldYCm: 1000f),
                    },
                    new[] { true, false });

                var followerSystem = new MassNavigationFormationFollowerSystem(engine, simulation);
                UpdateSystem(followerSystem);
                Assert.That(simulation.GetAgentWorldPositionCm(1).X, Is.EqualTo(1100f).Within(0.001f));

                simulation.ResetRuntimeState(engine.World);
                Entity rebuiltAnchor = CreateFormationAnchor(engine.World, formationId, slotCount: 1);
                Entity rebuiltFollower = CreateFormationFollower(engine.World, formationId, slotIndex: 0, localOffsetXCm: 100f, localOffsetYCm: 0f);
                simulation.RebuildFromAuthoredAgents(
                    engine.World,
                    new[] { rebuiltAnchor, rebuiltFollower },
                    new[]
                    {
                        CreateAgentSeed(simulation, worldXCm: 3000f, worldYCm: 1000f),
                        CreateAgentSeed(simulation, worldXCm: 3100f, worldYCm: 1000f),
                    },
                    new[] { true, false });

                UpdateSystem(followerSystem);

                Vector2 followerWorld = simulation.GetAgentWorldPositionCm(1);
                Assert.That(followerWorld.X, Is.EqualTo(3100f).Within(0.001f),
                    "Reusing a formation id after reset/rebuild must not displace the new follower by the previous carrier's delta.");
                Assert.That(followerWorld.Y, Is.EqualTo(1000f).Within(0.001f));
            }
        }

        [Test]
        public void MassNavigationFormationFollowerSystem_AppendFollowerDoesNotApplyPreviousCarrierDelta()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                int formationId = MassNavigationFormationRegistry.Register("tests.massNavigation.append.follower");
                Entity anchor = CreateFormationAnchor(engine.World, formationId, slotCount: 2);
                Entity firstFollower = CreateFormationFollower(engine.World, formationId, slotIndex: 0, localOffsetXCm: 100f, localOffsetYCm: 0f);
                simulation.RebuildFromAuthoredAgents(
                    engine.World,
                    new[] { anchor, firstFollower },
                    new[]
                    {
                        CreateAgentSeed(simulation, worldXCm: 1000f, worldYCm: 1000f),
                        CreateAgentSeed(simulation, worldXCm: 1100f, worldYCm: 1000f),
                    },
                    new[] { true, false });

                var followerSystem = new MassNavigationFormationFollowerSystem(engine, simulation);
                UpdateSystem(followerSystem);

                simulation.MassNavigationFlow.SetUnitPositionForTests(
                    index: 0,
                    localXCm: simulation.ToLocalXCm(1200f),
                    localYCm: simulation.ToLocalYCm(1000f));
                Entity appendedFollower = CreateFormationFollower(engine.World, formationId, slotIndex: 1, localOffsetXCm: 0f, localOffsetYCm: 200f);
                simulation.AppendAuthoredAgents(
                    engine.World,
                    new[] { appendedFollower },
                    new[] { CreateAgentSeed(simulation, worldXCm: 1400f, worldYCm: 1000f) },
                    new[] { false });

                UpdateSystem(followerSystem);

                Assert.That(simulation.GetAgentWorldPositionCm(1).X, Is.EqualTo(1300f).Within(0.001f),
                    "Existing followers should still receive the carrier delta.");
                Vector2 appendedFollowerWorld = simulation.GetAgentWorldPositionCm(2);
                Assert.That(appendedFollowerWorld.X, Is.EqualTo(1400f).Within(0.001f),
                    "A follower appended after the carrier moved must not receive carrier displacement from before it joined the formation.");
                Assert.That(appendedFollowerWorld.Y, Is.EqualTo(1000f).Within(0.001f));
            }
        }

        [Test]
        public void MassNavigationFormationFollowerSystem_PrunesSyncStateWhenAnchorsDisappear()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                int formationA = MassNavigationFormationRegistry.Register("tests.massNavigation.prune.a");
                int formationB = MassNavigationFormationRegistry.Register("tests.massNavigation.prune.b");
                Entity anchorA = CreateFormationAnchor(engine.World, formationA, slotCount: 1);
                Entity followerA = CreateFormationFollower(engine.World, formationA, slotIndex: 0, localOffsetXCm: 100f, localOffsetYCm: 0f);
                Entity anchorB = CreateFormationAnchor(engine.World, formationB, slotCount: 1);
                Entity followerB = CreateFormationFollower(engine.World, formationB, slotIndex: 0, localOffsetXCm: 100f, localOffsetYCm: 0f);
                simulation.RebuildFromAuthoredAgents(
                    engine.World,
                    new[] { anchorA, followerA, anchorB, followerB },
                    new[]
                    {
                        CreateAgentSeed(simulation, worldXCm: 1000f, worldYCm: 1000f),
                        CreateAgentSeed(simulation, worldXCm: 1100f, worldYCm: 1000f),
                        CreateAgentSeed(simulation, worldXCm: 3000f, worldYCm: 1000f),
                        CreateAgentSeed(simulation, worldXCm: 3100f, worldYCm: 1000f),
                    },
                    new[] { true, false, true, false });

                var followerSystem = new MassNavigationFormationFollowerSystem(engine, simulation);
                UpdateSystem(followerSystem);
                Assert.That(followerSystem.GetSyncStateCountForTests(), Is.EqualTo(2));

                engine.World.Destroy(anchorB);
                UpdateSystem(followerSystem);

                Assert.That(followerSystem.GetSyncStateCountForTests(), Is.EqualTo(1),
                    "Formation sync state must be pruned when a formation anchor leaves the authored ECS query.");
            }
        }

        [Test]
        public void MassNavigationEnvironmentBindingSystem_BindsEcsBlockersIntoMassNavigationFlowObstacles()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                Entity blocker = engine.World.Create(
                    new WorldPositionCm { Value = Fix64Vec2.FromInt(1234, 2345) });
                var projection = new MassNavigationFlowObstacleProjection
                {
                    ShapeSignature = 17,
                    PoseSignature = 23,
                };
                projection.SetPiece(
                    0,
                    ManifestationObstacleShape2D.Circle,
                    offsetXCm: 10,
                    offsetYCm: -20,
                    radiusCm: 175);
                engine.World.Add(blocker, projection);
                var environmentSystem = new MassNavigationEnvironmentBindingSystem(engine, simulation);

                UpdateSystem(environmentSystem);

                Assert.That(engine.World.Has<MassNavigationBlockerProfile>(blocker), Is.True);
                Assert.That(engine.World.Get<MassNavigationBlockerProfile>(blocker).RadiusCm, Is.EqualTo(175f));
                Assert.That(simulation.AgentState.BlockerCount, Is.EqualTo(1));
                Assert.That(simulation.NavigationObstacleCount, Is.EqualTo(1));
                MassNavigationObstacleSnapshot obstacle = simulation.GetObstacleWorldSnapshot(0);
                Assert.That(obstacle.WorldXCm, Is.EqualTo(1244f).Within(0.001f));
                Assert.That(obstacle.WorldYCm, Is.EqualTo(2325f).Within(0.001f));
                Assert.That(obstacle.RadiusCm, Is.EqualTo(175f).Within(0.001f));
            }
        }

        [Test]
        public void RuntimeSpawnComponentPatch_FeedsMassNavigationFlowObstacleRadius()
        {
            const string templateId = "mass_navigation_test_blocker_override";
            string templateJson = """
[
  {
    "id": "mass_navigation_test_blocker_override",
    "components": {
      "Name": { "Value": "MassNavigation.TestBlockerOverride" },
      "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
      "ManifestationObstacleIntent2D": {
        "shape": "Circle",
        "sinkPhysicsCollider": false,
        "sinkNavigationObstacle": true,
        "radiusCm": 260,
        "navRadiusCm": 260,
        "localOffsetCm": { "x": 0, "y": 0 }
      }
    }
  }
]
""";

            using TempTemplatePipeline temp = TempTemplatePipeline.Create(templateJson);
            var templates = new DataRegistry<EntityTemplate>(temp.Pipeline);
            templates.Load("Entities/templates.json", temp.Catalog);
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                var requests = new RuntimeEntitySpawnQueue(capacity: 4);
                var spawnSystem = new RuntimeEntitySpawnSystem(
                    engine.World,
                    requests,
                    templates,
                    new EntityTemplateKeyRegistry(),
                    new Ludots.Core.Presentation.PresentationStableIdAllocator());
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = templateId,
                    WorldPositionCm = Fix64Vec2.FromInt(2222, 3333),
                    HasWorldPosition = 1,
                }), Is.True);
                spawnSystem.Update(0f);
                var shapeStorage = engine.GetService(CoreServiceKeys.Physics2DShapeStorage) as Ludots.Core.Physics2D.ShapeDataStorage2D
                    ?? throw new InvalidOperationException("Physics2D shape storage service must be registered before manifestation obstacle bridge update.");
                new Ludots.Core.Physics2D.Systems.ManifestationObstacleBridge2DSystem(engine.World, shapeStorage).Update(0f);

                var environmentSystem = new MassNavigationEnvironmentBindingSystem(engine, simulation);
                UpdateSystem(environmentSystem);

                Assert.That(simulation.NavigationObstacleCount, Is.EqualTo(1));
                MassNavigationObstacleSnapshot obstacle = simulation.GetObstacleWorldSnapshot(0);
                Assert.That(obstacle.WorldXCm, Is.EqualTo(2222f).Within(0.001f));
                Assert.That(obstacle.WorldYCm, Is.EqualTo(3333f).Within(0.001f));
                Assert.That(obstacle.RadiusCm, Is.EqualTo(260f).Within(0.001f));
            }
        }

        [Test]
        public void FormationCapabilitySoldierBinding_UsesCoreOwnedMassNavigationRuntimeBinding()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability should bind soldier sidecars from Core-authored MassNavigationFormationFollower components.");

            int soldierCount = 0;
            var query = new QueryDescription().WithAll<
                FormationCapabilityShowcaseFormationSoldier,
                MassNavigationFormationFollower,
                MassNavigationAgentIndex,
                MassNavigationAgent,
                Team>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationSoldier soldier, ref MassNavigationFormationFollower follower) =>
            {
                Assert.That(soldier.SlotIndex, Is.EqualTo(follower.SlotIndex));
                Assert.That(MassNavigationFormationRegistry.GetName(follower.FormationId), Is.Not.Empty);
                soldierCount++;
            });

            Assert.That(soldierCount, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalSoldiers));
        }

        [Test]
        public void FormationCapabilityShowcaseObstacleOverlayPlans_AreBuiltAfterMassNavigationLoadsObstacleSsot()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      simulation.NavigationObstacleCount > 0,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Obstacle overlays should be queued only after MassNavigation has loaded obstacle snapshots.");

            Entity[] overlays = CaptureObstacleOverlays(engine, simulation.NavigationObstacleCount);
            Assert.That(overlays.Length, Is.EqualTo(simulation.NavigationObstacleCount));
        }

        [Test]
        public void FormationCapabilityShowcaseObstacleOverlayTemplate_DoesNotAuthorConfigOwnedValues()
        {
            string modRoot = FormationCapabilityModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "FormationCapabilityShowcaseConfig.json"));
            JsonObject obstacleOverlay = config["obstacleOverlay"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author obstacleOverlay.");

            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));
            JsonObject overlayTemplate = FindObjectById(templates, RequireString(obstacleOverlay, "templateId"));
            JsonObject components = overlayTemplate["components"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability obstacle overlay template must author components.");

            Assert.That(components.ContainsKey("FormationCapabilityShowcaseObstacleOverlay"), Is.False,
                "Obstacle overlay values come from FormationCapabilityShowcaseConfig and MassNavigation obstacle radius.");

            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Obstacle overlay entities should be bound from MassNavigation obstacle snapshots.");

            AssertObstacleOverlayComponentsMatchSimulation(engine, simulation, obstacleOverlay);
        }

        [Test]
        public void FormationCapabilityShowcaseObstacleOverlayPresentation_UsesConfiguredWidthAndFormalStableId()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Obstacle overlay presentation should emit configured ground-overlay rings.");

            AssertObstacleOverlays(engine, simulation);
        }

        [Test]
        public void FormationCapabilityShowcaseFormationOutlines_UseRoadSplinesSampledFromVisualHeightmap()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation outlines should be emitted after Formation Capability scenario binding.");

            AssertFormationOutlines(engine);
        }

        [Test]
        public void FormationCapabilityPresentationHotPathCollections_UseConfigDerivedCapacities()
        {
            FormationCapabilityShowcaseConfig config = FormationCapabilityShowcaseConfig.Load(
                ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json")));

            int expectedSplineCapacity = config.Formations.Sum(formation =>
                FormationCapabilityShowcaseFormationOutlineSegments.CountSplineSegments(
                    formation.Outline.ResolvedShape,
                    formation.Outline.FrontIndicatorLengthCm > 0f,
                    formation.Outline.CurveSampleCount));

            Assert.That(config.FormationOutlineOwnerCapacity, Is.EqualTo(config.Formations.Length));
            Assert.That(config.FormationOutlineSplineCapacity, Is.EqualTo(expectedSplineCapacity));
            Assert.That(config.InitialCommandSourceEntityCapacity, Is.GreaterThanOrEqualTo(FormationCapabilityAcceptance.ExpectedInitialCommandSource));

            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            InstallFlatVisualHeightmap(engine);
            var runtime = new FormationCapabilityShowcaseRuntime();
            Assert.DoesNotThrow(() => new FormationCapabilityShowcaseFormationOutlinePresentationSystem(engine, runtime, config));
            Assert.DoesNotThrow(() => new FormationCapabilityShowcaseObstacleOverlayPresentationSystem(engine, runtime, LoadBaseMassNavigationConfig().Solver.MaxObstacleCount));
        }

        [Test]
        public void MassNavigationFlowCrowdCost_UsesAgentLayerWorldsInsteadOfTeamWideLayerUnion()
        {
            var isolatedFlow = CreateTestFlowState();
            var alphaOnly = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var betaOnly = new MassNavigationAgentLayer(categoryMask: 2u, interactionMask: 2u);
            ResetFlowWithTwoOverlappingAgents(isolatedFlow, alphaOnly, betaOnly);
            StepHardResolve(isolatedFlow);
            Assert.That(isolatedFlow.GetPositionX(0), Is.EqualTo(5000f).Within(0.001f));
            Assert.That(isolatedFlow.GetPositionX(1), Is.EqualTo(5000f).Within(0.001f));

            var interactingFlow = CreateTestFlowState();
            var alphaSeesBeta = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 2u);
            var betaSeesAlpha = new MassNavigationAgentLayer(categoryMask: 2u, interactionMask: 1u);
            ResetFlowWithTwoOverlappingAgents(interactingFlow, alphaSeesBeta, betaSeesAlpha);
            StepHardResolve(interactingFlow);
            float dx = interactingFlow.GetPositionX(0) - interactingFlow.GetPositionX(1);
            float dy = interactingFlow.GetPositionY(0) - interactingFlow.GetPositionY(1);
            Assert.That((dx * dx) + (dy * dy), Is.GreaterThan(1f));
        }

        [Test]
        public void FormationCapabilityOutlinePresentation_IgnoresDestroyPendingFormationAgents()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation outlines should exist before destroy-pending filtering is verified.");

            Entity[] formations = CaptureFormationAgents(engine, FormationCapabilityAcceptance.ExpectedTotalFormations);
            RoadSplineBuffer splines = engine.GetService(CoreServiceKeys.RoadSplineBuffer)
                ?? throw new InvalidOperationException("RoadSplineBuffer is missing.");
            Assert.That(splines.Count, Is.EqualTo(FormationCapabilityAcceptance.ExpectedOutlineSplineSegments));

            engine.World.Add(formations[0], new PresentationDestroyPending());
            Tick(engine);

            Assert.That(splines.Count, Is.LessThan(FormationCapabilityAcceptance.ExpectedOutlineSplineSegments));
        }

        [Test]
        public void GroundOverlayAssetIds_DoNotAcceptCaseAliases()
        {
            Assert.That(Enum.TryParse("Ring", ignoreCase: false, out GroundOverlayShape exact), Is.True);
            Assert.That(exact, Is.EqualTo(GroundOverlayShape.Ring));
            Assert.That(Enum.TryParse("ring", ignoreCase: false, out GroundOverlayShape _), Is.False);
        }

        [Test]
        public void MassNavigationOrderIngestion_ConsumesOnlyControllableAgents()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability agents should be bound before OrderIngestion composition is verified.");

            int controllableOrderBuffers = 0;
            var formationQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent, MassNavigationAgentIndex, OrderBuffer>();
            engine.World.Query(in formationQuery, (ref FormationCapabilityShowcaseFormationAgent _, ref MassNavigationAgentIndex index) =>
            {
                Assert.That(simulation.AgentState.TryGetControllableEntity(index.Value, out Entity controllable), Is.True);
                Assert.That(controllable, Is.Not.EqualTo(Entity.Null));
                controllableOrderBuffers++;
            });

            int soldierOrderBuffers = 0;
            var soldierQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationSoldier, MassNavigationAgentIndex>();
            engine.World.Query(in soldierQuery, (Entity entity, ref FormationCapabilityShowcaseFormationSoldier _, ref MassNavigationAgentIndex _) =>
            {
                if (engine.World.Has<OrderBuffer>(entity))
                {
                    soldierOrderBuffers++;
                }
            });

            Assert.That(controllableOrderBuffers, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalFormations));
            Assert.That(soldierOrderBuffers, Is.EqualTo(0));
        }

        [Test]
        public void MassNavigationOrderIngestion_PreallocatesCommandBucketsFromConfiguredRuntimeCapacity()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                simulation.Config.ScenarioRuntime.RuntimeCapacity.OrderIngestionTokenCapacity = 1;
                simulation.Config.ScenarioRuntime.RuntimeCapacity.OrderIngestionMemberCapacity = 1;
                RegisterMoveOrderType(engine);

                CreateActiveMassNavigationMoveOrderEntity(engine, token: 101, agentIndex: 0);
                CreateActiveMassNavigationMoveOrderEntity(engine, token: 202, agentIndex: 1);

                var system = new MassNavigationOrderIngestionSystem(engine, simulation);
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
                Assert.That(ex.Message, Does.Contain("orderIngestionTokenCapacity"));
            }
        }

        [Test]
        public void MassNavigationMetadataSync_UsesScenarioTeamOrderAsSsot()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                int initialActiveTeam = simulation.ActiveTeamId;
                int[] configuredOrder = { 7, initialActiveTeam, 11 };
                simulation.ConfigureScenarioTeams(configuredOrder);

                Assert.That(simulation.TeamIds.ToArray(), Is.EqualTo(configuredOrder));
                simulation.SetActiveTeam(11);
                simulation.ConfigureScenarioTeams(configuredOrder);
                Assert.That(simulation.ActiveTeamId, Is.EqualTo(11));
            }
        }

        [Test]
        public void FormationCapabilityMapUnload_DestroysAllTrackedMassNavigationAgents()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be ready before map unload verification.");

            Entity[] previousAgents = CaptureTrackedAgents(simulation);
            engine.UnloadMap("formation_capability_showcase");
            TickUntil(
                engine,
                () => CountAliveWithMassNavigationRuntimeTags(engine, previousAgents) == 0,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Map unload should strip MassNavigation runtime bindings from every tracked formation and soldier agent.");
        }

        [Test]
        public void MassNavigationVisualScale_IsNavigationProfileMetadataNotPerformerSizeSsot()
        {
            var flow = CreateTestFlowState();
            var layer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            flow.ResetAuthoredAgents(new[]
            {
                new MassNavigationAgentSeed(
                    teamId: 1,
                    localPositionXCm: 100f,
                    localPositionYCm: 100f,
                    heavy: false,
                    navMass: 1f,
                    visualScale: 1.75f,
                    bodyRadiusCm: 120f,
                    speedCmPerSecond: 800f,
                    layer),
            });

            Assert.That(flow.GetVisualScale(0), Is.EqualTo(1.75f));
            Assert.That(flow.GetBodyRadiusCm(0), Is.EqualTo(120f));

            JsonArray performers = ReadArray(Path.Combine(FormationCapabilityModRoot(), "assets", "Presentation", "performers.json"));
            JsonObject soldierPerformer = FindObjectById(performers, "formation_capability_showcase_soldier_azure_light");
            Assert.That(ContainsJsonProperty(soldierPerformer, "localScale"), Is.True);
        }

        [Test]
        public void FormationCapabilitySystems_AreGatedByShowcaseMapNotMassNavigationConfig()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            var runtime = new FormationCapabilityShowcaseRuntime();

            FocusCurrentMapSession(engine, "mass_navigation");
            Assert.That(runtime.IsCurrentShowcaseMap(engine), Is.False);

            FocusCurrentMapSession(engine, "formation_capability_showcase");
            Assert.That(runtime.IsCurrentShowcaseMap(engine), Is.True);
        }

        [Test]
        public void MassNavigationRuntimeSystems_AreActivatedByMapFocusNotGameStart()
        {
            using var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(MassNavigationDependencyPaths(), Path.Combine(FindRepoRoot(), "assets"));
            engine.Start();

            FocusCurrentMapSession(engine, "mass_navigation");

            Assert.DoesNotThrow(() => Tick(engine),
                "MassNavigation runtime systems must not tick between CurrentMapSession focus and MapLoaded board-world binding.");
            Assert.That(engine.GetService(MassNavigationKeys.SimulationRuntime), Is.Null);
        }

        [Test]
        public void MassNavigationAgentState_DestroyTrackedUsesPresentationLifecycleOnly()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState();
            Entity performerRoot = world.Create();
            Entity agent = world.Create(
                new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.Register("light") },
                new MassNavigationAgentIndex { Value = 0 },
                new MassNavigationAgentProfile { Heavy = false, VisualScale = 0.2f, SpeedCmPerSecond = 800f },
                new PresentationStableId { Value = 1001 },
                new PresentationDestroyEventPublished(),
                new PresentationOwnerHasPerformerPayload { Count = 1, RootCount = 1, SingleRootPerformer = performerRoot });

            state.RegisterAgentAtIndex(agent, agentIndex: 0, controllable: true);
            state.DestroyTracked(world);

            Assert.That(world.IsAlive(agent), Is.True);
            Assert.That(world.Has<PresentationDestroyPending>(agent), Is.True);
            Assert.That(world.Has<PresentationDestroyEventPublished>(agent), Is.False);
            Assert.That(world.Has<MassNavigationAgent>(agent), Is.True);
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
            Entity agent = world.Create(new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.Register("light") });

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
            Assert.That(state.ControllableAgentSlotCount, Is.EqualTo(1));
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
        public void MassNavigationFlowRuntimeProfile_RejectsBelowSemanticMinimumInsteadOfClamping()
        {
            var flow = CreateTestFlowState();
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
            flow.ResetAuthoredAgents(seeds);

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitRuntimeProfile(0, 1, 0f, 1f, 20f, 800f, layer));
            Assert.That(flow.GetNavMass(0), Is.EqualTo(1f));

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitRuntimeProfile(0, 1, 1f, 0f, 20f, 800f, layer));
            Assert.That(flow.GetVisualScale(0), Is.EqualTo(1f));

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitRuntimeProfile(0, 1, 1f, 1f, 0f, 800f, layer));
            Assert.That(flow.GetBodyRadiusCm(0), Is.EqualTo(20f));
        }

        [Test]
        public void MassNavigationFlowUnitTargetApis_RejectOutOfRangeAgentIndex()
        {
            var flow = CreateTestFlowState();
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

            flow.ResetAuthoredAgents(seeds);

            Assert.Throws<InvalidOperationException>(() => flow.SetUnitTarget(flow.UnitCount, 100f, 100f));
            Assert.Throws<InvalidOperationException>(() => flow.ReleaseUnitToTeamTarget(flow.UnitCount));
            Assert.Throws<InvalidOperationException>(() => flow.HoldUnitAtCurrentPosition(flow.UnitCount));
        }

        [Test]
        public void MassNavigationFlowExternalDisplacementRange_CarriesPositionAndUnitTarget()
        {
            var flow = CreateTestFlowState();
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
            flow.ResetAuthoredAgents(seeds);

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
            var flow = CreateTestFlowState();
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
            flow.ResetAuthoredAgents(seeds);

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

                flow.SetUnitPositionForTests(0, 1000f, 1800f);

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
            var flow = CreateTestFlowState();
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
            flow.ResetAuthoredAgents(seeds);

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

                flow.SetUnitPositionForTests(0, 1800f, 1220f);

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
        public void MassNavigationFlowHardResolve_SeparatesLargeAgentsByConfiguredBodyRadius()
        {
            var flow = CreateTestFlowState();
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
            flow.ResetAuthoredAgents(seeds);
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
        public void MassNavigationFlowResolveUnitNavigableTarget_UsesUnitBodyRadiusBeforeSetUnitTarget()
        {
            var flow = CreateTestFlowState();
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

            flow.ResetAuthoredAgents(seeds);
            flow.ResetRuntimeObstaclesFromWorld(new[]
            {
                new MassNavigationObstacleSnapshot(5_000f, 5_000f, 200f),
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
                "MassNavigationFlow target writes that represent agent slots must resolve through the unit's authored body radius before SetUnitTarget.");
        }

        [Test]
        public void RuntimeSpawnReceiptQueue_CanDrainPendingReceiptsByChannel()
        {
            var channels = new RuntimeEntitySpawnReceiptChannelRegistry();
            int targetChannel = channels.Register("test.runtimeSpawnReceipts");
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
                ReceiptChannelId = targetChannel,
                ReceiptId = 11,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "formation_capability_showcase_formation_agent",
                MapId = new Ludots.Core.Map.MapId("formation_capability_showcase"),
            }), Is.True);
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = targetChannel,
                ReceiptId = 12,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "formation_capability_showcase_soldier_azure_light",
                MapId = new Ludots.Core.Map.MapId("formation_capability_showcase"),
            }), Is.True);

            int drained = 0;
            while (queue.TryDequeueForChannel(targetChannel, out _))
            {
                drained++;
            }

            Assert.That(drained, Is.EqualTo(2));
            Assert.That(queue.CountForChannel(targetChannel), Is.EqualTo(0));
            Assert.That(queue.Count, Is.EqualTo(1), "Draining a showcase receipt channel must not consume unrelated receipt channels.");
            Assert.That(queue.TryDequeueForChannel(otherChannel, out RuntimeEntitySpawnReceipt other), Is.True);
            Assert.That(other.TemplateId, Is.EqualTo("other_template"));
        }

        [Test]
        public void FormationCapabilityRuntime_ResetRemovesOwnPendingSpawnRequestsByMap()
        {
            var spawnQueue = new RuntimeEntitySpawnQueue();
            JsonObject configJson = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            FormationCapabilityShowcaseConfig config = FormationCapabilityShowcaseConfig.Load(configJson);
            var mapId = new MapId(config.MapId);
            Assert.That(spawnQueue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "formation_capability_showcase_formation_agent",
                MapId = mapId,
            }), Is.True);
            Assert.That(spawnQueue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_blocker",
                MapId = mapId,
            }), Is.True);
            Assert.That(spawnQueue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "unrelated_template",
                MapId = new MapId("other_map"),
            }), Is.True);

            Assert.That(
                spawnQueue.RemoveForMapAndTemplates(
                    mapId,
                    new[] { config.FormationAgent.TemplateId, config.ObstacleOverlay.TemplateId }),
                Is.EqualTo(1));

            Assert.That(spawnQueue.Count, Is.EqualTo(2));
            Assert.That(spawnQueue.TryDequeue(out RuntimeEntitySpawnRequest remaining), Is.True);
            Assert.That(remaining.TemplateId, Is.EqualTo("mass_navigation_blocker"));
            Assert.That(spawnQueue.TryDequeue(out remaining), Is.True);
            Assert.That(remaining.TemplateId, Is.EqualTo("unrelated_template"));
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
            var templateKeys = new EntityTemplateKeyRegistry();
            int exact = templateKeys.Register("mass_navigation_exact_template");

            Assert.That(templateKeys.TryGetId("mass_navigation_exact_template", out int resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(exact));
            Assert.That(templateKeys.TryGetId("Mass_Navigation_Exact_Template", out _), Is.False);
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
        public void RuntimeTemplateSpawn_UsesGenericComponentRegistryForMassNavigationTemplates()
        {
            string templateJson = """
[
  {
    "id": "mass_navigation_batch_agent",
    "components": {
      "Name": { "Value": "MassNavigation.BatchAgent" },
      "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
      "VisualHeightmapSampleState": {},
      "FacingDirection": { "AngleRad": 0.0 },
      "Team": { "Id": 7 },
      "PlayerOwner": { "PlayerId": 3 },
      "MassNavigationAgent": { "profileId": "light" },
      "EntityLayer": {
        "category": [ "massNavigation.agent" ],
        "mask": [ "massNavigation.agent" ]
      }
    }
  }
]
""";

            using TempTemplatePipeline temp = TempTemplatePipeline.Create(templateJson);
            var templates = new DataRegistry<EntityTemplate>(temp.Pipeline);
            templates.Load("Entities/templates.json", temp.Catalog);
            using var world = World.Create();
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts);

            const int receiptChannel = 101;
            for (int i = 0; i < 3; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "mass_navigation_batch_agent",
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i * 50, 200 + i * 60),
                    HasWorldPosition = 1,
                    FacingAngleRad = 0.25f * i,
                    HasFacing = 1,
                    EmitReceipt = 1,
                    ReceiptChannelId = receiptChannel,
                    ReceiptId = i + 1,
                }), Is.True);
            }

            system.Update(0f);

            Assert.That(receipts.CountForChannel(receiptChannel), Is.EqualTo(3));

            int spawned = 0;
            while (receipts.TryDequeueForChannel(receiptChannel, out RuntimeEntitySpawnReceipt receipt))
            {
                Entity entity = receipt.Entity;
                Assert.That(world.IsAlive(entity), Is.True);
                Assert.That(world.Has<MassNavigationAgent>(entity), Is.True);
                Assert.That(world.Get<Team>(entity).Id, Is.EqualTo(7));
                Assert.That(world.Get<PlayerOwner>(entity).PlayerId, Is.EqualTo(3));
                Assert.That(world.Get<Ludots.Core.Gameplay.Components.EntityLayer>(entity).Value.Category, Is.EqualTo(LayerRegistry.GetBit(MassNavigationAgentLayerName)));
                Assert.That(world.Get<EntityTemplateKeyRef>(entity).TemplateKeyId, Is.EqualTo(templateKeys.GetId("mass_navigation_batch_agent")));
                Assert.That(world.Get<WorldPositionCm>(entity).Value, Is.EqualTo(Fix64Vec2.FromInt(100 + spawned * 50, 200 + spawned * 60)));
                Assert.That(world.Get<PreviousWorldPositionCm>(entity).Value, Is.EqualTo(Fix64Vec2.FromInt(100 + spawned * 50, 200 + spawned * 60)));
                Assert.That(world.Get<FacingDirection>(entity).AngleRad, Is.EqualTo(0.25f * spawned).Within(0.001f));
                spawned++;
            }

            Assert.That(spawned, Is.EqualTo(3));
        }

        [Test]
        public void CoreComponentRegistry_RegistersMassNavigationAgentLayer()
        {
            Assert.That(Ludots.Core.Config.ComponentRegistry.TryGetComponentType("MassNavigationAgent", out _), Is.True);
            Assert.That(LayerRegistry.GetName(LayerRegistry.GetIndex(MassNavigationLayerNames.Agent)), Is.EqualTo(MassNavigationLayerNames.Agent));
            Assert.That(LayerRegistry.GetBit(MassNavigationLayerNames.Agent), Is.Not.EqualTo(0u));
        }

        [Test]
        public void MassNavigationFormationRuntime_UsesConfiguredSemanticSpacing()
        {
            MassNavigationGroupSemantics semantics = LoadBaseMassNavigationConfig().Semantics.Group;
            semantics.FormationLineSpacingCm = 240f;
            semantics.FormationSquareSpacingCm = 120f;
            semantics.FormationCircleSpacingCm = 300f;
            semantics.FormationCircleMinRadiusCm = 450f;
            semantics.FormationWedgeSpacingCm = 260f;
            semantics.FormationRotationEpsilonRadians = 0f;
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
        public void FormationCapabilityPlayable_PlayerSelectionCancelMarkersAndMoveOutlines_WorkThroughFormalRuntimeChains()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should spawn MassNavigation-authored formation and soldier agents, bind showcase-authored sidecars, and seed the authored command source.");

            Assert.That(simulation.AgentState.TotalAgents, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalAgents));
            Assert.That(simulation.AgentState.ControllableAgentCount, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalFormations));
            Assert.That(simulation.AgentState.ControllableAgentSlotCount, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalFormations));
            AssertConfiguredObstaclesAreEcsBlockers(engine, simulation);
            AssertFormationAgentsDoNotOverlap(engine, simulation);
            Assert.That(Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine).Length,
                Is.EqualTo(FormationCapabilityAcceptance.ExpectedInitialCommandSource));
            AssertInitialCommandSourceTargetsFormationAgents(engine);
            Assert.That(CountCommandMarkerPerformers(engine), Is.EqualTo(FormationCapabilityAcceptance.ExpectedInitialCommandSource),
                "Initial command markers must be created by performer rules from EntityCollectionMemberAdded events.");

            AssertFormationOutlines(engine);
            AssertObstacleOverlays(engine, simulation);
            AssertMassNavigationDoesNotOwnCullingProbe(engine);

            Entity[] initialCommandSource = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine);
            DragSelect(engine, GetInputBackend(engine), ProjectEntitiesDragRect(engine, initialCommandSource));
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource &&
                      CountCommandMarkerPerformers(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: BuildCommandSourceDiagnostics(engine));

            LeftClick(engine, GetInputBackend(engine), WorldToScreen(engine, FormationCapabilityAcceptance.EmptyGroundWorldCm));
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == 0 &&
                      Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine).Length == 0 &&
                      CountCommandMarkerPerformers(engine) == 0,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Empty ground click should clear the command source and destroy scoped marker performers.");

            DragSelect(engine, GetInputBackend(engine), ProjectEntitiesDragRect(engine, initialCommandSource));
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource &&
                      CountCommandMarkerPerformers(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: BuildCommandSourceDiagnostics(engine));

            int rejectsBeforeMove = simulation.CommandRejectsTotal;
            Vector2 moveTargetScreen = WorldToScreen(engine, FormationCapabilityAcceptance.MoveTargetWorldCm);
            AssertOutsideMinimapInteractiveRegion(engine, moveTargetScreen);
            WorldCmInt2 expectedMoveTarget = ResolveGroundWorldCm(engine, moveTargetScreen);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => simulation.LastCommandActorCount == FormationCapabilityAcceptance.ExpectedInitialCommandSource &&
                      simulation.CommandRejectsTotal == rejectsBeforeMove &&
                      CountActiveMoveOrders(engine, simulation) > 0,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Right-click command should flow through PlayerInputHandler, MassNavigationLocalCommandInputSystem, and OrderBuffer.");

            Assert.That(simulation.HasCommandFocus, Is.True);
            Assert.That(simulation.CommandFocusXCm, Is.EqualTo(expectedMoveTarget.X).Within(1f));
            Assert.That(simulation.CommandFocusYCm, Is.EqualTo(expectedMoveTarget.Y).Within(1f));
        }

        [Test]
        public void FormationCapabilityPlayable_MoveOrdersPreserveFacingAndRotateOrdersDriveSoldierSlots()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be fully spawned and selected before movement/facing verification.");

            Entity formation = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine)[0];
            Assert.That(engine.World.TryGet(formation, out MassNavigationAgentIndex formationAgentIndex), Is.True);
            Assert.That(engine.World.TryGet(formation, out FormationCapabilityShowcaseFormationAgent formationAgent), Is.True);
            float initialFacing = engine.World.Get<FacingDirection>(formation).AngleRad;
            int soldierAgentIndex = FindFirstSoldierAgentIndex(engine, formationAgent.FormationIndex);
            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(soldierAgentIndex, out float soldierTargetBeforeX, out float soldierTargetBeforeY), Is.True);
            Vector2 soldierBefore = simulation.GetAgentLocalPositionCm(soldierAgentIndex);

            Vector2 moveTargetScreen = WorldToScreen(engine, FormationCapabilityAcceptance.MoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => CountActiveMoveOrders(engine, simulation) > 0,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
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

            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInteraction);
            float facingAfterMove = engine.World.Get<FacingDirection>(formation).AngleRad;
            Assert.That(facingAfterMove, Is.EqualTo(initialFacing).Within(0.0001f),
                "Moving a formation must not implicitly rotate it toward the destination.");
            Vector2 soldierAfterMove = simulation.GetAgentLocalPositionCm(soldierAgentIndex);
            float soldierMoveDeltaX = soldierAfterMove.X - soldierBefore.X;
            float soldierMoveDeltaY = soldierAfterMove.Y - soldierBefore.Y;
            Assert.That((soldierMoveDeltaX * soldierMoveDeltaX) + (soldierMoveDeltaY * soldierMoveDeltaY), Is.GreaterThan(1f),
                "Soldier MassNavigation agents must actually move after a formation move order, not just receive stale slot targets.");
            AssertMassNavigationFlowEntityPositionSynced(engine, simulation, formationAgentIndex.Value);
            AssertMassNavigationFlowEntityPositionSynced(engine, simulation, soldierAgentIndex);

            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);
            TickUntil(
                engine,
                () => MathF.Abs(NormalizeAngleRadians(engine.World.Get<FacingDirection>(formation).AngleRad - initialFacing)) > 0.0001f,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
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
        public void FormationCapabilityPlayable_NonLocalPlayerOwnerFormationSelectionRejectsRightClickMoveOrder()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be fully spawned before non-local formation command verification.");

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
                () => CommandSourceCount(engine) == 1,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored command source should contain the non-local formation agent.");

            int rejectsBeforeMove = simulation.CommandRejectsTotal;
            Vector2 moveTargetScreen = WorldToScreen(engine, FormationCapabilityAcceptance.MoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => simulation.CommandRejectsTotal == rejectsBeforeMove + 1,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
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
            input.InjectButtonPress(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);

            Assert.That(
                NormalizeAngleRadians(engine.World.Get<FacingDirection>(enemyFormation).AngleRad - enemyFacingBeforeRotate),
                Is.EqualTo(0f).Within(0.0001f),
                "Q/E rotation must use the same local PlayerOwner command boundary as right-click move orders.");
            Assert.That(simulation.CommandRejectsTotal, Is.GreaterThanOrEqualTo(rejectsBeforeRotate + 1));
        }

        [Test]
        public void FormationCapabilityPlayable_BoxSelectionOnlySelectsLocalCommandableFormations()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be fully spawned before box acquisition ownership verification.");

            AssertFormationCommandSourceCandidateFacts(engine);
            Entity[] formations = CaptureFormationAgents(engine, FormationCapabilityAcceptance.ExpectedTotalFormations);
            DragSelect(engine, GetInputBackend(engine), ProjectEntitiesDragRect(engine, formations));
            int selectorTeamId = ResolveSelectionOwnerTeamId(engine);
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == CountFriendlyTeamFormations(engine, selectorTeamId),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Player box acquisition should include only formations accepted by the configured Friendly relationship filter.");

            Entity[] selected = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine);
            Assert.That(selected.Length, Is.EqualTo(CountFriendlyTeamFormations(engine, selectorTeamId)));
            for (int i = 0; i < selected.Length; i++)
            {
                Entity entity = selected[i];
                Assert.That(engine.World.Has<FormationCapabilityShowcaseFormationAgent>(entity), Is.True);
                Assert.That(engine.World.TryGet(entity, out Team team), Is.True);
                Assert.That(RelationshipFilterUtil.Passes(RelationshipFilter.Friendly, selectorTeamId, team.Id), Is.True);
            }
        }

        [Test]
        public void FormationCapabilityPlayable_MixedLocalAndNonLocalSelectionRejectsRotateForWholeSelection()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be fully spawned before mixed command-source rotate verification.");

            int localPlayerId = ResolveLocalPlayerOwnerId(engine);
            Entity localFormation = FindLocalPlayerOwnerFormation(engine, localPlayerId);
            Entity enemyFormation = FindNonLocalPlayerOwnerFormation(engine, localPlayerId);
            float localFacingBefore = engine.World.Get<FacingDirection>(localFormation).AngleRad;
            float enemyFacingBefore = engine.World.Get<FacingDirection>(enemyFormation).AngleRad;
            SelectFormations(engine, new[] { localFormation, enemyFormation });
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == 2,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored mixed command source should enter MassNavigation's command snapshot.");

            int rejectsBeforeRotate = simulation.CommandRejectsTotal;
            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);

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
        public void FormationCapabilityPlayable_SolverWindowRebaseDoesNotCarrySoldiersAwayFromFormation()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be ready before solver-window rebase verification.");

            Entity formation = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine)[0];
            Assert.That(engine.World.TryGet(formation, out MassNavigationAgentIndex formationAgentIndex), Is.True);
            Assert.That(engine.World.TryGet(formation, out FormationCapabilityShowcaseFormationAgent formationAgent), Is.True);
            int soldierAgentIndex = FindFirstSoldierAgentIndex(engine, formationAgent.FormationIndex);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInteraction);

            Vector2 beforeOffset = AgentWorldOffset(simulation, soldierAgentIndex, formationAgentIndex.Value);
            simulation.FocusSimulationWindow(FormationCapabilityAcceptance.SolverWindowRebaseFocusWorldCm);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInteraction);
            Vector2 afterOffset = AgentWorldOffset(simulation, soldierAgentIndex, formationAgentIndex.Value);

            Assert.That(simulation.SolverWindowMovesTotal, Is.GreaterThan(0));
            Assert.That(Vector2.DistanceSquared(afterOffset, beforeOffset),
                Is.LessThan(FormationCapabilityAcceptance.SoldierFormationOffsetRebaseToleranceSq),
                "Moving the solver window must not be interpreted as formation displacement by Formation Capability soldier carrier sync.");
            AssertMassNavigationFlowEntityPositionSynced(engine, simulation, formationAgentIndex.Value);
            AssertMassNavigationFlowEntityPositionSynced(engine, simulation, soldierAgentIndex);
        }

        [Test]
        public void FormationCapabilityPlayable_MultipleFormationMoveOrdersPreserveRelativeFormationSpacing()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be fully spawned before multi-formation order verification.");

            Entity[] formations = CaptureFormationAgents(engine, expectedCount: 3);
            float initialMinDistanceSq = MinPairDistanceSq(engine, simulation, formations, useOrderTargets: false);
            SelectFormations(engine, formations);
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == formations.Length,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored command source should contain the chosen formation agents.");

            Vector2 moveTargetScreen = WorldToScreen(engine, FormationCapabilityAcceptance.MultiFormationMoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () => CountActiveMoveOrders(engine, simulation) == formations.Length,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Multi-formation right-click should submit one shared MassNavigation order per selected formation.");

            float orderMinDistanceSq = MinPairDistanceSq(engine, simulation, formations, useOrderTargets: true);
            Assert.That(
                orderMinDistanceSq,
                Is.GreaterThanOrEqualTo(initialMinDistanceSq * FormationCapabilityAcceptance.MultiFormationSpacingRetentionRatio),
                "Multiple formation agents must translate their current relative shape to the move target instead of being repacked into a compact fallback layout.");
        }

        [Test]
        public void FormationCapabilityPlayable_ResetClearsSelectedMarkersAndDestroysTrackedAgentsThroughPresentationLifecycle()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource &&
                      CountCommandMarkerPerformers(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be fully spawned and selected before reset.");

            Entity[] previousAgents = CaptureTrackedAgents(simulation);
            Entity[] previousObstacleOverlays = CaptureObstacleOverlays(engine, simulation.NavigationObstacleCount);

            simulation.RequestSceneReset();
            TickUntil(
                engine,
                () => simulation.SceneResetCount > 0 &&
                      CommandSourceCount(engine) == 0 &&
                      Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine).Length == 0 &&
                      CountCommandMarkerPerformers(engine) == 0 &&
                      CountAliveWithMassNavigationRuntimeTags(engine, previousAgents) == 0,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Scene reset should clear the command source, remove scoped marker performers, and strip runtime tags from agents tracked before reset.");

            TickUntil(
                engine,
                () => CountAlive(engine, previousAgents) == 0 &&
                      CountAlive(engine, previousObstacleOverlays) == 0 &&
                      CountPresentationDestroyPending(engine) == 0 &&
                      CountCommandMarkerPerformers(engine) == 0,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForPresentationDestroy,
                failureMessage: "Presentation lifecycle should finalize previously tracked soldiers, obstacle overlays, and scoped markers after reset.");
        }

        [Test]
        public void CommandMarkerRules_CreateAndDestroyScopedPerformersThroughEntityCollectionEvents()
        {
            var world = World.Create();
            try
            {
                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var collections = new EntityCollectionStore(collectionKeys);
                int commandSourceKeyId = collectionKeys.Register(EntityCollectionKeys.CommandSource);
                var events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                var commands = new PerformerCommandBuffer();
                var definitions = new PerformerDefinitionRegistry();
                int markerDefId = definitions.Register("test_command_marker", new PerformerDefinition());
                int agentDefId = definitions.Register("test_agent", new PerformerDefinition
                {
                    Rules = new[]
                    {
                        new PerformerRule
                        {
                            Event = new EventFilter
                            {
                                Kind = PresentationEventKind.EntityCollectionMemberAdded,
                                KeyId = commandSourceKeyId,
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
                                Kind = PresentationEventKind.EntityCollectionMemberRemoved,
                                KeyId = commandSourceKeyId,
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
                using var collectionEvents = new EntityCollectionPresentationEventSystem(world, collections, events);
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

                ReplaceCommandSource(collections, owner, new[] { unit });
                collectionEvents.Update(0.016f);
                rules.Update(0.016f);
                Assert.That(commands.Count, Is.EqualTo(1));
                PerformerCommand create = commands.GetSpan()[0];
                Assert.That(create.CommandKind, Is.EqualTo(PerformerCommandKind.CreatePerformer));
                Assert.That(create.PerformerDefinitionId, Is.EqualTo(markerDefId));
                Assert.That(create.Source, Is.EqualTo(unit));
                Assert.That(create.ParentEntity, Is.EqualTo(rootPerformer));
                Assert.That(create.ScopeTag, Is.EqualTo(42));
                commands.Clear();

                Assert.That(collections.Remove(owner, EntityCollectionKeys.CommandSource), Is.True);
                collectionEvents.Update(0.016f);
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
        public void CoreMassNavigationRuntime_WhenCullingProbeExists_DoesNotOwnCameraCullingProbe()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                var focus = new CameraCullingFocusOverride();
                engine.SetService(CoreServiceKeys.CameraCullingFocusOverride, focus);
                Assert.That(MassNavigationIds.IsCurrentNavigationRuntimeReady(engine), Is.True);

                simulation.MassNavigationFlow.ResetAuthoredAgents(new[]
                {
                    CreateAgentSeed(simulation, worldXCm: 1000f, worldYCm: 1000f),
                });
                simulation.MassNavigationFlow.Step(
                    dt: 0f,
                    world: engine.World,
                    navGroupRuntime: simulation.NavGroupRuntime,
                    runHardResolve: false,
                    hardResolveCandidateThresholdAgents: 1);

                AssertMassNavigationDoesNotOwnCullingProbe(engine);
            }
        }

        [Test]
        public void GameEngine_WhenMassNavigationRuntimeLifecycleChanges_ReflectsRuntimeReadinessThroughServices()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                Assert.That(engine.GetService(MassNavigationKeys.SimulationRuntime), Is.SameAs(simulation));
                Assert.That(MassNavigationIds.IsCurrentNavigationRuntimeReady(engine), Is.True);

                simulation.SetWorldOperationsReady(false);
                Assert.That(MassNavigationIds.IsCurrentNavigationRuntimeReady(engine), Is.False);

                Assert.That(engine.RemoveService(MassNavigationKeys.SimulationRuntime), Is.True);
                Assert.That(engine.GetService(MassNavigationKeys.SimulationRuntime), Is.Null);
            }
        }

        [Test]
        public void GameEngine_WhenMassNavigationSystemsRegister_OrdersIngestAfterOrderBufferAndBeforeFormationTick()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                var orderTypes = new OrderTypeRegistry();
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = MassNavigationOrderKeys.Move,
                    OrderTypeId = TestMassNavigationMoveOrderTypeId,
                    Priority = 100,
                });
                var orderBufferSystem = new OrderBufferSystem(
                    engine.World,
                    new DiscreteClock(),
                    orderTypes,
                    new OrderRuleRegistry());
                engine.RegisterSystem(new MassNavigationFormationSystem(engine, simulation), SystemGroup.PostMovement);
                engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
                    new MassNavigationFormationFollowerSystem(engine, simulation),
                    SystemGroup.PostMovement);
                engine.InsertSystemBeforeRequired<MassNavigationFormationSystem>(
                    new MassNavigationPreSimulationStepSystem(),
                    SystemGroup.PostMovement);
                engine.RegisterSystem(orderBufferSystem, SystemGroup.AbilityActivation);
                engine.RegisterSystem(new MassNavigationOrderIngestionSystem(engine, simulation), SystemGroup.AbilityActivation);

                List<ISystem<float>> postMovementSystems = GetSystems(engine, SystemGroup.PostMovement);
                int formationIndex = postMovementSystems.FindIndex(system => system is MassNavigationFormationSystem);
                Assert.That(formationIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(postMovementSystems.FindIndex(system => system is MassNavigationFormationFollowerSystem), Is.LessThan(formationIndex));
                Assert.That(postMovementSystems.FindIndex(system => system is MassNavigationPreSimulationStepSystem), Is.LessThan(formationIndex));

                List<ISystem<float>> abilitySystems = GetSystems(engine, SystemGroup.AbilityActivation);
                int orderBufferIndex = abilitySystems.FindIndex(system => system is OrderBufferSystem);
                int ingestionIndex = abilitySystems.FindIndex(system => system is MassNavigationOrderIngestionSystem);
                Assert.That(orderBufferIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(ingestionIndex, Is.GreaterThan(orderBufferIndex));
            }
        }

        [Test]
        public void MassNavigationControlSystem_ResetRemovesOwnPendingSpawnRequests()
        {
            var queue = new RuntimeEntitySpawnQueue();
            var massNavigationMap = new MapId("mass_navigation");
            var otherMap = new MapId("other_map");
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_light",
                MapId = massNavigationMap,
            }), Is.True);
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "unrelated_template",
                MapId = otherMap,
                EmitReceipt = 1,
                ReceiptChannelId = 77,
            }), Is.True);

            Assert.That(queue.RemoveForMap(massNavigationMap), Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out RuntimeEntitySpawnRequest remaining), Is.True);
            Assert.That(remaining.TemplateId, Is.EqualTo("unrelated_template"));
            Assert.That(remaining.ReceiptChannelId, Is.EqualTo(77));
        }

        [Test]
        public void MassNavigationModEntry_IsDataOnlyAndDoesNotOwnRuntimeOrPanelLifecycle()
        {
            string modRoot = Path.Combine(FindRepoRoot(), "mods", "capabilities", "navigation", "MassNavigationMod");
            JsonObject manifest = ReadObject(Path.Combine(modRoot, "mod.json"));
            JsonObject dependencies = manifest["dependencies"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationMod mod.json must author dependencies.");
            string[] projectReferences = ReadProjectReferenceIncludes(Path.Combine(modRoot, "MassNavigationMod.csproj"));

            Assert.That(RequireString(manifest, "description"), Does.Contain("Data-only"));
            Assert.That(dependencies.ContainsKey("LudotsCoreMod"), Is.True);
            Assert.That(dependencies.ContainsKey("CameraProfilesMod"), Is.False);
            Assert.That(projectReferences, Has.Exactly(1).Contain("Ludots.Core.csproj"));
            Assert.That(projectReferences.Any(reference => reference.Contains("CoreInputMod", StringComparison.Ordinal)), Is.False);
            Assert.That(Directory.Exists(Path.Combine(modRoot, "UI")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(modRoot, "Systems")), Is.False);
        }

        [Test]
        public void MassNavigationBusinessShowcaseMods_DoNotDependOnMassNavigationDataMod()
        {
            string modsRoot = Path.Combine(FindRepoRoot(), "mods");
            string[] applyingMods =
            {
                Path.Combine(modsRoot, "showcases", "formation_capability", "FormationCapabilityShowcaseMod"),
                Path.Combine(modsRoot, "showcases", "road_network", "RoadNetworkShowcaseMod"),
                Path.Combine(modsRoot, "showcases", "capability_standard", "CapabilityStandardParticipantViewsMod"),
            };

            foreach (string modRoot in applyingMods)
            {
                JsonObject manifest = ReadObject(Path.Combine(modRoot, "mod.json"));
                JsonObject dependencies = manifest["dependencies"]?.AsObject()
                    ?? throw new InvalidOperationException($"{modRoot} mod.json must author dependencies.");
                Assert.That(dependencies.ContainsKey("MassNavigationMod"), Is.False,
                    $"MassNavigation-using showcase mods must not depend on the MassNavigation data mod. Mod: {modRoot}");

                string[] projectReferences = ReadProjectReferenceIncludes(Directory.EnumerateFiles(modRoot, "*.csproj").Single());
                Assert.That(projectReferences.Any(reference => reference.Contains("MassNavigationMod", StringComparison.Ordinal)), Is.False,
                    $"MassNavigation-using showcase projects must not reference the MassNavigation data mod. Mod: {modRoot}");
            }
        }

        [Test]
        public void CapabilityStandardMassNavigationLargeWorldEntry_ComposesFoundationMods()
        {
            string modRoot = Path.Combine(
                FindRepoRoot(),
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardMassNavigationLargeWorld10kMod");
            JsonObject manifest = ReadObject(Path.Combine(modRoot, "mod.json"));
            JsonObject dependencies = manifest["dependencies"]?.AsObject()
                ?? throw new InvalidOperationException($"{modRoot} mod.json must author dependencies.");
            string[] projectReferences = ReadProjectReferenceIncludes(Directory.EnumerateFiles(modRoot, "*.csproj").Single());

            Assert.That(dependencies.ContainsKey("LudotsCoreMod"), Is.True);
            Assert.That(dependencies.ContainsKey("CoreInputMod"), Is.True);
            Assert.That(dependencies.ContainsKey("MassNavigationMod"), Is.True);
            Assert.That(projectReferences.Any(reference => reference.Contains("MassNavigationMod", StringComparison.Ordinal)), Is.False,
                "The capability entry composes the MassNavigation foundation through the mod graph, not a code-level project reference.");
        }

        [Test]
        public void CapabilityStandardMassNavigationLargeWorldEntry_ConfiguresCoreMinimapOnMapFocus()
        {
            string modRoot = Path.Combine(
                FindRepoRoot(),
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardMassNavigationLargeWorld10kMod");
            string entrySource = File.ReadAllText(Path.Combine(modRoot, "CapabilityStandardMassNavigationLargeWorld10kModEntry.cs"));

            Assert.That(entrySource, Does.Contain("context.OnEvent(GameEvents.GameStart, ConfigureLargeWorldUatAsync);"));
            Assert.That(entrySource, Does.Contain("context.OnEvent(GameEvents.MapLoaded, ConfigureLargeWorldUatAsync);"));
            Assert.That(entrySource, Does.Contain("context.OnEvent(GameEvents.MapResumed, ConfigureLargeWorldUatAsync);"));
            Assert.That(entrySource, Does.Contain("engine.MergedConfig?.StartupMapId"));
            Assert.That(entrySource, Does.Contain("CoreServiceKeys.MinimapRuntime"));
            Assert.That(entrySource, Does.Contain("runtime.Visible = true;"));
            Assert.That(entrySource, Does.Contain("runtime.SetRotateWithCamera(false);"));
            Assert.That(entrySource, Does.Contain("runtime.UseRtsFullMapPreset();"));
            Assert.That(entrySource, Does.Not.Contain("\"mass_navigation\""),
                "The capability entry must use authored startupMapId instead of a code-level map-id duplicate.");
            Assert.That(entrySource, Does.Not.Contain("Environment.GetEnvironmentVariable"),
                "Capability-standard MassNavigation acceptance must not depend on env fallback toggles.");
        }

        [Test]
        public void MassNavigationAndFormationCapabilitySources_DoNotReintroduceFallbackAliasOrPrototypeNames()
        {
            JsonObject massNavigationConfig = LoadMergedFormationCapabilityMassNavigationConfigObject();
            JsonObject scenarioRuntime = massNavigationConfig["scenarioRuntime"]?.AsObject()
                ?? throw new InvalidOperationException("Merged MassNavigationConfig must author scenarioRuntime.");
            JsonObject runtimeCapacity = scenarioRuntime["runtimeCapacity"]?.AsObject()
                ?? throw new InvalidOperationException("Merged MassNavigationConfig must author scenarioRuntime.runtimeCapacity.");
            JsonObject formationConfig = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));

            Assert.That(runtimeCapacity.ContainsKey("fallback"), Is.False);
            Assert.That(formationConfig.ContainsKey("webParity"), Is.False);
            Assert.That(formationConfig.ContainsKey("alias"), Is.False);
        }

        [Test]
        public void FormationCapabilityRaylibLaunchGraph_DoesNotLoadPrototypeShowcaseMods()
        {
            string repoRoot = FindRepoRoot();
            string launchGraphPath = Path.Combine(
                repoRoot,
                "src",
                "Apps",
                "Raylib",
                "Ludots.App.Raylib",
                "raylib.formation-capability-showcase.launch.graph.json");

            JsonObject launchGraph = ReadObject(launchGraphPath);
            JsonArray orderedModIds = launchGraph["orderedModIds"]?.AsArray()
                ?? throw new InvalidOperationException("FormationCapability Raylib launch graph must author orderedModIds.");
            string[] ids = orderedModIds.Select(node => node?.GetValue<string>() ?? string.Empty).ToArray();

            Assert.That(ids, Does.Contain("MassNavigationMod"));
            Assert.That(ids, Does.Contain("FormationCapabilityShowcaseMod"));
            Assert.That(ids, Does.Not.Contain("PerformerBlacksmithShowcaseMod"));
            Assert.That(ids.Any(id => id.Contains("Blacksmith", StringComparison.Ordinal)), Is.False);
            Assert.That(ids.Any(id => id.Contains("WebParity", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void MassNavigationRuntimeBoundaries_UseExplicitAgentTermsAndCoreOwnedBinding()
        {
            var agentState = new MassNavigationAgentState();
            var world = World.Create();
            try
            {
                Entity agent = world.Create();
                agentState.RegisterAgentAtIndex(agent, agentIndex: 3, controllable: true);

                Assert.That(agentState.ControllableAgentCount, Is.EqualTo(1));
                Assert.That(agentState.ControllableAgentSlotCount, Is.EqualTo(1));
                Assert.That(agentState.TryGetControllableEntity(3, out Entity resolved), Is.True);
                Assert.That(resolved, Is.EqualTo(agent));
            }
            finally
            {
                World.Destroy(world);
            }

            var orderWorld = World.Create();
            try
            {
                var args = MassNavigationMoveOrderArgs.Encode(
                    new Vector2(1500f, 2500f),
                    MassNavigationFormationMode.Line,
                    rotationRadians: 0.5f);
                var order = new Order
                {
                    OrderId = 55,
                    OrderTypeId = 37,
                    Args = args,
                };
                MassNavigationMoveOrderArgs decoded = MassNavigationMoveOrderArgs.Decode(in order);
                Assert.That(decoded.DestinationCm, Is.EqualTo(new Vector2(1500f, 2500f)));
                Assert.That(decoded.FormationMode, Is.EqualTo(MassNavigationFormationMode.Line));
                Assert.That(decoded.RotationRadians, Is.EqualTo(0.5f));
            }
            finally
            {
                World.Destroy(orderWorld);
            }
        }

        [Test]
        public void MassNavigationGroupRuntime_ExposesOrderSlotTargetsAndDoesNotCollapseNoneFormationOrders()
        {
            AssertPublicMethod(typeof(MassNavigationGroupRuntime), nameof(MassNavigationGroupRuntime.TryGetGroupMemberOrderTarget));
            AssertPublicMethod(typeof(MassNavigationGroupRuntime), nameof(MassNavigationGroupRuntime.TryUpdateGroupMemberOrderPathAnchor));

            using MassNavigationGroupRuntimeFixture fixture = CreateGroupRuntimeFixture(
                new Vector2(1000f, 1000f),
                new Vector2(1200f, 1000f));

            int assigned = fixture.Runtime.UpsertOrderMoveCommand(
                fixture.Flow,
                fixture.AgentState,
                orderToken: 501,
                memberIndices: new[] { 0, 1 },
                teamId: 1,
                destinationWorldCm: new Vector2(4000f, 4000f),
                formationMode: MassNavigationFormationMode.None,
                rotationRadians: 0f);

            Assert.That(assigned, Is.EqualTo(2));
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(0, out float firstX, out float firstY), Is.True);
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(1, out float secondX, out float secondY), Is.True);
            Assert.That((firstX - secondX) * (firstX - secondX) + (firstY - secondY) * (firstY - secondY), Is.GreaterThan(1f));
        }

        [Test]
        public void MassNavigationFlowNeighborSearch_UsesLayerScopedBodyRadiusNotGlobalLargestAgent()
        {
            var flow = CreateTestFlowState();
            var smallLayer = new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u);
            var largeIsolatedLayer = new MassNavigationAgentLayer(categoryMask: 2u, interactionMask: 2u);
            flow.ResetAuthoredAgents(new[]
            {
                CreateAgentSeed(1, 5000f, 5000f, 20f, smallLayer),
                CreateAgentSeed(1, 5030f, 5000f, 20f, smallLayer),
                CreateAgentSeed(1, 5000f, 6500f, 900f, largeIsolatedLayer),
            });

            StepHardResolve(flow);

            float smallDx = flow.GetPositionX(0) - flow.GetPositionX(1);
            float smallDy = flow.GetPositionY(0) - flow.GetPositionY(1);
            Assert.That((smallDx * smallDx) + (smallDy * smallDy), Is.GreaterThan(30f * 30f));
            Assert.That(flow.GetPositionY(2), Is.EqualTo(6500f).Within(0.001f),
                "A large non-interacting layer must not become a global body-radius source for unrelated agents.");
        }

        private static List<string> MassNavigationDependencyPaths()
        {
            string repoRoot = FindRepoRoot();
            string modsRoot = Path.Combine(repoRoot, "mods");
            return new List<string>
            {
                Path.Combine(modsRoot, "LudotsCoreMod"),
                Path.Combine(modsRoot, "CoreInputMod"),
                Path.Combine(modsRoot, "capabilities", "navigation", "MassNavigationMod"),
            };
        }

        private static List<string> FormationCapabilityDependencyPaths()
        {
            List<string> paths = MassNavigationDependencyPaths();
            paths.Add(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "camera",
                "CameraProfilesMod"));
            paths.Add(Path.Combine(
                FindRepoRoot(),
                "mods",
                "showcases",
                "formation_capability",
                "FormationCapabilityShowcaseMod"));
            return paths;
        }

        private static AgentProfileRegistry LoadFormationCapabilityAgentProfiles()
        {
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(FormationCapabilityDependencyPaths(), Path.Combine(FindRepoRoot(), "assets"));
            return engine.GetService(CoreServiceKeys.AgentProfiles)
                ?? throw new InvalidOperationException("FormationCapability test expected AgentProfiles service.");
        }

        private static GameEngine CreatePlayableFormationCapabilityEngine()
        {
            EnsureFormationCapabilityEntryAssemblyPreloaded();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(FormationCapabilityDependencyPaths(), Path.Combine(FindRepoRoot(), "assets"));
            InstallPlayableInput(engine);

            var focusOverride = new CameraCullingFocusOverride();
            HeadlessPresentationTestHost.Install(engine, focusOverride);

            var mapping = new FormationCapabilityWorldScreenMapping(
                FormationCapabilityAcceptance.ScreenCenter,
                FormationCapabilityAcceptance.PixelsPerCm);
            engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)mapping);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)mapping);
            engine.GlobalContext[FormationCapabilityAcceptance.WorldScreenMappingKey] = mapping;

            var renderCameraDebug = new RenderCameraDebugState();
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            engine.RegisterPresentationSystem(new CullingVisualizationPresentationSystem(engine.GlobalContext));

            engine.Start();
            return engine;
        }

        private static void LoadFormationCapabilityMap(GameEngine engine)
        {
            int localPlayerId = engine.MergedConfig.StartupLocalPlayerId;
            Assert.That(localPlayerId, Is.EqualTo(1),
                "FormationCapability tests must exercise the same startup player binding path as the playable showcase.");
            engine.LoadMap(MapLoadRequest.FromMapId(
                "formation_capability_showcase",
                MapLaunchContext.Create(localPlayerId)));
        }

        private static void EnsureFormationCapabilityEntryAssemblyPreloaded()
        {
            GC.KeepAlive(typeof(FormationCapabilityShowcaseFormationAgent).Assembly);
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
            engine.GlobalContext[FormationCapabilityAcceptance.InputBackendKey] = backend;
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[FormationCapabilityAcceptance.InputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Formation Capability playable test input backend is missing.");
        }

        private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine)
        {
            return engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new InvalidOperationException("MassNavigationSimulationRuntime is missing.");
        }

        private static void Tick(GameEngine engine, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FormationCapabilityAcceptance.FrameSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
        }

        private static MassNavigationSimulationRuntime CreateFocusedMassNavigationSimulation(out GameEngine engine)
        {
            engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(FindRepoRoot(), "mods", "LudotsCoreMod") },
                Path.Combine(FindRepoRoot(), "assets"));

            MassNavigationConfig config = MassNavigationLocalCommandInputSystemTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100));
            simulation.SetWorldOperationsReady(true);
            engine.SetService(MassNavigationKeys.SimulationRuntime, simulation);
            FocusCurrentMapSession(engine, config.MapId);
            return simulation;
        }

        private static void FocusCurrentMapSession(GameEngine engine, string mapId)
        {
            var session = new MapSession(new MapId(mapId), new MapConfig { Id = mapId });
            engine.SetCurrentMapSessionForTests(session);
        }

        private static void InstallFlatVisualHeightmap(GameEngine engine)
        {
            var heightmap = new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(0, 0, 25_000, 25_000),
                    sampleColumns: 2,
                    sampleRows: 2,
                    heightSamplesCm: new short[4]));
            engine.SetService(CoreServiceKeys.VisualHeightmap, heightmap);
        }

        private static List<ISystem<float>> GetSystems(GameEngine engine, SystemGroup group)
        {
            var field = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            var systemGroups = field!.GetValue(engine) as Dictionary<SystemGroup, List<ISystem<float>>>;
            Assert.That(systemGroups, Is.Not.Null);
            Assert.That(systemGroups!.TryGetValue(group, out List<ISystem<float>>? systems), Is.True);
            return systems!;
        }

        private static void UpdateSystem(ISystem<float> system)
        {
            float dt = 0f;
            system.Update(in dt);
        }

        private static Entity CreateFormationAnchor(World world, int formationId, int slotCount)
        {
            Entity entity = world.Create(
                new MassNavigationFormationAnchor
                {
                    FormationId = formationId,
                    SlotCount = slotCount,
                },
                new MassNavigationFollowerLocomotion
                {
                    TargetChangeEpsilonCm = 1f,
                    FacingChangeEpsilonRadians = 0.00001f,
                },
                new FacingDirection { AngleRad = 0f });
            world.Add(entity, new PresentationStableId { Value = entity.Id });
            return entity;
        }

        private static Entity CreateFormationFollower(
            World world,
            int formationId,
            int slotIndex,
            float localOffsetXCm,
            float localOffsetYCm)
        {
            Entity entity = world.Create(new MassNavigationFormationFollower
            {
                FormationId = formationId,
                SlotIndex = slotIndex,
                LocalOffsetXCm = localOffsetXCm,
                LocalOffsetYCm = localOffsetYCm,
            });
            world.Add(entity, new PresentationStableId { Value = entity.Id });
            return entity;
        }

        private static MassNavigationAgentSeed CreateAgentSeed(
            MassNavigationSimulationRuntime simulation,
            float worldXCm,
            float worldYCm)
        {
            return new MassNavigationAgentSeed(
                teamId: 1,
                localPositionXCm: simulation.ToLocalXCm(worldXCm),
                localPositionYCm: simulation.ToLocalYCm(worldYCm),
                heavy: false,
                navMass: 1f,
                visualScale: 1f,
                bodyRadiusCm: 20f,
                speedCmPerSecond: 800f,
                new MassNavigationAgentLayer(categoryMask: 1u, interactionMask: 1u));
        }

        private static MassNavigationAgentSeed CreateAgentSeed(
            int teamId,
            float localXCm,
            float localYCm,
            float bodyRadiusCm,
            MassNavigationAgentLayer layer)
        {
            return new MassNavigationAgentSeed(
                teamId: teamId,
                localPositionXCm: localXCm,
                localPositionYCm: localYCm,
                heavy: bodyRadiusCm >= 200f,
                navMass: bodyRadiusCm >= 200f ? 12f : 1f,
                visualScale: 1f,
                bodyRadiusCm: bodyRadiusCm,
                speedCmPerSecond: 800f,
                layer);
        }

        private static void ResetFlowWithTwoOverlappingAgents(
            MassNavigationFlowSolverState flow,
            MassNavigationAgentLayer firstLayer,
            MassNavigationAgentLayer secondLayer)
        {
            flow.ResetAuthoredAgents(new[]
            {
                CreateAgentSeed(1, 5000f, 5000f, 80f, firstLayer),
                CreateAgentSeed(1, 5000f, 5000f, 80f, secondLayer),
            });
        }

        private static void StepHardResolve(MassNavigationFlowSolverState flow)
        {
            TeamManager.LoadConfig(new TeamConfig
            {
                DefaultRelationship = "Hostile",
                Relationships = new List<RelationshipEntry>(),
            });

            using MassNavigationGroupRuntimeFixture fixture = CreateGroupRuntimeFixture(new Vector2(1000f, 1000f));
            flow.Step(
                dt: 0f,
                world: fixture.World,
                navGroupRuntime: fixture.Runtime,
                runHardResolve: true,
                hardResolveCandidateThresholdAgents: 1);
        }

        private static void RegisterMoveOrderType(GameEngine engine)
        {
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = MassNavigationOrderKeys.Move,
                OrderTypeId = TestMassNavigationMoveOrderTypeId,
                Priority = 100,
            });
            engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
        }

        private static Entity CreateActiveMassNavigationMoveOrderEntity(
            GameEngine engine,
            int token,
            int agentIndex)
        {
            var order = new Order
            {
                OrderId = token,
                OrderTypeId = TestMassNavigationMoveOrderTypeId,
                Args = MassNavigationMoveOrderArgs.Encode(
                    new Vector2(2000f + (agentIndex * 100f), 2500f),
                    MassNavigationFormationMode.Line,
                    rotationRadians: 0f),
            };
            OrderBuffer orders = OrderBuffer.CreateEmpty();
            orders.SetActiveDirect(in order, priority: 100);

            return engine.World.Create(
                new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.Register("test.massNavigation.orderIngestion") },
                new MassNavigationAgentIndex { Value = agentIndex },
                new Team { Id = 1 },
                orders);
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

            Assert.That(predicate(), Is.True, $"{failureMessage} {BuildFormationAgentDiagnostics(engine)}");
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
                throw new InvalidOperationException($"Could not resolve screen point {screen} to Formation Capability ground.");
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
                    throw new InvalidOperationException($"Could not project Formation Capability entity {entity.Id}.");
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
                throw new InvalidOperationException("Entity set has no projectable Formation Capability entities.");
            }

            return new ScreenRect(
                minX - FormationCapabilityAcceptance.SelectionDragPaddingPixels,
                minY - FormationCapabilityAcceptance.SelectionDragPaddingPixels,
                maxX + FormationCapabilityAcceptance.SelectionDragPaddingPixels,
                maxY + FormationCapabilityAcceptance.SelectionDragPaddingPixels);
        }

        private static void DragSelect(GameEngine engine, TestInputBackend backend, in ScreenRect rect)
        {
            DragSelect(engine, backend, new Vector2(rect.MinX, rect.MinY), new Vector2(rect.MaxX, rect.MaxY));
        }

        private static void DragSelect(GameEngine engine, TestInputBackend backend, Vector2 start, Vector2 end)
        {
            backend.SetMousePosition(start);
            Tick(engine);
            backend.SetButton(FormationCapabilityAcceptance.LeftMousePath, true);
            Tick(engine);
            backend.SetMousePosition(end);
            Tick(engine);
            backend.SetButton(FormationCapabilityAcceptance.LeftMousePath, false);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);
        }

        private static void LeftClick(GameEngine engine, TestInputBackend backend, Vector2 position)
        {
            backend.SetMousePosition(position);
            Tick(engine);
            backend.SetButton(FormationCapabilityAcceptance.LeftMousePath, true);
            Tick(engine);
            backend.SetButton(FormationCapabilityAcceptance.LeftMousePath, false);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);
        }

        private static void RightClick(GameEngine engine, TestInputBackend backend, Vector2 position)
        {
            backend.SetMousePosition(position);
            Tick(engine);
            backend.SetButton(FormationCapabilityAcceptance.RightMousePath, true);
            Tick(engine);
            backend.SetButton(FormationCapabilityAcceptance.RightMousePath, false);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);
        }

        private static int CountCommandMarkerPerformers(GameEngine engine)
        {
            PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry is missing.");
            int formation = definitions.GetId("formation_capability_showcase_formation_command_marker");
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

        private static int CountAliveWithMassNavigationRuntimeTags(GameEngine engine, ReadOnlySpan<Entity> entities)
        {
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (engine.World.IsAlive(entity) &&
                    (engine.World.Has<MassNavigationAgent>(entity) ||
                     engine.World.Has<MassNavigationAgentIndex>(entity) ||
                     engine.World.Has<MassNavigationAgentProfile>(entity)))
                {
                    count++;
                }
            }

            return count;
        }

        private static int FindFirstSoldierAgentIndex(GameEngine engine, int formationIndex)
        {
            int agentIndex = -1;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationSoldier, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationSoldier soldier, ref MassNavigationAgentIndex index) =>
            {
                if (agentIndex < 0 && soldier.FormationIndex == formationIndex)
                {
                    agentIndex = index.Value;
                }
            });

            if (agentIndex < 0)
            {
                throw new InvalidOperationException($"No Formation Capability soldier was bound for formation index {formationIndex}.");
            }

            return agentIndex;
        }

        private static Entity[] CaptureFormationAgents(GameEngine engine, int expectedCount)
        {
            var formations = new List<(int FormationIndex, Entity Entity)>(expectedCount);
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent>();
            engine.World.Query(in query, (Entity entity, ref FormationCapabilityShowcaseFormationAgent formation) =>
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
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent>();
            engine.World.Query(in query, (Entity entity, ref FormationCapabilityShowcaseFormationAgent formation) =>
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
                $"Formation Capability command authorization test requires at least one non-local owner formation; formations without PlayerOwner={formationsWithoutOwner}.");
            return result;
        }

        private static Entity FindLocalPlayerOwnerFormation(GameEngine engine, int localPlayerId)
        {
            Entity result = Entity.Null;
            int formationIndex = int.MaxValue;
            int formationsWithoutOwner = 0;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent>();
            engine.World.Query(in query, (Entity entity, ref FormationCapabilityShowcaseFormationAgent formation) =>
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
                $"Formation Capability command authorization test requires at least one local owner formation; formations without PlayerOwner={formationsWithoutOwner}.");
            return result;
        }

        private static int CountFriendlyTeamFormations(GameEngine engine, int selectorTeamId)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent, Team>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationAgent _, ref Team team) =>
            {
                if (RelationshipFilterUtil.Passes(RelationshipFilter.Friendly, selectorTeamId, team.Id))
                {
                    count++;
                }
            });

            return count;
        }

        private static void AssertFormationCommandSourceCandidateFacts(GameEngine engine)
        {
            int selectorTeamId = ResolveSelectionOwnerTeamId(engine);
            int friendlyFormationCount = 0;
            int rejectedFormationCount = 0;
            int formationsWithoutTeam = 0;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent, CommandSourceSelectableState>();
            engine.World.Query(in query, (Entity entity, ref FormationCapabilityShowcaseFormationAgent _, ref CommandSourceSelectableState selectable) =>
            {
                Assert.That(engine.World.Has<CommandSourceSelectableTag>(entity), Is.True,
                    "Runtime-spawned Formation Capability formation anchors must satisfy Core command-source candidate tagging.");
                Assert.That(selectable.Enabled, Is.True,
                    "Formation Capability formation candidates stay generally selectable; Core relationship filtering gates player acquisition.");

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
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent, PlayerOwner>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationAgent _, ref PlayerOwner owner) =>
            {
                if (owner.PlayerId != localPlayerId)
                {
                    return;
                }

                localFormationCount++;
            });

            Assert.That(localFormationCount, Is.GreaterThan(0),
                "Formation Capability command authorization test requires at least one local owner formation.");
        }

        private static void SelectFormations(GameEngine engine, ReadOnlySpan<Entity> formations)
        {
            Entity owner = ResolveLocalPlayerEntity(engine);
            EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            ReplaceCommandSource(collections, owner, formations);

            Tick(engine);
        }

        private static void ReplaceCommandSource(EntityCollectionStore collections, Entity owner, ReadOnlySpan<Entity> members)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                owner,
                members.Length > 0 ? members[0] : Entity.Null,
                "Command source",
                $"{members.Length} entity(s)");
            collections.Replace(owner, descriptor, members, owner);
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

        private static void AssertMassNavigationFlowEntityPositionSynced(
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
            Assert.That(agents.Count, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalAgents));
            var snapshot = new Entity[agents.Count];
            for (int i = 0; i < agents.Count; i++)
            {
                snapshot[i] = agents[i];
                Assert.That(snapshot[i], Is.Not.EqualTo(Entity.Null), $"Formation Capability tracked agent {i} must be bound before reset.");
            }

            return snapshot;
        }

        private static Entity[] CaptureObstacleOverlays(GameEngine engine, int expectedCount)
        {
            var overlays = new List<Entity>(expectedCount);
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseObstacleOverlay>();
            engine.World.Query(in query, (Entity entity, ref FormationCapabilityShowcaseObstacleOverlay _) =>
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
            Entity[] actors = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine);
            for (int i = 0; i < actors.Length; i++)
            {
                Entity entity = actors[i];
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

        private static void AssertInitialCommandSourceTargetsFormationAgents(GameEngine engine)
        {
            Entity[] selected = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine);
            Assert.That(selected.Length, Is.EqualTo(FormationCapabilityAcceptance.ExpectedInitialCommandSource));
            for (int i = 0; i < selected.Length; i++)
            {
                Entity entity = selected[i];
                Assert.That(engine.World.Get<Name>(entity).Value, Is.EqualTo("MassNavigation.FormationCapabilityShowcase.FormationAgent"));
                Assert.That(engine.World.Has<OrderBuffer>(entity), Is.True);
                Assert.That(engine.World.Has<AttributeBuffer>(entity), Is.True);
                Assert.That(engine.World.Has<OrderBuffer>(entity), Is.True);
            }
        }

        private static void AssertFormationAgentsDoNotOverlap(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            int[] agentIndices = new int[FormationCapabilityAcceptance.ExpectedTotalFormations];
            int count = 0;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationAgent _, ref MassNavigationAgentIndex agentIndex) =>
            {
                if (count >= agentIndices.Length)
                {
                    throw new InvalidOperationException("Formation Capability playable test found more formation agents than expected.");
                }

                agentIndices[count++] = agentIndex.Value;
            });

            Assert.That(count, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalFormations), BuildFormationAgentDiagnostics(engine));
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

        private static bool IsFormationCapabilityScenarioReady(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            return simulation.AgentState.TotalAgents == FormationCapabilityAcceptance.ExpectedTotalAgents &&
                   CountFormationAgents(engine) == FormationCapabilityAcceptance.ExpectedTotalFormations &&
                   CountFormationSoldiers(engine) == FormationCapabilityAcceptance.ExpectedTotalSoldiers &&
                   CountMassNavigationFlowObstacleProjections(engine) == simulation.AgentState.BlockerCount &&
                   simulation.NavigationObstacleCount > 0 &&
                   CountObstacleOverlays(engine) == simulation.NavigationObstacleCount;
        }

        private static void AssertConfiguredObstaclesAreEcsBlockers(GameEngine engine, MassNavigationSimulationRuntime simulation)
        {
            int projectionCount = CountMassNavigationFlowObstacleProjections(engine);
            Assert.That(projectionCount, Is.GreaterThan(0),
                "Map-authored ManifestationObstacle/CompoundObstacle entities must be projected into MassNavigationFlow obstacle projections.");
            Assert.That(simulation.AgentState.BlockerCount, Is.EqualTo(projectionCount));
            Assert.That(simulation.NavigationObstacleCount, Is.GreaterThanOrEqualTo(projectionCount));
        }

        private static int CountMassNavigationFlowObstacleProjections(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<MassNavigationFlowObstacleProjection, MassNavigationBlockerProfile, WorldPositionCm>();
            engine.World.Query(in query, (ref MassNavigationFlowObstacleProjection _, ref MassNavigationBlockerProfile _, ref WorldPositionCm _) => count++);
            return count;
        }

        private static int CountFormationAgents(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationAgent _, ref MassNavigationAgentIndex _) => count++);
            return count;
        }

        private static int CountFormationSoldiers(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationSoldier, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseFormationSoldier _, ref MassNavigationAgentIndex _) => count++);
            return count;
        }

        private static int CountObstacleOverlays(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseObstacleOverlay>();
            engine.World.Query(in query, (ref FormationCapabilityShowcaseObstacleOverlay _) => count++);
            return count;
        }

        private static string BuildFormationAgentDiagnostics(GameEngine engine)
        {
            int formationOnly = 0;
            int indexOnly = 0;
            int orderable = 0;
            int anchorOnly = 0;
            int indexedAnchor = 0;
            int destroyPendingAnchor = 0;
            int formationCapabilityAnchor = 0;
            int followerOnly = 0;
            var formationQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent>();
            engine.World.Query(in formationQuery, (ref FormationCapabilityShowcaseFormationAgent _) => formationOnly++);
            var indexQuery = new QueryDescription().WithAll<MassNavigationAgentIndex>();
            engine.World.Query(in indexQuery, (ref MassNavigationAgentIndex _) => indexOnly++);
            var orderableQuery = new QueryDescription().WithAll<MassNavigationAgentIndex, OrderBuffer>();
            engine.World.Query(in orderableQuery, (ref MassNavigationAgentIndex _, ref OrderBuffer _) => orderable++);
            var anchorQuery = new QueryDescription().WithAll<MassNavigationFormationAnchor>();
            engine.World.Query(in anchorQuery, (ref MassNavigationFormationAnchor _) => anchorOnly++);
            var indexedAnchorQuery = new QueryDescription().WithAll<MassNavigationFormationAnchor, MassNavigationAgentIndex>();
            engine.World.Query(in indexedAnchorQuery, (ref MassNavigationFormationAnchor _, ref MassNavigationAgentIndex _) => indexedAnchor++);
            var destroyPendingAnchorQuery = new QueryDescription().WithAll<MassNavigationFormationAnchor, PresentationDestroyPending>();
            engine.World.Query(in destroyPendingAnchorQuery, (ref MassNavigationFormationAnchor _, ref PresentationDestroyPending _) => destroyPendingAnchor++);
            var formationCapabilityAnchorQuery = new QueryDescription().WithAll<MassNavigationFormationAnchor, FormationCapabilityShowcaseFormationAgent>();
            engine.World.Query(in formationCapabilityAnchorQuery, (ref MassNavigationFormationAnchor _, ref FormationCapabilityShowcaseFormationAgent _) => formationCapabilityAnchor++);
            var followerQuery = new QueryDescription().WithAll<MassNavigationFormationFollower>();
            engine.World.Query(in followerQuery, (ref MassNavigationFormationFollower _) => followerOnly++);
            return $"formationOnly={formationOnly} anchor={anchorOnly} indexedAnchor={indexedAnchor} destroyPendingAnchor={destroyPendingAnchor} formationCapabilityAnchor={formationCapabilityAnchor} follower={followerOnly} indexed={indexOnly} orderable={orderable} {BuildCommandSourceDiagnostics(engine)} {BuildSelectionCandidateDiagnostics(engine)}";
        }

        private static string BuildSelectionCandidateDiagnostics(GameEngine engine)
        {
            int formationSelectableTag = 0;
            int formationVisual = 0;
            int formationCull = 0;
            int formationVisible = 0;
            int formationCanAcquire = 0;
            int formationProjectable = 0;
            Entity owner = ResolveLocalPlayerEntity(engine);
            CommandSourceAcquisitionConfig acquisitionConfig = engine.GetService(CoreServiceKeys.CommandSourceAcquisitionConfig)
                ?? throw new InvalidOperationException("CommandSourceAcquisitionConfig is missing.");
            RelationshipFilter relationFilter = acquisitionConfig.TargetFilter?.ParseRelationFilter() ?? RelationshipFilter.All;
            IScreenProjector projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector is missing.");
            var query = new QueryDescription().WithAll<FormationCapabilityShowcaseFormationAgent>();
            engine.World.Query(in query, (Entity entity, ref FormationCapabilityShowcaseFormationAgent formation) =>
            {
                if (engine.World.Has<CommandSourceSelectableTag>(entity))
                {
                    formationSelectableTag++;
                }

                if (engine.World.Has<VisualTransform>(entity))
                {
                    formationVisual++;
                }

                if (engine.World.TryGet(entity, out CullState cull))
                {
                    formationCull++;
                    if (cull.IsVisible)
                    {
                        formationVisible++;
                    }
                }

                if (CommandSourceEligibility.CanAcquire(
                        engine.World,
                        engine.GlobalContext,
                        owner,
                        entity,
                        relationFilter))
                {
                    formationCanAcquire++;
                }

                if (SpatialBoundsUtility.TryProjectScreenBounds(engine.World, entity, projector, out _))
                {
                    formationProjectable++;
                }
            });

            return $"commandSourceCandidates tag={formationSelectableTag} visual={formationVisual} cull={formationCull} visible={formationVisible} canAcquire={formationCanAcquire} projectable={formationProjectable}";
        }

        private static int ResolveMassNavigationMoveOrderTypeId(GameEngine engine)
        {
            OrderTypeRegistry registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("OrderTypeRegistry is missing.");
            if (!registry.TryGetId(MassNavigationOrderKeys.Move, out int id))
            {
                throw new InvalidOperationException("massNavigationMove order type is not registered.");
            }

            return id;
        }

        private static void AssertFormationOutlines(GameEngine engine)
        {
            RoadSplineBuffer splines = engine.GetService(CoreServiceKeys.RoadSplineBuffer)
                ?? throw new InvalidOperationException("RoadSplineBuffer is missing.");
            Assert.That(splines.Count, Is.EqualTo(FormationCapabilityAcceptance.ExpectedOutlineSplineSegments));
            Assert.That(engine.GlobalContext.TryGetValue(FormationCapabilityShowcaseContextKeys.FormationOutlineCount, out object? outlineCount), Is.True);
            Assert.That(outlineCount, Is.EqualTo(FormationCapabilityAcceptance.ExpectedOutlineSplineSegments));

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
            JsonObject config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            JsonObject obstacleOverlay = config["obstacleOverlay"]?.AsObject()
                ?? throw new InvalidOperationException("Formation Capability showcase config requires obstacleOverlay.");
            float expectedBorderWidthM = WorldUnits.CmToM(obstacleOverlay["borderWidthCm"]?.GetValue<float>()
                ?? throw new InvalidOperationException("Formation Capability obstacleOverlay.borderWidthCm must be numeric."));
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

            Assert.That(
                ringCount,
                Is.GreaterThanOrEqualTo(simulation.NavigationObstacleCount),
                BuildObstacleOverlayDiagnostics(engine));
        }

        private static void AssertObstacleOverlayComponentsMatchSimulation(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            JsonObject obstacleOverlay)
        {
            float expectedBorderWidthCm = obstacleOverlay["borderWidthCm"]?.GetValue<float>()
                ?? throw new InvalidOperationException("Formation Capability obstacleOverlay.borderWidthCm must be numeric.");
            var overlaysByPosition = new Dictionary<(int X, int Y), FormationCapabilityShowcaseObstacleOverlay>(
                simulation.NavigationObstacleCount);
            var overlayQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseObstacleOverlay, WorldPositionCm>();
            engine.World.Query(in overlayQuery, (ref FormationCapabilityShowcaseObstacleOverlay overlay, ref WorldPositionCm position) =>
            {
                var key = (position.Value.X.ToInt(), position.Value.Y.ToInt());
                Assert.That(
                    overlaysByPosition.ContainsKey(key),
                    Is.False,
                    $"Formation Capability obstacle overlay position ({key.Item1}, {key.Item2}) must be unique.");
                overlaysByPosition.Add(key, overlay);
            });

            Assert.That(overlaysByPosition.Count, Is.EqualTo(simulation.NavigationObstacleCount));
            for (int i = 0; i < simulation.NavigationObstacleCount; i++)
            {
                MassNavigationObstacleSnapshot obstacle = simulation.GetObstacleWorldSnapshot(i);
                var key = ((int)MathF.Round(obstacle.WorldXCm), (int)MathF.Round(obstacle.WorldYCm));
                Assert.That(
                    overlaysByPosition.TryGetValue(key, out FormationCapabilityShowcaseObstacleOverlay overlay),
                    Is.True,
                    $"Formation Capability obstacle overlay should exist at MassNavigation obstacle position ({key.Item1}, {key.Item2}).");
                Assert.That(overlay.RadiusCm, Is.EqualTo(obstacle.RadiusCm).Within(0.001f));
                Assert.That(overlay.BorderWidthCm, Is.EqualTo(expectedBorderWidthCm).Within(0.001f));
            }
        }

        private static string BuildObstacleOverlayDiagnostics(GameEngine engine)
        {
            int overlayOnly = 0;
            int overlayVisual = 0;
            int overlayStable = 0;
            int overlayRenderable = 0;
            int obstacleTemplate = 0;
            int obstacleTemplateVisualStable = 0;
            int obstacleTemplateKeyId = 0;
            if (engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry) is EntityTemplateKeyRegistry templateKeys)
            {
                obstacleTemplateKeyId = templateKeys.GetId("formation_capability_showcase_obstacle_overlay");
            }

            var overlayQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseObstacleOverlay>();
            engine.World.Query(in overlayQuery, (ref FormationCapabilityShowcaseObstacleOverlay _) => overlayOnly++);
            var overlayVisualQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseObstacleOverlay, VisualTransform>();
            engine.World.Query(in overlayVisualQuery, (ref FormationCapabilityShowcaseObstacleOverlay _, ref VisualTransform _) => overlayVisual++);
            var overlayStableQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseObstacleOverlay, PresentationStableId>();
            engine.World.Query(in overlayStableQuery, (ref FormationCapabilityShowcaseObstacleOverlay _, ref PresentationStableId _) => overlayStable++);
            var overlayRenderableQuery = new QueryDescription().WithAll<FormationCapabilityShowcaseObstacleOverlay, VisualTransform, PresentationStableId>();
            engine.World.Query(in overlayRenderableQuery, (ref FormationCapabilityShowcaseObstacleOverlay _, ref VisualTransform _, ref PresentationStableId _) => overlayRenderable++);
            var templateQuery = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            engine.World.Query(in templateQuery, (Entity entity, ref EntityTemplateKeyRef templateKey) =>
            {
                if (templateKey.TemplateKeyId != obstacleTemplateKeyId)
                {
                    return;
                }

                obstacleTemplate++;
                if (engine.World.Has<VisualTransform>(entity) &&
                    engine.World.Has<PresentationStableId>(entity))
                {
                    obstacleTemplateVisualStable++;
                }
            });

            GroundOverlayBuffer overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer is missing.");
            return $"obstacleTemplateKey={obstacleTemplateKeyId} obstacleTemplates={obstacleTemplate} obstacleTemplateVisualStable={obstacleTemplateVisualStable} overlay={overlayOnly} overlayVisual={overlayVisual} overlayStable={overlayStable} overlayRenderable={overlayRenderable} groundOverlayCount={overlays.Count}";
        }

        private static void AssertMassNavigationDoesNotOwnCullingProbe(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.CameraCullingFocusOverride) is not CameraCullingFocusOverride focus)
            {
                return;
            }

            Assert.That(focus.Enabled, Is.False);
            Assert.That(focus.SourceId, Is.EqualTo(string.Empty));
        }

        private static string BuildCommandSourceDiagnostics(GameEngine engine)
        {
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            string view = Ludots.Tests.EntityCollectionTestAccess.TryDescribeCommandSourceView(engine, out EntityCollectionView descriptor)
                ? $"viewCount={descriptor.Count} viewRev={descriptor.Revision} viewOwner={descriptor.Owner.Id} viewContainer={descriptor.ContextEntity.Id}"
                : "view=<none>";
            return $"commandSource={CommandSourceCount(engine)} massNavCommandActors={simulation.CommandActorCount} simRev={simulation.CommandActorSnapshotRevision} {view} markers={CountCommandMarkerPerformers(engine)} agents={simulation.AgentState.TotalAgents}";
        }

        private static int CommandSourceCount(GameEngine engine)
            => Ludots.Tests.EntityCollectionTestAccess.GetCommandSourceCount(engine);

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
                    $"{label}.{field} must be owned by the navigation profile config layers.");
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

        private static void AssertPublicMethod(Type type, string methodName)
        {
            Assert.That(
                type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public),
                Is.Not.Null,
                $"{type.FullName} must expose public method {methodName}.");
        }

        private static bool ContainsJsonProperty(JsonNode? node, string propertyName)
        {
            if (node is JsonObject obj)
            {
                if (obj.ContainsKey(propertyName))
                {
                    return true;
                }

                foreach (KeyValuePair<string, JsonNode?> child in obj)
                {
                    if (ContainsJsonProperty(child.Value, propertyName))
                    {
                        return true;
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    if (ContainsJsonProperty(child, propertyName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string[] ReadProjectReferenceIncludes(string projectPath)
        {
            return XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
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

        private static MassNavigationConfig LoadBaseMassNavigationConfig()
        {
            using FileStream stream = File.OpenRead(Path.Combine(
                FindRepoRoot(),
                "mods",
                "capabilities",
                "navigation",
                "MassNavigationMod",
                "assets",
                "MassNavigationConfig.json"));
            return MassNavigationConfig.Load(stream);
        }

        private static JsonObject LoadMergedFormationCapabilityMassNavigationConfigObject()
        {
            ConfigPipeline pipeline = CreateFormationCapabilityConfigPipeline();
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                "MassNavigationConfig.json",
                ConfigMergePolicy.DeepObject);
            return pipeline.MergeDeepObjectFromCatalog(in entry, new ConfigConflictReport());
        }

        private static ConfigPipeline CreateFormationCapabilityConfigPipeline()
        {
            string repoRoot = FindRepoRoot();
            var vfs = new VirtualFileSystem();
            vfs.Mount(
                "MassNavigationMod",
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"));
            vfs.Mount("FormationCapabilityShowcaseMod", FormationCapabilityModRoot());
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("MassNavigationMod");
            modLoader.LoadedModIds.Add("FormationCapabilityShowcaseMod");
            return new ConfigPipeline(vfs, modLoader);
        }

        private static MassNavigationFlowSolverState CreateTestFlowState()
        {
            MassNavigationConfig config = LoadBaseMassNavigationConfig();
            var flow = new MassNavigationFlowSolverState(CreateTestSolverConfig());
            flow.ArrivalTuning.CopyFrom(config.Arrival);
            flow.AvoidanceTuning.CopyFrom(config.Avoidance);
            flow.Semantics.CopyFrom(config.Semantics);
            return flow;
        }

        private static MassNavigationGroupRuntimeFixture CreateGroupRuntimeFixture(params Vector2[] localPositions)
        {
            if (localPositions.Length <= 0)
            {
                throw new InvalidOperationException("MassNavigation group runtime fixture requires at least one position.");
            }

            var flow = CreateTestFlowState();
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

            flow.ResetAuthoredAgents(seeds);

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
                new MassNavigationFormationRuntime(LoadBaseMassNavigationConfig().Semantics.Group),
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
                CommandActorScratchCapacity = groupMemberCapacity,
                GroupMemberCapacity = groupMemberCapacity,
                OrderIngestionTokenCapacity = 8,
                OrderIngestionMemberCapacity = groupMemberCapacity,
                LoadedChunkCapacity = 16,
                MetadataTeamCapacity = 4,
            };
        }

        private static string FormationCapabilityModRoot()
        {
            return Path.Combine(FindRepoRoot(), "mods", "showcases", "formation_capability", "FormationCapabilityShowcaseMod");
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

        private static MassNavigationFlowSolverConfig CreateTestSolverConfig()
        {
            return new MassNavigationFlowSolverConfig
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
                MassNavigationFlowSolverState flow,
                MassNavigationAgentState agentState,
                MassNavigationGroupRuntime runtime)
            {
                World = world;
                Flow = flow;
                AgentState = agentState;
                Runtime = runtime;
            }

            public World World { get; }
            public MassNavigationFlowSolverState Flow { get; }
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
                Ludots.Core.Diagnostics.Log.GetOrCreateModChannel("FormationCapabilityShowcaseContractTests");

            public string ModId => "FormationCapabilityShowcaseContractTests";
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

        private static class FormationCapabilityAcceptance
        {
            public const string InputBackendKey = "Tests.FormationCapability.InputBackend";
            public const string WorldScreenMappingKey = "Tests.FormationCapability.WorldScreenMapping";
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
            public const int ExpectedInitialCommandSource = 1;
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

        private sealed class FormationCapabilityWorldScreenMapping : IScreenProjector, IScreenRayProvider
        {
            private readonly Vector2 _screenCenter;
            private readonly float _pixelsPerCm;

            public FormationCapabilityWorldScreenMapping(Vector2 screenCenter, float pixelsPerCm)
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
                    new Vector3(worldXCm / 100f, FormationCapabilityAcceptance.HeadlessRayOriginHeightM, worldYCm / 100f),
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
                string root = Path.Combine(Path.GetTempPath(), "ludots-formation-capability-template-" + Guid.NewGuid().ToString("N"));
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
