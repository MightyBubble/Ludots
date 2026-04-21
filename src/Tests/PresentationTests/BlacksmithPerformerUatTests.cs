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
using Ludots.Core.Presentation.Performers;
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
    public sealed class BlacksmithPerformerUatTests
    {
        private const int DurabilityAttributeId = 1;
        private const int WorkingTagId = 1;
        private const int BlacksmithTemplateKeyId = 1;

        private const int WorkshopIntactAssetId = 1001;
        private const int WorkshopDamagedAssetId = 1002;
        private const int WorkshopRuinedAssetId = 1003;
        private const int FurnaceAssetId = 1100;
        private const int SmokeAssetId = 1200;
        private const int WorkerAssetId = 1300;
        private const int HammerSoundAssetId = 1400;
        private const int BrickNorthMaterialId = 5001;
        private const int BrickSouthMaterialId = 5002;

        private const string BlacksmithRootKey = "blacksmith_root";
        private const string BlacksmithWorkshop1Key = "blacksmith_workshop_1";
        private const string BlacksmithWorkshop2Key = "blacksmith_workshop_2";
        private const string BlacksmithFurnaceKey = "blacksmith_furnace";
        private const string BlacksmithSmokeKey = "blacksmith_smoke";
        private const string BlacksmithWorkerKey = "blacksmith_worker";

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
            PerformerScopeTagRegistry.Clear();
            AttributeRegistry.Register("durability");
            TagRegistry.Register("working");
        }

        [TearDown]
        public void TearDown()
        {
            TagRegistry.Clear();
            PerformerScopeTagRegistry.Clear();
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
            Assert.That(fixture.CountVisualByAsset(WorkshopIntactAssetId), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByAsset(FurnaceAssetId), Is.EqualTo(1));
        }

        [Test]
        public void BlacksmithUat_PerformerFixture_UsesBehaviorSchemaWithoutVisualKind()
        {
            string performersPath = Path.Combine(
                _repoRoot,
                "mods",
                "fixtures",
                "blacksmith",
                "BlacksmithTestMod",
                "assets",
                "Presentation",
                "performers.json");

            string json = File.ReadAllText(performersPath);

            Assert.That(json, Does.Not.Contain("\"visualKind\""));
            Assert.That(json, Does.Contain("\"kind\": \"AssetBinding\""));
        }

        [Test]
        public void BlacksmithUat_DoesNotWirePrefabRegistryIntoPerformerRuntime()
        {
            string sourcePath = Path.Combine(
                _repoRoot,
                "src",
                "Tests",
                "PresentationTests",
                "BlacksmithPerformerUatTests.cs");

            string source = File.ReadAllText(sourcePath);

            string forbiddenConstruction = "new " + nameof(PrefabRegistry) + "()";
            Assert.That(source, Does.Not.Contain(forbiddenConstruction));
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
            Assert.That(fixture.CountVisualByAsset(SmokeAssetId), Is.EqualTo(1));
            Assert.That(fixture.CountVisualByAsset(WorkerAssetId), Is.EqualTo(1));
            Assert.That(fixture.HasSoundRequest(SoundRequestKind.PlayOrUpdate, HammerSoundAssetId), Is.True);
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
            Assert.That(fixture.CountVisualByAsset(SmokeAssetId), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAsset(WorkerAssetId), Is.EqualTo(0));
            Assert.That(fixture.HasSoundRequest(SoundRequestKind.Stop, HammerSoundAssetId), Is.True);
        }

        [Test]
        public void BlacksmithUat_GlobalDayNight_UpdatesLampParam()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.PublishGlobalDayNight(blacksmith, phase: 1f);
            fixture.Tick();

            Entity rootEntity = fixture.RequireHandle(BlacksmithRootKey);
            Assert.That(fixture.Instances.ResolveFloat(rootEntity, 200), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void BlacksmithUat_RegionNorth_UsesBlackBrick()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            fixture.SpawnBlacksmithAndWarmup();

            Assert.That(fixture.CountVisualByMaterial(BrickNorthMaterialId), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByMaterial(BrickSouthMaterialId), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithUat_RegionSouth_UsesRedBrick()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.PublishGlobalRegionChanged(blacksmith, regionId: 1);
            fixture.Tick();

            Assert.That(fixture.CountVisualByMaterial(BrickSouthMaterialId), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByMaterial(BrickNorthMaterialId), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithUat_DurabilityHalf_SwapsWorkshopToDamaged()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.SetDurability(blacksmith, current: 50f, max: 100f);
            fixture.Tick();

            Assert.That(fixture.CountVisualByAsset(WorkshopDamagedAssetId), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByAsset(WorkshopIntactAssetId), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAsset(WorkshopRuinedAssetId), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithUat_DurabilityZero_SwapsWorkshopToRuined()
        {
            using var fixture = BlacksmithPipelineFixture.Create(_repoRoot, _coreRoot);
            Entity blacksmith = fixture.SpawnBlacksmithAndWarmup();

            fixture.SetDurability(blacksmith, current: 0f, max: 100f);
            fixture.Tick();

            Assert.That(fixture.CountVisualByAsset(WorkshopRuinedAssetId), Is.EqualTo(2));
            Assert.That(fixture.CountVisualByAsset(WorkshopIntactAssetId), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAsset(WorkshopDamagedAssetId), Is.EqualTo(0));
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
            Assert.That(fixture.CountVisualByAsset(WorkshopIntactAssetId), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAsset(FurnaceAssetId), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAsset(SmokeAssetId), Is.EqualTo(0));
            Assert.That(fixture.CountVisualByAsset(WorkerAssetId), Is.EqualTo(0));
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
            private readonly PerformerDefinitionRegistry _definitions;
            private readonly PresentationEventStream _events;
            private readonly PerformerCommandBuffer _commands;
            private readonly PerformerEntityRuntime _instances;
            private readonly PerformerAnimatorStateBuffer _animatorStates;
            private readonly PresentationRequestBuffer _requests;
            private readonly SoundRequestBuffer _soundRequests;
            private readonly GlobalPresentationEventBuffer _globalEvents;

            private readonly PresentationEntityLifecycleSystem _entityLifecycle;
            private readonly GlobalEventBridgeSystem _globalBridge;
            private readonly PerformerRuleSystem _rules;
            private readonly PerformerRuntimeSystem _runtime;
            private readonly AnimatorRuntimeSystem _animator;
            private readonly PerformerBehaviorSystem _behavior;
            private readonly PerformerEmitSystem _emit;

            private int _stableSeed = 7000;

            private BlacksmithPipelineFixture(
                World world,
                ModLoader modLoader,
                PerformerDefinitionRegistry definitions,
                PresentationEventStream events,
                PerformerCommandBuffer commands,
                PerformerEntityRuntime instances,
                PerformerAnimatorStateBuffer animatorStates,
                PresentationRequestBuffer requests,
                SoundRequestBuffer soundRequests,
                GlobalPresentationEventBuffer globalEvents,
                PresentationEntityLifecycleSystem entityLifecycle,
                GlobalEventBridgeSystem globalBridge,
                PerformerRuleSystem rules,
                PerformerRuntimeSystem runtime,
                AnimatorRuntimeSystem animator,
                PerformerBehaviorSystem behavior,
                PerformerEmitSystem emit)
            {
                _world = world;
                _modLoader = modLoader;
                _definitions = definitions;
                _events = events;
                _commands = commands;
                _instances = instances;
                _animatorStates = animatorStates;
                _requests = requests;
                _soundRequests = soundRequests;
                _globalEvents = globalEvents;
                _entityLifecycle = entityLifecycle;
                _globalBridge = globalBridge;
                _rules = rules;
                _runtime = runtime;
                _animator = animator;
                _behavior = behavior;
                _emit = emit;
            }

            public PerformerEntityRuntime Instances => _instances;

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

                var meshAssets = new MeshAssetRegistry();
                new MeshAssetConfigLoader(pipeline, meshAssets).Load(catalog);

                var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
                var animatorControllers = new AnimatorControllerRegistry();
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
                animationClips.Register(
                    "worker_loop",
                    new AnimationClipDefinition
                    {
                        AssetKind = AnimationClipAssetKind.Clip,
                        Locators = [new AnimationClipLocatorDefinition("raylib", "animations/worker_loop.glb#anim:loop", string.Empty)]
                    });

                var animationProfiles = new AnimationProfileRegistry();
                animationProfiles.Register(
                    "worker_profile",
                    new AnimationProfileDefinition
                    {
                        AnimatorControllerId = animatorControllers.GetId("worker_anim"),
                        StateClips = [new AnimationStateClipBinding { PackedStateIndex = 12, ClipAssetId = animationClips.GetId("worker_loop") }]
                    });

                var definitions = new PerformerDefinitionRegistry();
                new PerformerDefinitionConfigLoader(
                    pipeline,
                    definitions,
                    resolveAttributeName: AttributeRegistry.GetId,
                    resolveMeshId: meshAssets.GetId,
                    resolveTextTokenId: textCatalog.GetTokenId,
                    resolveEntityTemplateKey: key => string.Equals(key, "blacksmith", StringComparison.Ordinal) ? BlacksmithTemplateKeyId : 0,
                    resolveEffectTemplateId: _ => 0,
                    resolveMaterialId: _ => 0,
                    resolveAnimatorControllerId: animatorControllers.GetId,
                    resolveAnimationProfileId: animationProfiles.GetId,
                    resolveBehaviorAssetId: ResolveBehaviorAssetId).Load(catalog);

                var events = new PresentationEventStream(512);
                var commands = new PerformerCommandBuffer(512);
                var instances = new PerformerEntityRuntime(world);
                var animatorStates = new PerformerAnimatorStateBuffer(64);
                var requests = new PresentationRequestBuffer(512);
                var soundRequests = new SoundRequestBuffer(256);
                var globalEvents = new GlobalPresentationEventBuffer(64);

                var gameSession = new GameSession();
                var graphPrograms = new GraphProgramRegistry();
                var graphApi = new GasGraphRuntimeApi(world);
                var stableIds = new PresentationStableIdAllocator();

                var entityLifecycle = new PresentationEntityLifecycleSystem(world, events, instances, definitions);
                var globalBridge = new GlobalEventBridgeSystem(world, globalEvents, events, gameSession);
                var rules = new PerformerRuleSystem(world, events, commands, definitions, instances, graphPrograms, graphApi, new Dictionary<string, object>());
                var runtime = new PerformerRuntimeSystem(
                    world,
                    commands,
                    events,
                    new TransientMarkerBuffer(),
                    requests,
                    instances,
                    stableIds,
                    definitions,
                    animatorStates);
                var animator = new AnimatorRuntimeSystem(world, animatorControllers, instances, definitions, animatorStates);
                var behavior = new PerformerBehaviorSystem(world, instances, definitions, events, soundRequests, new FlatHeightmap());
                var emit = new PerformerEmitSystem(world, instances, definitions, requests, new Dictionary<string, object>(), animatorStates, soundRequests);

                Assert.That(definitions.GetId(BlacksmithRootKey), Is.GreaterThan(0));
                Assert.That(PerformerScopeTagRegistry.GetId("working"), Is.GreaterThan(0));

                return new BlacksmithPipelineFixture(
                    world,
                    modLoader,
                    definitions,
                    events,
                    commands,
                    instances,
                    animatorStates,
                    requests,
                    soundRequests,
                    globalEvents,
                    entityLifecycle,
                    globalBridge,
                    rules,
                    runtime,
                    animator,
                    behavior,
                    emit);

                static int ResolveBehaviorAssetId(AssetKind kind, string key)
                {
                    return (kind, key) switch
                    {
                        (AssetKind.VFX, "chimney_smoke") => SmokeAssetId,
                        (AssetKind.Spline, "blacksmith_patrol") => 1500,
                        (AssetKind.Sound, "anvil_hammering") => HammerSoundAssetId,
                        _ => 0,
                    };
                }
            }

            public Entity SpawnBlacksmith()
            {
                var attributes = default(AttributeBuffer);
                attributes.SetBase(DurabilityAttributeId, 100f);
                attributes.SetCurrent(DurabilityAttributeId, 100f);

                return _world.Create(
                    new PresentationStableId { Value = _stableSeed++ },
                    new EntityTemplateKeyCm { TemplateKeyId = BlacksmithTemplateKeyId },
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
                attributes.SetBase(DurabilityAttributeId, max);
                attributes.SetCurrent(DurabilityAttributeId, current);

                int durabilityBandEventKey = current <= 0f ? 9102 : current <= 66f ? 9101 : 9100;
                Assert.That(_events.TryAdd(new PresentationEvent
                {
                    Kind = PresentationEventKind.AttributeValueChanged,
                    KeyId = durabilityBandEventKey,
                    Source = owner,
                    Target = owner,
                    Magnitude = max <= 0f ? 0f : current / max,
                }), Is.True);
            }

            public void SetWorking(Entity owner, bool enabled)
            {
                ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(owner);
                if (enabled)
                {
                    tags.AddTag(WorkingTagId);
                }
                else
                {
                    tags.RemoveTag(WorkingTagId);
                }
            }

            public void PublishTagEvent(Entity owner, bool gained)
            {
                Assert.That(
                    _events.TryAdd(new PresentationEvent
                    {
                        Kind = PresentationEventKind.TagEffectiveChanged,
                        KeyId = WorkingTagId,
                        Source = owner,
                        Target = owner,
                        Magnitude = gained ? 1f : 0f,
                    }),
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
                    _world.Add(owner, new PresentationLifecycleState { PendingDestroy = true, Spawned = true });
                    return;
                }

                ref PresentationLifecycleState state = ref _world.Get<PresentationLifecycleState>(owner);
                state.PendingDestroy = true;
            }

            public void Tick(float dt = 0.016f)
            {
                _requests.Clear();
                _soundRequests.Clear();

                _entityLifecycle.Update(dt);
                _globalBridge.Update(dt);
                for (int i = 0; i < 4; i++)
                {
                    _rules.Update(dt);
                    _runtime.Update(dt);
                    if (_commands.Count == 0 &&
                        (_events.Count == 0 || ContainsPerformerDestroyedEvent()))
                    {
                        break;
                    }
                }

                _animator.Update(dt);
                _behavior.Update(dt);
                _emit.Update(dt);
            }

            private bool ContainsPerformerDestroyedEvent()
            {
                foreach (ref readonly PresentationEvent evt in _events.GetSpan())
                {
                    if (evt.Kind == PresentationEventKind.PerformerDestroyed)
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
                var query = new QueryDescription().WithAll<PerformerState>();
                _world.Query(in query, (Entity entity, ref PerformerState state) =>
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
                var query = new QueryDescription().WithAll<PerformerState>();
                _world.Query(in query, (Entity entity, ref PerformerState state) =>
                {
                    if (state.DefId == definitionId && found == Entity.Null)
                    {
                        found = entity;
                    }
                });
                if (found == Entity.Null)
                    throw new InvalidOperationException($"No active performer for definition '{definitionKey}'.");
                return found;
            }

            public int CountVisualByAsset(int assetId)
            {
                int count = 0;
                foreach (ref readonly PresentationRequest request in _requests.GetSpan())
                {
                    if (request.Kind == PresentationRequestKind.VisualProxy &&
                        request.VisualProxy.MeshAssetId == assetId &&
                        request.VisualProxy.Visibility == VisualVisibility.Visible)
                    {
                        count++;
                    }
                }

                return count;
            }

            public int CountVisualByMaterial(int materialId)
            {
                int count = 0;
                foreach (ref readonly PresentationRequest request in _requests.GetSpan())
                {
                    if (request.Kind == PresentationRequestKind.VisualProxy &&
                        request.VisualProxy.MaterialId == materialId &&
                        request.VisualProxy.Visibility == VisualVisibility.Visible)
                    {
                        count++;
                    }
                }

                return count;
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

            public void Dispose()
            {
                _emit.Dispose();
                _behavior.Dispose();
                _animator.Dispose();
                _runtime.Dispose();
                _rules.Dispose();
                _globalBridge.Dispose();
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
