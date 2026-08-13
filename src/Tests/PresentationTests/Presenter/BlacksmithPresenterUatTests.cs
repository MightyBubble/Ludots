using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [NonParallelizable]
    public sealed class BlacksmithPresenterUatTests
    {
        private const string DurabilityAttributeKey = "durability";
        private const string HealthAttributeKey = "Health";
        private const string WorkingTagKey = "working";
        private const string BlacksmithTemplateId = "blacksmith";

        private const int HammerSoundAssetId = 1400;
        private const int PatrolSplineAssetId = 1500;

        private const string WorkshopIntactAssetKey = "blacksmith.fixture.workshop.intact";
        private const string WorkshopDamagedAssetKey = "blacksmith.fixture.workshop.damaged";
        private const string WorkshopRuinedAssetKey = "blacksmith.fixture.workshop.ruined";
        private const string FurnaceAssetKey = "blacksmith.fixture.furnace";
        private const string SmokeAssetKey = "blacksmith.fixture.smoke";
        private const string WorkerAssetKey = "blacksmith.fixture.worker";
        private const string HammerSoundAssetKey = "blacksmith.fixture.anvil_hammering";
        private const string PatrolSplineAssetKey = "blacksmith.fixture.patrol";
        private const string BrickNorthMaterialKey = "blacksmith.fixture.brick.north";
        private const string BrickSouthMaterialKey = "blacksmith.fixture.brick.south";

        private const string BlacksmithRootKey = "blacksmith_root";
        private const string BlacksmithWorkshop1Key = "blacksmith_workshop_1";
        private const string BlacksmithWorkshop2Key = "blacksmith_workshop_2";
        private const string BlacksmithFurnaceKey = "blacksmith_furnace";
        private const string BlacksmithSmokeKey = "blacksmith_smoke";
        private const string BlacksmithWorkerKey = "blacksmith_worker";
        private const string DayNightParamKey = "blacksmith.fixture.dayNight";

        private string _repoRoot = string.Empty;
        private string _tempRoot = string.Empty;
        private string _coreRoot = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _repoRoot = FindRepoRoot();
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_BlacksmithUat", Guid.NewGuid().ToString("N"));
            _coreRoot = Path.Combine(_tempRoot, "Core");
            Directory.CreateDirectory(_coreRoot);
            Directory.CreateDirectory(Path.Combine(_coreRoot, "Configs"));
            File.WriteAllText(
                Path.Combine(_coreRoot, "Configs", "config_catalog.json"),
                File.ReadAllText(Path.Combine(_repoRoot, "assets", "Configs", "config_catalog.json")));

            TagRegistry.Clear();
            PresenterScopeTagRegistry.Clear();
            AttributeRegistry.Clear();
            AttributeRegistry.Register(HealthAttributeKey);
            AttributeRegistry.Register(DurabilityAttributeKey);
            TagRegistry.Register(WorkingTagKey);
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
            PresenterScopeTagRegistry.Clear();
            AttributeRegistry.Clear();
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // Ignore teardown races for temp folders.
            }
        }

        [Test]
        public void BlacksmithUat_CreateEntity_ShowsTwoWorkshopsAndOneFurnace()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            fixture.SpawnBlacksmithAndWarmup();

            Assert.That(fixture.CountActiveByDefinition(BlacksmithWorkshop1Key), Is.EqualTo(1));
            Assert.That(fixture.CountActiveByDefinition(BlacksmithWorkshop2Key), Is.EqualTo(1));
            Assert.That(fixture.CountActiveByDefinition(BlacksmithFurnaceKey), Is.EqualTo(1));
            Assert.That(fixture.CountVisualByAssetKey(WorkshopIntactAssetKey), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByAssetKey(FurnaceAssetKey), Is.EqualTo(1));
        }

        [Test]
        public void BlacksmithUat_PresenterFixture_UsesBehaviorSchemaWithoutVisualKind()
        {
            string presentersPath = Path.Combine(
                _repoRoot,
                "mods",
                "fixtures",
                "blacksmith",
                "BlacksmithTestMod",
                "assets",
                "Presentation",
                "presenters.json");

            string json = File.ReadAllText(presentersPath);

            Assert.That(json, Does.Not.Contain("\"visualKind\""));
            Assert.That(json, Does.Contain("\"kind\": \"AssetBinding\""));
        }

        [Test]
        public void BlacksmithUat_DoesNotWirePrefabRegistryIntoPresenterRuntime()
        {
            string sourcePath = Path.Combine(
                _repoRoot,
                "src",
                "Tests",
                "PresentationTests",
                "BlacksmithPresenterUatTests.cs");

            string source = File.ReadAllText(sourcePath);

            int runtimeStart = source.IndexOf("var runtime = new PresenterRuntimeSystem(", StringComparison.Ordinal);
            Assert.That(runtimeStart, Is.GreaterThanOrEqualTo(0));
            int runtimeEnd = source.IndexOf(");", runtimeStart, StringComparison.Ordinal);
            Assert.That(runtimeEnd, Is.GreaterThan(runtimeStart));
            string runtimeConstruction = source.Substring(runtimeStart, runtimeEnd - runtimeStart);
            Assert.That(runtimeConstruction, Does.Not.Contain(nameof(PrefabRegistry)));
        }

        [Test]
        public void BlacksmithUat_WorkingOn_ShowsSmokeWorkerAndHammerLoop()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.SetWorking(blacksmith, enabled: true);
            fixture.PublishTagEvent(blacksmith, gained: true);
            fixture.Tick();

            Assert.That(fixture.CountActiveByDefinition(BlacksmithSmokeKey), Is.EqualTo(1));
            Assert.That(fixture.CountActiveByDefinition(BlacksmithWorkerKey), Is.EqualTo(1));
            Assert.That(fixture.CountVisualByAssetKey(SmokeAssetKey), Is.EqualTo(1));
            Assert.That(fixture.CountVisualByAssetKey(WorkerAssetKey), Is.EqualTo(1));
            Assert.That(fixture.HasSoundRequestByAssetKey(SoundRequestKind.PlayOrUpdate, HammerSoundAssetKey), Is.True);
        }

        [Test]
        public void BlacksmithUat_WorkingOff_RemovesSmokeWorkerAndStopsHammer()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();
            fixture.SetWorking(blacksmith, enabled: true);
            fixture.PublishTagEvent(blacksmith, gained: true);
            fixture.Tick();

            fixture.SetWorking(blacksmith, enabled: false);
            fixture.PublishTagEvent(blacksmith, gained: false);
            fixture.Tick();

            Assert.That(fixture.CountActiveByDefinition(BlacksmithSmokeKey), Is.EqualTo(0));
            Assert.That(fixture.CountActiveByDefinition(BlacksmithWorkerKey), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(SmokeAssetKey), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(WorkerAssetKey), Is.EqualTo(0));
            Assert.That(fixture.HasSoundRequestByAssetKey(SoundRequestKind.Stop, HammerSoundAssetKey), Is.True);
        }

        [Test]
        public void BlacksmithUat_GlobalDayNight_UpdatesLampParam()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.PublishGlobalDayNight(blacksmith, phase: 1f);
            fixture.Tick();

            Entity rootEntity = fixture.RequireHandle(BlacksmithRootKey);
            Assert.That(fixture.Instances.ResolveFloat(rootEntity, fixture.DayNightParamKeyId), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void BlacksmithUat_RegionNorth_UsesBlackBrick()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            fixture.SpawnBlacksmithAndWarmup();

            Assert.That(fixture.CountVisualByMaterialKey(BrickNorthMaterialKey), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByMaterialKey(BrickSouthMaterialKey), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithUat_RegionSouth_UsesRedBrick()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.PublishGlobalRegionChanged(blacksmith, regionId: 1);
            fixture.Tick();

            Assert.That(fixture.CountVisualByMaterialKey(BrickSouthMaterialKey), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByMaterialKey(BrickNorthMaterialKey), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithUat_DurabilityHalf_SwapsWorkshopToDamaged()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.SetDurability(blacksmith, current: 50f, max: 100f);
            fixture.Tick();

            Assert.That(fixture.CountVisualByAssetKey(WorkshopDamagedAssetKey), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByAssetKey(WorkshopIntactAssetKey), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(WorkshopRuinedAssetKey), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithUat_DurabilityZero_SwapsWorkshopToRuined()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.SetDurability(blacksmith, current: 0f, max: 100f);
            fixture.Tick();

            Assert.That(fixture.CountVisualByAssetKey(WorkshopRuinedAssetKey), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByAssetKey(WorkshopIntactAssetKey), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(WorkshopDamagedAssetKey), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithUat_DestroyEntity_RemovesWholeSubtree()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();
            fixture.SetWorking(blacksmith, enabled: true);
            fixture.PublishTagEvent(blacksmith, gained: true);
            fixture.Tick();

            fixture.MarkPendingDestroy(blacksmith);
            fixture.Tick();

            Assert.That(fixture.Instances.ActiveCount, Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(WorkshopIntactAssetKey), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(FurnaceAssetKey), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(SmokeAssetKey), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAssetKey(WorkerAssetKey), Is.EqualTo(0));
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

                current = Path.GetDirectoryName(current) ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private sealed class BlacksmithPipelineFixture : IDisposable
        {
            private readonly World _world;
            private readonly ModLoader _modLoader;
            private readonly MeshAssetRegistry _meshAssets;
            private readonly PresentationMaterialRegistry _materialAssets;
            private readonly int _blacksmithTemplateKeyId;
            private readonly int _durabilityAttributeId;
            private readonly int _workingTagId;
            private readonly int _dayNightParamKeyId;
            private readonly PresenterDefinitionRegistry _definitions;
            private readonly PresentationEventStream _events;
            private readonly PresentationOwnerChangeBuffer _ownerChanges;
            private readonly PresenterCommandBuffer _commands;
            private readonly PresenterEntityRuntime _instances;
            private readonly PresenterAnimatorStateBuffer _animatorStates;
            private readonly PresentationRequestBuffer _requests;
            private readonly SoundRequestBuffer _soundRequests;
            private readonly StableDrawCache _stableDrawCache;
            private readonly PrimitiveDrawBuffer _primitives;
            private readonly GroundOverlayBuffer _groundOverlays;
            private readonly WorldHudBatchBuffer _worldHud;
            private readonly SplineRibbonBuffer _splineRibbons;
            private readonly PrimitiveDrawBuffer _snapshotBuffer;
            private readonly PresentationVisualProxyBuffer _proxyBuffer;
            private readonly SkinnedVisualBatchBuffer _skinnedBatchBuffer;
            private readonly GlobalPresentationEventBuffer _globalEvents;

            private readonly PresentationEntityLifecycleSystem _entityLifecycle;
            private readonly GlobalPresentationEventProjectionSystem _globalProjection;
            private readonly PresenterRuleSystem _rules;
            private readonly PresenterRuntimeSystem _runtime;
            private readonly AnimatorRuntimeSystem _animator;
            private readonly PresenterBehaviorSystem _behavior;
            private readonly PresenterEmitSystem _emit;
            private readonly PresentationRequestFlushSystem _flush;

            private int _stableSeed = 7000;

            private BlacksmithPipelineFixture(
                World world,
                ModLoader modLoader,
                MeshAssetRegistry meshAssets,
                PresentationMaterialRegistry materialAssets,
                int blacksmithTemplateKeyId,
                int durabilityAttributeId,
                int workingTagId,
                int dayNightParamKeyId,
                PresenterDefinitionRegistry definitions,
                PresentationEventStream events,
                PresentationOwnerChangeBuffer ownerChanges,
                PresenterCommandBuffer commands,
                PresenterEntityRuntime instances,
                PresenterAnimatorStateBuffer animatorStates,
                PresentationRequestBuffer requests,
                SoundRequestBuffer soundRequests,
                StableDrawCache stableDrawCache,
                PrimitiveDrawBuffer primitives,
                GroundOverlayBuffer groundOverlays,
                WorldHudBatchBuffer worldHud,
                SplineRibbonBuffer splineRibbons,
                PrimitiveDrawBuffer snapshotBuffer,
                PresentationVisualProxyBuffer proxyBuffer,
                SkinnedVisualBatchBuffer skinnedBatchBuffer,
                GlobalPresentationEventBuffer globalEvents,
                PresentationEntityLifecycleSystem entityLifecycle,
                GlobalPresentationEventProjectionSystem globalProjection,
                PresenterRuleSystem rules,
                PresenterRuntimeSystem runtime,
                AnimatorRuntimeSystem animator,
                PresenterBehaviorSystem behavior,
                PresenterEmitSystem emit,
                PresentationRequestFlushSystem flush)
            {
                _world = world;
                _modLoader = modLoader;
                _meshAssets = meshAssets;
                _materialAssets = materialAssets;
                _blacksmithTemplateKeyId = blacksmithTemplateKeyId;
                _durabilityAttributeId = durabilityAttributeId;
                _workingTagId = workingTagId;
                _dayNightParamKeyId = dayNightParamKeyId;
                _definitions = definitions;
                _events = events;
                _ownerChanges = ownerChanges;
                _commands = commands;
                _instances = instances;
                _animatorStates = animatorStates;
                _requests = requests;
                _soundRequests = soundRequests;
                _stableDrawCache = stableDrawCache;
                _primitives = primitives;
                _groundOverlays = groundOverlays;
                _worldHud = worldHud;
                _splineRibbons = splineRibbons;
                _snapshotBuffer = snapshotBuffer;
                _proxyBuffer = proxyBuffer;
                _skinnedBatchBuffer = skinnedBatchBuffer;
                _globalEvents = globalEvents;
                _entityLifecycle = entityLifecycle;
                _globalProjection = globalProjection;
                _rules = rules;
                _runtime = runtime;
                _animator = animator;
                _behavior = behavior;
                _emit = emit;
                _flush = flush;
            }

            public PresenterEntityRuntime Instances => _instances;
            public int DayNightParamKeyId => _dayNightParamKeyId;

            public static BlacksmithPipelineFixture Create(string repoRoot, string coreRoot)
            {
                var world = World.Create();
                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", coreRoot);

                string blacksmithModPath = Path.Combine(repoRoot, "mods", "fixtures", "blacksmith", "BlacksmithTestMod");
                string coreModPath = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                modLoader.LoadMods(new List<string> { coreModPath, blacksmithModPath });

                var pipeline = new ConfigPipeline(vfs, modLoader);
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                var mapLoader = new Ludots.Core.Systems.MapLoader(world, new Ludots.Core.Map.WorldMap(), pipeline);
                mapLoader.LoadTemplates(catalog);
                int blacksmithTemplateKeyId = mapLoader.EntityTemplateKeys.GetId(BlacksmithTemplateId);
                Assert.That(blacksmithTemplateKeyId, Is.GreaterThan(0));

                var meshAssets = new MeshAssetRegistry();
                var presentationPrefabs = new PrefabRegistry();
                new MeshAssetConfigLoader(pipeline, meshAssets, presentationPrefabs).Load(catalog);
                var materialAssets = new PresentationMaterialRegistry();
                RegisterFixtureAssets(meshAssets, materialAssets);

                var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
                var animatorControllers = new AnimatorControllerRegistry();
                new AnimatorControllerConfigLoader(pipeline, animatorControllers).Load(catalog);
                animatorControllers.Register("worker_anim", new AnimatorControllerDefinition
                {
                    DefaultStateIndex = 0,
                    States =
                    [
                        new AnimatorStateDefinition { PackedStateIndex = 12, DurationSeconds = 1f, PlaybackSpeed = 1f, Loop = true }
                    ],
                    Transitions = Array.Empty<AnimatorTransitionDefinition>(),
                });

                var animationClips = new AnimationClipRegistry();
                new AnimationClipConfigLoader(pipeline, animationClips).Load(catalog);
                animationClips.Register(
                    "worker_loop",
                    new AnimationClipDefinition
                    {
                        AssetKind = AnimationClipAssetKind.Clip,
                        Locators = [new AnimationClipLocatorDefinition("raylib", "animations/worker_loop.glb#anim:loop", string.Empty)]
                    });

                var animationProfiles = new AnimationProfileRegistry();
                new AnimationProfileConfigLoader(pipeline, animationProfiles, animatorControllers, animationClips).Load(catalog);
                animationProfiles.Register(
                    "worker_profile",
                    new AnimationProfileDefinition
                    {
                        AnimatorControllerId = animatorControllers.GetId("worker_anim"),
                        StateClips = [new AnimationStateClipBinding { PackedStateIndex = 12, ClipAssetId = animationClips.GetId("worker_loop") }]
                    });

                var definitions = new PresenterDefinitionRegistry();
                new PresenterDefinitionConfigLoader(
                    pipeline,
                    definitions,
                    resolveAttributeName: AttributeRegistry.GetId,
                    resolveMeshId: meshAssets.GetId,
                    resolveTextTokenId: textCatalog.GetTokenId,
                    resolveEntityTemplateKey: mapLoader.EntityTemplateKeys.GetId,
                    resolveEffectTemplateId: ResolveUnsupportedEffectTemplateId,
                    resolveMaterialId: ResolveFixtureMaterialId,
                    resolveAnimatorControllerId: animatorControllers.GetId,
                    resolveAnimationProfileId: animationProfiles.GetId,
                    resolveBehaviorAssetId: ResolveBehaviorAssetId).Load(catalog);
                int durabilityAttributeId = AttributeRegistry.GetId(DurabilityAttributeKey);
                int workingTagId = TagRegistry.GetId(WorkingTagKey);
                int dayNightParamKeyId = PresenterParamKeyRegistry.Register(DayNightParamKey);
                Assert.That(durabilityAttributeId, Is.Not.EqualTo(AttributeRegistry.InvalidId));
                Assert.That(workingTagId, Is.Not.EqualTo(TagRegistry.InvalidId));
                Assert.That(dayNightParamKeyId, Is.GreaterThan(0));

                var events = new PresentationEventStream(512);
                var ownerChanges = new PresentationOwnerChangeBuffer(512);
                var commands = new PresenterCommandBuffer(512);
                var instances = new PresenterEntityRuntime(world);
                var animatorStates = new PresenterAnimatorStateBuffer(64);
                var requests = new PresentationRequestBuffer(512);
                var soundRequests = new SoundRequestBuffer(256);
                var stableDrawCache = new StableDrawCache(512);
                var primitives = new PrimitiveDrawBuffer(512);
                var groundOverlays = new GroundOverlayBuffer(128);
                var worldHud = new WorldHudBatchBuffer(128);
                var splineRibbons = new SplineRibbonBuffer(128);
                var snapshotBuffer = new PrimitiveDrawBuffer(512);
                var proxyBuffer = new PresentationVisualProxyBuffer(512);
                var skinnedBatchBuffer = new SkinnedVisualBatchBuffer(128);
                var globalEvents = new GlobalPresentationEventBuffer(64);

                var gameSession = new GameSession();
                var graphPrograms = new GraphProgramRegistry();
                var graphApi = new GasGraphRuntimeApi(world);
                var stableIds = new PresentationStableIdAllocator();
                var visualStableIds = new PresenterVisualStableIdTable(stableIds, capacity: 512);

                var entityLifecycle = new PresentationEntityLifecycleSystem(world, events, instances, definitions, stableIds);
                var globalProjection = new GlobalPresentationEventProjectionSystem(world, globalEvents, events, gameSession);
                var rules = new PresenterRuleSystem(world, events, commands, definitions, instances, graphPrograms, graphApi, new Dictionary<string, object>());
                var runtime = new PresenterRuntimeSystem(
                    world,
                    commands,
                    events,
                    new TransientMarkerBuffer(),
                    requests,
                    instances,
                    stableIds,
                    definitions,
                    animatorStates,
                    stableDrawCache,
                    visualStableIds);
                var animator = new AnimatorRuntimeSystem(world, animatorControllers, instances, definitions, animatorStates);
                var behavior = new PresenterBehaviorSystem(world, instances, definitions, events, ownerChanges, soundRequests, new FlatHeightmap());
                var emit = new PresenterEmitSystem(
                    world,
                    instances,
                    definitions,
                    requests,
                    new Dictionary<string, object>(),
                    animatorStates,
                    soundRequests,
                    stableDrawCache: stableDrawCache,
                    visualStableIds: visualStableIds);
                var flush = new PresentationRequestFlushSystem(
                    world,
                    requests,
                    presentationPrefabs,
                    meshAssets,
                    stableDrawCache,
                    primitives,
                    groundOverlays,
                    worldHud,
                    splineRibbons,
                    snapshotBuffer,
                    proxyBuffer,
                    skinnedBatchBuffer);

                Assert.That(definitions.GetId(BlacksmithRootKey), Is.GreaterThan(0));
                Assert.That(PresenterScopeTagRegistry.GetId("working"), Is.GreaterThan(0));

                return new BlacksmithPipelineFixture(
                    world,
                    modLoader,
                    meshAssets,
                    materialAssets,
                    blacksmithTemplateKeyId,
                    durabilityAttributeId,
                    workingTagId,
                    dayNightParamKeyId,
                    definitions,
                    events,
                    ownerChanges,
                    commands,
                    instances,
                    animatorStates,
                    requests,
                    soundRequests,
                    stableDrawCache,
                    primitives,
                    groundOverlays,
                    worldHud,
                    splineRibbons,
                    snapshotBuffer,
                    proxyBuffer,
                    skinnedBatchBuffer,
                    globalEvents,
                    entityLifecycle,
                    globalProjection,
                    rules,
                    runtime,
                    animator,
                    behavior,
                    emit,
                    flush);

                int ResolveBehaviorAssetId(AssetKind kind, string key)
                {
                    return kind switch
                    {
                        AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.VFX => meshAssets.GetId(key),
                        AssetKind.Spline when string.Equals(key, PatrolSplineAssetKey, StringComparison.Ordinal) => PatrolSplineAssetId,
                        AssetKind.Sound when string.Equals(key, HammerSoundAssetKey, StringComparison.Ordinal) => HammerSoundAssetId,
                        _ => throw new InvalidOperationException(
                            $"Blacksmith presenter UAT has no fixture asset registered for {kind} '{key}'."),
                    };
                }

                int ResolveUnsupportedEffectTemplateId(string key)
                {
                    throw new InvalidOperationException(
                        $"Blacksmith presenter UAT does not load effect templates; presenter event references '{key}'.");
                }

                int ResolveFixtureMaterialId(string key)
                {
                    return materialAssets.GetId(key);
                }
            }

            private static void RegisterFixtureAssets(
                MeshAssetRegistry meshAssets,
                PresentationMaterialRegistry materialAssets)
            {
                RegisterPrimitiveMesh(meshAssets, WorkshopIntactAssetKey);
                RegisterPrimitiveMesh(meshAssets, WorkshopDamagedAssetKey);
                RegisterPrimitiveMesh(meshAssets, WorkshopRuinedAssetKey);
                RegisterPrimitiveMesh(meshAssets, FurnaceAssetKey);
                RegisterPrimitiveMesh(meshAssets, SmokeAssetKey);
                RegisterPrimitiveMesh(meshAssets, WorkerAssetKey);
                materialAssets.Register(
                    BrickNorthMaterialKey,
                    MaterialAssetDomain.Surface,
                    new[] { "materials/blacksmith_fixture_brick_north.mat" },
                    MaterialAssetFlags.None);
                materialAssets.Register(
                    BrickSouthMaterialKey,
                    MaterialAssetDomain.Surface,
                    new[] { "materials/blacksmith_fixture_brick_south.mat" },
                    MaterialAssetFlags.None);
            }

            private static void RegisterPrimitiveMesh(MeshAssetRegistry meshAssets, string key)
            {
                MeshAssetDescriptor descriptor = MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Cube);
                meshAssets.Register(key, in descriptor);
            }

            public Entity SpawnBlacksmith()
            {
                var attributes = default(AttributeBuffer);
                attributes.SetBase(_durabilityAttributeId, 100f);
                attributes.SetCurrent(_durabilityAttributeId, 100f);

                return _world.Create(
                    new PresentationStableId { Value = _stableSeed++ },
                    new EntityTemplateKeyRef { TemplateKeyId = _blacksmithTemplateKeyId },
                    new VisualTransform { Position = new Vector3(10f, 0f, 20f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                    new CullState { IsVisible = true, LOD = LODLevel.High },
                    attributes,
                    default(GameplayTagContainer));
            }

            public Entity SpawnBlacksmithAndWarmup()
            {
                Entity owner = SpawnBlacksmith();
                Tick();
                return owner;
            }

            public void SetDurability(Entity owner, float current, float max)
            {
                ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(owner);
                attributes.SetBase(_durabilityAttributeId, max);
                attributes.SetCurrent(_durabilityAttributeId, current);

                Assert.That(_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.AttributeValueChanged,
                    KeyId = _durabilityAttributeId,
                    Source = owner,
                    Target = owner,
                    Magnitude = max <= 0f ? 0f : current / max,
                }), Is.True);
                Assert.That(
                    _ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Attribute, _durabilityAttributeId)),
                    Is.True);
            }

            public void SetWorking(Entity owner, bool enabled)
            {
                ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(owner);
                if (enabled)
                {
                    tags.AddTag(_workingTagId);
                }
                else
                {
                    tags.RemoveTag(_workingTagId);
                }
            }

            public void PublishTagEvent(Entity owner, bool gained)
            {
                Assert.That(
                    _events.TryAdd(new PresentationEvent
                    {
                        Kind = PresentationEventKind.TagEffectiveChanged,
                        KeyId = _workingTagId,
                        Source = owner,
                        Target = owner,
                        Magnitude = gained ? 1f : 0f,
                    }),
                    Is.True);
                Assert.That(
                    _ownerChanges.TryAdd(new PresentationOwnerChange(owner, PresentationOwnerChangeKind.Tag, _workingTagId, gained ? (byte)1 : (byte)0)),
                    Is.True);
            }

            public void PublishGlobalDayNight(Entity owner, float phase)
            {
                Assert.That(_globalEvents.TryAdd(new GlobalPresentationEvent
                {
                    Kind = PresentationEventKind.GlobalDayNight,
                    KeyId = 1,
                    Magnitude = phase,
                    Source = owner,
                    Target = owner,
                }), Is.True);
            }

            public void PublishGlobalRegionChanged(Entity owner, int regionId)
            {
                Assert.That(_globalEvents.TryAdd(new GlobalPresentationEvent
                {
                    Kind = PresentationEventKind.GlobalRegionChanged,
                    KeyId = regionId,
                    Source = owner,
                    Target = owner,
                }), Is.True);
            }

            public void MarkPendingDestroy(Entity owner)
            {
                if (!_world.Has<PresentationLifecycleState>(owner))
                {
                    _world.Add(owner, new PresentationLifecycleState { Spawned = true });
                }

                if (!_world.Has<PresentationDestroyPending>(owner))
                {
                    _world.Add(owner, new PresentationDestroyPending());
                }

                if (_world.Has<PresentationDestroyEventPublished>(owner))
                {
                    _world.Remove<PresentationDestroyEventPublished>(owner);
                }
            }

            public void Tick(float dt = 0.016f)
            {
                _requests.Clear();
                _soundRequests.Clear();

                _entityLifecycle.Update(dt);
                _globalProjection.Update(dt);
                for (int i = 0; i < 4; i++)
                {
                    _rules.Update(dt);
                    _runtime.Update(dt);
                    if (_commands.Count == 0 &&
                        (_events.Count == 0 || ContainsPresenterDestroyedEvent()))
                    {
                        break;
                    }
                }

                _animator.Update(dt);
                _behavior.Update(dt);
                _emit.Update(dt);
                _flush.Update(dt);
            }

            private bool ContainsPresenterDestroyedEvent()
            {
                foreach (ref readonly PresentationEvent evt in _events.GetSpan())
                {
                    if (evt.Kind == PresentationEventKind.PresenterDestroyed)
                    {
                        return true;
                    }
                }

                return false;
            }

            public int CountActiveByDefinition(string definitionKey)
            {
                int definitionId = _definitions.GetId(definitionKey);
                int count = 0;
                var query = new QueryDescription().WithAll<PresenterState>();
                _world.Query(in query, (Entity entity, ref PresenterState state) =>
                {
                    if (state.DefId == definitionId)
                    {
                        count++;
                    }
                });
                return count;
            }

            public Entity RequireHandle(string definitionKey)
            {
                int definitionId = _definitions.GetId(definitionKey);
                Entity found = Entity.Null;
                var query = new QueryDescription().WithAll<PresenterState>();
                _world.Query(in query, (Entity entity, ref PresenterState state) =>
                {
                    if (state.DefId == definitionId && found == Entity.Null)
                    {
                        found = entity;
                    }
                });
                if (found == Entity.Null)
                    throw new InvalidOperationException($"No active presenter for definition '{definitionKey}'.");
                return found;
            }

            public int CountVisualByAsset(int assetId)
            {
                int count = 0;
                foreach (ref readonly PrimitiveDrawItem item in _primitives.GetSpan())
                {
                    if (item.MeshAssetId == assetId &&
                        item.Visibility == VisualVisibility.Visible)
                    {
                        count++;
                    }
                }

                return count;
            }

            public int CountVisualByAssetKey(string assetKey)
            {
                return CountVisualByAsset(_meshAssets.GetId(assetKey));
            }

            public int CountVisualByMaterial(int materialId)
            {
                int count = 0;
                foreach (ref readonly PrimitiveDrawItem item in _primitives.GetSpan())
                {
                    if (item.MaterialId == materialId &&
                        item.Visibility == VisualVisibility.Visible)
                    {
                        count++;
                    }
                }

                return count;
            }

            public int CountVisualByMaterialKey(string materialKey)
            {
                return CountVisualByMaterial(_materialAssets.GetId(materialKey));
            }

            public bool HasSoundRequest(SoundRequestKind kind, int soundAssetId)
            {
                foreach (ref readonly SoundRequest request in _soundRequests.GetSpan())
                {
                    if (request.Kind == kind && request.SoundAssetId == soundAssetId)
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool HasSoundRequestByAssetKey(SoundRequestKind kind, string soundAssetKey)
            {
                if (!string.Equals(soundAssetKey, HammerSoundAssetKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Blacksmith presenter UAT has no fixture sound asset registered for '{soundAssetKey}'.");
                }

                return HasSoundRequest(kind, HammerSoundAssetId);
            }

            public void Dispose()
            {
                _flush.Dispose();
                _emit.Dispose();
                _behavior.Dispose();
                _animator.Dispose();
                _runtime.Dispose();
                _rules.Dispose();
                _globalProjection.Dispose();
                _entityLifecycle.Dispose();
                _modLoader.UnloadAll();
                _world.Dispose();
            }

            private sealed class FlatHeightmap : IVisualHeightmap
            {
                public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
                {
                    heightCm = 0f;
                    return true;
                }

                public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
                {
                    for (int i = 0; i < outHeightCm.Length; i++)
                    {
                        outHeightCm[i] = 0f;
                    }

                    return true;
                }

                public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
                {
                    hit = new VisualGroundHit(ray.Origin.X * 100f, ray.Origin.Z * 100f, 0f, layerIndex, 0f, Vector3.UnitY);
                    return true;
                }

                public bool RaycastGroundBatch(
                    ReadOnlySpan<float> originXMeters,
                    ReadOnlySpan<float> originYMeters,
                    ReadOnlySpan<float> originZMeters,
                    ReadOnlySpan<float> directionX,
                    ReadOnlySpan<float> directionY,
                    ReadOnlySpan<float> directionZ,
                    Span<float> outWorldXCm,
                    Span<float> outWorldYCm,
                    Span<float> outHeightCm,
                    Span<float> outDistanceMeters,
                    Span<float> outNormalX,
                    Span<float> outNormalY,
                    Span<float> outNormalZ,
                    Span<int> outLayerIndex,
                    Span<byte> outHitMask,
                    int layerIndex = 0)
                {
                    for (int i = 0; i < outHeightCm.Length; i++)
                    {
                        outWorldXCm[i] = originXMeters[i] * 100f;
                        outWorldYCm[i] = originZMeters[i] * 100f;
                        outHeightCm[i] = 0f;
                        outDistanceMeters[i] = 0f;
                        outNormalX[i] = 0f;
                        outNormalY[i] = 1f;
                        outNormalZ[i] = 0f;
                        outLayerIndex[i] = layerIndex;
                        outHitMask[i] = 1;
                    }

                    return true;
                }
            }
        }
    }
}
