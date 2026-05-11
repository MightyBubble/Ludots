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
        public void TotalWarConfig_AuthorsFormationAnchorTemplateAndSquareCircleOutlines()
        {
            string modRoot = TotalWarModRoot();
            JsonObject config = ReadObject(Path.Combine(modRoot, "assets", "TotalWarShowcaseConfig.json"));
            string anchorTemplateId = RequireString(config, "formationAnchorTemplateId");
            Assert.That(anchorTemplateId, Is.EqualTo("mass_navigation_total_war_formation_anchor"));

            JsonArray templates = ReadArray(Path.Combine(modRoot, "assets", "Entities", "templates.json"));
            JsonObject anchorTemplate = FindObjectById(templates, anchorTemplateId);
            JsonObject components = anchorTemplate["components"]?.AsObject()
                ?? throw new InvalidOperationException("TotalWar formation anchor template must author components.");
            Assert.That(components.ContainsKey("Name"), Is.True);
            Assert.That(components.ContainsKey("WorldPositionCm"), Is.True);
            Assert.That(components.ContainsKey("VisualHeightmapSampleState"), Is.True,
                "Formation outline height must follow the visual-heightmap SSOT through the anchor visual transform.");
            Assert.That(components.ContainsKey("FacingDirection"), Is.True);
            Assert.That(components.ContainsKey("SpatialPartitionExcluded"), Is.True,
                "Formation anchors are presentation owners, not navigation obstacles or spatial-query participants.");

            JsonArray formations = config["formations"]?.AsArray()
                ?? throw new InvalidOperationException("TotalWar config must author formations.");
            Assert.That(formations.Count, Is.GreaterThan(0));
            string[] shapes = formations
                .Select(node => RequireString(node?.AsObject() ?? throw new InvalidOperationException("Formation must be an object."), "outline", "shape"))
                .ToArray();
            Assert.That(shapes, Does.Contain("Rectangle"));
            Assert.That(shapes, Does.Contain("Circle"));

            foreach (JsonObject formation in formations.Select(node => node?.AsObject() ?? throw new InvalidOperationException("Formation must be an object.")))
            {
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
            string[] required =
            {
                "mapId",
                "world",
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
                "viewResidency",
            };
            foreach (string property in required)
            {
                Assert.That(config.ContainsKey(property), Is.True, $"TotalWar MassNavigationConfig must author '{property}'.");
            }

            Assert.That(RequireString(config, "mapId"), Is.EqualTo("mass_navigation_total_war"));
            Assert.That(config["scenarioRuntime"]?["autoSpawnConfiguredScenario"]?.GetValue<bool>(), Is.False);
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
            Assert.That(source, Does.Contain("TotalWarSpawnReceiptBinding.ForFormationAnchor"));
            Assert.That(source, Does.Contain("RegisterSpawnedFormationAnchor(GameEngine engine"));
            Assert.That(source, Does.Contain("PresentationDestroyPending"));
            Assert.That(source, Does.Not.Contain("World.Create("),
                "TotalWar formation anchors must be spawned through the runtime template spawn path.");
            Assert.That(source, Does.Not.Contain("World.Destroy("),
                "TotalWar formation anchors must enter presentation destroy lifecycle instead of direct ECS destroy.");
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
                new MassNavigationAgentProfile { Heavy = false, NavMass = 1f, VisualScale = 0.2f },
                new PresentationStableId { Value = 1001 },
                new PresentationDestroyEventPublished(),
                new PresentationOwnerHasPerformerPayload { Count = 1, RootCount = 1, SingleRootPerformer = performerRoot });

            state.RegisterAgent(agent, controllable: true);
            state.DestroyTracked(world);

            Assert.That(world.IsAlive(agent), Is.True);
            Assert.That(world.Has<PresentationDestroyPending>(agent), Is.True);
            Assert.That(world.Has<PresentationDestroyEventPublished>(agent), Is.False);
            Assert.That(world.Has<PresentationOwnerHasPerformerPayload>(agent), Is.False);
            Assert.That(world.Has<MassNavigationAgentTag>(agent), Is.False);
            Assert.That(world.Has<MassNavigationControllable>(agent), Is.False);
            Assert.That(world.Has<MassNavigationAgentIndex>(agent), Is.False);
            Assert.That(world.Has<MassNavigationAgentProfile>(agent), Is.False);
            Assert.That(state.TotalAgents, Is.EqualTo(0));
            Assert.That(state.ControllableCount, Is.EqualTo(0));
        }

        [Test]
        public void MassNavigationAgentState_DestroyTrackedFailsWithoutPresentationStableId()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState();
            Entity agent = world.Create(new MassNavigationAgentTag());

            state.RegisterAgent(agent, controllable: true);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => state.DestroyTracked(world))!;
            Assert.That(ex.Message, Does.Contain("cannot be destroyed without PresentationStableId"));
            Assert.That(world.IsAlive(agent), Is.True);
        }

        [Test]
        public void MassNavigationAgentState_RegisterAgentAtIndexHandlesSparseResizeAndRejectsDuplicates()
        {
            var world = World.Create();
            var state = new MassNavigationAgentState();
            Entity first = world.Create();
            Entity second = world.Create();

            state.RegisterAgentAtIndex(first, controllableIndex: 5, controllable: true);

            Assert.That(state.TotalAgents, Is.EqualTo(6));
            Assert.That(state.ControllableCount, Is.EqualTo(6));
            Assert.That(state.AllAgents[0], Is.EqualTo(Entity.Null));
            Assert.That(state.AllAgents[5], Is.EqualTo(first));
            Assert.That(state.ControllableAgents[5], Is.EqualTo(first));
            Assert.That(state.TryGetControllableIndex(first, out int index), Is.True);
            Assert.That(index, Is.EqualTo(5));

            Assert.Throws<InvalidOperationException>(() => state.RegisterAgentAtIndex(second, controllableIndex: 5, controllable: true));
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
                TemplateId = "mass_navigation_total_war_formation_anchor",
                MapId = new Ludots.Core.Map.MapId("mass_navigation_total_war"),
            }), Is.True);
            Assert.That(queue.TryEnqueue(new RuntimeEntitySpawnReceipt
            {
                ReceiptChannelId = totalWarChannel,
                ReceiptId = 12,
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = "mass_navigation_agent_azure_light",
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
                TemplateId = "mass_navigation_total_war_formation_anchor",
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
                TemplateId = "mass_navigation_total_war_formation_anchor",
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
        public void GroundOverlayBuffer_TransientFormationOutlinesDoNotAccumulate()
        {
            var buffer = new GroundOverlayBuffer(capacity: 8);
            var item = new GroundOverlayItem
            {
                StableId = 0,
                Shape = GroundOverlayShape.Line,
                Center = new System.Numerics.Vector3(1f, 0f, 1f),
                Length = 4f,
                Width = 0.1f,
            };

            for (int frame = 0; frame < 4; frame++)
            {
                Assert.That(buffer.TryAdd(in item), Is.True);
                Assert.That(buffer.TryAdd(in item), Is.True);
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
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalSoldiers &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should spawn soldiers, bind receipts, and seed the authored initial selection.");

            Assert.That(simulation.AgentState.TotalAgents, Is.EqualTo(TotalWarAcceptance.ExpectedTotalSoldiers));
            Assert.That(SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext).Length,
                Is.EqualTo(TotalWarAcceptance.ExpectedInitialSelection));
            Assert.That(CountSelectionMarkerPerformers(engine), Is.EqualTo(TotalWarAcceptance.ExpectedInitialSelection),
                "Initial selection markers must be created by performer rules from SelectionMemberAdded events.");

            AssertFormationOutlines(engine);
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
                failureMessage: "Right-click command should flow through PlayerInputHandler, MassNavigationCommandBridgeSystem, and OrderBuffer.");

            Assert.That(simulation.HasCommandFocus, Is.True);
            Assert.That(simulation.CommandFocusXCm, Is.EqualTo(expectedMoveTarget.X).Within(1f));
            Assert.That(simulation.CommandFocusYCm, Is.EqualTo(expectedMoveTarget.Y).Within(1f));
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
                () => simulation.AgentState.TotalAgents == TotalWarAcceptance.ExpectedTotalSoldiers &&
                      simulation.SelectedCount == TotalWarAcceptance.ExpectedInitialSelection &&
                      CountSelectionMarkerPerformers(engine) == TotalWarAcceptance.ExpectedInitialSelection,
                maxFrames: TotalWarAcceptance.FrameBudgetForScenarioReady,
                failureMessage: "Total War scenario should be fully spawned and selected before reset.");

            Entity[] previousAgents = CaptureTrackedAgents(simulation);

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
                      CountPresentationDestroyPending(engine) == 0 &&
                      CountSelectionMarkerPerformers(engine) == 0,
                maxFrames: TotalWarAcceptance.FrameBudgetForPresentationDestroy,
                failureMessage: "Presentation lifecycle should finalize previously tracked soldiers and scoped markers after reset.");
        }

        [Test]
        public void SelectionMarkerRules_CreateAndDestroyScopedPerformersThroughSelectionEvents()
        {
            var world = World.Create();
            try
            {
                var selectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var selection = new SelectionRuntime(world, new SelectionRuntimeConfig(), selectionKeys);
                var events = new PresentationEventStream();
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
                TemplateId = "mass_navigation_agent_azure_light",
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
            Assert.That(panelSource, Does.Contain("Formation-owned scenarios use their own authored formation config for unit counts."));
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
            Assert.That(runtimeSource, Does.Contain("InsertSystemBeforeRequired<MassNavigationOrderBridgeSystem>"));
            Assert.That(runtimeSource, Does.Contain("InsertSystemBeforeRequired<MassNavigationCommandApplySystem>"));
            Assert.That(runtimeSource.IndexOf("new MassNavigationFormationSystem", StringComparison.Ordinal),
                Is.LessThan(runtimeSource.IndexOf("InsertSystemBeforeRequired<MassNavigationFormationSystem>", StringComparison.Ordinal)));
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
            int light = definitions.GetId("mass_navigation_agent_selection_marker_light");
            int heavy = definitions.GetId("mass_navigation_agent_selection_marker_heavy");
            int count = 0;
            var query = new QueryDescription().WithAll<PerformerState>();
            engine.World.Query(in query, (ref PerformerState state) =>
            {
                if (state.DefId == light || state.DefId == heavy)
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

        private static Entity[] CaptureTrackedAgents(MassNavigationSimulationRuntime simulation)
        {
            IReadOnlyList<Entity> agents = simulation.AgentState.AllAgents;
            Assert.That(agents.Count, Is.EqualTo(TotalWarAcceptance.ExpectedTotalSoldiers));
            var snapshot = new Entity[agents.Count];
            for (int i = 0; i < agents.Count; i++)
            {
                snapshot[i] = agents[i];
                Assert.That(snapshot[i], Is.Not.EqualTo(Entity.Null), $"Total War tracked agent {i} must be bound before reset.");
            }

            return snapshot;
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
            GroundOverlayBuffer overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer is missing.");
            ReadOnlySpan<GroundOverlayItem> items = overlays.GetSpan();
            int lineCount = 0;
            int ringCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Shape == GroundOverlayShape.Line)
                {
                    lineCount++;
                }
                else if (items[i].Shape == GroundOverlayShape.Ring)
                {
                    ringCount++;
                }
            }

            Assert.That(lineCount, Is.EqualTo(TotalWarAcceptance.ExpectedOutlineLines));
            Assert.That(ringCount, Is.EqualTo(TotalWarAcceptance.ExpectedOutlineRings));
            Assert.That(items.Length, Is.EqualTo(TotalWarAcceptance.ExpectedOutlineItems));
            Assert.That(engine.GlobalContext.TryGetValue("MassNavigation.TotalWar.FormationOutlineCount", out object? outlineCount), Is.True);
            Assert.That(outlineCount, Is.EqualTo(TotalWarAcceptance.ExpectedOutlineItems));
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
            public const float FrameSeconds = 1f / 20f;
            public const float PixelsPerCm = 0.08f;
            public const float HeadlessRayOriginHeightM = 2000f;
            public const int FrameBudgetForMapEntry = 4;
            public const int FrameBudgetForScenarioReady = 220;
            public const int FrameBudgetForInteraction = 40;
            public const int FrameBudgetForInputRelease = 2;
            public const int FrameBudgetForPresentationDestroy = 80;
            public const int ExpectedTotalSoldiers = 1280;
            public const int ExpectedInitialSelection = 240;
            public const int ExpectedOutlineLines = 22;
            public const int ExpectedOutlineRings = 2;
            public const int ExpectedOutlineItems = 24;
            public const float SelectionDragPaddingPixels = 24f;
            public static readonly Vector2 ScreenCenter = new(960f, 540f);
            public static readonly Vector2 EmptyGroundWorldCm = new(18000f, 18000f);
            public static readonly Vector2 MoveTargetWorldCm = new(-3600f, -400f);
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
