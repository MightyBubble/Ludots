using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationPerformerContractTests
    {
        private const string AgentHealthDriftEffectId = "Effect.MassNavigation.Agent.HealthDrift";
        private const string AgentHealthDriftGraphId = "Graph.MassNavigation.Agent.HealthDrift";
        private const string AgentHealthAttributeName = "Health";
        private const string LocomotionSpeedParamKey = "mass_navigation.agent.locomotion.speed";
        private const string HealthRatioParamKey = "massNavigation.agent.health.ratio";
        private const string HealthCurrentParamKey = "massNavigation.agent.health.current";
        private const string HealthBaseParamKey = "massNavigation.agent.health.base";
        private const string LargeWorldCameraId = "MassNavigation.Camera.LargeWorldHeightmap";

        [Test]
        public void AgentPerformers_UseMassNavigationOwnedGpuSkinnedAsset()
        {
            string modRoot = MassNavigationModRoot();
            JsonArray performers = ReadArray(Path.Combine(modRoot, "assets", "Presentation", "performers.json"));

            AssertAgentBodyUsesMassNavigationSoldier(FindObjectById(performers, "mass_navigation_agent_light"), "mass_navigation_agent_light", expectedScale: 0.45f);
            AssertAgentBodyUsesMassNavigationSoldier(FindObjectById(performers, "mass_navigation_agent_heavy"), "mass_navigation_agent_heavy", expectedScale: 0.62f);

            JsonObject manifest = ReadObject(Path.Combine(modRoot, "mod.json"));
            JsonObject dependencies = manifest["dependencies"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationMod mod.json must declare dependencies.");
            Assert.That(dependencies.ContainsKey("PerformerBlacksmithShowcaseMod"), Is.False,
                "MassNavigation capability visuals must be owned by MassNavigation, not a showcase asset pack.");

            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonArray requiredMeshAssets = config["presentation"]?["requiredMeshAssetIds"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigationConfig.presentation.requiredMeshAssetIds missing.");
            Assert.That(requiredMeshAssets.Select(node => node?.GetValue<string>()).ToArray(), Does.Contain("mass_navigation.agent.soldier"));
        }

        [Test]
        public void AgentTemplates_AuthorHealthAndOnSpawnGasEffect()
        {
            string modRoot = MassNavigationModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));

            foreach (JsonObject team in EnumeratePresentationTeams(config))
            {
                AssertAgentTemplateAuthorsHealth(
                    FindObjectById(templates, RequireString(team, "lightTemplateId")),
                    expectedBase: 100f,
                    expectedCurrent: 82f);
                AssertAgentTemplateAuthorsHealth(
                    FindObjectById(templates, RequireString(team, "heavyTemplateId")),
                    expectedBase: 260f,
                    expectedCurrent: 236f);
            }
        }

        [Test]
        public void AgentHealthHud_UsesPerformerWorldHudAttributeRatio()
        {
            string modRoot = MassNavigationModRoot();
            JsonArray performers = ReadArray(Path.Combine(modRoot, "assets", "Presentation", "performers.json"));

            AssertDefinitionHasChild(
                FindObjectById(performers, "mass_navigation_agent_light"),
                "mass_navigation_agent_light",
                "mass_navigation_agent_health_hud_light");
            AssertDefinitionHasChild(
                FindObjectById(performers, "mass_navigation_agent_light"),
                "mass_navigation_agent_light",
                "mass_navigation_agent_health_text_light");
            AssertDefinitionHasChild(
                FindObjectById(performers, "mass_navigation_agent_heavy"),
                "mass_navigation_agent_heavy",
                "mass_navigation_agent_health_hud_heavy");
            AssertDefinitionHasChild(
                FindObjectById(performers, "mass_navigation_agent_heavy"),
                "mass_navigation_agent_heavy",
                "mass_navigation_agent_health_text_heavy");

            JsonObject lightHud = FindObjectById(performers, "mass_navigation_agent_health_hud_light");
            AssertHudDefinitionUsesHealthRatio(lightHud, "mass_navigation_agent_health_hud_light", expectedWidth: 42f, expectedHeight: 5f);

            JsonObject heavyHud = FindObjectById(performers, "mass_navigation_agent_health_hud_heavy");
            Assert.That(RequireString(heavyHud, "extends"), Is.EqualTo("mass_navigation_agent_health_hud_light"),
                "Heavy HUD should inherit the same Health ratio binding instead of duplicating a second binding source.");
            AssertHudWorldHudBinding(heavyHud, "mass_navigation_agent_health_hud_heavy", expectedWidth: 58f, expectedHeight: 6f);

            AssertWorldTextDefinitionUsesHealthCurrentOverBase(
                FindObjectById(performers, "mass_navigation_agent_health_text_light"),
                "mass_navigation_agent_health_text_light",
                expectedFontSize: 11);
            JsonObject heavyText = FindObjectById(performers, "mass_navigation_agent_health_text_heavy");
            Assert.That(RequireString(heavyText, "extends"), Is.EqualTo("mass_navigation_agent_health_text_light"),
                "Heavy text should inherit the same Health current/base binding instead of duplicating a second binding source.");
            AssertWorldTextBinding(heavyText, "mass_navigation_agent_health_text_heavy", expectedFontSize: 12);
        }

        [Test]
        public void AgentSelectionMarkers_AreSeparateScopedPerformers()
        {
            string modRoot = MassNavigationModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonArray performers = ReadArray(Path.Combine(modRoot, "assets", "Presentation", "performers.json"));

            JsonObject presentation = config["presentation"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.presentation missing.");
            Assert.That(presentation.ContainsKey("selectionVisibilityParamKey"), Is.False,
                "Selection visibility must not keep a hidden mesh inside every agent root performer.");
            Assert.That(presentation.ContainsKey("selectionMarkerLightPerformerId"), Is.False,
                "Command marker performer ownership belongs to performer rules, not MassNavigation presentation config fields.");
            Assert.That(presentation.ContainsKey("selectionMarkerHeavyPerformerId"), Is.False,
                "Command marker performer ownership belongs to performer rules, not MassNavigation presentation config fields.");

            const string lightMarkerId = "mass_navigation_agent_command_marker_light";
            const string heavyMarkerId = "mass_navigation_agent_command_marker_heavy";

            JsonObject lightAgent = FindObjectById(performers, "mass_navigation_agent_light");
            JsonObject heavyAgent = FindObjectById(performers, "mass_navigation_agent_heavy");
            AssertPerformerDoesNotBindMeshAsset(lightAgent, "mass_navigation_agent_light", "mass_navigation.command.marker");
            AssertPerformerDoesNotBindMeshAsset(heavyAgent, "mass_navigation_agent_heavy", "mass_navigation.command.marker");
            AssertSelectionMarkerLifecycleRules(lightAgent, "mass_navigation_agent_light", lightMarkerId);
            AssertSelectionMarkerLifecycleRules(heavyAgent, "mass_navigation_agent_heavy", heavyMarkerId);
            AssertSelectionMarkerDefinition(
                FindObjectById(performers, lightMarkerId),
                lightMarkerId,
                expectedScaleX: 0.55f,
                expectedScaleY: 0.05f,
                expectedScaleZ: 0.55f,
                expectedOffsetY: 0.035f);
            JsonObject heavyMarker = FindObjectById(performers, heavyMarkerId);
            Assert.That(RequireString(heavyMarker, "extends"), Is.EqualTo(lightMarkerId));
            AssertSelectionMarkerDefinition(
                heavyMarker,
                heavyMarkerId,
                expectedScaleX: 0.78f,
                expectedScaleY: 0.06f,
                expectedScaleZ: 0.78f,
                expectedOffsetY: 0.04f);

            Assert.That(
                File.Exists(Path.Combine(modRoot, "Systems", "MassNavigationSelectionPerformerSyncSystem.cs")),
                Is.False,
                "MassNavigation must not own command marker lifecycle in a private presentation sync system.");
        }

        [Test]
        public void CoreHudDefinitions_AreDefinitionsOnly_NotGlobalAttributeWildcards()
        {
            string repoRoot = FindRepoRoot();
            JsonArray performers = ReadArray(Path.Combine(repoRoot, "mods", "LudotsCoreMod", "assets", "Presentation", "performers.json"));

            AssertDefinitionHasNoRules(FindObjectById(performers, "entity_health_bar"), "entity_health_bar");
            AssertDefinitionHasNoRules(FindObjectById(performers, "entity_world_text"), "entity_world_text");
        }

        [Test]
        public void AgentGasAuthoring_UsesRegisteredEffectGraphOpsAndHealthConstraint()
        {
            string modRoot = MassNavigationModRoot();
            JsonObject constraints = ReadObject(Path.Combine(modRoot, "assets", "GAS", "attribute_constraints.json"));
            JsonArray effects = ReadArray(Path.Combine(modRoot, "assets", "GAS", "effects.json"));
            JsonArray graphs = ReadArray(Path.Combine(modRoot, "assets", "GAS", "graphs.json"));

            JsonObject healthConstraint = constraints[AgentHealthAttributeName]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigation agents must author a Health attribute constraint.");
            Assert.That(healthConstraint["clampToBase"]?.GetValue<bool>(), Is.True);
            Assert.That(healthConstraint["min"]?.GetValue<float>(), Is.EqualTo(0f));

            JsonObject effect = FindObjectById(effects, AgentHealthDriftEffectId);
            Assert.That(effect["presetType"]?.GetValue<string>(), Is.EqualTo("Buff"));
            Assert.That(effect["lifetime"]?.GetValue<string>(), Is.EqualTo("Infinite"));
            Assert.That(effect["duration"]?["periodTicks"]?.GetValue<int>(), Is.EqualTo(60));
            Assert.That(
                effect["phaseGraphs"]?["OnApply"]?["post"]?.GetValue<string>(),
                Is.EqualTo(AgentHealthDriftGraphId));
            Assert.That(
                effect["phaseGraphs"]?["OnPeriod"]?["post"]?.GetValue<string>(),
                Is.EqualTo(AgentHealthDriftGraphId));

            JsonObject graph = FindObjectById(graphs, AgentHealthDriftGraphId);
            JsonArray nodes = graph["nodes"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigation HealthDrift graph must declare nodes.");
            Assert.That(nodes.Count, Is.LessThanOrEqualTo(16), "Effect phase graph bindings have a fixed max-step budget.");

            var ops = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject node in nodes.Select(node => node?.AsObject() ?? throw new InvalidOperationException("Graph node must be an object.")))
            {
                string op = RequireString(node, "op");
                Assert.That(GraphNodeOpParser.TryParse(op, out _), Is.True, $"Graph op '{op}' must be registered in GraphNodeOp.");
                ops.Add(op);
            }

            Assert.That(ops, Does.Not.Contain("LoadAttributeBase"), "GAS Graph currently exposes AttributeBase to performer bindings, not as a graph op.");
            Assert.That(ops, Does.Contain("ModifyAttributeAdd"));
            JsonObject modifyNode = nodes
                .Select(node => node?.AsObject())
                .FirstOrDefault(node => string.Equals(node?["op"]?.GetValue<string>(), "ModifyAttributeAdd", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("HealthDrift graph must modify Health.");
            Assert.That(modifyNode["attribute"]?.GetValue<string>(), Is.EqualTo(AgentHealthAttributeName));
        }

        [Test]
        public void WorldBounds_AreAuthoredOnlyByBoardNotMassNavigationConfig()
        {
            string modRoot = MassNavigationModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonObject world = config["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world missing.");

            Assert.That(world.ContainsKey("worldWidthCm"), Is.False,
                "MassNavigation world width must come from the active board WorldSizeSpec, not duplicated in MassNavigation config.");
            Assert.That(world.ContainsKey("worldHeightCm"), Is.False,
                "MassNavigation world height must come from the active board WorldSizeSpec, not duplicated in MassNavigation config.");

            JsonObject map = ReadObject(Path.Combine(modRoot, "assets", "Maps", "mass_navigation.json"));
            JsonObject board = map["Boards"]?.AsArray()?.FirstOrDefault()?.AsObject()
                ?? throw new InvalidOperationException("MassNavigation map must author a primary board.");
            Assert.That(board["WidthInMacroTiles"]?.GetValue<int>(), Is.EqualTo(250));
            Assert.That(board["HeightInMacroTiles"]?.GetValue<int>(), Is.EqualTo(250));
            Assert.That(board["GridCellSizeCm"]?.GetValue<int>(), Is.EqualTo(100));
        }

        [Test]
        public void ScenarioContracts_AuthorLocalPlayerAndObstacles()
        {
            string modRoot = MassNavigationModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonObject world = config["world"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.world missing.");
            Assert.That(world.ContainsKey("obstacles"), Is.False,
                "MassNavigationConfig.world.obstacles[] is obsolete; obstacles must be authored as map/template ECS components.");

            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));
            JsonObject blockerTemplate = FindObjectById(templates, "mass_navigation_blocker");
            JsonObject blockerComponents = blockerTemplate["components"]?.AsObject()
                ?? throw new InvalidOperationException("mass_navigation_blocker must author components.");
            AssertObstacleAuthoring(blockerComponents, "mass_navigation_blocker template");

            JsonObject localPlayerTemplate = FindObjectById(templates, "mass_navigation_local_player");
            JsonObject localPlayerComponents = localPlayerTemplate["components"]?.AsObject()
                ?? throw new InvalidOperationException("mass_navigation_local_player must author components.");
            Assert.That(localPlayerComponents.ContainsKey("PlayerOwner"), Is.True,
                "MassNavigation must not create a hidden PlayerOwner at runtime.");
            Assert.That(localPlayerComponents.ContainsKey("CommandSourceDragState"), Is.True,
                "MassNavigation local command-source owner must author CommandSourceDragState via the formal component registry.");

            JsonObject map = ReadObject(Path.Combine(modRoot, "assets", "Maps", "mass_navigation.json"));
            JsonArray entities = map["Entities"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigation map must author entities.");
            Assert.That(
                entities.Select(node => node?["Template"]?.GetValue<string>()).ToArray(),
                Does.Contain("mass_navigation_local_player"),
                "The local player must be a map-authored entity, not a runtime fallback.");
            JsonObject[] blockerEntities = entities
                .Select(node => node?.AsObject() ?? throw new InvalidOperationException("MassNavigation map entities must be objects."))
                .Where(entity => string.Equals(entity["Template"]?.GetValue<string>(), "mass_navigation_blocker", StringComparison.Ordinal))
                .ToArray();
            Assert.That(blockerEntities.Length, Is.GreaterThan(0),
                "MassNavigation map must author obstacle entities through the shared manifestation obstacle components.");
            foreach (JsonObject entity in blockerEntities)
            {
                JsonObject overrides = entity["Overrides"]?.AsObject()
                    ?? throw new InvalidOperationException("MassNavigation blocker map entity must author component overrides.");
                AssertObstacleAuthoring(overrides, entity["InstanceId"]?.GetValue<string>() ?? "mass_navigation_blocker entity");
            }
        }

        [Test]
        public void ScenarioRuntimeCapacity_CoversAuthoredScenarioOrderMembers()
        {
            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject scenario = config["scenario"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario missing.");
            JsonArray teams = scenario["teams"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario.teams missing.");
            int authoredAgentCount = checked(teams.Count * (scenario["agentsPerTeam"]?.GetValue<int>()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenario.agentsPerTeam missing.")));
            JsonObject scenarioRuntime = config["scenarioRuntime"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenarioRuntime missing.");
            JsonObject runtimeCapacity = scenarioRuntime["runtimeCapacity"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.scenarioRuntime.runtimeCapacity missing.");

            Assert.That(
                runtimeCapacity["groupMemberCapacity"]?.GetValue<int>(),
                Is.EqualTo(authoredAgentCount));
            Assert.That(
                runtimeCapacity["orderIngestionMemberCapacity"]?.GetValue<int>(),
                Is.EqualTo(authoredAgentCount));
        }

        [Test]
        public void MassNavigationFlowRuntime_IsConfigDrivenForCadenceAndAgentProfiles()
        {
            string modRoot = MassNavigationModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            JsonObject cadence = config["cadence"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.cadence missing.");
            Assert.That(cadence["simulationHz"]?.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(cadence["targetUpdateHz"]?.GetValue<int>(), Is.GreaterThanOrEqualTo(0));
            Assert.That(cadence["flowStepHz"]?.GetValue<int>(), Is.GreaterThanOrEqualTo(0));
            Assert.That(cadence["flowCrowdStampHz"]?.GetValue<int>(), Is.GreaterThanOrEqualTo(0));
            Assert.That(cadence["flowObstacleStampHz"]?.GetValue<int>(), Is.GreaterThanOrEqualTo(0));
            Assert.That(cadence["hardResolveHz"]?.GetValue<int>(), Is.GreaterThanOrEqualTo(0));
            Assert.That(cadence["entitySyncHz"]?.GetValue<int>(), Is.GreaterThanOrEqualTo(0));
            Assert.That(cadence["maxStepsPerFixedTick"]?.GetValue<int>(), Is.GreaterThan(0));

            JsonObject profiles = config["agentProfiles"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.agentProfiles missing.");
            string defaultProfileId = profiles["defaultProfileId"]?.GetValue<string>() ?? string.Empty;
            Assert.That(defaultProfileId, Is.Not.Empty);
            JsonArray profileEntries = profiles["profiles"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigationConfig.agentProfiles.profiles missing.");
            Assert.That(profileEntries.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(
                profileEntries.Select(node => node?["id"]?.GetValue<string>()).ToArray(),
                Does.Contain(defaultProfileId));

            JsonObject heavy = profileEntries
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["heavy"]?.GetValue<bool>() == true)
                ?? throw new InvalidOperationException("MassNavigation agentProfiles must author at least one heavy profile.");
            Assert.That(heavy["everyNth"]?.GetValue<int>(), Is.GreaterThan(0),
                "Heavy distribution is an authored profile rule, not a solver hardcode.");
            Assert.That(heavy["visualScale"]?.GetValue<float>(), Is.GreaterThan(0f));
            Assert.That(heavy.ContainsKey("navMass"), Is.False,
                "MassNavigation execution profiles must not own geometry or solver mass.");
            Assert.That(heavy.ContainsKey("bodyRadiusCm"), Is.False,
                "MassNavigation execution profiles must not own geometry or solver radius.");

            JsonArray geometryProfiles = ReadArray(Path.Combine(FindRepoRoot(), "assets", "Configs", "Navigation", "agent_profiles.json"));
            JsonObject heavyGeometry = FindObjectById(geometryProfiles, "heavy");
            Assert.That(heavyGeometry["mass"]?.GetValue<float>(), Is.GreaterThan(1f));
            Assert.That(heavyGeometry["radiusCm"]?.GetValue<float>(), Is.GreaterThan(0f));

            JsonObject avoidance = config["avoidance"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.avoidance missing.");
            Assert.That(avoidance.ContainsKey("lightNavMass"), Is.False,
                "Agent mass must be owned only by Navigation/agent_profiles.json, not duplicated in avoidance.");
            Assert.That(avoidance.ContainsKey("heavyNavMass"), Is.False,
                "Agent mass must be owned only by Navigation/agent_profiles.json, not duplicated in avoidance.");
            Assert.That(avoidance.ContainsKey("lightVisualScale"), Is.False,
                "Agent visualScale must be owned only by agentProfiles, not duplicated in avoidance.");
            Assert.That(avoidance.ContainsKey("heavyVisualScale"), Is.False,
                "Agent visualScale must be owned only by agentProfiles, not duplicated in avoidance.");
            Assert.That(avoidance["dominantMassRatio"]?.GetValue<float>(), Is.GreaterThan(0f));

            JsonObject obstacle = config["semantics"]?["obstacle"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.semantics.obstacle missing.");
            Assert.That(obstacle.ContainsKey("agentBodyRadiusCm"), Is.False,
                "Obstacle hard-block radius must use Navigation/agent_profiles.json radiusCm, not a global obstacle body radius.");

            JsonObject legacyAvoidanceConfig = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            legacyAvoidanceConfig["avoidance"]!["lightNavMass"] = 1.0f;
            JsonException legacyAvoidanceField = Assert.Throws<JsonException>(
                () => MassNavigationConfig.Load(legacyAvoidanceConfig))!;
            Assert.That(legacyAvoidanceField.Message, Does.Contain("lightNavMass"));

            JsonObject legacyObstacleConfig = ReadObject(Path.Combine(modRoot, "assets", "MassNavigationConfig.json"));
            legacyObstacleConfig["semantics"]!["obstacle"]!["agentBodyRadiusCm"] = 20.0f;
            JsonException legacyObstacleField = Assert.Throws<JsonException>(
                () => MassNavigationConfig.Load(legacyObstacleConfig))!;
            Assert.That(legacyObstacleField.Message, Does.Contain("agentBodyRadiusCm"));
        }

        [Test]
        public void MassNavigationAssets_UseConfigMergeAndFormalOrderContracts()
        {
            string repoRoot = FindRepoRoot();
            string modRoot = MassNavigationModRoot();

            JsonArray catalog = ReadArray(Path.Combine(modRoot, "assets", "Configs", "config_catalog.json"));
            JsonObject massNavigationConfigSource = catalog
                .Select(node => node?.AsObject())
                .FirstOrDefault(source => string.Equals(source?["Path"]?.GetValue<string>(), "MassNavigationConfig.json", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("MassNavigationConfig.json must be registered in config catalog.");
            Assert.That(massNavigationConfigSource["Policy"]?.GetValue<string>(), Is.EqualTo("DeepObject"));

            JsonObject orderTypesRoot = ReadObject(Path.Combine(modRoot, "assets", "GAS", "order_types.json"));
            JsonObject orderBlackboardKeys = orderTypesRoot["orderBlackboardKeys"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigation orderBlackboardKeys must be authored.");
            Assert.That(orderBlackboardKeys.Count, Is.Zero);

            JsonObject orderTypes = orderTypesRoot["orderTypes"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigation orderTypes must be authored.");
            JsonObject moveOrder = orderTypes["massNavigationMove"]?.AsObject()
                ?? throw new InvalidOperationException("massNavigationMove order type must be authored.");
            Assert.That(moveOrder["intArg0BlackboardKey"]?.GetValue<string>(), Is.EqualTo("none"));
            Assert.That(moveOrder["spatialBlackboardKey"]?.GetValue<string>(), Is.EqualTo("none"));
            Assert.That(moveOrder["entityBlackboardKey"]?.GetValue<string>(), Is.EqualTo("none"));

            JsonObject orderRules = orderTypesRoot["orderRules"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigation orderRules must be authored.");
            JsonObject moveRule = orderRules["massNavigationMove"]?.AsObject()
                ?? throw new InvalidOperationException("massNavigationMove order rule must be authored.");
            Assert.That(moveRule.ContainsKey("interruptsActiveOrderTypeIds"), Is.False);
            Assert.That(moveRule["interruptsActiveOrderTypeKeys"]?.AsArray()?.Select(node => node?.GetValue<string>()).ToArray(),
                Does.Contain("attackTarget"));

            JsonObject game = ReadObject(Path.Combine(modRoot, "assets", "game.json"));
            string[] previewOrderKeys = game["commandSource"]?["movePathPreviewOrderTypeKeys"]?.AsArray()
                ?.Select(node => node?.GetValue<string>() ?? string.Empty)
                .ToArray()
                ?? throw new InvalidOperationException("MassNavigation game.json must author move path preview keys.");
            Assert.That(previewOrderKeys, Is.EqualTo(new[] { "massNavigationMove" }));

            Assert.That(Directory.Exists(Path.Combine(modRoot, "UI")), Is.False);
            Assert.That(
                File.Exists(Path.Combine(modRoot, "Systems", "MassNavigationSelectionPerformerSyncSystem.cs")),
                Is.False,
                "Command marker lifecycle is now generic entity-collection presentation events plus performer rules.");
            Assert.That(File.Exists(Path.Combine(modRoot, "Runtime", "MassNavigationComponentAuthoring.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(repoRoot, "mods", "CoreInputMod", "Systems", "MassNavigationSelectionPerformerSyncSystem.cs")), Is.False);
        }

        [Test]
        public void OrderBlackboardKeys_MassNavigationMoveDoesNotRegisterFormationPayload()
        {
            string repoRoot = FindRepoRoot();
            string modRoot = MassNavigationModRoot();
            OrderBlackboardKeyRegistry.ResetToBuiltins();
            Assert.That(OrderBlackboardKeyRegistry.TryGetId("MassNavigation.FormationMode", out _), Is.False);

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(repoRoot, "assets"));
            vfs.Mount("LudotsCoreMod", Path.Combine(repoRoot, "mods", "LudotsCoreMod"));
            vfs.Mount("MassNavigationMod", modRoot);
            var modLoader = new Ludots.Core.Modding.ModLoader(
                vfs,
                new Ludots.Core.Scripting.FunctionRegistry(),
                new Ludots.Core.Scripting.TriggerManager());
            modLoader.LoadedModIds.Add("LudotsCoreMod");
            modLoader.LoadedModIds.Add("MassNavigationMod");
            var pipeline = new Ludots.Core.Config.ConfigPipeline(vfs, modLoader);
            var catalog = Ludots.Core.Config.ConfigCatalogLoader.Load(pipeline);
            var orderTypes = new OrderTypeRegistry();
            var orderRules = new OrderRuleRegistry();

            new OrderTypeConfigLoader(pipeline).Load(orderTypes, orderRules, catalog);

            Assert.That(OrderBlackboardKeyRegistry.TryGetId("MassNavigation.FormationMode", out _), Is.False);
            Assert.That(orderTypes.Get(orderTypes.GetId("massNavigationMove")).IntArg0BlackboardKey, Is.EqualTo(-1));

            OrderBlackboardKeyRegistry.ResetToBuiltins();
        }

        [Test]
        public void OrderBlackboardKeys_BuiltinKeysCannotBeRedeclaredInConfig()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_OrderBlackboardKeys", Guid.NewGuid().ToString("N"));
            string gasDir = Path.Combine(root, "assets", "GAS");
            Directory.CreateDirectory(gasDir);
            File.WriteAllText(
                Path.Combine(gasDir, "order_types.json"),
                """
                {
                  "orderBlackboardKeys": {
                    "Cast.SlotIndex": true
                  },
                  "orderTypes": {
                    "testOrder": {
                      "orderTypeId": "testOrder",
                      "label": "Test",
                      "maxQueueSize": 1,
                      "sameTypePolicy": "Replace",
                      "queueFullPolicy": "RejectNew",
                      "priority": 1,
                      "bufferWindowMs": 0,
                      "pendingBufferWindowMs": 0,
                      "canInterruptSelf": false,
                      "queuedModeMaxSize": 1,
                      "allowQueuedMode": false,
                      "clearQueueOnActivate": false,
                      "spatialBlackboardKey": "none",
                      "entityBlackboardKey": "none",
                      "intArg0BlackboardKey": "Cast.SlotIndex",
                      "validationGraph": "none"
                    }
                  },
                  "orderRules": {}
                }
                """);

            try
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                var vfs = new Ludots.Core.Modding.VirtualFileSystem();
                vfs.Mount("TestMod", root);
                var modLoader = new Ludots.Core.Modding.ModLoader(
                    vfs,
                    new Ludots.Core.Scripting.FunctionRegistry(),
                    new Ludots.Core.Scripting.TriggerManager());
                modLoader.LoadedModIds.Add("TestMod");
                var pipeline = new Ludots.Core.Config.ConfigPipeline(vfs, modLoader);
                var catalog = new Ludots.Core.Config.ConfigCatalog();
                catalog.Add(new Ludots.Core.Config.ConfigCatalogEntry("GAS/order_types.json", Ludots.Core.Config.ConfigMergePolicy.DeepObject));

                var ex = Assert.Throws<InvalidOperationException>(
                    () => new OrderTypeConfigLoader(pipeline).Load(new OrderTypeRegistry(), new OrderRuleRegistry(), catalog));

                Assert.That(ex!.Message, Does.Contain("LUDOTS_GAS_ORDER_BLACKBOARD_BUILTIN_REDECLARED"));
            }
            finally
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void ConfigLoader_UsesConfigPipelineDeepObjectMerge()
        {
            string modRoot = MassNavigationModRoot();
            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("MassNavigationMod", modRoot);
            var modLoader = new Ludots.Core.Modding.ModLoader(
                vfs,
                new Ludots.Core.Scripting.FunctionRegistry(),
                new Ludots.Core.Scripting.TriggerManager());
            modLoader.LoadedModIds.Add("MassNavigationMod");
            var pipeline = new Ludots.Core.Config.ConfigPipeline(vfs, modLoader);
            var catalog = Ludots.Core.Config.ConfigCatalogLoader.Load(pipeline);
            Assert.That(catalog.TryGet("MassNavigationConfig.json", out Ludots.Core.Config.ConfigCatalogEntry entry), Is.True);
            Assert.That(entry.MergePolicy, Is.EqualTo(Ludots.Core.Config.ConfigMergePolicy.DeepObject));
            var report = new Ludots.Core.Config.ConfigConflictReport();
            var config = new MassNavigationConfigLoader(pipeline).Load(catalog, report);

            Assert.That(config.MapId, Is.EqualTo("mass_navigation"));
            Assert.That(config.World, Is.Not.Null);
            Assert.That(config.World!.SolverWindowWidthCm, Is.EqualTo(10_000));
            Assert.That(config.Presentation.ResolveAgentTemplateId(1, heavy: false), Is.EqualTo("mass_navigation_agent_azure_light"));
        }

        [Test]
        public void LargeWorldMap_UsesVisualHeightmapCameraProfile()
        {
            string modRoot = MassNavigationModRoot();
            JsonObject map = ReadObject(Path.Combine(modRoot, "assets", "Maps", "mass_navigation.json"));
            JsonObject defaultCamera = map["DefaultCamera"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigation map must declare DefaultCamera.");

            Assert.That(RequireString(defaultCamera, "VirtualCameraId"), Is.EqualTo(LargeWorldCameraId));
            Assert.That(defaultCamera["TargetXCm"]?.GetValue<float>(), Is.EqualTo(0f));
            Assert.That(defaultCamera["TargetYCm"]?.GetValue<float>(), Is.EqualTo(0f));

            JsonArray catalog = ReadArray(Path.Combine(modRoot, "assets", "Configs", "config_catalog.json"));
            Assert.That(
                catalog.Select(node => node?.AsObject()).Any(entry =>
                    string.Equals(entry?["Path"]?.GetValue<string>(), "Camera/virtual_cameras.json", StringComparison.Ordinal) &&
                    string.Equals(entry?["Policy"]?.GetValue<string>(), "ArrayById", StringComparison.Ordinal) &&
                    string.Equals(entry?["IdField"]?.GetValue<string>(), "id", StringComparison.Ordinal)),
                Is.True,
                "MassNavigation camera profiles must be registered through ConfigPipeline, not by host-loop defaults.");

            JsonArray cameras = ReadArray(Path.Combine(modRoot, "assets", "Configs", "Camera", "virtual_cameras.json"));
            JsonObject camera = FindObjectById(cameras, LargeWorldCameraId);
            Assert.That(camera["targetHeightMode"]?.GetValue<string>(), Is.EqualTo("VisualHeightmap"));
            Assert.That(camera["targetHeightLayerIndex"]?.GetValue<int>(), Is.EqualTo(0));

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("MassNavigationMod", modRoot);
            var modLoader = new Ludots.Core.Modding.ModLoader(
                vfs,
                new Ludots.Core.Scripting.FunctionRegistry(),
                new Ludots.Core.Scripting.TriggerManager());
            modLoader.LoadedModIds.Add("MassNavigationMod");
            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog loadedCatalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new VirtualCameraRegistry();
            new VirtualCameraDefinitionLoader(pipeline, registry).Load(loadedCatalog, new ConfigConflictReport());

            Assert.That(registry.TryGet(LargeWorldCameraId, out VirtualCameraDefinition? definition), Is.True);
            Assert.That(definition!.TargetHeightMode, Is.EqualTo(VirtualCameraTargetHeightMode.VisualHeightmap));
            Assert.That(definition.TargetHeightLayerIndex, Is.EqualTo(0));
        }

        [Test]
        public void PresentationTeams_AreRequiredOnlyForAutoSpawnScenarios()
        {
            JsonObject missingAutoSpawnTemplateConfig = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonObject autoSpawnPresentation = missingAutoSpawnTemplateConfig["presentation"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.presentation missing.");
            autoSpawnPresentation.Remove("blockerTemplateId");

            InvalidOperationException missingTemplate = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(missingAutoSpawnTemplateConfig))!;
            Assert.That(missingTemplate.Message, Does.Contain("blockerTemplateId"));

            JsonObject config = ReadObject(Path.Combine(MassNavigationModRoot(), "assets", "MassNavigationConfig.json"));
            JsonArray teams = config["presentation"]?["teams"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigationConfig.presentation.teams missing.");
            teams.Clear();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(ex.Message, Does.Contain("presentation team style count must match scenario teams"));

            config["scenarioRuntime"]!["autoSpawnConfiguredScenario"] = false;
            config["scenario"]!["agentsPerTeam"] = 0;
            JsonObject presentation = config["presentation"]?.AsObject()
                ?? throw new InvalidOperationException("MassNavigationConfig.presentation missing.");
            presentation.Remove("blockerPerformerId");
            presentation.Remove("hotspotPerformerId");
            presentation.Remove("hotspotTemplateId");
            MassNavigationConfig formationOwnedConfig =
                MassNavigationConfig.Load(config);

            Assert.That(formationOwnedConfig.ScenarioRuntime.AutoSpawnConfiguredScenario, Is.False);
            Assert.That(formationOwnedConfig.Presentation.Teams, Is.Empty);
            Assert.That(formationOwnedConfig.Presentation.BlockerTemplateId, Is.EqualTo("mass_navigation_blocker"));
            Assert.That(formationOwnedConfig.Presentation.HotspotTemplateId, Is.EqualTo(string.Empty));

            presentation.Remove("blockerTemplateId");
            InvalidOperationException missingExternalBlockerTemplate = Assert.Throws<InvalidOperationException>(() => MassNavigationConfig.Load(config))!;
            Assert.That(missingExternalBlockerTemplate.Message, Does.Contain("blockerTemplateId"));
        }

        private static void AssertAgentBodyUsesMassNavigationSoldier(JsonObject definition, string definitionId, float expectedScale)
        {
            JsonArray behaviors = definition["behaviors"]?.AsArray()
                ?? throw new InvalidOperationException($"Performer '{definitionId}' must declare behaviors.");
            JsonObject body = behaviors
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["slot"]?.GetValue<string>() == "body")
                ?? throw new InvalidOperationException($"Performer '{definitionId}' must declare semantic body slot.");
            JsonObject assetBinding = body["assetBinding"]?.AsObject()
                ?? throw new InvalidOperationException($"Performer '{definitionId}' body slot must be AssetBinding.");

            Assert.That(assetBinding["assetKind"]?.GetValue<string>(), Is.EqualTo("SkinnedMesh"));
            Assert.That(assetBinding["assetId"]?.GetValue<string>(), Is.EqualTo("mass_navigation.agent.soldier"));
            Assert.That(assetBinding["renderPath"]?.GetValue<string>(), Is.EqualTo("GpuSkinnedInstance"));
            Assert.That(assetBinding["mobility"]?.GetValue<string>(), Is.EqualTo("Movable"));
            AssertVector3(assetBinding["localScale"]?.AsArray(), expectedScale, $"Performer '{definitionId}' MassNavigation soldier scale");

            JsonObject animator = behaviors
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["kind"]?.GetValue<string>() == "Animator")?["animator"]?.AsObject()
                ?? throw new InvalidOperationException($"Performer '{definitionId}' must declare MassNavigation animator behavior.");
            Assert.That(animator["animatorControllerId"]?.GetValue<string>(), Is.EqualTo("mass_navigation.agent.locomotion"));
            Assert.That(animator["animationProfileId"]?.GetValue<string>(), Is.EqualTo("mass_navigation.agent.profile"));
            Assert.That(animator["speedParamKey"]?.GetValue<string>(), Is.EqualTo(LocomotionSpeedParamKey));
            Assert.That(animator["stateParamKey"]?.GetValue<string>(), Is.EqualTo("none"));
        }

        private static void AssertObstacleAuthoring(JsonObject components, string owner)
        {
            JsonObject obstacle = components["ManifestationObstacleIntent2D"]?.AsObject()
                ?? throw new InvalidOperationException($"{owner} must author ManifestationObstacleIntent2D.");
            Assert.That(RequireString(obstacle, "shape"), Is.EqualTo("Circle"));
            Assert.That(obstacle["sinkNavigationObstacle"]?.GetValue<bool>(), Is.True,
                $"{owner} must project into the navigation obstacle sink.");
            Assert.That(obstacle["sinkPhysicsCollider"]?.GetValue<bool>(), Is.False,
                $"{owner} MassNavigationFlow blocker authoring must not implicitly duplicate a physics collider.");
            Assert.That(obstacle["radiusCm"]?.GetValue<float>(), Is.GreaterThan(0f));
            Assert.That(obstacle["navRadiusCm"]?.GetValue<float>(), Is.GreaterThan(0f));
        }

        private static void AssertAgentTemplateAuthorsHealth(JsonObject template, float expectedBase, float expectedCurrent)
        {
            string templateId = RequireString(template, "id");
            Assert.That(template["onSpawnEffect"]?.GetValue<string>(), Is.EqualTo(AgentHealthDriftEffectId),
                $"Template '{templateId}' should use the configured GAS on-spawn effect path.");

            JsonObject attributes = template["components"]?["AttributeBuffer"]?.AsObject()
                ?? throw new InvalidOperationException($"Template '{templateId}' must author AttributeBuffer.");
            Assert.That(
                attributes["base"]?[AgentHealthAttributeName]?.GetValue<float>(),
                Is.EqualTo(expectedBase),
                $"Template '{templateId}' must author Health base.");
            Assert.That(
                attributes["current"]?[AgentHealthAttributeName]?.GetValue<float>(),
                Is.EqualTo(expectedCurrent),
                $"Template '{templateId}' must author Health current.");
        }

        private static void AssertDefinitionHasChild(JsonObject definition, string definitionId, string childDefinitionId)
        {
            JsonArray children = definition["children"]?.AsArray()
                ?? throw new InvalidOperationException($"Performer '{definitionId}' must declare children.");
            Assert.That(
                children.Select(node => node?["definitionId"]?.GetValue<string>()).ToArray(),
                Does.Contain(childDefinitionId),
                $"Performer '{definitionId}' must attach '{childDefinitionId}' through performer children.");
        }

        private static void AssertHudDefinitionUsesHealthRatio(JsonObject definition, string definitionId, float expectedWidth, float expectedHeight)
        {
            string materialParamKey = AssertHudWorldHudBinding(definition, definitionId, expectedWidth, expectedHeight);
            JsonArray bindings = definition["bindings"]?.AsArray()
                ?? throw new InvalidOperationException($"HUD performer '{definitionId}' must declare param bindings.");
            JsonObject binding = bindings
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["paramKey"]?.GetValue<string>() == materialParamKey)
                ?? throw new InvalidOperationException($"HUD performer '{definitionId}' must bind its material param.");

            Assert.That(binding["source"]?.GetValue<string>(), Is.EqualTo("attributeRatio"));
            Assert.That(binding["attributeId"]?.GetValue<string>(), Is.EqualTo(AgentHealthAttributeName));
        }

        private static string AssertHudWorldHudBinding(JsonObject definition, string definitionId, float expectedWidth, float expectedHeight)
        {
            JsonArray behaviors = definition["behaviors"]?.AsArray()
                ?? throw new InvalidOperationException($"HUD performer '{definitionId}' must declare behaviors.");
            JsonObject assetBinding = behaviors
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["kind"]?.GetValue<string>() == "AssetBinding")?["assetBinding"]?.AsObject()
                ?? throw new InvalidOperationException($"HUD performer '{definitionId}' must declare an AssetBinding behavior.");

            Assert.That(assetBinding["assetKind"]?.GetValue<string>(), Is.EqualTo("WorldHud"));
            Assert.That(assetBinding["mobility"]?.GetValue<string>(), Is.EqualTo("Movable"));
            string materialParamKey = assetBinding["materialParamKey"]?.GetValue<string>() ?? string.Empty;
            Assert.That(materialParamKey, Is.EqualTo(HealthRatioParamKey), $"HUD performer '{definitionId}' must drive a semantic Health ratio param.");
            AssertVector3(assetBinding["localScale"]?.AsArray(), expectedWidth, expectedHeight, 1f, $"HUD performer '{definitionId}' scale");
            return materialParamKey;
        }

        private static void AssertWorldTextDefinitionUsesHealthCurrentOverBase(JsonObject definition, string definitionId, int expectedFontSize)
        {
            (string currentParamKey, string baseParamKey) = AssertWorldTextBinding(definition, definitionId, expectedFontSize);
            JsonArray bindings = definition["bindings"]?.AsArray()
                ?? throw new InvalidOperationException($"Text performer '{definitionId}' must declare param bindings.");

            JsonObject currentBinding = bindings
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["paramKey"]?.GetValue<string>() == currentParamKey)
                ?? throw new InvalidOperationException($"Text performer '{definitionId}' must bind its current Health param.");
            Assert.That(currentBinding["source"]?.GetValue<string>(), Is.EqualTo("attribute"));
            Assert.That(currentBinding["attributeId"]?.GetValue<string>(), Is.EqualTo(AgentHealthAttributeName));

            JsonObject baseBinding = bindings
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["paramKey"]?.GetValue<string>() == baseParamKey)
                ?? throw new InvalidOperationException($"Text performer '{definitionId}' must bind its base Health param.");
            Assert.That(baseBinding["source"]?.GetValue<string>(), Is.EqualTo("attributeBase"));
            Assert.That(baseBinding["attributeId"]?.GetValue<string>(), Is.EqualTo(AgentHealthAttributeName));
        }

        private static (string CurrentParamKey, string BaseParamKey) AssertWorldTextBinding(JsonObject definition, string definitionId, int expectedFontSize)
        {
            Assert.That(definition["worldTextMode"]?.GetValue<string>(), Is.EqualTo("AttributeCurrentOverBase"));
            Assert.That(definition["defaultFontSize"]?.GetValue<int>(), Is.EqualTo(expectedFontSize));

            JsonArray behaviors = definition["behaviors"]?.AsArray()
                ?? throw new InvalidOperationException($"Text performer '{definitionId}' must declare behaviors.");
            JsonObject assetBinding = behaviors
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["kind"]?.GetValue<string>() == "AssetBinding")?["assetBinding"]?.AsObject()
                ?? throw new InvalidOperationException($"Text performer '{definitionId}' must declare an AssetBinding behavior.");

            Assert.That(assetBinding["assetKind"]?.GetValue<string>(), Is.EqualTo("WorldText"));
            Assert.That(assetBinding["assetId"]?.GetValue<string>(), Is.EqualTo("hud.attribute.current_over_base"));
            Assert.That(assetBinding["mobility"]?.GetValue<string>(), Is.EqualTo("Movable"));
            string currentParamKey = assetBinding["scaleParamKey"]?.GetValue<string>() ?? string.Empty;
            string baseParamKey = assetBinding["materialParamKey"]?.GetValue<string>() ?? string.Empty;
            Assert.That(currentParamKey, Is.EqualTo(HealthCurrentParamKey), $"Text performer '{definitionId}' must drive current value from a semantic param.");
            Assert.That(baseParamKey, Is.EqualTo(HealthBaseParamKey), $"Text performer '{definitionId}' must drive base value from a semantic param.");
            return (currentParamKey, baseParamKey);
        }

        private static void AssertPerformerDoesNotBindMeshAsset(JsonObject definition, string definitionId, string forbiddenAssetId)
        {
            JsonArray behaviors = definition["behaviors"]?.AsArray()
                ?? throw new InvalidOperationException($"Performer '{definitionId}' must declare behaviors.");
            foreach (JsonObject behavior in behaviors.Select(node => node?.AsObject() ?? throw new InvalidOperationException($"Performer '{definitionId}' behavior must be an object.")))
            {
                JsonObject? assetBinding = behavior["assetBinding"]?.AsObject();
                string assetId = assetBinding?["assetId"]?.GetValue<string>() ?? string.Empty;
                Assert.That(assetId, Is.Not.EqualTo(forbiddenAssetId),
                    $"Performer '{definitionId}' must not carry always-present hidden asset '{forbiddenAssetId}'.");
            }
        }

        private static void AssertSelectionMarkerDefinition(
            JsonObject definition,
            string definitionId,
            float expectedScaleX,
            float expectedScaleY,
            float expectedScaleZ,
            float expectedOffsetY)
        {
            JsonArray behaviors = definition["behaviors"]?.AsArray()
                ?? throw new InvalidOperationException($"Command marker '{definitionId}' must declare behaviors.");

            JsonObject assetBinding = behaviors
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["kind"]?.GetValue<string>() == "AssetBinding")?["assetBinding"]?.AsObject()
                ?? throw new InvalidOperationException($"Command marker '{definitionId}' must declare an AssetBinding behavior.");
            Assert.That(assetBinding["assetKind"]?.GetValue<string>(), Is.EqualTo("Mesh"));
            Assert.That(assetBinding["assetId"]?.GetValue<string>(), Is.EqualTo("mass_navigation.command.marker"));
            Assert.That(assetBinding["renderPath"]?.GetValue<string>(), Is.EqualTo("InstancedStaticMesh"));
            Assert.That(assetBinding["mobility"]?.GetValue<string>(), Is.EqualTo("Movable"));
            Assert.That(assetBinding.ContainsKey("localOffset"), Is.False,
                $"Command marker '{definitionId}' position must come from parent Attachment, not duplicated mesh localOffset.");
            Assert.That(assetBinding.ContainsKey("visibilityParamKey"), Is.False,
                $"Command marker '{definitionId}' visibility is controlled by scoped create/destroy, not a root visibility param.");
            AssertVector3(assetBinding["localScale"]?.AsArray(), expectedScaleX, expectedScaleY, expectedScaleZ, $"Command marker '{definitionId}' scale");

            JsonObject attachment = behaviors
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => obj?["kind"]?.GetValue<string>() == "Attachment")?["attachment"]?.AsObject()
                ?? throw new InvalidOperationException($"Command marker '{definitionId}' must follow the agent root through an Attachment behavior.");
            Assert.That(attachment["target"]?.GetValue<string>(), Is.EqualTo("Parent"));
            AssertVector3(attachment["offset"]?.AsArray(), 0f, expectedOffsetY, 0f, $"Command marker '{definitionId}' attachment offset");
            Assert.That(attachment["inheritScale"]?.GetValue<bool>(), Is.False);
        }

        private static void AssertSelectionMarkerLifecycleRules(JsonObject definition, string definitionId, string markerDefinitionId)
        {
            JsonArray rules = definition["rules"]?.AsArray()
                ?? throw new InvalidOperationException($"Performer '{definitionId}' must declare command marker lifecycle rules.");

            AssertSelectionMarkerLifecycleRule(
                rules,
                definitionId,
                markerDefinitionId,
                eventKind: "EntityCollectionMemberAdded",
                commandKind: "CreatePerformer");
            AssertSelectionMarkerLifecycleRule(
                rules,
                definitionId,
                markerDefinitionId,
                eventKind: "EntityCollectionMemberRemoved",
                commandKind: "DestroyScopedPerformer");
        }

        private static void AssertSelectionMarkerLifecycleRule(
            JsonArray rules,
            string definitionId,
            string markerDefinitionId,
            string eventKind,
            string commandKind)
        {
            JsonObject? match = rules
                .Select(node => node?.AsObject())
                .FirstOrDefault(rule =>
                {
                    JsonObject? evt = rule?["event"]?.AsObject();
                    JsonObject? command = rule?["command"]?.AsObject();
                    return string.Equals(evt?["kind"]?.GetValue<string>(), eventKind, StringComparison.Ordinal) &&
                           string.Equals(evt?["key"]?.GetValue<string>(), EntityCollectionKeys.CommandSource, StringComparison.Ordinal) &&
                           string.Equals(command?["kind"]?.GetValue<string>(), commandKind, StringComparison.Ordinal) &&
                           string.Equals(command?["definitionId"]?.GetValue<string>(), markerDefinitionId, StringComparison.Ordinal);
                });

            Assert.That(match, Is.Not.Null,
                $"Performer '{definitionId}' must map {eventKind} for {EntityCollectionKeys.CommandSource} to {commandKind} '{markerDefinitionId}'.");
            JsonObject commandObj = match!["command"]!.AsObject();
            Assert.That(commandObj["scopeSource"]?.GetValue<string>(), Is.EqualTo("SourceStableId"),
                $"Performer '{definitionId}' must scope command marker lifecycle by source stable id.");
        }

        private static void AssertDefinitionHasNoRules(JsonObject definition, string definitionId)
        {
            Assert.That(definition.ContainsKey("rules"), Is.False,
                $"Core performer '{definitionId}' must not auto-spawn for every AttributeBuffer entity; gameplay mods should attach HUD explicitly.");
        }

        private static JsonObject FindObjectById(JsonArray array, string id)
        {
            return array
                .Select(node => node?.AsObject())
                .FirstOrDefault(obj => string.Equals(obj?["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"JSON object id '{id}' not found.");
        }

        private static void AssertVector3(JsonArray? values, float expected, string label)
        {
            Assert.That(values, Is.Not.Null, $"{label} must be authored.");
            Assert.That(values!.Count, Is.EqualTo(3), $"{label} must contain xyz.");
            for (int i = 0; i < values.Count; i++)
            {
                Assert.That(values[i]?.GetValue<float>(), Is.EqualTo(expected).Within(0.0001f), $"{label}[{i}]");
            }
        }

        private static void AssertVector3(JsonArray? values, float expectedX, float expectedY, float expectedZ, string label)
        {
            Assert.That(values, Is.Not.Null, $"{label} must be authored.");
            Assert.That(values!.Count, Is.EqualTo(3), $"{label} must contain xyz.");
            Assert.That(values[0]?.GetValue<float>(), Is.EqualTo(expectedX).Within(0.0001f), $"{label}[0]");
            Assert.That(values[1]?.GetValue<float>(), Is.EqualTo(expectedY).Within(0.0001f), $"{label}[1]");
            Assert.That(values[2]?.GetValue<float>(), Is.EqualTo(expectedZ).Within(0.0001f), $"{label}[2]");
        }

        private static IEnumerable<JsonObject> EnumeratePresentationTeams(JsonObject config)
        {
            JsonArray teams = config["presentation"]?["teams"]?.AsArray()
                ?? throw new InvalidOperationException("MassNavigationConfig.presentation.teams missing.");
            foreach (JsonNode? node in teams)
            {
                yield return node?.AsObject()
                    ?? throw new InvalidOperationException("MassNavigationConfig.presentation.teams entries must be objects.");
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

        private static string MassNavigationModRoot()
        {
            string repoRoot = FindRepoRoot();
            return Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod");
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
    }
}
