using System;
using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Vision;
using Ludots.Core.Vision.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class CoreHeroSkillInfraTests
    {
        [Test]
        public void VisionFogLayerConfigLoader_LoadsFormalFogLayersThroughConfigPipeline()
        {
            string root = CreateTempRoot("Ludots_Issue590_FogLayers");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Vision"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""Vision/fog_layers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                File.WriteAllText(
                    Path.Combine(root, "Vision", "fog_layers.json"),
                    @"[
  { ""id"": ""ground"", ""cellSizeCm"": 100, ""updateHz"": 10 },
  { ""id"": ""detection"", ""cellSizeCm"": 125, ""updateHz"": 5 }
]");

                ConfigPipeline pipeline = CreatePipeline(root);
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                var registry = new FogLayerRegistry();

                new VisionFogLayerConfigLoader(pipeline, registry).Load(catalog);

                FogLayerId ground = registry.GetId("ground");
                FogLayerId detection = registry.GetId("detection");
                Assert.That(ground.Value, Is.GreaterThan(0));
                Assert.That(detection.Value, Is.GreaterThan(0));
                Assert.That(registry.Get(ground).CellSizeCm, Is.EqualTo(100));
                Assert.That(registry.Get(detection).UpdateHz, Is.EqualTo(5));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void KnowledgeAreaRevealRuntime_RevealsAreaAndDecaysProjectionToKnown()
        {
            using World world = World.Create();
            var layers = new FogLayerRegistry();
            FogLayerId ground = layers.Register("ground", cellSizeCm: 100, updateHz: 10);
            uint groundMask = layers.ToMask(ground);
            var fields = new FogFieldStore();
            var knowledge = new KnowledgeProjectionStore();
            var resolver = new VisionResolver(layers, fields);
            var projector = new FogKnowledgeProjector(knowledge);
            var runtime = new KnowledgeAreaRevealRuntime(world, layers, fields, resolver, projector);
            Entity viewer = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity target = world.Create(
                WorldPositionCm.FromCm(50, 0),
                new FogOccupantCm { ExposeLayerMask = groundMask });
            var descriptor = new KnowledgeAreaRevealDescriptor(1, radiusCm: 150, stackalloc[] { ground }, memoryTtlTicks: 20);

            KnowledgeAreaRevealResult reveal = runtime.Reveal(viewer, viewer, new WorldCmInt2(0, 0), in descriptor, currentTick: 7);

            Assert.That(reveal.RasterizedCells, Is.GreaterThan(0));
            Assert.That(reveal.ProjectedTargets, Is.EqualTo(1));
            Assert.That(fields.TryGet(1, ground, out FogField field), Is.True);
            Assert.That(field.GetVisibility(new FogCell(0, 0)), Is.EqualTo(CellVisibility.Visible));
            Assert.That(knowledge.TryGet(viewer, target, currentTick: 7, out KnowledgeDisclosureRecord live), Is.True);
            Assert.That(live.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));

            KnowledgeAreaRevealResult decay = runtime.DecayArea(viewer, viewer, new WorldCmInt2(0, 0), in descriptor, currentTick: 9);

            Assert.That(decay.DecayedTargets, Is.EqualTo(1));
            Assert.That(knowledge.TryGet(viewer, target, currentTick: 9, out KnowledgeDisclosureRecord known), Is.True);
            Assert.That(known.Presence, Is.EqualTo(KnowledgePresence.Known));
            Assert.That(known.Position, Is.EqualTo(KnowledgePositionAccess.LastKnown));
            Assert.That(known.ExpiryTick, Is.EqualTo(29));
        }

        [Test]
        public void RevealAreaMainGraphs_ExecuteApplyAndRemoveThroughPhaseExecutor()
        {
            using World world = World.Create();
            var layers = new FogLayerRegistry();
            FogLayerId ground = layers.Register("ground", cellSizeCm: 100, updateHz: 10);
            uint groundMask = layers.ToMask(ground);
            var fields = new FogFieldStore();
            var knowledge = new KnowledgeProjectionStore();
            var resolver = new VisionResolver(layers, fields);
            var projector = new FogKnowledgeProjector(knowledge);
            var revealRuntime = new KnowledgeAreaRevealRuntime(world, layers, fields, resolver, projector);
            Entity viewer = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity target = world.Create(
                WorldPositionCm.FromCm(50, 0),
                new FogOccupantCm { ExposeLayerMask = groundMask });

            var presetTypes = new PresetTypeRegistry();
            var programs = new GraphProgramRegistry();
            const int revealGraphId = 5901;
            const int decayGraphId = 5902;
            programs.Register(revealGraphId,
            [
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.InvokeBuiltin,
                    Imm = (int)BuiltinHandlerId.RevealArea
                }
            ], GraphKind.Effect);
            programs.Register(decayGraphId,
            [
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.InvokeBuiltin,
                    Imm = (int)BuiltinHandlerId.DecayRevealArea
                }
            ], GraphKind.Effect);

            var behavior = new EffectPhaseGraphBindings();
            Assert.That(behavior.TryAddStep(EffectPhaseId.OnApply, PhaseSlot.Main, revealGraphId), Is.True);
            Assert.That(behavior.TryAddStep(EffectPhaseId.OnRemove, PhaseSlot.Main, decayGraphId), Is.True);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var templates = new EffectTemplateRegistry();
            const int templateId = 590;
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.None,
                RevealArea = new KnowledgeAreaRevealDescriptor(1, radiusCm: 150, stackalloc[] { ground }, memoryTtlTicks: 30)
            });
            var executor = new EffectPhaseExecutor(
                programs,
                presetTypes,
                builtinHandlers,
                new GasGraphOpHandlerTable(),
                templates);
            var runtime = new BuiltinHandlerExecutionContext
            {
                KnowledgeAreaReveal = revealRuntime,
                CurrentStep = 3
            };

            executor.ExecutePhase(
                world,
                new GasGraphRuntimeApi(world, null, null, null),
                viewer,
                Entity.Null,
                Entity.Null,
                default,
                EffectPhaseId.OnApply,
                behavior,
                EffectPresetType.None,
                effectTagId: 0,
                effectTemplateId: templateId,
                builtinRuntime: runtime);

            Assert.That(knowledge.TryGet(viewer, target, currentTick: 3, out KnowledgeDisclosureRecord live), Is.True);
            Assert.That(live.Presence, Is.EqualTo(KnowledgePresence.LiveVisible));

            runtime.CurrentStep = 5;
            executor.ExecutePhase(
                world,
                new GasGraphRuntimeApi(world, null, null, null),
                viewer,
                Entity.Null,
                Entity.Null,
                default,
                EffectPhaseId.OnRemove,
                behavior,
                EffectPresetType.None,
                effectTagId: 0,
                effectTemplateId: templateId,
                builtinRuntime: runtime);

            Assert.That(knowledge.TryGet(viewer, target, currentTick: 5, out KnowledgeDisclosureRecord known), Is.True);
            Assert.That(known.Presence, Is.EqualTo(KnowledgePresence.Known));
        }

        [Test]
        public void EffectTemplateLoader_RevealArea_CompilesRegisteredScopeAndFogLayers()
        {
            GraphIdRegistry.Clear();
            int revealGraphId = GraphIdRegistry.Register("Graph.Vision.RevealArea");
            int decayGraphId = GraphIdRegistry.Register("Graph.Vision.DecayRevealArea");
            string root = CreateTempRoot("Ludots_Issue590_RevealAreaEffect");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "GAS"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                File.WriteAllText(
                    Path.Combine(root, "GAS", "effects.json"),
                    @"[
  {
    ""id"": ""hero_reveal"",
    ""presetType"": ""None"",
    ""lifetime"": ""After"",
    ""duration"": { ""durationTicks"": 30, ""periodTicks"": 5, ""clockId"": ""FixedFrame"" },
    ""participatesInResponse"": true,
    ""phaseGraphs"": {
      ""OnApply"": { ""main"": ""Graph.Vision.RevealArea"" },
      ""OnPeriod"": { ""main"": ""Graph.Vision.RevealArea"" },
      ""OnRemove"": { ""main"": ""Graph.Vision.DecayRevealArea"" }
    },
    ""revealArea"": {
      ""radius"": 600,
      ""scope"": ""team"",
      ""layers"": [""ground"", ""detection""],
      ""memoryTtlTicks"": 90,
      ""detectionStrength"": 2
    }
  }
]");

                ConfigPipeline pipeline = CreatePipeline(root);
                var templates = new EffectTemplateRegistry();
                var scopes = new ScopeKeyRegistry();
                scopes.Register("team");
                var layers = new FogLayerRegistry();
                FogLayerId ground = layers.Register("ground", cellSizeCm: 100, updateHz: 10);
                FogLayerId detection = layers.Register("detection", cellSizeCm: 100, updateHz: 10);

                var loader = new EffectTemplateLoader(
                    pipeline,
                    templates,
                    progressionScopeKeys: scopes,
                    fogLayers: layers);
                loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("hero_reveal");
                Assert.That(templateId, Is.GreaterThan(0));
                Assert.That(templates.TryGetRef(templateId, out int index), Is.True);
                ref readonly EffectTemplateData template = ref templates.GetRef(index);
                Assert.That(template.PresetType, Is.EqualTo(EffectPresetType.None));
                Assert.That(template.PhaseGraphBindings.GetGraphId(EffectPhaseId.OnApply, PhaseSlot.Main), Is.EqualTo(revealGraphId));
                Assert.That(template.PhaseGraphBindings.GetGraphId(EffectPhaseId.OnRemove, PhaseSlot.Main), Is.EqualTo(decayGraphId));
                Assert.That(template.RevealArea.RadiusCm, Is.EqualTo(600));
                Assert.That(template.RevealArea.ScopeKeyId, Is.EqualTo(scopes.GetId("team")));
                Assert.That(template.RevealArea.LayerCount, Is.EqualTo(2));
                Assert.That(template.RevealArea.Layer0, Is.EqualTo(ground));
                Assert.That(template.RevealArea.Layer1, Is.EqualTo(detection));
                Assert.That(template.RevealArea.MemoryTtlTicks, Is.EqualTo(90));
                Assert.That(template.RevealArea.DetectionStrength, Is.EqualTo(2));
            }
            finally
            {
                GraphIdRegistry.Clear();
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void EffectTemplateLoader_RevealArea_FailsFastForUnregisteredFogLayer()
        {
            GraphIdRegistry.Clear();
            GraphIdRegistry.Register("Graph.Vision.RevealArea");
            string root = CreateTempRoot("Ludots_Issue590_RevealAreaMissingLayer");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "GAS"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                File.WriteAllText(
                    Path.Combine(root, "GAS", "effects.json"),
                    @"[
  {
    ""id"": ""hero_reveal"",
    ""presetType"": ""None"",
    ""lifetime"": ""Instant"",
    ""participatesInResponse"": true,
    ""phaseGraphs"": {
      ""OnApply"": { ""main"": ""Graph.Vision.RevealArea"" }
    },
    ""revealArea"": {
      ""radius"": 600,
      ""scope"": ""team"",
      ""layers"": [""missing""]
    }
  }
]");

                ConfigPipeline pipeline = CreatePipeline(root);
                var scopes = new ScopeKeyRegistry();
                scopes.Register("team");
                var layers = new FogLayerRegistry();
                var loader = new EffectTemplateLoader(
                    pipeline,
                    new EffectTemplateRegistry(),
                    progressionScopeKeys: scopes,
                    fogLayers: layers);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                    () => loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json"))!;

                Assert.That(ex.Message, Does.Contain("not registered"));
                Assert.That(ex.Message, Does.Contain("missing"));
            }
            finally
            {
                GraphIdRegistry.Clear();
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void RelationEnsureLink_HandlerCreatesTypedRelationshipLink()
        {
            using World world = World.Create();
            RelationshipRuntime relationships = CreateRelationshipRuntime(world, out RelationshipTypeRegistry types);
            int captiveTypeId = types.Register("Captive");
            Entity captor = world.Create();
            Entity captive = world.Create();

            var presetTypes = new PresetTypeRegistry();
            var relationPreset = new PresetTypeDefinition { Type = EffectPresetType.Relation };
            relationPreset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.ApplyRelation);
            presetTypes.Register(in relationPreset);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var templates = new EffectTemplateRegistry();
            const int templateId = 591;
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.Relation,
                Relation = new RelationDescriptor
                {
                    Operation = RelationOperation.EnsureLink,
                    Subject = RelationEntitySlot.Source,
                    Parent = RelationEntitySlot.Target,
                    RelationshipTypeId = captiveTypeId
                }
            });
            var executor = new EffectPhaseExecutor(
                new GraphProgramRegistry(),
                presetTypes,
                builtinHandlers,
                new GasGraphOpHandlerTable(),
                templates);
            var runtime = new BuiltinHandlerExecutionContext { Relationships = relationships };

            executor.ExecutePhase(
                world,
                new GasGraphRuntimeApi(world, null, null, null),
                captor,
                captive,
                Entity.Null,
                default,
                EffectPhaseId.OnApply,
                default,
                EffectPresetType.Relation,
                effectTagId: 0,
                effectTemplateId: templateId,
                builtinRuntime: runtime);

            Assert.That(relationships.HasLink(captor, captive, captiveTypeId), Is.True);
        }

        [Test]
        public void EffectTemplateLoader_RelationEnsureLink_CompilesRelationshipType()
        {
            string root = CreateTempRoot("Ludots_Issue590_RelationEnsureLink");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "GAS"));
                File.WriteAllText(
                    Path.Combine(root, "config_catalog.json"),
                    @"[{ ""Path"": ""GAS/effects.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
                File.WriteAllText(
                    Path.Combine(root, "GAS", "effects.json"),
                    @"[
  {
    ""id"": ""capture_link"",
    ""presetType"": ""Relation"",
    ""lifetime"": ""Instant"",
    ""participatesInResponse"": true,
    ""relation"": {
      ""operation"": ""EnsureLink"",
      ""subject"": ""Source"",
      ""parent"": ""Target"",
      ""snapSubjectToParentPosition"": false,
      ""relationshipType"": ""Captive""
    }
  }
]");

                ConfigPipeline pipeline = CreatePipeline(root);
                var templates = new EffectTemplateRegistry();
                var relationshipTypes = new RelationshipTypeRegistry();
                int captiveTypeId = relationshipTypes.Register("Captive");
                var loader = new EffectTemplateLoader(
                    pipeline,
                    templates,
                    relationshipTypes: relationshipTypes);

                loader.Load(ConfigCatalogLoader.Load(pipeline), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("capture_link");
                Assert.That(templates.TryGetRef(templateId, out int index), Is.True);
                ref readonly EffectTemplateData template = ref templates.GetRef(index);
                Assert.That(template.Relation.Operation, Is.EqualTo(RelationOperation.EnsureLink));
                Assert.That(template.Relation.RelationshipTypeId, Is.EqualTo(captiveTypeId));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void FacingSector_QueryConeUsesBlackboardCastFacingBeforePersistentFacing()
        {
            using World world = World.Create();
            Entity source = world.Create(
                WorldPositionCm.FromCm(0, 0),
                new FacingDirection { AngleRad = WorldPlane2D.DegToRadValue(90f) });
            Entity target = world.Create(WorldPositionCm.FromCm(100, 0));
            var blackboard = new BlackboardFloatBuffer();
            blackboard.Set(OrderBlackboardKeys.Cast_Facing, 270f);
            world.Add(source, blackboard);
            var query = new TargetQueryDescriptor
            {
                Kind = TargetResolverKind.BuiltinSpatial,
                Spatial = new BuiltinSpatialDescriptor
                {
                    Shape = SpatialShape.Cone,
                    RadiusCm = 500,
                    HalfAngleDeg = 45,
                }
            };
            var service = new CapturingSpatialQueryService(target);
            Entity[] buffer = new Entity[4];

            int count = TargetResolverFanOutHelper.ResolveTargets(
                world,
                new EffectContext { Source = source, Target = target },
                in query,
                service,
                buffer);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(service.LastConeDirectionDeg, Is.EqualTo(270));
        }

        [Test]
        public void SearchDispatch_LineQueryPublishesResolvedEntityContext()
        {
            using World world = World.Create();
            Entity source = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity originalTarget = world.Create(WorldPositionCm.FromCm(600, 0));
            Entity resolved = world.Create(WorldPositionCm.FromCm(300, 0));
            var query = new TargetQueryDescriptor
            {
                Kind = TargetResolverKind.BuiltinSpatial,
                Spatial = new BuiltinSpatialDescriptor
                {
                    Shape = SpatialShape.Line,
                    LengthCm = 700,
                    HalfWidthCm = 60,
                }
            };
            var filter = new TargetFilterDescriptor
            {
                RelationFilter = RelationshipFilter.All,
                MaxTargets = 1,
            };
            var dispatch = new TargetDispatchDescriptor
            {
                PayloadEffectTemplateId = 777,
                ContextMapping = new TargetResolverContextMapping
                {
                    PayloadSource = ContextSlot.OriginalSource,
                    PayloadTarget = ContextSlot.ResolvedEntity,
                    PayloadTargetContext = ContextSlot.OriginalTarget,
                }
            };
            var service = new CapturingSpatialQueryService(resolved);
            var commands = new FanOutCommandBuffer(capacity: 4);
            var budget = new RootBudgetTable(16);
            Entity[] buffer = new Entity[4];

            TargetResolverFanOutHelper.CollectFanOutTargets(
                world,
                new EffectContext { RootId = 9, Source = source, Target = originalTarget },
                in query,
                in filter,
                in dispatch,
                service,
                budget,
                commands,
                buffer);

            var queue = new EffectRequestQueue();
            TargetResolverFanOutHelper.PublishFanOutCommands(commands, queue);

            Assert.That(commands, Has.Count.EqualTo(1));
            Assert.That(service.LastLineDirectionDeg, Is.EqualTo(0));
            Assert.That(queue.Count, Is.EqualTo(1));
            EffectRequest request = queue[0];
            Assert.That(request.TemplateId, Is.EqualTo(777));
            Assert.That(request.Source, Is.EqualTo(source));
            Assert.That(request.Target, Is.EqualTo(resolved));
            Assert.That(request.TargetContext, Is.EqualTo(originalTarget));
        }

        [Test]
        public void SearchDispatch_FailsBeforeGrowingBeyondConfiguredCommandCapacity()
        {
            using World world = World.Create();
            Entity source = world.Create(WorldPositionCm.FromCm(0, 0));
            Entity firstTarget = world.Create(WorldPositionCm.FromCm(100, 0));
            Entity secondTarget = world.Create(WorldPositionCm.FromCm(200, 0));
            var context = new EffectContext { RootId = 9, Source = source, Target = firstTarget };
            var query = new TargetQueryDescriptor();
            var filter = new TargetFilterDescriptor { RelationFilter = RelationshipFilter.All };
            var dispatch = new TargetDispatchDescriptor
            {
                PayloadEffectTemplateId = 777,
                ContextMapping = TargetResolverContextMapping.Default,
            };
            Entity[] candidates = { firstTarget, secondTarget };
            var commands = new FanOutCommandBuffer(capacity: 1);
            var budget = new RootBudgetTable(16);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                TargetResolverFanOutHelper.ValidateAndCollect(
                    world,
                    in context,
                    in query,
                    in filter,
                    in dispatch,
                    candidates,
                    candidates.Length,
                    budget,
                    commands))!;

            Assert.That(error.Message, Does.StartWith(TargetResolverFanOutHelper.CommandCapacityExceededError));
            Assert.That(commands, Has.Count.EqualTo(1));
            Assert.That(commands.Capacity, Is.EqualTo(1));
        }

        [Test]
        public void GameplayPresentationProjection_ProjectsCastAndEffectLifecycleEvents()
        {
            using World world = World.Create();
            var gasEvents = new GasPresentationEventBuffer(8);
            var stream = new PresentationEventStream(32);
            var projection = new GameplayPresentationProjectionSystem(
                world,
                new GameplayEventBus(),
                stream,
                new GameSession(),
                gasEvents,
                new PresentationOwnerChangeBuffer(8));
            Entity actor = world.Create();
            Entity target = world.Create();
            gasEvents.Publish(new GasPresentationEvent { Kind = GasPresentationEventKind.CastStarted, Actor = actor, Target = target, AbilitySlot = 1, AbilityId = 11 });
            gasEvents.Publish(new GasPresentationEvent { Kind = GasPresentationEventKind.CastFinished, Actor = actor, Target = target, AbilitySlot = 1, AbilityId = 11 });
            gasEvents.Publish(new GasPresentationEvent { Kind = GasPresentationEventKind.CastInterrupted, Actor = actor, Target = target, AbilitySlot = 2, AbilityId = 12 });
            gasEvents.Publish(new GasPresentationEvent { Kind = GasPresentationEventKind.EffectExpired, Actor = actor, Target = target, EffectTemplateId = 21 });
            gasEvents.Publish(new GasPresentationEvent { Kind = GasPresentationEventKind.EffectCancelled, Actor = actor, Target = target, EffectTemplateId = 22 });

            try
            {
                projection.Update(1f / 60f);
            }
            finally
            {
                projection.Dispose();
            }

            ReadOnlySpan<PresentationEvent> events = stream.GetSpan();
            bool hasCastStarted = Contains(events, PresentationEventKind.CastStarted, keyId: 11);
            bool hasCastFinished = Contains(events, PresentationEventKind.CastFinished, keyId: 11);
            bool hasCastInterrupted = Contains(events, PresentationEventKind.CastInterrupted, keyId: 12);
            bool hasEffectExpired = Contains(events, PresentationEventKind.EffectExpired, keyId: 21);
            bool hasEffectCancelled = Contains(events, PresentationEventKind.EffectCancelled, keyId: 22);
            Assert.Multiple(() =>
            {
                Assert.That(hasCastStarted, Is.True);
                Assert.That(hasCastFinished, Is.True);
                Assert.That(hasCastInterrupted, Is.True);
                Assert.That(hasEffectExpired, Is.True);
                Assert.That(hasEffectCancelled, Is.True);
                Assert.That(gasEvents.Count, Is.EqualTo(0));
            });
        }

        private static bool Contains(ReadOnlySpan<PresentationEvent> events, PresentationEventKind kind, int keyId)
        {
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].Kind == kind && events[i].KeyId == keyId)
                {
                    return true;
                }
            }

            return false;
        }

        private static RelationshipRuntime CreateRelationshipRuntime(World world, out RelationshipTypeRegistry types)
        {
            types = new RelationshipTypeRegistry();
            return new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
        }

        private static ConfigPipeline CreatePipeline(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            return new ConfigPipeline(vfs, modLoader);
        }

        private static string CreateTempRoot(string prefix)
        {
            string root = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

        private sealed class CapturingSpatialQueryService : ISpatialQueryService
        {
            private readonly Entity[] _hits;

            public CapturingSpatialQueryService(params Entity[] hits)
            {
                _hits = hits;
            }

            public int LastConeDirectionDeg { get; private set; } = int.MinValue;
            public int LastLineDirectionDeg { get; private set; } = int.MinValue;

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => Write(buffer);

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => Write(buffer);

            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer)
            {
                LastConeDirectionDeg = directionDeg;
                return Write(buffer);
            }

            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => Write(buffer);

            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
            {
                LastLineDirectionDeg = directionDeg;
                return Write(buffer);
            }

            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);

            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer) => Write(buffer);

            private SpatialQueryResult Write(Span<Entity> buffer)
            {
                int count = Math.Min(buffer.Length, _hits.Length);
                for (int i = 0; i < count; i++)
                {
                    buffer[i] = _hits[i];
                }

                return new SpatialQueryResult(count, _hits.Length - count);
            }
        }
    }
}
