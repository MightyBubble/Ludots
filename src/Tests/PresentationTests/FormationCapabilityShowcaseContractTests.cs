using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Layers;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Formation;
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
        private static readonly string[] FormationCapabilityInstalledSystemTypeNames = new[]
        {
            "FormationCapabilityShowcaseScenarioBindingSystem",
            "FormationExecutionTargetSystem",
            "FormationCapabilityShowcaseStateSystem",
            "FormationCapabilityLocalOrderSourceSystem",
            "FormationCapabilityCommandSourceRotateSystem",
            "FormationOrderSystem",
            "FormationCapabilityShowcaseFormationOutlinePresentationSystem",
            "FormationCapabilityShowcaseObstacleOverlayPresentationSystem",
        };

        private const string TeamAuthoredBatchTemplateJson = """
[
  {
    "id": "team_authored_batch_agent",
    "components": {
      "Name": { "Value": "Team Authored Batch Agent" },
      "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
      "FacingDirection": { "AngleRad": 0.0 },
      "Team": { "Id": 7 }
    }
  }
]
""";

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
            JsonObject input = config["input"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author showcase input policy.");
            Assert.That(RequireString(input, "rotateLeftActionId"), Is.EqualTo("FormationCapability_RotateLeft"));
            Assert.That(RequireString(input, "rotateRightActionId"), Is.EqualTo("FormationCapability_RotateRight"));
            Assert.That(input["rotateStepRadians"]?.GetValue<float>(), Is.EqualTo(0.3926991f).Within(0.000001f),
                "The 22.5 degree rotation step is showcase input policy and must be data-authored, not a runtime constant.");
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
            JsonObject defaultCamera = mapConfig["DefaultCamera"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability map must author a visual-height-aware default camera.");
            Assert.That(RequireString(defaultCamera, "VirtualCameraId"), Is.EqualTo("MassNavigation.Camera.LargeWorldHeightmap"),
                "Formation Capability terrain is elevated, so the camera target height must come from the same visual-heightmap SSOT.");
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
            Assert.That(components.ContainsKey("EntityLocalClock"), Is.False,
                "Formation MassNavigation time is controlled by the system simulation clock, not an entity-local GAS clock.");
            AssertAttributeBufferDoesNotAuthorTimeScale(components["AttributeBuffer"]?.AsObject(), "formation agent");
            Assert.That(components.ContainsKey("SpatialBounds"), Is.False,
                "Formation footprint is derived from FormationCapabilityShowcaseConfig outline during scenario binding, not authored in the template.");
            Assert.That(components.ContainsKey("SpatialFootprint2D"), Is.False,
                "Formation footprint vertices must not drift away from the configured outline.");
            Assert.That(components.ContainsKey("MassNavigationFormationAnchor"), Is.False,
                "Formation identity is per spawned formation and must be applied by runtime component patch, not an empty template placeholder.");
            Assert.That(components.ContainsKey("MassNavigationFollowerLocomotion"), Is.False,
                "Formation follower tuning belongs to the Showcase owner, not MassNavigation Core authoring.");

            JsonArray formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("FormationCapability config must author formations.");
            Assert.That(formations.Count, Is.GreaterThan(0));
            string[] shapes = formations
                .Select(node => RequireString(node?.AsObject() ?? throw new InvalidOperationException("Formation must be an object."), "outline", "shape"))
                .ToArray();
            Assert.That(shapes, Does.Contain("Rectangle"));
            Assert.That(shapes, Does.Contain("Circle"));

            Assert.That(config.ContainsKey("soldierTargetSync"), Is.False,
                "Formation target refresh tuning is owned by the Showcase's explicit epsilon fields, not a parallel follower-sync block.");
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
                Assert.That(soldierComponents.ContainsKey("AttributeBuffer"), Is.False,
                    "Soldier MassNavigation agents must not author AttributeBuffer just to carry showcase time; showcase time is system-level.");
                Assert.That(soldierComponents.ContainsKey("EntityLocalClock"), Is.False,
                    "Soldier MassNavigation time is controlled by the system simulation clock, not an entity-local GAS clock.");

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
                "FormationCapabilityShowcaseConfig must explicitly author initial command-source capacity.");
            Assert.That(showcaseConfig["orderBatchCapacity"]?.GetValue<int>(), Is.GreaterThan(0),
                "FormationCapabilityShowcaseConfig must own the capacity of its rotate-order producer.");
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
                Assert.That(group.ContainsKey(property), Is.False, $"MassNavigationConfig must not own Formation semantic '{property}'.");
            }

            AssertPositive(showcaseConfig, "targetChangeEpsilonCm");
            AssertPositive(showcaseConfig, "facingChangeEpsilonRadians");
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
            config.Remove("input");
            InvalidOperationException missingInput = Assert.Throws<InvalidOperationException>(
                () => FormationCapabilityShowcaseConfig.Load(config))!;
            Assert.That(missingInput.Message, Does.Contain("input"));

            config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            JsonObject input = config["input"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author input.");
            input["rotateRightActionId"] = input["rotateLeftActionId"]?.GetValue<string>();
            InvalidOperationException duplicateRotateAction = Assert.Throws<InvalidOperationException>(
                () => FormationCapabilityShowcaseConfig.Load(config))!;
            Assert.That(duplicateRotateAction.Message, Does.Contain("distinct rotate action ids"));

            config = ReadObject(Path.Combine(FormationCapabilityModRoot(), "assets", "FormationCapabilityShowcaseConfig.json"));
            input = config["input"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapability config must author input.");
            input["rotateStepRadians"] = 0f;
            InvalidOperationException invalidRotateStep = Assert.Throws<InvalidOperationException>(
                () => FormationCapabilityShowcaseConfig.Load(config))!;
            Assert.That(invalidRotateStep.Message, Does.Contain("input.rotateStepRadians"));

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
        public void TeamRelationshipConfig_PreservesDataDrivenStanceKeys()
        {
            JsonObject config = LoadMergedFormationCapabilityMassNavigationConfigObject();
            JsonObject relationships = config["teamRelationships"]?.AsObject()
                ?? throw new InvalidOperationException("teamRelationships must be authored.");
            const string catalogStanceKey = "Custom.Mod.Stance";
            relationships["defaultRelationship"] = catalogStanceKey;

            MassNavigationConfig loaded = MassNavigationConfig.Load(config);
            Assert.That(loaded.TeamRelationships.DefaultRelationship, Is.EqualTo(catalogStanceKey));
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
            Assert.That(agents.Any(entity => engine.World.Has<FormationAnchorState>(entity)), Is.True);
            Assert.That(agents.Any(entity => engine.World.Has<FormationMemberState>(entity)), Is.True);
        }

        [Test]
        public void FormationCapabilityMapUnload_UnregistersFormationSystemsUntilFormationMapReloads()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            Assert.That(CountFormationCapabilitySystems(engine), Is.Zero,
                "Loading the Formation Capability mod alone must not register Formation systems before the Formation map is focused.");

            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);
            Assert.That(CountFormationCapabilitySystems(engine), Is.GreaterThan(0),
                "Focusing the Formation map should install the optional Formation systems for that authored scenario.");

            engine.UnloadMap("formation_capability_showcase");
            Assert.That(CountFormationCapabilitySystems(engine), Is.Zero,
                "After the Formation map unloads, non-Formation maps must not keep Formation systems or steady-state Formation work.");

            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);
            Assert.That(CountFormationCapabilitySystems(engine), Is.EqualTo(FormationCapabilityInstalledSystemTypeNames.Length),
                "Reloading the Formation map should install one copy of each Formation system, not duplicate stale systems.");
        }

        [Test]
        public void FormationCapabilityRuntime_OwnsSoldierSlotBindingsOutsideMassNavigationCore()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability soldiers should bind showcase-owned slot state.");

            int soldierFollowers = 0;
            var query = new QueryDescription().WithAll<
                FormationMemberState,
                MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationMemberState soldier, ref MassNavigationAgentIndex _) =>
            {
                Assert.That(soldier.FormationIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(soldier.SlotIndex, Is.GreaterThanOrEqualTo(0));
                soldierFollowers++;
            });

            Assert.That(soldierFollowers, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalSoldiers));
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
                var environmentSystem = new MassNavigationEnvironmentBindingSystem(engine);

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

                var environmentSystem = new MassNavigationEnvironmentBindingSystem(engine);
                UpdateSystem(environmentSystem);

                Assert.That(simulation.NavigationObstacleCount, Is.EqualTo(1));
                MassNavigationObstacleSnapshot obstacle = simulation.GetObstacleWorldSnapshot(0);
                Assert.That(obstacle.WorldXCm, Is.EqualTo(2222f).Within(0.001f));
                Assert.That(obstacle.WorldYCm, Is.EqualTo(3333f).Within(0.001f));
                Assert.That(obstacle.RadiusCm, Is.EqualTo(260f).Within(0.001f));
            }
        }

        [Test]
        public void FormationCapabilitySoldierBinding_UsesShowcaseOwnedSlotStateAndCoreAgentBinding()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability should bind showcase soldier state to MassNavigation agents.");

            int soldierCount = 0;
            var query = new QueryDescription().WithAll<
                FormationMemberState,
                MassNavigationAgentIndex,
                MassNavigationAgent>();
            engine.World.Query(in query, (Entity entity, ref FormationMemberState soldier) =>
            {
                Assert.That(soldier.FormationIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(soldier.SlotIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(engine.World.Has<Team>(entity), Is.False);
                Assert.That(engine.World.Has<PlayerOwner>(entity), Is.False);
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
        public void FormationCapabilityExecutionHotPath_AfterWarmupAllocatesZeroBytes()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before allocation measurement.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            for (int i = 0; i < 4; i++)
            {
                UpdateSystem(system);
            }

            long firstStart = GC.GetAllocatedBytesForCurrentThread();
            UpdateSystem(system);
            long firstBytes = GC.GetAllocatedBytesForCurrentThread() - firstStart;
            long secondStart = GC.GetAllocatedBytesForCurrentThread();
            UpdateSystem(system);
            long secondBytes = GC.GetAllocatedBytesForCurrentThread() - secondStart;

            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            ref FormationCommandState command = ref engine.World.Get<FormationCommandState>(anchor);
            command.TargetCenterXCm += 500;
            command.TargetFacingMicroRad = EncodeFormationFacing(
                NormalizeAngleRadians(DecodeFormationFacing(command.TargetFacingMicroRad) + 0.25f));
            long changedStart = GC.GetAllocatedBytesForCurrentThread();
            UpdateSystem(system);
            long changedBytes = GC.GetAllocatedBytesForCurrentThread() - changedStart;

            Assert.That(firstBytes, Is.Zero,
                $"Stable Formation target refresh must allocate 0 B after warmup; first sample was {firstBytes} B.");
            Assert.That(secondBytes, Is.Zero,
                $"Stable Formation target refresh must allocate 0 B after warmup; samples were {firstBytes} B and {secondBytes} B.");
            Assert.That(changedBytes, Is.Zero,
                $"Changed Formation target preparation and commit must allocate 0 B after warmup; sample was {changedBytes} B.");
        }

        [Test]
        public void FormationCapabilityExecution_RemovedAnchorClearsMemberNavigationTarget()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before missing-anchor target clearing validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            UpdateSystem(system);

            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity soldier = FindFirstSoldierEntity(engine, formation.FormationIndex);
            int soldierAgentIndex = engine.World.Get<MassNavigationAgentIndex>(soldier).Value;
            Assert.That(
                simulation.TryGetAgentNavigationTargetWorldCm(soldierAgentIndex, out _, out _),
                Is.True,
                "Warmup must establish the member navigation target before the anchor disappears.");

            engine.World.Destroy(anchor);
            UpdateSystem(system);

            Assert.That(
                simulation.TryGetAgentNavigationTargetWorldCm(soldierAgentIndex, out _, out _),
                Is.False,
                "A Formation member whose anchor disappeared must not keep walking toward the stale anchor target.");
        }

        [Test]
        public void FormationCapabilityExecution_InvalidSoldierAgentIndexFailsBeforeAnyAnchorStateChanges()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before atomic execution validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity soldier = FindFirstSoldierEntity(engine, formation.FormationIndex);
            FormationExecutionBatchSnapshot before = CaptureFormationExecutionBatchSnapshot(engine, simulation);
            int committedAgentIndex = engine.World.Get<MassNavigationAgentIndex>(soldier).Value;

            ref FormationCommandState command = ref engine.World.Get<FormationCommandState>(anchor);
            command.TargetCenterXCm += 500;
            command.TargetFacingMicroRad = EncodeFormationFacing(
                NormalizeAngleRadians(DecodeFormationFacing(command.TargetFacingMicroRad) + 0.25f));
            engine.World.Get<MassNavigationAgentIndex>(soldier).Value = simulation.NavigationAgentCount + 7;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(ex.Message, Does.Contain("agent index"));
            AssertFormationExecutionBatchSnapshotUnchanged(engine, simulation, before);

            engine.World.Get<MassNavigationAgentIndex>(soldier).Value = committedAgentIndex;
            Assert.DoesNotThrow(() => UpdateSystem(system));
            Assert.That(engine.World.Get<FacingDirection>(anchor).AngleRad, Is.EqualTo(DecodeFormationFacing(command.TargetFacingMicroRad)).Within(0.000001f));
            AssertFormationMemberTargetChanged(simulation, before);
        }

        [Test]
        public void FormationCapabilityExecution_StableTargetStillRejectsInvalidSoldierBindingBeforeAnyCommit()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before stable-target validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity soldier = FindFirstSoldierEntity(engine, formation.FormationIndex);
            FormationExecutionBatchSnapshot before = CaptureFormationExecutionBatchSnapshot(engine, simulation);

            engine.World.Get<MassNavigationAgentIndex>(soldier).Value = simulation.NavigationAgentCount + 7;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(ex.Message, Does.Contain("committed MassNavigation binding"));
            AssertFormationExecutionBatchSnapshotUnchanged(engine, simulation, before);
        }

        [Test]
        public void FormationCapabilityExecution_MemberLayoutChangeRetargetsStableFormationPose()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before stable-pose member retarget validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            UpdateSystem(system);
            UpdateSystem(system);

            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity soldier = FindFirstSoldierEntity(engine, formation.FormationIndex);
            int soldierAgentIndex = engine.World.Get<MassNavigationAgentIndex>(soldier).Value;
            Assert.That(
                simulation.TryGetAgentNavigationTargetWorldCm(soldierAgentIndex, out float targetBeforeX, out float targetBeforeY),
                Is.True);

            ref FormationMemberState member = ref engine.World.Get<FormationMemberState>(soldier);
            member.LocalOffsetXCm += 450;
            member.LocalOffsetYCm += 125;

            UpdateSystem(system);

            Assert.That(
                simulation.TryGetAgentNavigationTargetWorldCm(soldierAgentIndex, out float targetAfterX, out float targetAfterY),
                Is.True);
            Assert.That(
                MathF.Abs(targetAfterX - targetBeforeX) + MathF.Abs(targetAfterY - targetBeforeY),
                Is.GreaterThan(1f),
                "Changing a Formation member's slot layout must refresh that member target even when the formation center and facing are stable.");
        }

        [Test]
        public void FormationCapabilityExecution_RuntimeSwitchResendsMemberTargets()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before runtime-switch validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            UpdateSystem(system);
            UpdateSystem(system);

            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity soldier = FindFirstSoldierEntity(engine, formation.FormationIndex);
            int soldierAgentIndex = engine.World.Get<MassNavigationAgentIndex>(soldier).Value;
            Assert.That(
                simulation.TryGetAgentNavigationTargetWorldCm(soldierAgentIndex, out _, out _),
                Is.True,
                "Warmup must establish the original runtime member target before the replacement runtime is installed.");

            Entity[] agents = CaptureTrackedAgents(simulation);
            var replacement = new MassNavigationSimulationRuntime(simulation.Config);
            replacement.BindBoardWorld(
                new WorldSizeSpec(simulation.WorldBounds, 100),
                new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(replacement.WorldConfig.StreamingChunkSizeCm));
            var seeds = new MassNavigationAgentSeed[agents.Length];
            var controllable = new bool[agents.Length];
            for (int i = 0; i < agents.Length; i++)
            {
                Vector2 position = simulation.GetAgentWorldPositionCm(i);
                seeds[i] = CreateAgentSeed(replacement, position.X, position.Y);
                controllable[i] = true;
                if (engine.World.Has<MassNavigationAgentIndex>(agents[i]))
                {
                    engine.World.Remove<MassNavigationAgentIndex>(agents[i]);
                }

                if (engine.World.Has<MassNavigationAgentProfile>(agents[i]))
                {
                    engine.World.Remove<MassNavigationAgentProfile>(agents[i]);
                }
            }

            replacement.RebuildFromAuthoredAgents(engine.World, agents, seeds, controllable);
            MassNavigationRuntimeBinding binding = engine.GetService(MassNavigationKeys.RuntimeBinding)
                ?? throw new InvalidOperationException("Formation Capability runtime binding is missing.");
            MapId mapId = engine.CurrentMapSession?.MapId
                ?? throw new InvalidOperationException("Formation Capability current map session is missing.");
            binding.Clear(mapId, simulation);
            binding.Activate(mapId, replacement);
            binding.MarkPrepared(mapId, replacement);

            Assert.That(
                replacement.TryGetAgentNavigationTargetWorldCm(soldierAgentIndex, out _, out _),
                Is.False,
                "Replacement runtime starts without the previous runtime's member target cache.");

            UpdateSystem(system);

            Assert.That(
                replacement.TryGetAgentNavigationTargetWorldCm(soldierAgentIndex, out _, out _),
                Is.True,
                "Switching MassNavigation runtime must invalidate Formation target snapshots and resend member targets.");
        }

        [Test]
        public void FormationCapabilityExecution_SoldierAgentIndexAtCapacityFailsBeforeAnyCommit()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before capacity validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity soldier = FindFirstSoldierEntity(engine, formation.FormationIndex);
            FormationExecutionBatchSnapshot before = CaptureFormationExecutionBatchSnapshot(engine, simulation);

            ref FormationCommandState command = ref engine.World.Get<FormationCommandState>(anchor);
            command.TargetCenterYCm += 500;
            engine.World.Get<MassNavigationAgentIndex>(soldier).Value =
                simulation.Config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(ex.Message, Does.Contain("exceeding configured capacity"));
            AssertFormationExecutionBatchSnapshotUnchanged(engine, simulation, before);
        }

        [Test]
        public void FormationCapabilityExecution_DuplicateSoldierSlotFailsBeforeAnyCommit()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before duplicate-slot validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity[] soldiers = FindSoldierEntities(engine, formation.FormationIndex, expectedCount: 2);
            FormationExecutionBatchSnapshot before = CaptureFormationExecutionBatchSnapshot(engine, simulation);

            engine.World.Get<FormationMemberState>(soldiers[1]) =
                engine.World.Get<FormationMemberState>(soldiers[0]);
            ref FormationCommandState command = ref engine.World.Get<FormationCommandState>(anchor);
            command.TargetCenterXCm += 500;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(ex.Message, Does.Contain("bound more than once"));
            AssertFormationExecutionBatchSnapshotUnchanged(engine, simulation, before);
        }

        [Test]
        public void FormationCapabilityExecution_SuspendedSoldierFailsWholeBatchBeforeAnchorCommit()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before cross-map execution validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationAnchorState formation = engine.World.Get<FormationAnchorState>(anchor);
            Entity soldier = FindFirstSoldierEntity(engine, formation.FormationIndex);
            FormationExecutionBatchSnapshot before = CaptureFormationExecutionBatchSnapshot(engine, simulation);

            engine.World.Add(soldier, new SuspendedTag());
            ref FormationCommandState command = ref engine.World.Get<FormationCommandState>(anchor);
            command.TargetCenterYCm += 500;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(ex.Message, Does.Contain("suspended"));
            AssertFormationExecutionBatchSnapshotUnchanged(engine, simulation, before);
        }

        [Test]
        public void FormationCapabilityExecution_SuspendedAnchorFailsExplicitlyWithoutMemberCommit()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must finish binding before invalid-anchor validation.");

            FormationExecutionTargetSystem system = GetSystems(engine, SystemGroup.PostMovement)
                .OfType<FormationExecutionTargetSystem>()
                .Single();
            Entity anchor = CaptureFormationAgents(engine, expectedCount: 1)[0];
            FormationExecutionBatchSnapshot before = CaptureFormationExecutionBatchSnapshot(engine, simulation);
            engine.World.Add(anchor, new SuspendedTag());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(ex.Message, Does.Contain("anchor"));
            AssertFormationExecutionBatchSnapshotUnchanged(engine, simulation, before);
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
            var formationQuery = new QueryDescription().WithAll<FormationAnchorState, MassNavigationAgentIndex, OrderBuffer>();
            engine.World.Query(in formationQuery, (ref FormationAnchorState _, ref MassNavigationAgentIndex index) =>
            {
                Assert.That(simulation.AgentState.TryGetControllableEntity(index.Value, out Entity controllable), Is.True);
                Assert.That(controllable, Is.Not.EqualTo(Entity.Null));
                controllableOrderBuffers++;
            });

            int soldierOrderBuffers = 0;
            var soldierQuery = new QueryDescription().WithAll<FormationMemberState, MassNavigationAgentIndex>();
            engine.World.Query(in soldierQuery, (Entity entity, ref FormationMemberState _, ref MassNavigationAgentIndex _) =>
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

                var system = new MassNavigationOrderIngestionSystem(engine, simulation.Config);
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
                Assert.That(ex.Message, Does.Contain("orderIngestionTokenCapacity"));
            }
        }

        [Test]
        public void MassNavigationOrderIngestion_IgnoresSuspendedMapAgents()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                RegisterMoveOrderType(engine);
                Entity suspended = CreateActiveMassNavigationMoveOrderEntity(engine, token: 101, agentIndex: 999);
                engine.World.Add(suspended, new SuspendedTag());

                var system = new MassNavigationOrderIngestionSystem(engine, simulation.Config);
                Assert.DoesNotThrow(() => UpdateSystem(system));
                Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.Zero);
            }
        }

        [Test]
        public void MassNavigationOrderIngestion_IncomingRevisionWakesIdleScanAndSameTokenRetargetReapplies()
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
                    CanInterruptSelf = true,
                });
                var queue = new OrderQueue(capacity: 64);
                var orderBufferSystem = new OrderBufferSystem(
                    engine.World,
                    new DiscreteClock(),
                    orderTypes,
                    new OrderRuleRegistry(),
                    queue);
                engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
                engine.SetService(CoreServiceKeys.OrderBufferSystem, orderBufferSystem);

                int profileId = MassNavigationProfileRegistry.Register("test.massNavigation.orderIngestion.revision");
                Entity first = engine.World.Create(
                    new MassNavigationAgent { ProfileId = profileId },
                    new FacingDirection { AngleRad = 0f },
                    OrderBuffer.CreateEmpty());
                Entity second = engine.World.Create(
                    new MassNavigationAgent { ProfileId = profileId },
                    new FacingDirection { AngleRad = 0f },
                    OrderBuffer.CreateEmpty());
                simulation.RebuildFromAuthoredAgents(
                    engine.World,
                    new[] { first, second },
                    new[]
                    {
                        CreateAgentSeed(simulation, worldXCm: 1000f, worldYCm: 1000f),
                        CreateAgentSeed(simulation, worldXCm: 1200f, worldYCm: 1000f),
                    },
                    new[] { true, true });

                var ingestion = new MassNavigationOrderIngestionSystem(engine, simulation.Config);
                UpdateSystem(ingestion);
                Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.Zero);

                Order[] initial = CreateMoveOrderBatch(first, second, token: 77, destination: new Vector2(3000f, 3000f));
                Assert.That(queue.TryEnqueueBatch(initial), Is.True);
                orderBufferSystem.Update(0f);
                Assert.That(orderBufferSystem.IncomingRevision, Is.EqualTo(2u));

                UpdateSystem(ingestion);
                Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.EqualTo(1));
                Assert.That(simulation.CommandCountFrame, Is.EqualTo(1));
                Assert.That(simulation.LastOrderMemberCount, Is.EqualTo(2));
                Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float beforeX, out float beforeY), Is.True);

                Order[] retargeted = CreateMoveOrderBatch(first, second, token: 77, destination: new Vector2(3600f, 3000f));
                Assert.That(queue.TryEnqueueBatch(retargeted), Is.True);
                orderBufferSystem.Update(0f);
                UpdateSystem(ingestion);

                Assert.That(simulation.CommandCountFrame, Is.EqualTo(2));
                Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out float afterX, out float afterY), Is.True);
                Assert.That(((afterX - beforeX) * (afterX - beforeX)) + ((afterY - beforeY) * (afterY - beforeY)),
                    Is.GreaterThan(1f));

                engine.World.Get<OrderBuffer>(first).ClearActive();
                engine.World.Get<OrderBuffer>(second).ClearActive();
                UpdateSystem(ingestion);
                Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.Zero);

                Order[] reusedToken = CreateMoveOrderBatch(first, second, token: 77, destination: new Vector2(3600f, 3000f));
                Assert.That(queue.TryEnqueueBatch(reusedToken), Is.True);
                orderBufferSystem.Update(0f);
                UpdateSystem(ingestion);
                Assert.That(simulation.CommandCountFrame, Is.EqualTo(3),
                    "A token reused after becoming inactive must not inherit its pruned application signature.");
            }
        }

        [Test]
        public void MassNavigationOrderCommand_MemberChangeUsesExactComparisonForFormerHashCollision()
        {
            const int agentCount = 397;
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(FindRepoRoot(), "mods", "LudotsCoreMod") },
                Path.Combine(FindRepoRoot(), "assets"));
            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            config.ScenarioRuntime.RuntimeCapacity.GroupMembershipAgentCapacity = 512;
            config.ScenarioRuntime.RuntimeCapacity.GroupMemberCapacity = 512;
            config.ScenarioRuntime.RuntimeCapacity.OrderIngestionMemberCapacity = 512;
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
                new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(simulation.WorldConfig.StreamingChunkSizeCm));
            FocusCurrentMapSession(engine, config.MapId);
            var binding = new MassNavigationRuntimeBinding();
            MapId mapId = engine.CurrentMapSession!.MapId;
            binding.Activate(mapId, simulation);
            binding.MarkPrepared(mapId, simulation);
            engine.SetService(MassNavigationKeys.RuntimeBinding, binding);

            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                Key = MassNavigationOrderKeys.Move,
                OrderTypeId = TestMassNavigationMoveOrderTypeId,
                Priority = 100,
                CanInterruptSelf = true,
            });
            var orderBufferSystem = new OrderBufferSystem(
                engine.World,
                new DiscreteClock(),
                orderTypes,
                new OrderRuleRegistry());
            engine.SetService(CoreServiceKeys.OrderTypeRegistry, orderTypes);
            engine.SetService(CoreServiceKeys.OrderBufferSystem, orderBufferSystem);

            var entities = new Entity[agentCount];
            var seeds = new MassNavigationAgentSeed[agentCount];
            var controllable = new bool[agentCount];
            int profileId = MassNavigationProfileRegistry.Register("test.massNavigation.orderMemberCollision");
            for (int i = 0; i < agentCount; i++)
            {
                entities[i] = engine.World.Create(
                    new MassNavigationAgent { ProfileId = profileId },
                    new FacingDirection { AngleRad = 0f },
                    OrderBuffer.CreateEmpty());
                seeds[i] = CreateAgentSeed(
                    simulation,
                    worldXCm: 1000f + ((i % 20) * 100f),
                    worldYCm: 1000f + ((i / 20) * 100f));
                controllable[i] = true;
            }

            simulation.RebuildFromAuthoredAgents(engine.World, entities, seeds, controllable);
            Vector2 destination = new(4000f, 4000f);
            Order CreateOrder(Entity actor) => new()
            {
                OrderId = 77,
                OrderTypeId = TestMassNavigationMoveOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = MassNavigationMoveOrderArgs.Encode(destination),
            };

            Order firstOrder = CreateOrder(entities[0]);
            Order secondOrder = CreateOrder(entities[1]);
            engine.World.Get<OrderBuffer>(entities[0]).SetActiveDirect(in firstOrder, priority: 100);
            engine.World.Get<OrderBuffer>(entities[1]).SetActiveDirect(in secondOrder, priority: 100);
            var ingestion = new MassNavigationOrderIngestionSystem(engine, simulation.Config);

            UpdateSystem(ingestion);
            Assert.That(simulation.CommandCountFrame, Is.EqualTo(1));

            engine.World.Get<OrderBuffer>(entities[0]).ClearActive();
            Order replacementOrder = CreateOrder(entities[396]);
            engine.World.Get<OrderBuffer>(entities[396]).SetActiveDirect(in replacementOrder, priority: 100);
            UpdateSystem(ingestion);

            Assert.That(simulation.CommandCountFrame, Is.EqualTo(2),
                "[0,1] and [1,396] shared the retired integer hash but are different command memberships.");
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(0, out _, out _), Is.False);
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(396, out _, out _), Is.True);
        }

        [Test]
        public void MassNavigationMetadataSync_UsesScenarioTeamOrderAsSsot()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                int[] configuredOrder = { 7, 3, 11 };
                simulation.ConfigureScenarioTeams(configuredOrder);

                Assert.That(simulation.TeamIds.ToArray(), Is.EqualTo(configuredOrder));
                simulation.ConfigureScenarioTeams(configuredOrder);
                Assert.That(simulation.TeamIds.ToArray(), Is.EqualTo(configuredOrder));
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
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.HasActiveCamera, Is.False,
                "Unloading the focused map must release its heightmap-dependent camera before the next tick.");
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
            Assert.That(engine.GetService(MassNavigationKeys.RuntimeBinding), Is.Null);
        }

        [Test]
        public void MassNavigationAgentState_DestroyTrackedUsesPresentationLifecycleOnly()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState(agentCapacity: 8);
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
            var state = new MassNavigationAgentState(agentCapacity: 8);
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
            var state = new MassNavigationAgentState(agentCapacity: 8);
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
            var state = new MassNavigationAgentState(agentCapacity: 8);
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
        public void MassNavigationGroupMemberRemoval_PreservesExplicitTargetsForUnaffectedMembers()
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
                destinationWorldCm: new Vector2(3000f, 3000f));
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(2, out float beforeX, out float beforeY), Is.True);
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(1, out float movedMemberBeforeX, out float movedMemberBeforeY), Is.True);

            fixture.Runtime.UpsertOrderMoveCommand(
                fixture.Flow,
                fixture.AgentState,
                orderToken: 22,
                memberIndices: new[] { 1, 3 },
                teamId: 1,
                destinationWorldCm: new Vector2(6000f, 3000f));
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(2, out float afterX, out float afterY), Is.True);

            Assert.That((afterX - beforeX) * (afterX - beforeX) + (afterY - beforeY) * (afterY - beforeY),
                Is.LessThanOrEqualTo(1f),
                "Removing another member must not regenerate or reshape an unaffected member's explicit target.");
            Assert.That(fixture.Runtime.TryGetGroupMemberOrderTarget(1, out float movedMemberAfterX, out float movedMemberAfterY), Is.True);
            Assert.That((movedMemberAfterX - movedMemberBeforeX) * (movedMemberAfterX - movedMemberBeforeX) +
                        (movedMemberAfterY - movedMemberBeforeY) * (movedMemberAfterY - movedMemberBeforeY),
                Is.GreaterThan(1f),
                "A member moved into another order group must receive that group's explicit destination.");
            Assert.That(fixture.Runtime.TryGetOrderGroup(22, out _), Is.True);
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
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            Entity teamSevenRepresentative = world.Create(new TeamIdentity { TeamId = 7 });
            var teamLookup = new TeamEntityLookup();
            teamLookup.Register(7, teamSevenRepresentative);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts,
                teamLookup: teamLookup,
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

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
                Assert.That(relationships.HasLink(entity, teamSevenRepresentative, memberOfTypeId), Is.True);
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
        public void RuntimeTemplateBatchSpawn_AppliesExplicitMembershipRelationshipsForEveryEntity()
        {
            string templateJson = """
[
  {
    "id": "relationship_batch_agent",
    "components": {
      "Name": { "Value": "Relationship Batch Agent" },
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
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            Entity membershipTarget = world.Create(new TeamIdentity { TeamId = 7 });
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                new EntityTemplateKeyRegistry(),
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "relationship_batch_agent",
                    MapId = new MapId("relationship_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                    MembershipTarget = membershipTarget,
                    HasMembershipTarget = 1,
                }), Is.True);
            }

            system.Update(0f);

            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            world.Query(in query, (Entity entity, ref EntityTemplateKeyRef _) =>
            {
                Assert.That(relationships.HasLink(entity, membershipTarget, memberOfTypeId), Is.True);
                spawned++;
            });
            Assert.That(spawned, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeTemplateBatchSpawn_InvalidExplicitMembershipDoesNotPublishSuccessOrCreateEntities()
        {
            string templateJson = """
[
  {
    "id": "relationship_batch_agent",
    "components": {
      "Name": { "Value": "Relationship Batch Agent" },
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
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var presentationEvents = new PresentationEventStream(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts,
                presentationEvents: presentationEvents,
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            const int receiptChannel = 202;
            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "relationship_batch_agent",
                    MapId = new MapId("relationship_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                    MembershipTarget = Entity.Null,
                    HasMembershipTarget = 1,
                    EmitReceipt = 1,
                    ReceiptChannelId = receiptChannel,
                    ReceiptId = i + 1,
                }), Is.True);
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("MembershipTarget"));
            Assert.That(receipts.CountForChannel(receiptChannel), Is.Zero);
            Assert.That(presentationEvents.Count, Is.Zero);

            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            world.Query(in query, (ref EntityTemplateKeyRef _) => spawned++);
            Assert.That(spawned, Is.Zero, "Invalid explicit relationship prerequisites must fail before batch entities are created.");
        }

        [TestCase(RuntimeEntitySpawnKind.Template)]
        [TestCase(RuntimeEntitySpawnKind.UnitType)]
        [TestCase(RuntimeEntitySpawnKind.Assembly)]
        public void RuntimeSingleSpawn_InvalidExplicitMembershipDoesNotPublishSuccessOrCreateEntities(RuntimeEntitySpawnKind kind)
        {
            string templateJson = """
[
  {
    "id": "relationship_single_agent",
    "components": {
      "Name": { "Value": "Relationship Single Agent" },
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
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var presentationEvents = new PresentationEventStream(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts,
                presentationEvents: presentationEvents,
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            const int receiptChannel = 205;
            int unitTypeId = UnitTypeRegistry.Register("RuntimeSingleSpawnInvalidMembershipUnit");
            Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = kind,
                TemplateId = kind == RuntimeEntitySpawnKind.Template ? "relationship_single_agent" : string.Empty,
                UnitTypeId = kind == RuntimeEntitySpawnKind.UnitType ? unitTypeId : 0,
                MapId = new MapId("relationship_single_map"),
                WorldPositionCm = Fix64Vec2.FromInt(100, 200),
                HasWorldPosition = 1,
                MembershipTarget = Entity.Null,
                HasMembershipTarget = 1,
                EmitReceipt = 1,
                ReceiptChannelId = receiptChannel,
                ReceiptId = 1,
            }), Is.True);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("MembershipTarget"));
            Assert.That(receipts.CountForChannel(receiptChannel), Is.Zero);
            Assert.That(presentationEvents.Count, Is.Zero);
            Assert.That(world.Size, Is.Zero, "Single runtime spawn paths must fail relationship preflight before creating an entity.");
        }

        [Test]
        public void RuntimeTemplateBatchSpawn_CrossWorldExplicitMembershipFailsBeforeCreation()
        {
            string templateJson = """
[
  {
    "id": "relationship_batch_agent",
    "components": {
      "Name": { "Value": "Relationship Batch Agent" },
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
            using var otherWorld = World.Create();
            _ = world.Create(new TeamIdentity { TeamId = 7 });
            Entity externalMembershipTarget = otherWorld.Create(new TeamIdentity { TeamId = 7 });
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var presentationEvents = new PresentationEventStream(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts,
                presentationEvents: presentationEvents,
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            const int receiptChannel = 204;
            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "relationship_batch_agent",
                    MapId = new MapId("relationship_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                    MembershipTarget = externalMembershipTarget,
                    HasMembershipTarget = 1,
                    EmitReceipt = 1,
                    ReceiptChannelId = receiptChannel,
                    ReceiptId = i + 1,
                }), Is.True);
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("MembershipTarget"));
            Assert.That(receipts.CountForChannel(receiptChannel), Is.Zero);
            Assert.That(presentationEvents.Count, Is.Zero);

            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            world.Query(in query, (ref EntityTemplateKeyRef _) => spawned++);
            Assert.That(spawned, Is.Zero, "Cross-world explicit relationship targets must fail before batch entities are created.");
        }

        [Test]
        public void RuntimeTemplateBatchSpawn_UnregisteredMemberOfTypeFailsBeforeOwnershipOrEntityCreation()
        {
            string templateJson = """
[
  {
    "id": "relationship_batch_agent",
    "components": {
      "Name": { "Value": "Relationship Batch Agent" },
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
            var relationshipTypes = new RelationshipTypeRegistry();
            int ownsTypeId = relationshipTypes.Register("Owns");
            const int unregisteredMemberOfTypeId = 1;
            var relationships = new RelationshipRuntime(
                world,
                relationshipTypes,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
            var ownership = new OwnershipResolver(relationships, ownsTypeId);
            Entity owner = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity membershipTarget = world.Create(new TeamIdentity { TeamId = 7 });
            int worldSizeBefore = world.Size;
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var presentationEvents = new PresentationEventStream(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts,
                presentationEvents: presentationEvents,
                ownership: ownership,
                relationships: relationships,
                memberOfTypeId: unregisteredMemberOfTypeId);

            const int receiptChannel = 207;
            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "relationship_batch_agent",
                    MapId = new MapId("relationship_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                    OwnershipSource = owner,
                    HasOwnershipSource = 1,
                    MembershipTarget = membershipTarget,
                    HasMembershipTarget = 1,
                    EmitReceipt = 1,
                    ReceiptChannelId = receiptChannel,
                    ReceiptId = i + 1,
                }), Is.True);
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("MemberOf"));
            Assert.That(receipts.CountForChannel(receiptChannel), Is.Zero);
            Assert.That(presentationEvents.Count, Is.Zero);
            Assert.That(world.Size, Is.EqualTo(worldSizeBefore), "Unregistered relationship type ids must fail before batch entities or ownership edges are created.");

            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            world.Query(in query, (Entity entity, ref EntityTemplateKeyRef _) =>
            {
                Assert.That(relationships.HasLink(owner, entity, ownsTypeId), Is.False);
                spawned++;
            });
            Assert.That(spawned, Is.Zero);
        }

        [Test]
        public void RuntimeTemplateBatchSpawn_TemplateTeamAndExplicitMembershipTargetMustNotConflict()
        {
            using TempTemplatePipeline temp = TempTemplatePipeline.Create(TeamAuthoredBatchTemplateJson);
            var templates = new DataRegistry<EntityTemplate>(temp.Pipeline);
            templates.Load("Entities/templates.json", temp.Catalog);
            using var world = World.Create();
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            Entity teamSevenRepresentative = world.Create(new TeamIdentity { TeamId = 7 });
            Entity teamEightRepresentative = world.Create(new TeamIdentity { TeamId = 8 });
            var teamLookup = new TeamEntityLookup();
            teamLookup.Register(7, teamSevenRepresentative);
            teamLookup.Register(8, teamEightRepresentative);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts,
                teamLookup: teamLookup,
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            const int receiptChannel = 203;
            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "team_authored_batch_agent",
                    MapId = new MapId("team_authored_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                    MembershipTarget = teamEightRepresentative,
                    HasMembershipTarget = 1,
                    EmitReceipt = 1,
                    ReceiptChannelId = receiptChannel,
                    ReceiptId = i + 1,
                }), Is.True);
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("conflicts"));
            Assert.That(receipts.CountForChannel(receiptChannel), Is.Zero);

            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            world.Query(in query, (ref EntityTemplateKeyRef _) => spawned++);
            Assert.That(spawned, Is.Zero, "Conflicting relationship authoring must fail before batch entities are created.");
        }

        [Test]
        public void RuntimeTemplateBatchSpawn_TemplateTeamAndRequestTeamOverrideMustNotConflict()
        {
            using TempTemplatePipeline temp = TempTemplatePipeline.Create(TeamAuthoredBatchTemplateJson);
            var templates = new DataRegistry<EntityTemplate>(temp.Pipeline);
            templates.Load("Entities/templates.json", temp.Catalog);
            using var world = World.Create();
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            Entity teamSevenRepresentative = world.Create(new TeamIdentity { TeamId = 7 });
            Entity teamEightRepresentative = world.Create(new TeamIdentity { TeamId = 8 });
            var teamLookup = new TeamEntityLookup();
            teamLookup.Register(7, teamSevenRepresentative);
            teamLookup.Register(8, teamEightRepresentative);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var receipts = new RuntimeEntitySpawnReceiptQueue(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                receipts: receipts,
                teamLookup: teamLookup,
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            const int receiptChannel = 206;
            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "team_authored_batch_agent",
                    MapId = new MapId("team_authored_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                    TeamIdOverride = 8,
                    EmitReceipt = 1,
                    ReceiptChannelId = receiptChannel,
                    ReceiptId = i + 1,
                }), Is.True);
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("conflicts"));
            Assert.That(receipts.CountForChannel(receiptChannel), Is.Zero);

            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            world.Query(in query, (ref EntityTemplateKeyRef _) => spawned++);
            Assert.That(spawned, Is.Zero, "Conflicting Team sources must fail before batch entities are created.");
        }

        [Test]
        public void RuntimeTemplateSpawn_BatchTemplateAuthoredTeamLinksMembershipForEveryEntity()
        {
            using TempTemplatePipeline temp = TempTemplatePipeline.Create(TeamAuthoredBatchTemplateJson);
            var templates = new DataRegistry<EntityTemplate>(temp.Pipeline);
            templates.Load("Entities/templates.json", temp.Catalog);
            using var world = World.Create();
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            Entity teamRepresentative = world.Create(new TeamIdentity { TeamId = 7 });
            var teamLookup = new TeamEntityLookup();
            teamLookup.Register(7, teamRepresentative);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                teamLookup: teamLookup,
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "team_authored_batch_agent",
                    MapId = new MapId("team_authored_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                }), Is.True);
            }

            system.Update(0f);

            int templateKeyId = templateKeys.GetId("team_authored_batch_agent");
            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef, Team>();
            world.Query(in query, (Entity entity, ref EntityTemplateKeyRef templateKey, ref Team team) =>
            {
                if (templateKey.TemplateKeyId != templateKeyId)
                {
                    return;
                }

                Assert.That(team.Id, Is.EqualTo(7));
                Assert.That(relationships.HasLink(entity, teamRepresentative, memberOfTypeId), Is.True);
                spawned++;
            });

            Assert.That(spawned, Is.EqualTo(2));
        }

        [Test]
        public void RuntimeTemplateSpawn_BatchTemplateAuthoredTeamMissingRepresentativeFailsBeforeCreation()
        {
            using TempTemplatePipeline temp = TempTemplatePipeline.Create(TeamAuthoredBatchTemplateJson);
            var templates = new DataRegistry<EntityTemplate>(temp.Pipeline);
            templates.Load("Entities/templates.json", temp.Catalog);
            using var world = World.Create();
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out int memberOfTypeId);
            var requests = new RuntimeEntitySpawnQueue(capacity: 8);
            var templateKeys = new EntityTemplateKeyRegistry();
            var system = new RuntimeEntitySpawnSystem(
                world,
                requests,
                templates,
                templateKeys,
                new Ludots.Core.Presentation.PresentationStableIdAllocator(),
                teamLookup: new TeamEntityLookup(),
                relationships: relationships,
                memberOfTypeId: memberOfTypeId);

            for (int i = 0; i < 2; i++)
            {
                Assert.That(requests.TryEnqueue(new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = "team_authored_batch_agent",
                    MapId = new MapId("team_authored_batch_map"),
                    WorldPositionCm = Fix64Vec2.FromInt(100 + i, 200 + i),
                    HasWorldPosition = 1,
                }), Is.True);
            }

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
            Assert.That(ex.Message, Does.Contain("no live team relationship representative"));

            int spawned = 0;
            var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
            world.Query(in query, (ref EntityTemplateKeyRef _) => spawned++);
            Assert.That(spawned, Is.Zero, "Missing relationship prerequisites must fail before batch entities are created.");
        }

        [Test]
        public void CoreComponentRegistry_RegistersMassNavigationAgentLayer()
        {
            Assert.That(Ludots.Core.Config.ComponentRegistry.TryGetComponentType("MassNavigationAgent", out _), Is.True);
            Assert.That(LayerRegistry.GetName(LayerRegistry.GetIndex(MassNavigationLayerNames.Agent)), Is.EqualTo(MassNavigationLayerNames.Agent));
            Assert.That(LayerRegistry.GetBit(MassNavigationLayerNames.Agent), Is.Not.EqualTo(0u));
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

            Vector2 moveTargetScreen = WorldToScreen(engine, FormationCapabilityAcceptance.MoveTargetWorldCm);
            AssertOutsideMinimapInteractiveRegion(engine, moveTargetScreen);
            WorldCmInt2 expectedMoveTarget = ResolveGroundWorldCm(engine, moveTargetScreen);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () =>
                {
                    Entity[] selected = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine);
                    if (selected.Length != FormationCapabilityAcceptance.ExpectedInitialCommandSource)
                    {
                        return false;
                    }

                    FormationCommandState command = engine.World.Get<FormationCommandState>(selected[0]);
                    return MathF.Abs(command.TargetCenterXCm - expectedMoveTarget.X) <= 1f &&
                           MathF.Abs(command.TargetCenterYCm - expectedMoveTarget.Y) <= 1f;
                },
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Right-click command should flow through PlayerInputHandler and OrderBufferSystem into the Formation owner.");

            Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.Zero,
                "Formation orders must not create MassNavigation-owned formation groups.");
        }

        [Test]
        public void FormationCapabilityPlayable_StationaryRotateUsesFormationOrderWithoutMassNavigationMove()
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
                failureMessage: "Formation Capability must be ready before stationary rotation.");

            Assert.That(Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity formation), Is.True);
            FormationCommandState before = engine.World.Get<FormationCommandState>(formation);
            int activeNavigationGroupsBefore = simulation.NavGroupRuntime.ActiveOrderGroupCount;
            OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("Formation Capability requires OrderTypeRegistry.");
            Assert.That(orderTypes.GetId(FormationOrderKeys.Rotate),
                Is.Not.EqualTo(orderTypes.GetId(MassNavigationOrderKeys.Move)));

            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(FormationCapabilityAcceptance.RotateRightActionId);
            TickUntil(
                engine,
                () => MathF.Abs(NormalizeAngleRadians(
                    DecodeFormationFacing(engine.World.Get<FormationCommandState>(formation).TargetFacingMicroRad) -
                    DecodeFormationFacing(before.TargetFacingMicroRad))) > 0.0001f,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Stationary rotate should update the Showcase-owned target facing.");

            FormationCommandState after = engine.World.Get<FormationCommandState>(formation);
            Assert.That(after.TargetCenterXCm, Is.EqualTo(before.TargetCenterXCm));
            Assert.That(after.TargetCenterYCm, Is.EqualTo(before.TargetCenterYCm));
            Assert.That(simulation.NavGroupRuntime.ActiveOrderGroupCount, Is.EqualTo(activeNavigationGroupsBefore),
                "Formation rotation must not manufacture a MassNavigation move group to the current position.");
        }

        [Test]
        public void FormationCapabilityOrders_RejectRetiredOrCrossOrderPayloadFields()
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
                failureMessage: "Formation Capability must be ready before order payload validation.");

            Assert.That(Ludots.Tests.EntityCollectionTestAccess.TryGetCommandSourcePrimary(engine, out Entity formation), Is.True);
            FormationOrderSystem system = GetSystems(engine, SystemGroup.AbilityActivation)
                .OfType<FormationOrderSystem>()
                .Single();
            OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("Formation Capability requires OrderTypeRegistry.");
            int moveOrderTypeId = orderTypes.GetId(FormationOrderKeys.Move);
            int rotateOrderTypeId = orderTypes.GetId(FormationOrderKeys.Rotate);
            ref OrderBuffer buffer = ref engine.World.Get<OrderBuffer>(formation);

            OrderArgs validMoveArgs = OrderArgs.CreateSingleWorldCm(new Vector3(1000f, 0f, 2000f));
            var missingOrderId = new Order { OrderId = 0, OrderTypeId = moveOrderTypeId, Actor = formation, Args = validMoveArgs };
            buffer.SetActiveDirect(in missingOrderId, priority: 100);
            InvalidOperationException missingOrderIdEx = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(missingOrderIdEx.Message, Does.Contain("positive OrderId"));
            buffer.ClearActive();

            string[] retiredMoveFields = { "I0", "I1", "F0", "I2", "F1" };
            for (int i = 0; i < retiredMoveFields.Length; i++)
            {
                OrderArgs args = OrderArgs.CreateSingleWorldCm(new Vector3(1000f, 0f, 2000f));
                switch (retiredMoveFields[i])
                {
                    case "I0": args.I0 = 1; break;
                    case "I1": args.I1 = 1; break;
                    case "F0": args.F0 = 1f; break;
                    case "I2": args.I2 = 1; break;
                    case "F1": args.F1 = 1f; break;
                }

                var order = new Order { OrderId = 100 + i, OrderTypeId = moveOrderTypeId, Actor = formation, Args = args };
                buffer.SetActiveDirect(in order, priority: 100);
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
                Assert.That(ex.Message, Does.Contain("Formation move order"));
                buffer.ClearActive();
            }

            foreach (float invalidCoordinate in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                OrderArgs args = OrderArgs.CreateSingleWorldCm(new Vector3(invalidCoordinate, 0f, 2000f));
                var order = new Order { OrderId = 150, OrderTypeId = moveOrderTypeId, Actor = formation, Args = args };
                buffer.SetActiveDirect(in order, priority: 100);
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
                Assert.That(ex.Message, Does.Contain("Formation move order"));
                buffer.ClearActive();
            }

            OrderArgs invalidHeightArgs = OrderArgs.CreateSingleWorldCm(new Vector3(1000f, float.NaN, 2000f));
            var invalidHeightOrder = new Order { OrderId = 151, OrderTypeId = moveOrderTypeId, Actor = formation, Args = invalidHeightArgs };
            buffer.SetActiveDirect(in invalidHeightOrder, priority: 100);
            Assert.Throws<InvalidOperationException>(() => UpdateSystem(system));
            buffer.ClearActive();

            OrderArgs rotateArgs = default;
            rotateArgs.F0 = 0.5f;
            rotateArgs.I2 = 1;
            var rotateOrder = new Order { OrderId = 200, OrderTypeId = rotateOrderTypeId, Actor = formation, Args = rotateArgs };
            buffer.SetActiveDirect(in rotateOrder, priority: 100);
            InvalidOperationException rotateEx = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;
            Assert.That(rotateEx.Message, Does.Contain("Formation rotate order"));
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
            Assert.That(engine.World.TryGet(formation, out FormationAnchorState formationAgent), Is.True);
            float initialFacing = engine.World.Get<FacingDirection>(formation).AngleRad;
            int soldierAgentIndex = FindFirstSoldierAgentIndex(engine, formationAgent.FormationIndex);
            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(soldierAgentIndex, out _, out _), Is.True);
            Vector2 soldierBefore = simulation.GetAgentLocalPositionCm(soldierAgentIndex);

            Vector2 moveTargetScreen = WorldToScreen(engine, FormationCapabilityAcceptance.MoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            TickUntil(
                engine,
                () =>
                {
                    FormationCommandState command = engine.World.Get<FormationCommandState>(formation);
                    return MathF.Abs(command.TargetCenterXCm - FormationCapabilityAcceptance.MoveTargetWorldCm.X) <= 1f &&
                           MathF.Abs(command.TargetCenterYCm - FormationCapabilityAcceptance.MoveTargetWorldCm.Y) <= 1f;
                },
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Right-click move should update the Showcase-owned formation center target.");

            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(formationAgentIndex.Value, out _, out _), Is.False,
                "Formation movement must use explicit MovePlanning targets instead of a MassNavigation-owned formation group.");
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
            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(
                soldierAgentIndex,
                out float soldierTargetBeforeRotateX,
                out float soldierTargetBeforeRotateY), Is.True);

            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);
            TickUntil(
                engine,
                () => MathF.Abs(NormalizeAngleRadians(
                    DecodeFormationFacing(engine.World.Get<FormationCommandState>(formation).TargetFacingMicroRad) - initialFacing)) > 0.0001f,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Explicit rotate input should change the Showcase-owned formation facing target.");

            Assert.That(simulation.TryGetAgentNavigationTargetLocalCm(soldierAgentIndex, out float soldierTargetAfterX, out float soldierTargetAfterY), Is.True);
            float targetDeltaX = soldierTargetAfterX - soldierTargetBeforeRotateX;
            float targetDeltaY = soldierTargetAfterY - soldierTargetBeforeRotateY;
            Assert.That((targetDeltaX * targetDeltaX) + (targetDeltaY * targetDeltaY), Is.GreaterThan(1f),
                "Soldier slot targets must follow explicit formation facing changes.");
        }

        [Test]
        public void FormationCapabilityPlayable_TimeFlowTokenRequestsDriveSimulationTimeFlow()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            engine.LoadMap("formation_capability_showcase");
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);

            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TimeFlowService timeFlow = RequireTimeFlow(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability scenario should be fully spawned and command-source ready before time-control verification.");

            TimeFlowToken pause = timeFlow.AcquirePauseToken(
                TimeFlowDomainIds.Simulation,
                "FormationCapabilityShowcase.Tests",
                "verify system-level pause token");
            Assert.That(SimulationTimeScaleMatches(engine, 0), Is.True,
                "A pause token should pause the shared simulation time source.");
            timeFlow.ReleaseToken(pause);

            TimeFlowToken scale500 = timeFlow.AcquireScaleToken(
                TimeFlowDomainIds.Simulation,
                500,
                "FormationCapabilityShowcase.Tests",
                "verify system-level scale token");
            Assert.That(SimulationTimeScaleMatches(engine, 500), Is.True,
                "A scale token should set the shared simulation time source to 500 permille.");
            timeFlow.ReleaseToken(scale500);

            Assert.That(SimulationTimeScaleMatches(engine, 1000), Is.True,
                "Releasing the only active token should restore 1000 permille.");

            TimeFlowToken scale2000 = timeFlow.AcquireScaleToken(
                TimeFlowDomainIds.Simulation,
                2000,
                "FormationCapabilityShowcase.Tests",
                "verify system-level scale token");
            Assert.That(SimulationTimeScaleMatches(engine, 2000), Is.True,
                "A scale token should set the shared simulation time source to 2000 permille.");

            TimeFlowToken externalScale = timeFlow.AcquireScaleToken(
                TimeFlowDomainIds.Simulation,
                500,
                "FormationCapabilityShowcase.Tests",
                "verify token release preserves other active tokens");
            try
            {
                Assert.That(SimulationTimeScaleMatches(engine, 1000), Is.True,
                    "Composed 500 and 2000 permille scale tokens should produce 1000 effective permille.");

                timeFlow.ReleaseToken(scale2000);
                Assert.That(SimulationTimeScaleMatches(engine, 500), Is.True,
                    "Releasing one scale token must not override other active simulation time tokens.");
            }
            finally
            {
                timeFlow.ReleaseToken(externalScale);
            }
        }

        [Test]
        public void FormationCapabilityPlayable_NonLocalControlDomainFormationSelectionRejectsRightClickMoveOrder()
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

            Entity localDomain = ResolveLocalPlayerEntity(engine);
            AssertLocalControlDomainFormations(engine, localDomain);
            Entity enemyFormation = FindNonLocalControlDomainFormation(engine, localDomain);
            Assert.That(engine.World.TryGet(enemyFormation, out MassNavigationAgentIndex enemyAgentIndex), Is.True);
            Assert.That(ResolveControlDomain(engine, enemyFormation), Is.Not.EqualTo(localDomain));
            Assert.That(engine.World.Has<PlayerOwner>(enemyFormation), Is.False);
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(
                    enemyAgentIndex.Value,
                    out float _,
                    out float _),
                Is.False);
            FormationCommandState commandBefore = engine.World.Get<FormationCommandState>(enemyFormation);

            SelectFormations(engine, new[] { enemyFormation });
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == 1,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored command source should contain the non-local formation agent.");

            Vector2 moveTargetScreen = WorldToScreen(engine, FormationCapabilityAcceptance.MoveTargetWorldCm);
            RightClick(engine, GetInputBackend(engine), moveTargetScreen);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInteraction);

            FormationCommandState commandAfter = engine.World.Get<FormationCommandState>(enemyFormation);
            Assert.That(commandAfter.TargetCenterXCm, Is.EqualTo(commandBefore.TargetCenterXCm));
            Assert.That(commandAfter.TargetCenterYCm, Is.EqualTo(commandBefore.TargetCenterYCm));
            Assert.That(ResolveControlDomain(engine, enemyFormation), Is.Not.EqualTo(localDomain));
            Assert.That(simulation.NavGroupRuntime.TryGetGroupMemberOrderTarget(
                    enemyAgentIndex.Value,
                    out float _,
                    out float _),
                Is.False);

            float enemyFacingBeforeRotate = engine.World.Get<FacingDirection>(enemyFormation).AngleRad;
            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is missing.");
            input.InjectButtonPress(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine);
            input.InjectButtonRelease(FormationCapabilityAcceptance.RotateRightActionId);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForInputRelease);

            Assert.That(
                NormalizeAngleRadians(engine.World.Get<FacingDirection>(enemyFormation).AngleRad - enemyFacingBeforeRotate),
                Is.EqualTo(0f).Within(0.0001f),
                "Q/E rotation must use the same relationship control-domain boundary as right-click move orders.");
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
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == CountFriendlyDomainFormations(engine),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Player box acquisition should include only formations accepted by the configured Friendly relationship filter.");

            Entity[] selected = Ludots.Tests.EntityCollectionTestAccess.SnapshotCommandSource(engine);
            Assert.That(selected.Length, Is.EqualTo(CountFriendlyDomainFormations(engine)));
            for (int i = 0; i < selected.Length; i++)
            {
                Entity entity = selected[i];
                Assert.That(engine.World.Has<FormationAnchorState>(entity), Is.True);
                Assert.That(IsFriendlyToLocalDomain(engine, entity), Is.True);
                Assert.That(engine.World.Has<Team>(entity), Is.False);
                Assert.That(engine.World.Has<PlayerOwner>(entity), Is.False);
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

            Entity localDomain = ResolveLocalPlayerEntity(engine);
            Entity localFormation = FindLocalControlDomainFormation(engine, localDomain);
            Entity enemyFormation = FindNonLocalControlDomainFormation(engine, localDomain);
            float localFacingBefore = engine.World.Get<FacingDirection>(localFormation).AngleRad;
            float enemyFacingBefore = engine.World.Get<FacingDirection>(enemyFormation).AngleRad;
            FormationCapabilityShowcaseRuntime runtime = GetSystems(engine, SystemGroup.InputCollection)
                .OfType<FormationCapabilityShowcaseRuntime.FormationCapabilityCommandSourceRotateSystem>()
                .Single()
                .Runtime;
            int rejectionCountBefore = runtime.RotateOrderRejectCount;
            SelectFormations(engine, new[] { localFormation, enemyFormation });
            TickUntil(
                engine,
                () => CommandSourceCount(engine) == 2,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Test-authored mixed command source should enter MassNavigation's command snapshot.");

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
            Assert.That(runtime.RotateOrderRejectCount, Is.EqualTo(rejectionCountBefore + 1),
                "Rejected mixed-domain rotation must be recorded explicitly instead of failing silently.");
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
            Assert.That(engine.World.TryGet(formation, out FormationAnchorState formationAgent), Is.True);
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
                () => formations.All(formation =>
                {
                    FormationCommandState command = engine.World.Get<FormationCommandState>(formation);
                    Assert.That(engine.World.TryGet(formation, out MassNavigationAgentIndex index), Is.True);
                    Vector2 current = simulation.GetAgentWorldPositionCm(index.Value);
                    float dx = command.TargetCenterXCm - current.X;
                    float dy = command.TargetCenterYCm - current.Y;
                    return (dx * dx) + (dy * dy) > 1f;
                }),
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForInteraction,
                failureMessage: "Multi-formation right-click should update every selected Formation target.");

            float orderMinDistanceSq = MinPairDistanceSq(engine, simulation, formations, useOrderTargets: true);
            Assert.That(
                orderMinDistanceSq,
                Is.GreaterThanOrEqualTo(initialMinDistanceSq * FormationCapabilityAcceptance.MultiFormationSpacingRetentionRatio),
                "Multiple formation agents must translate their current relative shape to the move target instead of being repacked into a compact fallback layout.");
        }

        [Test]
        public void FormationCapabilityMoveBatch_DifferentOrderIdsAtSameTargetRemainIndependent()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            (Entity[] formations, FormationOrderSystem system, int moveOrderTypeId) =
                PrepareFormationMoveBatchTest(engine, formationCount: 2);
            var target = new Vector2(9_000f, 7_000f);

            SetActiveFormationMoveOrder(engine, formations[0], moveOrderTypeId, orderId: 501, target);
            SetActiveFormationMoveOrder(engine, formations[1], moveOrderTypeId, orderId: 502, target);
            UpdateSystem(system);

            for (int i = 0; i < formations.Length; i++)
            {
                FormationCommandState command = engine.World.Get<FormationCommandState>(formations[i]);
                Assert.Multiple(() =>
                {
                    Assert.That(command.TargetCenterXCm, Is.EqualTo(target.X).Within(0.001f));
                    Assert.That(command.TargetCenterYCm, Is.EqualTo(target.Y).Within(0.001f));
                });
            }
        }

        [Test]
        public void FormationCapabilityMoveBatch_SharedOrderIdPreservesRelativeLayout()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            (Entity[] formations, FormationOrderSystem system, int moveOrderTypeId) =
                PrepareFormationMoveBatchTest(engine, formationCount: 2);
            Vector2 firstPosition = GetWorldPositionCm(engine, formations[0]);
            Vector2 secondPosition = GetWorldPositionCm(engine, formations[1]);
            Vector2 expectedOffset = firstPosition - secondPosition;
            var target = new Vector2(9_000f, 7_000f);

            SetActiveFormationMoveOrder(engine, formations[0], moveOrderTypeId, orderId: 601, target);
            SetActiveFormationMoveOrder(engine, formations[1], moveOrderTypeId, orderId: 601, target);
            UpdateSystem(system);

            FormationCommandState first = engine.World.Get<FormationCommandState>(formations[0]);
            FormationCommandState second = engine.World.Get<FormationCommandState>(formations[1]);
            Assert.Multiple(() =>
            {
                Assert.That(first.TargetCenterXCm - second.TargetCenterXCm, Is.EqualTo(expectedOffset.X).Within(0.001f));
                Assert.That(first.TargetCenterYCm - second.TargetCenterYCm, Is.EqualTo(expectedOffset.Y).Within(0.001f));
            });
        }

        [Test]
        public void FormationCapabilityOrderBatch_FinalEncodingFailurePreservesAllCommandStatesAcrossChunks()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            (Entity[] formations, FormationOrderSystem system, int moveOrderTypeId) =
                PrepareFormationMoveBatchTest(engine, formationCount: 2);
            if (formations[0].Id > formations[1].Id)
            {
                (formations[0], formations[1]) = (formations[1], formations[0]);
            }

            engine.World.Add(formations[1], new FormationOrderChunkSplitMarker { Value = 1 });
            var firstBefore = new FormationCommandState
            {
                TargetCenterXCm = 111,
                TargetCenterYCm = 222,
                TargetFacingMicroRad = 333,
                HasMoveTarget = 1,
            };
            var secondBefore = new FormationCommandState
            {
                TargetCenterXCm = 444,
                TargetCenterYCm = 555,
                TargetFacingMicroRad = 666,
                HasMoveTarget = 1,
            };
            engine.World.Get<FormationCommandState>(formations[0]) = firstBefore;
            engine.World.Get<FormationCommandState>(formations[1]) = secondBefore;

            SetActiveFormationMoveOrder(
                engine,
                formations[0],
                moveOrderTypeId,
                orderId: 701,
                new Vector2(9_000f, 7_000f));
            SetActiveFormationMoveOrder(
                engine,
                formations[1],
                moveOrderTypeId,
                orderId: 702,
                new Vector2(float.MaxValue, 7_000f));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;

            Assert.Multiple(() =>
            {
                Assert.That(ex.Message, Does.Contain("Formation order 702"));
                Assert.That(ex.Message, Does.Contain($"entity {formations[1].Id}:"));
                Assert.That(ex.Message, Does.Contain("TargetCenterXCm"));
                Assert.That(ex.Message, Does.Contain(float.MaxValue.ToString("R")));
                Assert.That(engine.World.Get<FormationCommandState>(formations[0]), Is.EqualTo(firstBefore));
                Assert.That(engine.World.Get<FormationCommandState>(formations[1]), Is.EqualTo(secondBefore));
                Assert.That(engine.World.Get<OrderBuffer>(formations[0]).HasActive, Is.True);
                Assert.That(engine.World.Get<OrderBuffer>(formations[1]).HasActive, Is.True);
            });
        }

        [Test]
        public void FormationCapabilityOrderBatch_SharedLayoutOffsetOverflowFailsBeforeCommit()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            (Entity[] formations, FormationOrderSystem system, int moveOrderTypeId) =
                PrepareFormationMoveBatchTest(engine, formationCount: 2);
            engine.World.Get<WorldPositionCm>(formations[0]).Value = Fix64Vec2.FromInt(0, 0);
            engine.World.Get<WorldPositionCm>(formations[1]).Value = Fix64Vec2.FromInt(10_000, 0);
            var firstBefore = engine.World.Get<FormationCommandState>(formations[0]);
            var secondBefore = engine.World.Get<FormationCommandState>(formations[1]);
            const float encodableCenterXCm = 2_147_483_000f;
            Assert.That(FormationNumericEncoding.TryRoundCm(encodableCenterXCm, out _), Is.True);

            var target = new Vector2(encodableCenterXCm, 7_000f);
            SetActiveFormationMoveOrder(engine, formations[0], moveOrderTypeId, orderId: 801, target);
            SetActiveFormationMoveOrder(engine, formations[1], moveOrderTypeId, orderId: 801, target);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;

            Assert.Multiple(() =>
            {
                Assert.That(ex.Message, Does.Contain("Formation order 801"));
                Assert.That(ex.Message, Does.Contain("TargetCenterXCm"));
                Assert.That(engine.World.Get<FormationCommandState>(formations[0]), Is.EqualTo(firstBefore));
                Assert.That(engine.World.Get<FormationCommandState>(formations[1]), Is.EqualTo(secondBefore));
            });
        }

        [Test]
        public void FormationCapabilityOrderBatch_InvalidFinalFacingPreservesEarlierMoveState()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            (Entity[] formations, FormationOrderSystem system, int moveOrderTypeId) =
                PrepareFormationMoveBatchTest(engine, formationCount: 2);
            OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("Formation Capability requires OrderTypeRegistry.");
            int rotateOrderTypeId = orderTypes.GetId(FormationOrderKeys.Rotate);
            var firstBefore = engine.World.Get<FormationCommandState>(formations[0]);
            var secondBefore = engine.World.Get<FormationCommandState>(formations[1]);

            SetActiveFormationMoveOrder(
                engine,
                formations[0],
                moveOrderTypeId,
                orderId: 901,
                new Vector2(9_000f, 7_000f));
            SetActiveFormationRotateOrder(
                engine,
                formations[1],
                rotateOrderTypeId,
                orderId: 902,
                facingRadians: 1_000_001f);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UpdateSystem(system))!;

            Assert.Multiple(() =>
            {
                Assert.That(ex.Message, Does.Contain("Formation order 902"));
                Assert.That(ex.Message, Does.Contain("TargetFacingRadians"));
                Assert.That(engine.World.Get<FormationCommandState>(formations[0]), Is.EqualTo(firstBefore));
                Assert.That(engine.World.Get<FormationCommandState>(formations[1]), Is.EqualTo(secondBefore));
            });
        }

        [Test]
        public void FormationCapabilityOrderBatch_AfterWarmupAllocatesZeroBytes()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            (Entity[] formations, FormationOrderSystem system, int moveOrderTypeId) =
                PrepareFormationMoveBatchTest(engine, formationCount: 2);
            var target = new Vector2(9_000f, 7_000f);

            for (int i = 0; i < 4; i++)
            {
                SetActiveFormationMoveOrder(engine, formations[0], moveOrderTypeId, 1_000 + i, target);
                SetActiveFormationMoveOrder(engine, formations[1], moveOrderTypeId, 2_000 + i, target);
                UpdateSystem(system);
            }

            SetActiveFormationMoveOrder(engine, formations[0], moveOrderTypeId, 3_001, target);
            SetActiveFormationMoveOrder(engine, formations[1], moveOrderTypeId, 3_002, target);
            long firstStart = GC.GetAllocatedBytesForCurrentThread();
            UpdateSystem(system);
            long firstBytes = GC.GetAllocatedBytesForCurrentThread() - firstStart;

            SetActiveFormationMoveOrder(engine, formations[0], moveOrderTypeId, 4_001, target);
            SetActiveFormationMoveOrder(engine, formations[1], moveOrderTypeId, 4_002, target);
            long secondStart = GC.GetAllocatedBytesForCurrentThread();
            UpdateSystem(system);
            long secondBytes = GC.GetAllocatedBytesForCurrentThread() - secondStart;

            Assert.Multiple(() =>
            {
                Assert.That(firstBytes, Is.Zero,
                    $"Formation order prepare/preflight/commit must allocate 0 B after warmup; first sample was {firstBytes} B.");
                Assert.That(secondBytes, Is.Zero,
                    $"Formation order prepare/preflight/commit must allocate 0 B after warmup; second sample was {secondBytes} B.");
            });
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
                MassNavigationRuntimeBinding binding = engine.GetService(MassNavigationKeys.RuntimeBinding)
                    ?? throw new InvalidOperationException("MassNavigationRuntimeBinding is missing.");
                Assert.That(binding.Current, Is.SameAs(simulation));
                Assert.That(MassNavigationIds.IsCurrentNavigationRuntimeReady(engine), Is.True);

                MapId mapId = engine.CurrentMapSession!.MapId;
                binding.Clear(mapId, simulation);
                Assert.That(MassNavigationIds.IsCurrentNavigationRuntimeReady(engine), Is.False);

                Assert.That(engine.RemoveService(MassNavigationKeys.RuntimeBinding), Is.True);
                Assert.That(engine.GetService(MassNavigationKeys.RuntimeBinding), Is.Null);
            }
        }

        [Test]
        public void MassNavigationPreSimulationStep_AdvancesFrameAndResetsPerFrameTelemetryThroughProductionGate()
        {
            MassNavigationSimulationRuntime simulation = CreateFocusedMassNavigationSimulation(out GameEngine engine);
            using (engine)
            {
                simulation.MarkCommandApply();
                Assert.That(simulation.CommandCountFrame, Is.EqualTo(1));
                int frameBefore = simulation.FrameIndex;
                var system = new MassNavigationPreSimulationStepSystem(engine);

                system.Update(1f / 60f);

                Assert.That(simulation.FrameIndex, Is.EqualTo(frameBefore + 1));
                Assert.That(simulation.CommandCountFrame, Is.Zero);
                Assert.That(simulation.FrameMs, Is.EqualTo(1000f / 60f).Within(0.001f));
                Assert.That(simulation.Fps, Is.EqualTo(60f).Within(0.001f));
            }
        }

        [Test]
        public void FormationCapabilityMap_PushAndPop_SuspendsAndResumesTheSameMassNavigationRuntime()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            TickUntilMassNavigationReady(engine);
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            int frameBeforePush = simulation.FrameIndex;

            engine.PushMap("mass_navigation");
            Tick(engine, 3);

            Assert.That(MassNavigationIds.IsCurrentNavigationRuntimeReady(engine), Is.False);
            Assert.That(simulation.FrameIndex, Is.EqualTo(frameBeforePush),
                "A suspended map must not advance its MassNavigation frame.");

            engine.PopMap();
            Assert.That(RequireSimulation(engine), Is.SameAs(simulation));
            Assert.That(simulation.FrameIndex, Is.EqualTo(frameBeforePush),
                "Restoring map focus must not advance MassNavigation synchronously inside PopMap.");
            for (int frame = 0; frame < 4 && simulation.FrameIndex == frameBeforePush; frame++)
            {
                Tick(engine);
            }

            MassNavigationRuntimeBinding resumedBinding = engine.GetService(MassNavigationKeys.RuntimeBinding)
                ?? throw new InvalidOperationException("MassNavigationRuntimeBinding is missing after map resume.");
            Assert.That(
                MassNavigationIds.IsCurrentNavigationRuntimeReady(engine),
                Is.True,
                $"currentMap={engine.CurrentMapSession?.MapId.Value ?? "<none>"}, bindingMap={resumedBinding.CurrentMapId.Value ?? "<none>"}, revision={resumedBinding.Revision}, preparedRevision={resumedBinding.PreparedRevision}, frame={simulation.FrameIndex}");
            Assert.That(simulation.FrameIndex, Is.EqualTo(frameBeforePush + 1));
        }

        [Test]
        public void FormationCapabilityMap_UnloadAndReload_CreatesNewMassNavigationRuntime()
        {
            using GameEngine engine = CreatePlayableFormationCapabilityEngine();
            LoadFormationCapabilityMap(engine);
            TickUntilMassNavigationReady(engine);
            MassNavigationSimulationRuntime first = RequireSimulation(engine);

            engine.UnloadMap("formation_capability_showcase");
            Assert.That(MassNavigationIds.IsCurrentNavigationRuntimeReady(engine), Is.False);

            LoadFormationCapabilityMap(engine);
            TickUntilMassNavigationReady(engine);
            MassNavigationSimulationRuntime second = RequireSimulation(engine);

            Assert.That(second, Is.Not.SameAs(first),
                "Unload destroys the map-scoped MassNavigation runtime; reloading the same map id must create a new simulation generation.");
        }

        [Test]
        public void GameEngine_WhenMassNavigationSystemsRegister_OrdersIngestAfterOrderBufferAndBeforeSimulationStep()
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
                engine.RegisterSystem(new MassNavigationSimulationStepSystem(engine), SystemGroup.PostMovement);
                engine.InsertSystemBeforeRequired<MassNavigationSimulationStepSystem>(
                    new MassNavigationPreSimulationStepSystem(engine),
                    SystemGroup.PostMovement);
                engine.RegisterSystem(orderBufferSystem, SystemGroup.AbilityActivation);
                engine.RegisterSystem(new MassNavigationOrderIngestionSystem(engine, simulation.Config), SystemGroup.AbilityActivation);

                List<ISystem<float>> postMovementSystems = GetSystems(engine, SystemGroup.PostMovement);
                int simulationStepIndex = postMovementSystems.FindIndex(system => system is MassNavigationSimulationStepSystem);
                Assert.That(simulationStepIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(postMovementSystems.FindIndex(system => system is MassNavigationPreSimulationStepSystem), Is.LessThan(simulationStepIndex));

                List<ISystem<float>> abilitySystems = GetSystems(engine, SystemGroup.AbilityActivation);
                int orderBufferIndex = abilitySystems.FindIndex(system => system is OrderBufferSystem);
                int ingestionIndex = abilitySystems.FindIndex(system => system is MassNavigationOrderIngestionSystem);
                Assert.That(orderBufferIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(ingestionIndex, Is.GreaterThan(orderBufferIndex));
            }
        }

        [Test]
        public void GameEngine_MapUnloadRemovesOnlyMatchingPendingMassNavigationSpawnRequests()
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
        public void MassNavigationBusinessShowcaseMods_ComposeMassNavigationDataOnlyWhenTheyReuseItsAuthoredAssets()
        {
            string modsRoot = Path.Combine(FindRepoRoot(), "mods");
            string formationModRoot = Path.Combine(
                modsRoot,
                "showcases",
                "formation_capability",
                "FormationCapabilityShowcaseMod");
            JsonObject formationManifest = ReadObject(Path.Combine(formationModRoot, "mod.json"));
            JsonObject formationDependencies = formationManifest["dependencies"]?.AsObject()
                ?? throw new InvalidOperationException("FormationCapabilityShowcaseMod mod.json must author dependencies.");
            Assert.That(formationDependencies.ContainsKey("MassNavigationMod"), Is.True,
                "Formation Capability extends MassNavigation-authored performers and mesh assets, so the provider Mod must be explicit in the launch graph.");

            string[] independentMods =
            {
                Path.Combine(modsRoot, "showcases", "road_network", "RoadNetworkShowcaseMod"),
                Path.Combine(modsRoot, "showcases", "capability_standard", "CapabilityStandardParticipantViewsMod"),
            };

            foreach (string modRoot in independentMods)
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

            string[] formationProjectReferences = ReadProjectReferenceIncludes(
                Directory.EnumerateFiles(formationModRoot, "*.csproj").Single());
            Assert.That(formationProjectReferences.Any(reference => reference.Contains("MassNavigationMod", StringComparison.Ordinal)), Is.False,
                "Formation Capability consumes the data Mod through declared composition, not a code-level project reference.");
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
            string mapFocusSource = File.ReadAllText(Path.Combine(modRoot, "CapabilityStandardMassNavigationLargeWorld10kMapFocus.cs"));
            string runtimeSource = entrySource + mapFocusSource;

            Assert.That(entrySource, Does.Contain("context.OnEvent(GameEvents.GameStart, ConfigureLargeWorldShowcaseAsync);"));
            Assert.That(entrySource, Does.Contain("context.OnEvent(GameEvents.MapLoaded, ConfigureLargeWorldShowcaseAsync);"));
            Assert.That(entrySource, Does.Contain("context.OnEvent(GameEvents.MapResumed, ConfigureLargeWorldShowcaseAsync);"));
            Assert.That(entrySource, Does.Contain("MassNavigationObserverVisibilityBindingSystem"));
            Assert.That(entrySource, Does.Contain("SystemGroup.RuntimeEntityBinding"));
            Assert.That(runtimeSource, Does.Contain("engine.MergedConfig?.StartupMapId"));
            Assert.That(entrySource, Does.Contain("CoreServiceKeys.MinimapRuntime"));
            Assert.That(entrySource, Does.Contain("runtime.Visible = true;"));
            Assert.That(entrySource, Does.Contain("runtime.SetRotateWithCamera(false);"));
            Assert.That(entrySource, Does.Contain("runtime.UseRtsFullMapPreset();"));
            Assert.That(runtimeSource, Does.Not.Contain("\"mass_navigation\""),
                "The capability runtime must use authored startupMapId instead of a code-level map-id duplicate.");
            Assert.That(runtimeSource, Does.Not.Contain("Environment.GetEnvironmentVariable"),
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
            var agentState = new MassNavigationAgentState(agentCapacity: 8);
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
                var args = MassNavigationMoveOrderArgs.Encode(new Vector2(1500f, 2500f));
                var order = new Order
                {
                    OrderId = 55,
                    OrderTypeId = 37,
                    Args = args,
                };
                MassNavigationMoveOrderArgs decoded = MassNavigationMoveOrderArgs.Decode(in order);
                Assert.That(decoded.DestinationCm, Is.EqualTo(new Vector2(1500f, 2500f)));
                Assert.That(order.Args.I0, Is.Zero);
                Assert.That(order.Args.I1, Is.Zero);
                Assert.That(order.Args.F0, Is.Zero);
            }
            finally
            {
                World.Destroy(orderWorld);
            }
        }

        [Test]
        public void MassNavigationGroupRuntime_ExposesDistinctMemberTargetsForGroupedMoveOrders()
        {
            AssertPublicMethod(typeof(MassNavigationGroupRuntime), nameof(MassNavigationGroupRuntime.TryGetGroupMemberOrderTarget));

            using MassNavigationGroupRuntimeFixture fixture = CreateGroupRuntimeFixture(
                new Vector2(1000f, 1000f),
                new Vector2(1200f, 1000f));

            int assigned = fixture.Runtime.UpsertOrderMoveCommand(
                fixture.Flow,
                fixture.AgentState,
                orderToken: 501,
                memberIndices: new[] { 0, 1 },
                teamId: 1,
                destinationWorldCm: new Vector2(4000f, 4000f));

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

        private static RelationshipRuntime CreateRelationshipRuntime(World world, out int memberOfTypeId)
        {
            var relationshipTypes = new RelationshipTypeRegistry();
            memberOfTypeId = relationshipTypes.Register("MemberOf");
            return new RelationshipRuntime(
                world,
                relationshipTypes,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
        }

        private static (
            Entity[] Formations,
            FormationOrderSystem System,
            int MoveOrderTypeId) PrepareFormationMoveBatchTest(GameEngine engine, int formationCount)
        {
            LoadFormationCapabilityMap(engine);
            Tick(engine, FormationCapabilityAcceptance.FrameBudgetForMapEntry);
            MassNavigationSimulationRuntime simulation = RequireSimulation(engine);
            TickUntil(
                engine,
                () => IsFormationCapabilityScenarioReady(engine, simulation) &&
                      CommandSourceCount(engine) == FormationCapabilityAcceptance.ExpectedInitialCommandSource,
                maxFrames: FormationCapabilityAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Formation Capability must be ready before move-batch order verification.");

            Entity[] formations = CaptureFormationAgents(engine, formationCount);
            FormationOrderSystem system = GetSystems(engine, SystemGroup.AbilityActivation)
                .OfType<FormationOrderSystem>()
                .Single();
            OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("Formation Capability requires OrderTypeRegistry.");
            return (formations, system, orderTypes.GetId(FormationOrderKeys.Move));
        }

        private static void SetActiveFormationMoveOrder(
            GameEngine engine,
            Entity formation,
            int moveOrderTypeId,
            int orderId,
            Vector2 target)
        {
            var order = new Order
            {
                OrderId = orderId,
                OrderTypeId = moveOrderTypeId,
                PlayerId = 1,
                Actor = formation,
                SubmitStep = 42,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = OrderArgs.CreateSingleWorldCm(new Vector3(target.X, 0f, target.Y)),
            };
            engine.World.Get<OrderBuffer>(formation).SetActiveDirect(in order, priority: 100);
        }

        private static void SetActiveFormationRotateOrder(
            GameEngine engine,
            Entity formation,
            int rotateOrderTypeId,
            int orderId,
            float facingRadians)
        {
            var args = new OrderArgs { F0 = facingRadians };
            var order = new Order
            {
                OrderId = orderId,
                OrderTypeId = rotateOrderTypeId,
                PlayerId = 1,
                Actor = formation,
                SubmitStep = 42,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = args,
            };
            engine.World.Get<OrderBuffer>(formation).SetActiveDirect(in order, priority: 100);
        }

        private static Vector2 GetWorldPositionCm(GameEngine engine, Entity entity)
        {
            Fix64Vec2 position = engine.World.Get<WorldPositionCm>(entity).Value;
            return new Vector2(position.X.ToFloat(), position.Y.ToFloat());
        }

        private struct FormationOrderChunkSplitMarker
        {
            public byte Value;
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
            GC.KeepAlive(typeof(FormationAnchorState).Assembly);
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
            return engine.GetService(MassNavigationKeys.RuntimeBinding)?.RequireCurrent()
                ?? throw new InvalidOperationException("Prepared MassNavigationRuntimeBinding is missing.");
        }

        private static TimeFlowService RequireTimeFlow(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.TimeFlow)
                ?? throw new InvalidOperationException("TimeFlowService is missing.");
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

        private static void TickUntilMassNavigationReady(GameEngine engine)
        {
            for (int frame = 0; frame < FormationCapabilityAcceptance.FrameBudgetForMapEntry; frame++)
            {
                if (MassNavigationIds.IsCurrentNavigationRuntimeReady(engine))
                {
                    return;
                }

                Tick(engine);
            }

            MassNavigationRuntimeBinding binding = engine.GetService(MassNavigationKeys.RuntimeBinding)
                ?? throw new InvalidOperationException("MassNavigationRuntimeBinding is missing.");
            Assert.Fail(
                $"MassNavigation runtime did not become prepared within {FormationCapabilityAcceptance.FrameBudgetForMapEntry} frames. " +
                $"currentMap={engine.CurrentMapSession?.MapId.Value ?? "<none>"}, bindingMap={binding.CurrentMapId.Value ?? "<none>"}, revision={binding.Revision}, preparedRevision={binding.PreparedRevision}.");
        }

        private static MassNavigationSimulationRuntime CreateFocusedMassNavigationSimulation(out GameEngine engine)
        {
            engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new List<string> { Path.Combine(FindRepoRoot(), "mods", "LudotsCoreMod") },
                Path.Combine(FindRepoRoot(), "assets"));

            MassNavigationConfig config = MassNavigationOrderChainTests.CreateConfigForTests();
            var simulation = new MassNavigationSimulationRuntime(config);
            simulation.BindBoardWorld(
                new WorldSizeSpec(new WorldAabbCm(0, 0, 25_000, 25_000), 100),
                new Ludots.Core.Navigation.GraphWorld.WorldGridLoadedChunks(simulation.WorldConfig.StreamingChunkSizeCm));
            FocusCurrentMapSession(engine, config.MapId);
            var binding = new MassNavigationRuntimeBinding();
            MapId mapId = engine.CurrentMapSession!.MapId;
            binding.Activate(mapId, simulation);
            binding.MarkPrepared(mapId, simulation);
            engine.SetService(MassNavigationKeys.RuntimeBinding, binding);
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
                Args = MassNavigationMoveOrderArgs.Encode(new Vector2(2000f + (agentIndex * 100f), 2500f)),
            };
            OrderBuffer orders = OrderBuffer.CreateEmpty();
            orders.SetActiveDirect(in order, priority: 100);

            return engine.World.Create(
                new MassNavigationAgent { ProfileId = MassNavigationProfileRegistry.Register("test.massNavigation.orderIngestion") },
                new MassNavigationAgentIndex { Value = agentIndex },
                orders);
        }

        private static Order[] CreateMoveOrderBatch(
            Entity first,
            Entity second,
            int token,
            Vector2 destination)
        {
            Order Create(Entity actor) => new()
            {
                OrderId = token,
                OrderTypeId = TestMassNavigationMoveOrderTypeId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = MassNavigationMoveOrderArgs.Encode(destination),
            };

            return new[] { Create(first), Create(second) };
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
            var query = new QueryDescription().WithAll<FormationMemberState, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationMemberState soldier, ref MassNavigationAgentIndex index) =>
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

        private static Entity FindFirstSoldierEntity(GameEngine engine, int formationIndex)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<FormationMemberState, MassNavigationAgentIndex>();
            engine.World.Query(in query, (Entity entity, ref FormationMemberState soldier) =>
            {
                if (result == Entity.Null && soldier.FormationIndex == formationIndex)
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"No Formation Capability soldier entity was bound for formation index {formationIndex}.");
            }

            return result;
        }

        private static Entity[] FindSoldierEntities(GameEngine engine, int formationIndex, int expectedCount)
        {
            var soldiers = new List<(int SlotIndex, Entity Entity)>(expectedCount);
            var query = new QueryDescription().WithAll<FormationMemberState, MassNavigationAgentIndex>();
            engine.World.Query(in query, (Entity entity, ref FormationMemberState soldier) =>
            {
                if (soldier.FormationIndex == formationIndex)
                {
                    soldiers.Add((soldier.SlotIndex, entity));
                }
            });

            soldiers.Sort(static (left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
            Assert.That(soldiers.Count, Is.GreaterThanOrEqualTo(expectedCount));
            var result = new Entity[expectedCount];
            for (int i = 0; i < expectedCount; i++)
            {
                result[i] = soldiers[i].Entity;
            }

            return result;
        }

        private static FormationExecutionBatchSnapshot CaptureFormationExecutionBatchSnapshot(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation)
        {
            var anchors = new List<FormationAnchorExecutionSnapshot>(FormationCapabilityAcceptance.ExpectedTotalFormations);
            var anchorQuery = new QueryDescription().WithAll<
                FormationAnchorState,
                FormationRuntimeState,
                FacingDirection,
                WorldPositionCm,
                MassNavigationAgentIndex>();
            engine.World.Query(in anchorQuery, (
                Entity entity,
                ref FormationAnchorState formation,
                ref FormationRuntimeState state,
                ref FacingDirection facing,
                ref WorldPositionCm worldPosition,
                ref MassNavigationAgentIndex agentIndex) =>
            {
                bool hasTarget = simulation.TryGetAgentNavigationTargetWorldCm(agentIndex.Value, out float targetX, out float targetY);
                anchors.Add(new FormationAnchorExecutionSnapshot(
                    formation.FormationIndex,
                    entity,
                    agentIndex.Value,
                    facing.AngleRad,
                    state,
                    worldPosition.Value,
                    hasTarget,
                    targetX,
                    targetY));
            });

            var soldiers = new List<FormationSoldierExecutionSnapshot>(FormationCapabilityAcceptance.ExpectedTotalSoldiers);
            var soldierQuery = new QueryDescription().WithAll<
                FormationMemberState,
                FacingDirection,
                WorldPositionCm,
                MassNavigationAgentIndex>();
            engine.World.Query(in soldierQuery, (
                Entity entity,
                ref FormationMemberState soldier,
                ref FacingDirection facing,
                ref WorldPositionCm worldPosition,
                ref MassNavigationAgentIndex agentIndex) =>
            {
                bool hasTarget = simulation.TryGetAgentNavigationTargetWorldCm(agentIndex.Value, out float targetX, out float targetY);
                soldiers.Add(new FormationSoldierExecutionSnapshot(
                    soldier.FormationIndex,
                    soldier.SlotIndex,
                    entity,
                    agentIndex.Value,
                    facing.AngleRad,
                    worldPosition.Value,
                    hasTarget,
                    targetX,
                    targetY));
            });

            anchors.Sort(static (left, right) => left.FormationIndex.CompareTo(right.FormationIndex));
            soldiers.Sort(static (left, right) =>
            {
                int formationComparison = left.FormationIndex.CompareTo(right.FormationIndex);
                return formationComparison != 0
                    ? formationComparison
                    : left.SlotIndex.CompareTo(right.SlotIndex);
            });
            return new FormationExecutionBatchSnapshot(anchors.ToArray(), soldiers.ToArray());
        }

        private static void AssertFormationExecutionBatchSnapshotUnchanged(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            in FormationExecutionBatchSnapshot before)
        {
            Assert.That(before.Anchors.Length, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalFormations));
            Assert.That(before.Soldiers.Length, Is.EqualTo(FormationCapabilityAcceptance.ExpectedTotalSoldiers));

            for (int i = 0; i < before.Anchors.Length; i++)
            {
                FormationAnchorExecutionSnapshot expected = before.Anchors[i];
                Assert.That(engine.World.IsAlive(expected.Entity), Is.True);
                Assert.That(engine.World.Get<FacingDirection>(expected.Entity).AngleRad, Is.EqualTo(expected.FacingRad));
                Assert.That(engine.World.Get<FormationRuntimeState>(expected.Entity), Is.EqualTo(expected.FormationState));
                Assert.That(engine.World.Get<WorldPositionCm>(expected.Entity).Value, Is.EqualTo(expected.WorldPositionCm));
                bool hasTarget = simulation.TryGetAgentNavigationTargetWorldCm(
                    expected.AgentIndex,
                    out float targetX,
                    out float targetY);
                Assert.That(hasTarget, Is.EqualTo(expected.HasNavigationTarget));
                Assert.That(targetX, Is.EqualTo(expected.NavigationTargetXCm));
                Assert.That(targetY, Is.EqualTo(expected.NavigationTargetYCm));
            }

            for (int i = 0; i < before.Soldiers.Length; i++)
            {
                FormationSoldierExecutionSnapshot expected = before.Soldiers[i];
                Assert.That(engine.World.IsAlive(expected.Entity), Is.True);
                Assert.That(engine.World.Get<FacingDirection>(expected.Entity).AngleRad, Is.EqualTo(expected.FacingRad));
                Assert.That(engine.World.Get<WorldPositionCm>(expected.Entity).Value, Is.EqualTo(expected.WorldPositionCm));
                bool hasTarget = simulation.TryGetAgentNavigationTargetWorldCm(
                    expected.AgentIndex,
                    out float targetX,
                    out float targetY);
                Assert.That(hasTarget, Is.EqualTo(expected.HasNavigationTarget));
                Assert.That(targetX, Is.EqualTo(expected.NavigationTargetXCm));
                Assert.That(targetY, Is.EqualTo(expected.NavigationTargetYCm));
            }
        }

        private static void AssertFormationMemberTargetChanged(
            MassNavigationSimulationRuntime simulation,
            in FormationExecutionBatchSnapshot before)
        {
            for (int i = 0; i < before.Soldiers.Length; i++)
            {
                FormationSoldierExecutionSnapshot expected = before.Soldiers[i];
                if (!simulation.TryGetAgentNavigationTargetWorldCm(expected.AgentIndex, out float targetX, out float targetY))
                {
                    continue;
                }

                if (!expected.HasNavigationTarget ||
                    targetX != expected.NavigationTargetXCm ||
                    targetY != expected.NavigationTargetYCm)
                {
                    return;
                }
            }

            Assert.Fail("Repairing a failed Formation batch must retry the same command and update at least one member target.");
        }

        private readonly record struct FormationExecutionBatchSnapshot(
            FormationAnchorExecutionSnapshot[] Anchors,
            FormationSoldierExecutionSnapshot[] Soldiers);

        private readonly record struct FormationAnchorExecutionSnapshot(
            int FormationIndex,
            Entity Entity,
            int AgentIndex,
            float FacingRad,
            FormationRuntimeState FormationState,
            Fix64Vec2 WorldPositionCm,
            bool HasNavigationTarget,
            float NavigationTargetXCm,
            float NavigationTargetYCm);

        private readonly record struct FormationSoldierExecutionSnapshot(
            int FormationIndex,
            int SlotIndex,
            Entity Entity,
            int AgentIndex,
            float FacingRad,
            Fix64Vec2 WorldPositionCm,
            bool HasNavigationTarget,
            float NavigationTargetXCm,
            float NavigationTargetYCm);

        private static bool SimulationTimeScaleMatches(GameEngine engine, int expectedScalePermille)
        {
            TimeFlowService timeFlow = RequireTimeFlow(engine);
            return timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation) == expectedScalePermille &&
                   timeFlow.IsPaused(TimeFlowDomainIds.Simulation) == (expectedScalePermille == 0);
        }

        private static Entity[] CaptureFormationAgents(GameEngine engine, int expectedCount)
        {
            var formations = new List<(int FormationIndex, Entity Entity)>(expectedCount);
            var query = new QueryDescription().WithAll<FormationAnchorState>();
            engine.World.Query(in query, (Entity entity, ref FormationAnchorState formation) =>
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

        private static Entity FindNonLocalControlDomainFormation(GameEngine engine, Entity localDomain)
        {
            Entity result = Entity.Null;
            int formationIndex = int.MaxValue;
            int formationsWithoutDomain = 0;
            var query = new QueryDescription().WithAll<FormationAnchorState>();
            engine.World.Query(in query, (Entity entity, ref FormationAnchorState formation) =>
            {
                if (!TryResolveControlDomain(engine, entity, out Entity domain))
                {
                    formationsWithoutDomain++;
                    return;
                }

                if (domain == localDomain || formation.FormationIndex >= formationIndex)
                {
                    return;
                }

                formationIndex = formation.FormationIndex;
                result = entity;
            });

            Assert.That(result, Is.Not.EqualTo(Entity.Null),
                $"Formation Capability command authorization test requires at least one non-local relationship domain; formations without domain={formationsWithoutDomain}.");
            return result;
        }

        private static Entity FindLocalControlDomainFormation(GameEngine engine, Entity localDomain)
        {
            Entity result = Entity.Null;
            int formationIndex = int.MaxValue;
            int formationsWithoutDomain = 0;
            var query = new QueryDescription().WithAll<FormationAnchorState>();
            engine.World.Query(in query, (Entity entity, ref FormationAnchorState formation) =>
            {
                if (!TryResolveControlDomain(engine, entity, out Entity domain))
                {
                    formationsWithoutDomain++;
                    return;
                }

                if (domain != localDomain || formation.FormationIndex >= formationIndex)
                {
                    return;
                }

                formationIndex = formation.FormationIndex;
                result = entity;
            });

            Assert.That(result, Is.Not.EqualTo(Entity.Null),
                $"Formation Capability command authorization test requires at least one local relationship domain; formations without domain={formationsWithoutDomain}.");
            return result;
        }

        private static int CountFriendlyDomainFormations(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<FormationAnchorState>();
            engine.World.Query(in query, (Entity entity, ref FormationAnchorState _) =>
            {
                if (IsFriendlyToLocalDomain(engine, entity))
                {
                    count++;
                }
            });

            return count;
        }

        private static void AssertFormationCommandSourceCandidateFacts(GameEngine engine)
        {
            int friendlyFormationCount = 0;
            int rejectedFormationCount = 0;
            int formationsWithoutDomain = 0;
            var query = new QueryDescription().WithAll<FormationAnchorState, CommandSourceSelectableState>();
            engine.World.Query(in query, (Entity entity, ref FormationAnchorState formation, ref CommandSourceSelectableState selectable) =>
            {
                Assert.That(engine.World.Has<CommandSourceSelectableTag>(entity), Is.True,
                    "Runtime-spawned Formation Capability formation anchors must satisfy Core command-source candidate tagging.");
                Assert.That(selectable.Enabled, Is.True,
                    "Formation Capability formation candidates stay generally selectable; Core relationship filtering gates player acquisition.");

                if (!TryResolveControlDomain(engine, entity, out _))
                {
                    formationsWithoutDomain++;
                    return;
                }

                if (IsFriendlyToLocalDomain(engine, entity))
                {
                    friendlyFormationCount++;
                }
                else
                {
                    rejectedFormationCount++;
                }
            });

            Assert.That(formationsWithoutDomain, Is.EqualTo(0));
            Assert.That(friendlyFormationCount, Is.GreaterThan(0));
            Assert.That(rejectedFormationCount, Is.GreaterThan(0));
        }

        private static void AssertLocalControlDomainFormations(GameEngine engine, Entity localDomain)
        {
            int localFormationCount = 0;
            var query = new QueryDescription().WithAll<FormationAnchorState>();
            engine.World.Query(in query, (Entity entity, ref FormationAnchorState _) =>
            {
                if (!TryResolveControlDomain(engine, entity, out Entity domain) || domain != localDomain)
                {
                    return;
                }

                localFormationCount++;
            });

            Assert.That(localFormationCount, Is.GreaterThan(0),
                "Formation Capability command authorization test requires at least one local relationship-domain formation.");
        }

        private static Entity ResolveControlDomain(GameEngine engine, Entity entity)
        {
            Assert.That(TryResolveControlDomain(engine, entity, out Entity domain), Is.True);
            return domain;
        }

        private static bool TryResolveControlDomain(GameEngine engine, Entity entity, out Entity domain)
        {
            ControlDomainQuery controlDomains = engine.GetService(CoreServiceKeys.ControlDomainQuery)
                ?? throw new InvalidOperationException("ControlDomainQuery is missing.");
            DomainStanceQuery stances = engine.GetService(CoreServiceKeys.DomainStanceQuery)
                ?? throw new InvalidOperationException("DomainStanceQuery is missing.");
            return controlDomains.TryResolveControlDomain(entity, out domain) ||
                stances.TryResolveStanceDomain(entity, out domain);
        }

        private static bool IsFriendlyToLocalDomain(GameEngine engine, Entity entity)
        {
            Entity local = ResolveLocalPlayerEntity(engine);
            if (!TryResolveControlDomain(engine, local, out Entity localDomain) ||
                !TryResolveControlDomain(engine, entity, out Entity candidateDomain))
            {
                return false;
            }

            DomainStanceQuery stances = engine.GetService(CoreServiceKeys.DomainStanceQuery)
                ?? throw new InvalidOperationException("DomainStanceQuery is missing.");
            Assert.That(stances.TryResolveStanceId(nameof(RelationshipFilter.Friendly), out int friendlyStanceId), Is.True);
            return stances.GetStance(localDomain, candidateDomain) == friendlyStanceId;
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
                FormationCommandState command = engine.World.Get<FormationCommandState>(formation);
                return new Vector2(command.TargetCenterXCm, command.TargetCenterYCm);
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
            Entity[] overlays = CaptureEntitiesWithRuntimeComponents(
                engine,
                expectedCount,
                ResolveRuntimeShowcaseType(engine, "FormationCapabilityShowcaseObstacleOverlay"));
            Assert.That(overlays.Length, Is.EqualTo(expectedCount));
            return overlays;
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

        private static int EncodeFormationFacing(float angle)
        {
            return FormationNumericEncoding.EncodeRadians(angle, "Formation Capability test facing");
        }

        private static float DecodeFormationFacing(int encodedAngle)
        {
            return FormationNumericEncoding.DecodeRadians(encodedAngle);
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
            var query = new QueryDescription().WithAll<FormationAnchorState, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationAnchorState _, ref MassNavigationAgentIndex agentIndex) =>
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
            var query = new QueryDescription().WithAll<FormationAnchorState, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationAnchorState _, ref MassNavigationAgentIndex _) => count++);
            return count;
        }

        private static int CountFormationSoldiers(GameEngine engine)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<FormationMemberState, MassNavigationAgentIndex>();
            engine.World.Query(in query, (ref FormationMemberState _, ref MassNavigationAgentIndex _) => count++);
            return count;
        }

        private static int CountObstacleOverlays(GameEngine engine)
        {
            return CountEntitiesWithRuntimeComponents(
                engine,
                ResolveRuntimeShowcaseType(engine, "FormationCapabilityShowcaseObstacleOverlay"));
        }

        private static string BuildFormationAgentDiagnostics(GameEngine engine)
        {
            int formationOnly = 0;
            int soldierOnly = 0;
            int indexOnly = 0;
            int orderable = 0;
            var formationQuery = new QueryDescription().WithAll<FormationAnchorState>();
            engine.World.Query(in formationQuery, (ref FormationAnchorState _) => formationOnly++);
            var soldierQuery = new QueryDescription().WithAll<FormationMemberState>();
            engine.World.Query(in soldierQuery, (ref FormationMemberState _) => soldierOnly++);
            var indexQuery = new QueryDescription().WithAll<MassNavigationAgentIndex>();
            engine.World.Query(in indexQuery, (ref MassNavigationAgentIndex _) => indexOnly++);
            var orderableQuery = new QueryDescription().WithAll<MassNavigationAgentIndex, OrderBuffer>();
            engine.World.Query(in orderableQuery, (ref MassNavigationAgentIndex _, ref OrderBuffer _) => orderable++);
            MassNavigationSimulationRuntime? simulation = engine.GetService(MassNavigationKeys.RuntimeBinding)?.Current;
            int projections = CountMassNavigationFlowObstacleProjections(engine);
            int overlays = CountObstacleOverlays(engine);
            string obstacleDiagnostics = simulation != null
                ? $"projections={projections} blockers={simulation.AgentState.BlockerCount} navObstacles={simulation.NavigationObstacleCount} overlays={overlays}"
                : $"projections={projections} blockers=<no-runtime> navObstacles=<no-runtime> overlays={overlays}";
            RuntimeEntitySpawnQueue? spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);
            string spawnDiagnostics = spawnQueue != null
                ? $"spawnQueue={spawnQueue.Count}/{spawnQueue.Capacity}"
                : "spawnQueue=<missing>";
            return $"formationOnly={formationOnly} soldier={soldierOnly} indexed={indexOnly} orderable={orderable} {obstacleDiagnostics} {spawnDiagnostics} {BuildObstacleOverlayDiagnostics(engine)} {BuildCommandSourceDiagnostics(engine)} {BuildSelectionCandidateDiagnostics(engine)}";
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
            var query = new QueryDescription().WithAll<FormationAnchorState>();
            engine.World.Query(in query, (Entity entity, ref FormationAnchorState formation) =>
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
            Type overlayType = ResolveRuntimeShowcaseType(engine, "FormationCapabilityShowcaseObstacleOverlay");
            var overlaysByPosition = new Dictionary<(int X, int Y), ObstacleOverlaySnapshot>(
                simulation.NavigationObstacleCount);
            Entity[] overlayEntities = CaptureEntitiesWithRuntimeComponents(
                engine,
                simulation.NavigationObstacleCount,
                overlayType,
                typeof(WorldPositionCm));
            for (int i = 0; i < overlayEntities.Length; i++)
            {
                Entity entity = overlayEntities[i];
                ObstacleOverlaySnapshot overlay = ReadObstacleOverlaySnapshot(entity, overlayType);
                WorldPositionCm position = engine.World.Get<WorldPositionCm>(entity);
                var key = (position.Value.X.ToInt(), position.Value.Y.ToInt());
                Assert.That(
                    overlaysByPosition.ContainsKey(key),
                    Is.False,
                    $"Formation Capability obstacle overlay position ({key.Item1}, {key.Item2}) must be unique.");
                overlaysByPosition.Add(key, overlay);
            }

            Assert.That(overlaysByPosition.Count, Is.EqualTo(simulation.NavigationObstacleCount));
            for (int i = 0; i < simulation.NavigationObstacleCount; i++)
            {
                MassNavigationObstacleSnapshot obstacle = simulation.GetObstacleWorldSnapshot(i);
                var key = ((int)MathF.Round(obstacle.WorldXCm), (int)MathF.Round(obstacle.WorldYCm));
                Assert.That(
                    overlaysByPosition.TryGetValue(key, out ObstacleOverlaySnapshot overlay),
                    Is.True,
                    $"Formation Capability obstacle overlay should exist at MassNavigation obstacle position ({key.Item1}, {key.Item2}).");
                Assert.That(overlay.RadiusCm, Is.EqualTo(obstacle.RadiusCm).Within(0.001f));
                Assert.That(overlay.BorderWidthCm, Is.EqualTo(expectedBorderWidthCm).Within(0.001f));
            }
        }

        private static Type ResolveRuntimeShowcaseType(GameEngine engine, string typeName)
        {
            const string runtimeNamespace = "FormationCapabilityShowcaseMod.Runtime.";
            foreach (ISystem<float> system in EnumerateRegisteredSystems(engine))
            {
                Type systemType = system.GetType();
                if (!string.Equals(systemType.Assembly.GetName().Name, "FormationCapabilityShowcaseMod", StringComparison.Ordinal))
                {
                    continue;
                }

                return systemType.Assembly.GetType(runtimeNamespace + typeName, throwOnError: true)
                    ?? throw new InvalidOperationException($"Formation Capability runtime type '{runtimeNamespace}{typeName}' could not be resolved.");
            }

            throw new InvalidOperationException("Formation Capability runtime assembly is not loaded.");
        }

        private static IEnumerable<ISystem<float>> EnumerateRegisteredSystems(GameEngine engine)
        {
            FieldInfo field = typeof(GameEngine).GetField("_systemGroups", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine system group field is missing.");
            var systemGroups = field.GetValue(engine) as Dictionary<SystemGroup, List<ISystem<float>>>
                ?? throw new InvalidOperationException("GameEngine system groups are missing.");
            foreach (List<ISystem<float>> systems in systemGroups.Values)
            {
                for (int i = 0; i < systems.Count; i++)
                {
                    yield return systems[i];
                }
            }
        }

        private static IEnumerable<ISystem<float>> EnumerateRegisteredPresentationSystems(GameEngine engine)
        {
            FieldInfo field = typeof(GameEngine).GetField("_presentationSystems", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GameEngine presentation system field is missing.");
            var systems = field.GetValue(engine) as List<ISystem<float>>
                ?? throw new InvalidOperationException("GameEngine presentation systems are missing.");
            for (int i = 0; i < systems.Count; i++)
            {
                yield return systems[i];
            }
        }

        private static int CountFormationCapabilitySystems(GameEngine engine)
        {
            var names = new HashSet<string>(FormationCapabilityInstalledSystemTypeNames, StringComparer.Ordinal);
            int count = 0;
            foreach (ISystem<float> system in EnumerateRegisteredSystems(engine).Concat(EnumerateRegisteredPresentationSystems(engine)))
            {
                if (names.Contains(system.GetType().Name))
                {
                    count++;
                }
            }

            return count;
        }

        private static QueryDescription BuildRuntimeComponentQuery(params Type[] componentTypes)
        {
            if (componentTypes.Length == 0)
            {
                throw new InvalidOperationException("Runtime component query requires at least one component type.");
            }

            Arch.Core.Signature signature = Arch.Core.Signature.Null;
            for (int i = 0; i < componentTypes.Length; i++)
            {
                Type componentMetadata = typeof(Arch.Core.Component<>).MakeGenericType(componentTypes[i]);
                FieldInfo signatureField = componentMetadata.GetField("Signature", BindingFlags.Static | BindingFlags.Public)
                    ?? throw new InvalidOperationException($"Arch component metadata for '{componentTypes[i].FullName}' does not expose Signature.");
                signature += (Arch.Core.Signature)(signatureField.GetValue(null)
                    ?? throw new InvalidOperationException($"Arch component signature for '{componentTypes[i].FullName}' is null."));
            }

            return new QueryDescription(all: signature);
        }

        private static int CountEntitiesWithRuntimeComponents(GameEngine engine, params Type[] componentTypes)
        {
            int count = 0;
            QueryDescription query = BuildRuntimeComponentQuery(componentTypes);
            foreach (ref Chunk chunk in engine.World.Query(in query))
            {
                count += chunk.Count;
            }

            return count;
        }

        private static Entity[] CaptureEntitiesWithRuntimeComponents(GameEngine engine, int expectedCount, params Type[] componentTypes)
        {
            var entities = new List<Entity>(expectedCount);
            QueryDescription query = BuildRuntimeComponentQuery(componentTypes);
            foreach (ref Chunk chunk in engine.World.Query(in query))
            {
                foreach (int index in chunk)
                {
                    entities.Add(chunk.Entity(index));
                }
            }

            return entities.ToArray();
        }

        private static ObstacleOverlaySnapshot ReadObstacleOverlaySnapshot(Entity entity, Type overlayType)
        {
            object overlay = entity.GetAllComponents().FirstOrDefault(component => component?.GetType() == overlayType)
                ?? throw new InvalidOperationException($"Entity {entity.Id} does not have runtime obstacle overlay component '{overlayType.FullName}'.");
            return new ObstacleOverlaySnapshot(
                ReadFloatField(overlay, overlayType, "RadiusCm"),
                ReadFloatField(overlay, overlayType, "BorderWidthCm"));
        }

        private static float ReadFloatField(object value, Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"Runtime obstacle overlay component '{type.FullName}' is missing field '{fieldName}'.");
            return (float)(field.GetValue(value)
                ?? throw new InvalidOperationException($"Runtime obstacle overlay field '{fieldName}' is null."));
        }

        private readonly record struct ObstacleOverlaySnapshot(float RadiusCm, float BorderWidthCm);

        private static string BuildObstacleOverlayDiagnostics(GameEngine engine)
        {
            Type overlayType = ResolveRuntimeShowcaseType(engine, "FormationCapabilityShowcaseObstacleOverlay");
            int overlayOnly = CountEntitiesWithRuntimeComponents(engine, overlayType);
            int overlayVisual = CountEntitiesWithRuntimeComponents(engine, overlayType, typeof(VisualTransform));
            int overlayStable = CountEntitiesWithRuntimeComponents(engine, overlayType, typeof(PresentationStableId));
            int overlayRenderable = CountEntitiesWithRuntimeComponents(engine, overlayType, typeof(VisualTransform), typeof(PresentationStableId));
            int obstacleTemplate = 0;
            int obstacleTemplateVisualStable = 0;
            int obstacleTemplateKeyId = 0;
            if (engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry) is EntityTemplateKeyRegistry templateKeys)
            {
                obstacleTemplateKeyId = templateKeys.GetId("formation_capability_showcase_obstacle_overlay");
            }

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
            return $"commandSource={CommandSourceCount(engine)} {view} markers={CountCommandMarkerPerformers(engine)} agents={simulation.AgentState.TotalAgents}";
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

        private static void AssertAttributeBufferDoesNotAuthorTimeScale(JsonObject? attributeBuffer, string label)
        {
            Assert.That(attributeBuffer, Is.Not.Null, $"{label} must author AttributeBuffer.");
            JsonObject baseValues = attributeBuffer!["base"]?.AsObject()
                ?? throw new InvalidOperationException($"{label} AttributeBuffer must author base values.");
            JsonObject currentValues = attributeBuffer["current"]?.AsObject()
                ?? throw new InvalidOperationException($"{label} AttributeBuffer must author current values.");
            Assert.That(baseValues.ContainsKey("time.scale_permille"), Is.False,
                $"{label} base attributes must not author system-level time.");
            Assert.That(currentValues.ContainsKey("time.scale_permille"), Is.False,
                $"{label} current attributes must not author system-level time.");
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
            var agentState = new MassNavigationAgentState(agentCapacity: localPositions.Length);
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
                LoadBaseMassNavigationConfig().Semantics.Group,
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
                GroupMemberCapacity = groupMemberCapacity,
                OrderIngestionTokenCapacity = 8,
                OrderIngestionMemberCapacity = groupMemberCapacity,
                RouteStateCapacity = 8,
                RouteMaxExpandedPerRequest = 128,
                RouteWaypointCapacityPerAgent = 64,
                LoadedChunkCapacity = 16,
                RelationshipDomainCapacity = 4,
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
            public const string RotateRightActionId = "FormationCapability_RotateRight";
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
