using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
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
using Ludots.Core.Presentation.Requests;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    [Category("benchmark")]
    public class PresenterBenchmarkTests
    {
        private const int ENTITY_COUNT = 1000;
        private const int INSTANCE_COUNT = 500;
        private const int EVENTS_PER_FRAME = 200;
        private const int FRAMES = 100;

        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private GameplayEventBus _eventBus;
        private PresentationEventStream _presEvents;
        private PresenterCommandBuffer _commands;
        private PresenterDefinitionRegistry _defs;
        private PresenterEntityRuntime _instances;
        private GraphProgramRegistry _programs;
        private Dictionary<string, object> _globals;
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;
        private SplineRibbonBuffer _splineRibbons;
        private PresentationRequestBuffer _requests;
        private SoundRequestBuffer _soundRequests;
        private PresentationOwnerChangeBuffer _ownerChanges;
        private PresentationRequestFlushSystem _flush;
        private GameplayPresentationProjectionSystem _projection;
        private PresenterRuleSystem _ruleSystem;
        private PresenterRuntimeSystem _runtimeSystem;
        private PresenterBehaviorSystem _behaviorSystem;
        private PresenterEmitSystem _emitSystem;
        private PresentationStableIdAllocator _stableIds;
        private int _healthAttrId;
        private int _healthBarDefId;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _gasEvents = new GasPresentationEventBuffer(16384);
            _eventBus = new GameplayEventBus();
            _presEvents = new PresentationEventStream(16384);
            _commands = new PresenterCommandBuffer(16384);
            _defs = new PresenterDefinitionRegistry();
            _instances = new PresenterEntityRuntime(_world);
            _programs = new GraphProgramRegistry();
            _globals = new Dictionary<string, object>();
            _primitives = new PrimitiveDrawBuffer(16384);
            var stableDrawCache = new StableDrawCache(16384);
            var snapshotBuffer = new PrimitiveDrawBuffer(16384);
            var proxyBuffer = new PresentationVisualProxyBuffer(16384);
            var skinnedBatchBuffer = new SkinnedVisualBatchBuffer(16384);
            _hud = new WorldHudBatchBuffer(16384);
            _overlays = new GroundOverlayBuffer(4096);
            _splineRibbons = new SplineRibbonBuffer();
            _requests = new PresentationRequestBuffer();
            _soundRequests = new SoundRequestBuffer();
            _ownerChanges = new PresentationOwnerChangeBuffer(16384);
            _stableIds = new PresentationStableIdAllocator();

            _healthAttrId = AttributeRegistry.Register("Health");

            LoadCorePresenterDefinitions(_defs, _healthAttrId);
            _healthBarDefId = RegisterHealthBarDefinition(_defs, _healthAttrId);

            var session = new GameSession();
            var graphApi = new GasGraphRuntimeApi(_world, null, null, null);
            _projection = new GameplayPresentationProjectionSystem(_world, _eventBus, _presEvents, session, _gasEvents, _ownerChanges);
            _ruleSystem = new PresenterRuleSystem(_world, _presEvents, _commands, _defs, _instances, _programs, graphApi, _globals);
            _runtimeSystem = new PresenterRuntimeSystem(_world, _commands, _presEvents, new TransientMarkerBuffer(), _requests, _instances, _stableIds, _defs);
            _behaviorSystem = new PresenterBehaviorSystem(_world, _instances, _defs, _presEvents, _ownerChanges, _soundRequests);
            _emitSystem = new PresenterEmitSystem(_world, _instances, _defs, _requests, _globals);
            _flush = new PresentationRequestFlushSystem(_world, _requests, new MeshAssetRegistry(), stableDrawCache, _primitives, _overlays, _hud, _splineRibbons, snapshotBuffer, proxyBuffer, skinnedBatchBuffer);
        }

        [TearDown]
        public void TearDown()
        {
            _emitSystem?.Dispose();
            _behaviorSystem?.Dispose();
            _flush?.Dispose();
            _runtimeSystem?.Dispose();
            _ruleSystem?.Dispose();
            _projection?.Dispose();
            _world?.Dispose();
        }

        private void ClearOutputBuffers()
        {
            _requests.Clear();
            _soundRequests.Clear();
            _hud.Clear();
            _primitives.Clear();
            _overlays.Clear();
            _splineRibbons.Clear();
        }

        private Entity CreateOwner(Vector3 position, AttributeBuffer attributes = default, bool hasAttributes = false, bool visible = true)
            => CreateOwner(_world, position, attributes, hasAttributes, visible);

        private Entity CreateOwner(World world, Vector3 position, AttributeBuffer attributes = default, bool hasAttributes = false, bool visible = true)
        {
            var entity = world.Create(new VisualTransform { Position = position });
            world.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });
            if (hasAttributes)
            {
                world.Add(entity, attributes);
            }
            if (!visible)
            {
                world.Add(entity, new CullState { IsVisible = false });
            }
            return entity;
        }

        private void TickPipeline(float dt)
        {
            ClearOutputBuffers();
            _projection.Update(dt);
            _ruleSystem.Update(dt);
            _runtimeSystem.Update(dt);
            _behaviorSystem.Update(0f);
            _emitSystem.Update(dt);
            _flush.Update(dt);
        }

        [Test]
        public void Benchmark_RuleSystem_EventMatching()
        {
            var actor = CreateOwner(Vector3.Zero);
            for (int i = 0; i < 20; i++)
            {
                int defId = _defs.GetOrRegisterId($"bench.rule.{i}");
                _defs.Register($"bench.rule.{i}", new PresenterDefinition
                {
                    Rules = new[]
                    {
                        new PresenterRule
                        {
                            Event = new EventFilter { Kind = PresentationEventKind.EffectApplied, KeyId = i },
                            Condition = ConditionRef.AlwaysTrue,
                            Command = new PresenterCommand
                            {
                                CommandKind = PresenterCommandKind.CreatePresenter,
                                PresenterDefinitionId = defId,
                                ScopeTag = -1,
                            }
                        }
                    }
                });
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                for (int e = 0; e < EVENTS_PER_FRAME; e++)
                {
                    _presEvents.TryAdd(new PresentationEvent
                    {
                        Kind = PresentationEventKind.EffectApplied,
                        KeyId = e % 20,
                        Source = actor,
                        Target = actor,
                    });
                }

                _ruleSystem.Update(0.016f);
                _commands.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresenterRuleSystem.EventMatching", sw, startAlloc, endAlloc, FRAMES * EVENTS_PER_FRAME);
        }

        [Test]
        public void Benchmark_EmitSystem_InstanceScoped_Text()
        {
            for (int i = 0; i < INSTANCE_COUNT; i++)
            {
                var owner = CreateOwner(new Vector3(i, 0f, i));
                var entity = _instances.Create(_defs.GetId(WellKnownPresenterKeys.FloatingCombatText), owner, -1);
                Assert.That(entity, Is.Not.EqualTo(Entity.Null));
                ref var state = ref _world.Get<PresenterState>(entity);
                state.BehaviorActiveMask = 1u;
                _instances.SetParam(entity, WellKnownPresenterParamKeys.TextValue0, ParamLane.Float, -25f, 0, Vector4.Zero);
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _emitSystem.Update(0.016f);
                _flush.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresenterEmitSystem.InstanceScoped.Text", sw, startAlloc, endAlloc, INSTANCE_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_EmitSystem_InstanceScoped_HealthBars()
        {
            int defId = _healthBarDefId;
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 70f + (i % 30));
                var owner = CreateOwner(new Vector3(i, 0f, i), attrBuf, hasAttributes: true);
                var entity = _instances.Create(defId, owner, i + 1);
                Assert.That(entity, Is.Not.EqualTo(Entity.Null));
                ref var state = ref _world.Get<PresenterState>(entity);
                state.BehaviorActiveMask = 0b11u;
                _instances.SetParam(
                    entity,
                    WellKnownPresenterParamKeys.BarFillRatio,
                    ParamLane.Float,
                    attrBuf.GetCurrent(_healthAttrId) / attrBuf.GetBase(_healthAttrId),
                    0,
                    Vector4.Zero);
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _behaviorSystem.Update(0f);
                _emitSystem.Update(0.016f);
                _flush.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresenterEmitSystem.InstanceScoped.HealthBars", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_EmitSystem_InstanceScoped_WithCulling()
        {
            int defId = _healthBarDefId;
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 80f);
                var owner = CreateOwner(new Vector3(i, 0f, i), attrBuf, hasAttributes: true, visible: i % 3 != 0);
                var entity = _instances.Create(defId, owner, i + 1);
                Assert.That(entity, Is.Not.EqualTo(Entity.Null));
                ref var state = ref _world.Get<PresenterState>(entity);
                state.BehaviorActiveMask = 0b11u;
                _instances.SetParam(entity, WellKnownPresenterParamKeys.BarFillRatio, ParamLane.Float, 0.8f, 0, Vector4.Zero);
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _behaviorSystem.Update(0f);
                _emitSystem.Update(0.016f);
                _flush.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresenterEmitSystem.InstanceScoped.WithCulling", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_InstanceBuffer_AllocateRelease()
        {
            var benchWorld = World.Create();
            var buf = new PresenterEntityRuntime(benchWorld);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("bench.runtime.instance", new PresenterDefinition());
            buf.BindDefinitions(definitions);
            var entity = CreateOwner(benchWorld, Vector3.Zero);

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            int totalOps = 0;
            for (int frame = 0; frame < FRAMES; frame++)
            {
                var created = new Entity[INSTANCE_COUNT];
                for (int i = 0; i < INSTANCE_COUNT; i++)
                {
                    created[i] = buf.Create(defId, entity, i % 10);
                    totalOps++;
                }

                for (int i = 0; i < INSTANCE_COUNT / 2; i++)
                {
                    buf.Destroy(created[i]);
                    totalOps++;
                }

                for (int s = 0; s < 5; s++)
                {
                    buf.DestroyScope(s + 1);
                    totalOps++;
                }
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresenterEntityRuntime.AllocateRelease", sw, startAlloc, endAlloc, totalOps);
            benchWorld.Dispose();
        }

        [Test]
        public void Benchmark_InstanceBuffer_ProcessActive_SparseSlots()
        {
            var benchWorld = World.Create();
            var buf = new PresenterEntityRuntime(benchWorld);
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("bench.runtime.instance", new PresenterDefinition());
            buf.BindDefinitions(definitions);
            var entity = CreateOwner(benchWorld, Vector3.Zero);
            var created = new Entity[2000];
            for (int i = 0; i < 2000; i++)
            {
                created[i] = buf.Create(defId, entity, 1);
            }
            for (int i = 0; i < 2000; i += 2)
            {
                buf.Destroy(created[i]);
            }

            int callbackCount = 0;
            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            var query = new QueryDescription().WithAll<PresenterState>();
            for (int frame = 0; frame < FRAMES * 10; frame++)
            {
                benchWorld.Query(in query, (Entity e, ref PresenterState state) => { callbackCount++; });
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresenterEntityRuntime.ProcessActive.SparseSlots", sw, startAlloc, endAlloc, callbackCount);
            benchWorld.Dispose();
        }

        [Test]
        public void Benchmark_FullPipeline_RealisticFrame()
        {
            var actor = CreateOwner(Vector3.Zero);
            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                for (int e = 0; e < 10; e++)
                {
                    _gasEvents.Publish(new GasPresentationEvent
                    {
                        Kind = e % 3 == 0 ? GasPresentationEventKind.CastCommitted : GasPresentationEventKind.EffectApplied,
                        Actor = actor,
                        Target = actor,
                        Delta = -10f,
                        AttributeId = _healthAttrId,
                        EffectTemplateId = 1,
                        AbilityId = 1,
                    });
                }

                TickPipeline(0.016f);
                _gasEvents.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("FullPipeline.RealisticFrame", sw, startAlloc, endAlloc, FRAMES);
        }

        [Test]
        public void Benchmark_FullPipeline_StressTest()
        {
            var actor = CreateOwner(Vector3.Zero);
            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                for (int e = 0; e < 50; e++)
                {
                    _gasEvents.Publish(new GasPresentationEvent
                    {
                        Kind = GasPresentationEventKind.EffectApplied,
                        Actor = actor,
                        Target = actor,
                        Delta = -(e + 1),
                        AttributeId = _healthAttrId,
                        EffectTemplateId = 1,
                    });
                }

                TickPipeline(0.016f);
                _gasEvents.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("FullPipeline.StressTest", sw, startAlloc, endAlloc, FRAMES);
        }

        [Test]
        public void Benchmark_ParamResolution_ManyBindings()
        {
            int defId = RegisterHealthBarDefinition(_defs, _healthAttrId);

            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 50f + (i % 50));
                var owner = CreateOwner(new Vector3(i, 0f, i), attrBuf, hasAttributes: true);
                var entity = _instances.Create(defId, owner, i + 1);
                Assert.That(entity, Is.Not.EqualTo(Entity.Null));
                ref var state = ref _world.Get<PresenterState>(entity);
                state.BehaviorActiveMask = 0b11u;
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _behaviorSystem.Update(0f);
                _emitSystem.Update(0.016f);
                _flush.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresenterEmitSystem.ParamResolution.ManyBindings", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_GameplayPresentationProjectionSystem_TagChangedBits()
        {
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var entity = _world.Create(new GameplayTagEffectiveChangedBits(), new GameplayTagEffectiveCache());
                ref var bits = ref _world.Get<GameplayTagEffectiveChangedBits>(entity);
                unsafe
                {
                    fixed (ulong* words = bits.Bits)
                    {
                        words[0] = (ulong)(i % 7 + 1);
                    }
                }
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                _projection.Update(0.016f);
                _presEvents.Clear();
                _ownerChanges.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("GameplayPresentationProjectionSystem.TagChangedBits", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_RuleSystem_DefinitionScaling()
        {
            var actor = CreateOwner(Vector3.Zero);
            int[] defCounts = { 5, 20, 50, 100 };

            foreach (int defCount in defCounts)
            {
                var defs = new PresenterDefinitionRegistry();
                var events = new PresentationEventStream(16384);
            var commands = new PresenterCommandBuffer(16384);
                var programs = new GraphProgramRegistry();
                var graphApi = new GasGraphRuntimeApi(_world, null, null, null);
            using var system = new PresenterRuleSystem(_world, events, commands, defs, runtime: null, programs, graphApi, _globals);

                for (int d = 0; d < defCount; d++)
                {
                    int defId = defs.GetOrRegisterId($"bench.scale.{d}");
                    defs.Register($"bench.scale.{d}", new PresenterDefinition
                    {
                        Rules = new[]
                        {
                            new PresenterRule
                            {
                                Event = new EventFilter { Kind = PresentationEventKind.EffectApplied, KeyId = d + 1000 },
                                Condition = ConditionRef.AlwaysTrue,
                                Command = new PresenterCommand
                                {
                                    CommandKind = PresenterCommandKind.CreatePresenter,
                                    PresenterDefinitionId = defId,
                                    ScopeTag = -1,
                                }
                            }
                        }
                    });
                }

                WarmUpGC();
                var sw = Stopwatch.StartNew();
                for (int frame = 0; frame < FRAMES; frame++)
                {
                    for (int e = 0; e < EVENTS_PER_FRAME; e++)
                    {
                        events.TryAdd(new PresentationEvent
                        {
                            Kind = PresentationEventKind.EffectApplied,
                            KeyId = 1000 + (e % defCount),
                            Source = actor,
                        });
                    }
                    system.Update(0.016f);
                    commands.Clear();
                }
                sw.Stop();
                Console.WriteLine($"[Benchmark] PresenterRuleSystem.DefinitionScaling defs={defCount}: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms/frame");
            }
        }

        [Test]
        public void Benchmark_OverheadComparison_SyncVsEmit()
        {
            int defId = _healthBarDefId;
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 80f);
                var owner = CreateOwner(new Vector3(i, 0f, i), attrBuf, hasAttributes: true);
                var entity = _instances.Create(defId, owner, i + 1);
                Assert.That(entity, Is.Not.EqualTo(Entity.Null));
                ref var state = ref _world.Get<PresenterState>(entity);
                state.BehaviorActiveMask = 0b11u;
            }

            WarmUpGC();
            var swEmit = Stopwatch.StartNew();
            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _behaviorSystem.Update(0f);
                _emitSystem.Update(0.016f);
                _flush.Update(0.016f);
            }
            swEmit.Stop();

            double emitAvgMs = swEmit.ElapsedMilliseconds / (double)FRAMES;
            double budgetPercent = emitAvgMs / 16.6 * 100;
            Console.WriteLine($"[Benchmark] PresenterEmitSystem.InstanceScoped avg frame: {emitAvgMs:F2}ms ({budgetPercent:F1}% of 16.6ms budget)");
        }

        private static void WarmUpGC()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void PrintResult(string name, Stopwatch sw, long startAlloc, long endAlloc, int totalOps, params string[] extra)
        {
            long allocBytes = endAlloc - startAlloc;
            double opsPerSecond = totalOps / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"[Benchmark] {name}:");
            Console.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Operations: {totalOps:N0}");
            Console.WriteLine($"  Ops/sec: {opsPerSecond:N0}");
            Console.WriteLine($"  GC Allocated (thread): {allocBytes:N0} bytes ({allocBytes / 1024.0:F2} KB)");
            Console.WriteLine($"  GC Collections: Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}");
            foreach (var line in extra)
            {
                Console.WriteLine(line);
            }
        }

        private static int RegisterHealthBarDefinition(PresenterDefinitionRegistry defs, int healthAttrId)
        {
            return defs.Register(WellKnownPresenterKeys.EntityHealthBar, new PresenterDefinition
            {
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AttributeBinding,
                        ActiveByDefault = true,
                        AttributeBinding = new AttributeBindingConfig
                        {
                            AttributeId = healthAttrId,
                            TargetParamKey = WellKnownPresenterParamKeys.BarFillRatio,
                            Mode = ValueSourceKind.AttributeRatio,
                            Thresholds = Array.Empty<ThresholdMapping>(),
                        }
                    },
                    new BehaviorSlot
                    {
                        SlotIndex = 1,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.WorldHud,
                            MaterialId = 0,
                            RenderPath = VisualRenderPath.None,
                            Mobility = VisualMobility.Movable,
                            LocalOffset = Vector3.Zero,
                            LocalRotation = Quaternion.Identity,
                            LocalScale = new Vector3(50f, 8f, 1f),
                            ScaleParamKey = -1,
                            ColorParamKey = -1,
                            MaterialParamKey = WellKnownPresenterParamKeys.BarFillRatio,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                            VisibilityParamKey = -1,
                        },
                        Style = new BehaviorStyleConfig
                        {
                            HasColor = true,
                            Color = new Vector4(0f, 1f, 0f, 1f),
                        },
                    }
                },
                PositionOffset = new Vector3(0f, 1.5f, 0f),
            });
        }

        private static void LoadCorePresenterDefinitions(PresenterDefinitionRegistry defs, int healthAttrId)
        {
            string repoRoot = FindRepoRoot();
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(repoRoot, "assets"));

            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            vfs.Mount("LudotsCoreMod", Path.Combine(repoRoot, "mods", "LudotsCoreMod"));
            modLoader.LoadedModIds.Add("LudotsCoreMod");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var meshes = new MeshAssetRegistry();
            new MeshAssetConfigLoader(pipeline, meshes).Load(catalog);
            var materialAssets = new PresentationMaterialRegistry();
            var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
            var templateRegistry = new DataRegistry<EntityTemplate>(pipeline);
            templateRegistry.Load("Entities/templates.json", catalog);
            var templateKeys = new EntityTemplateKeyRegistry();
            foreach (EntityTemplate template in templateRegistry.GetAll())
            {
                templateKeys.Register(template.Id);
            }

            var animatorControllers = new AnimatorControllerRegistry();
            new AnimatorControllerConfigLoader(pipeline, animatorControllers).Load(catalog);
            var animationClips = new AnimationClipRegistry();
            new AnimationClipConfigLoader(pipeline, animationClips).Load(catalog);
            var animationProfiles = new AnimationProfileRegistry();
            new AnimationProfileConfigLoader(pipeline, animationProfiles, animatorControllers, animationClips).Load(catalog);

            new PresenterDefinitionConfigLoader(
                pipeline,
                defs,
                resolveAttributeName: name => string.Equals(name, "Health", StringComparison.Ordinal) ? healthAttrId : 0,
                resolveMeshId: meshes.GetId,
                resolveTextTokenId: textCatalog.GetTokenId,
                resolveEntityTemplateKey: templateKeys.GetId,
                resolveMaterialId: materialAssets.GetId,
                resolveAnimatorControllerId: animatorControllers.GetId,
                resolveAnimationProfileId: animationProfiles.GetId,
                resolveBehaviorAssetId: (kind, key) => kind switch
                {
                    AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Spline or AssetKind.Sound => meshes.GetId(key),
                    AssetKind.WorldText => textCatalog.GetTokenId(key),
                    AssetKind.GroundOverlay => Enum.TryParse<GroundOverlayShape>(key, ignoreCase: false, out var shape) ? (int)shape : 0,
                    _ => 0,
                }).Load(catalog);
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
