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
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public class PerformerBenchmarkTests
    {
        private const int ENTITY_COUNT = 1000;
        private const int INSTANCE_COUNT = 500;
        private const int EVENTS_PER_FRAME = 200;
        private const int FRAMES = 100;

        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private GameplayEventBus _eventBus;
        private PresentationEventStream _presEvents;
        private PerformerCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private PerformerInstanceBuffer _instances;
        private GraphProgramRegistry _programs;
        private Dictionary<string, object> _globals;
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;
        private RoadSplineBuffer _roadSplines;
        private PresentationRequestBuffer _requests;
        private SoundRequestBuffer _soundRequests;
        private PresentationRequestFlushSystem _flush;
        private PresentationBridgeSystem _bridge;
        private PerformerRuleSystem _ruleSystem;
        private PerformerRuntimeSystem _runtimeSystem;
        private PerformerBehaviorSystem _behaviorSystem;
        private PerformerEmitSystem _emitSystem;
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
            _commands = new PerformerCommandBuffer(16384);
            _defs = new PerformerDefinitionRegistry();
            _instances = new PerformerInstanceBuffer(8192);
            _programs = new GraphProgramRegistry();
            _globals = new Dictionary<string, object>();
            _primitives = new PrimitiveDrawBuffer(16384);
            _hud = new WorldHudBatchBuffer(16384);
            _overlays = new GroundOverlayBuffer(4096);
            _roadSplines = new RoadSplineBuffer();
            _requests = new PresentationRequestBuffer();
            _soundRequests = new SoundRequestBuffer();
            _stableIds = new PresentationStableIdAllocator();

            _healthAttrId = AttributeRegistry.Register("Health");

            LoadCorePerformerDefinitions(_defs, _healthAttrId);
            _healthBarDefId = RegisterHealthBarDefinition(_defs, _healthAttrId);

            var session = new GameSession();
            var graphApi = new GasGraphRuntimeApi(_world, null, null, null);
            _bridge = new PresentationBridgeSystem(_world, _eventBus, _presEvents, session, _gasEvents);
            _ruleSystem = new PerformerRuleSystem(_world, _presEvents, _commands, _defs, _instances, _programs, graphApi, _globals);
            _runtimeSystem = new PerformerRuntimeSystem(_world, _commands, _presEvents, new TransientMarkerBuffer(), _requests, _instances, _stableIds, _defs);
            _behaviorSystem = new PerformerBehaviorSystem(_world, _instances, _defs, _presEvents, _soundRequests);
            _emitSystem = new PerformerEmitSystem(_world, _instances, _defs, _requests, _globals);
            _flush = new PresentationRequestFlushSystem(_world, _requests, new PrefabRegistry(), new MeshAssetRegistry(), _primitives, _overlays, _hud, _roadSplines);
        }

        [TearDown]
        public void TearDown()
        {
            _emitSystem?.Dispose();
            _behaviorSystem?.Dispose();
            _flush?.Dispose();
            _runtimeSystem?.Dispose();
            _ruleSystem?.Dispose();
            _bridge?.Dispose();
            _world?.Dispose();
        }

        private void ClearOutputBuffers()
        {
            _requests.Clear();
            _soundRequests.Clear();
            _hud.Clear();
            _primitives.Clear();
            _overlays.Clear();
            _roadSplines.Clear();
        }

        private Entity CreateOwner(Vector3 position, AttributeBuffer attributes = default, bool hasAttributes = false, bool visible = true)
        {
            var entity = _world.Create(new VisualTransform { Position = position });
            _world.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });
            if (hasAttributes)
            {
                _world.Add(entity, attributes);
            }
            if (!visible)
            {
                _world.Add(entity, new CullState { IsVisible = false });
            }
            return entity;
        }

        private void TickPipeline(float dt)
        {
            ClearOutputBuffers();
            _bridge.Update(dt);
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
                _defs.Register($"bench.rule.{i}", new PerformerDefinition
                {
                    Rules = new[]
                    {
                        new PerformerRule
                        {
                            Event = new EventFilter { Kind = PresentationEventKind.EffectApplied, KeyId = i },
                            Condition = ConditionRef.AlwaysTrue,
                            Command = new PerformerCommand
                            {
                                CommandKind = PerformerCommandKind.CreatePerformer,
                                PerformerDefinitionId = defId,
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
            PrintResult("PerformerRuleSystem.EventMatching", sw, startAlloc, endAlloc, FRAMES * EVENTS_PER_FRAME);
        }

        [Test]
        public void Benchmark_EmitSystem_InstanceScoped_Text()
        {
            for (int i = 0; i < INSTANCE_COUNT; i++)
            {
                var owner = CreateOwner(new Vector3(i, 0f, i));
                Assert.That(_instances.TryAllocate(_defs.GetId(WellKnownPerformerKeys.FloatingCombatText), owner, -1, out int handle), Is.True);
                _instances.Get(handle).BehaviorActiveMask = 1u;
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
            PrintResult("PerformerEmitSystem.InstanceScoped.Text", sw, startAlloc, endAlloc, INSTANCE_COUNT * FRAMES);
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
                Assert.That(_instances.TryAllocate(defId, owner, i + 1, out int handle), Is.True);
                _instances.Get(handle).BehaviorActiveMask = 0b11u;
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
            PrintResult("PerformerEmitSystem.InstanceScoped.HealthBars", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
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
                Assert.That(_instances.TryAllocate(defId, owner, i + 1, out int handle), Is.True);
                _instances.Get(handle).BehaviorActiveMask = 0b11u;
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
            PrintResult("PerformerEmitSystem.InstanceScoped.WithCulling", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_InstanceBuffer_AllocateRelease()
        {
            var buf = new PerformerInstanceBuffer(8192);
            var entity = CreateOwner(Vector3.Zero);

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            int totalOps = 0;
            for (int frame = 0; frame < FRAMES; frame++)
            {
                for (int i = 0; i < INSTANCE_COUNT; i++)
                {
                    buf.TryAllocate(1, entity, i % 10, out _);
                    totalOps++;
                }

                for (int i = 0; i < INSTANCE_COUNT / 2; i++)
                {
                    buf.Release(i);
                    totalOps++;
                }

                for (int s = 0; s < 5; s++)
                {
                    buf.ReleaseScope(s);
                    totalOps++;
                }

                buf.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PerformerInstanceBuffer.AllocateRelease", sw, startAlloc, endAlloc, totalOps);
        }

        [Test]
        public void Benchmark_InstanceBuffer_ProcessActive_SparseSlots()
        {
            var buf = new PerformerInstanceBuffer(4096);
            var entity = CreateOwner(Vector3.Zero);
            for (int i = 0; i < 2000; i++)
            {
                buf.TryAllocate(1, entity, 0, out _);
            }
            for (int i = 0; i < 2000; i += 2)
            {
                buf.Release(i);
            }

            int callbackCount = 0;
            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES * 10; frame++)
            {
                callbackCount += buf.ProcessActive(0.016f, (int handle, ref PerformerInstance inst) => { });
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PerformerInstanceBuffer.ProcessActive.SparseSlots", sw, startAlloc, endAlloc, callbackCount);
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
                Assert.That(_instances.TryAllocate(defId, owner, i + 1, out int handle), Is.True);
                _instances.Get(handle).BehaviorActiveMask = 0b11u;
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
            PrintResult("PerformerEmitSystem.ParamResolution.ManyBindings", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_BridgeSystem_TagChangedBits()
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
                _bridge.Update(0.016f);
                _presEvents.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            PrintResult("PresentationBridgeSystem.TagChangedBits", sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES);
        }

        [Test]
        public void Benchmark_RuleSystem_DefinitionScaling()
        {
            var actor = CreateOwner(Vector3.Zero);
            int[] defCounts = { 5, 20, 50, 100 };

            foreach (int defCount in defCounts)
            {
                var defs = new PerformerDefinitionRegistry();
                var events = new PresentationEventStream(16384);
            var commands = new PerformerCommandBuffer(16384);
                var programs = new GraphProgramRegistry();
                var graphApi = new GasGraphRuntimeApi(_world, null, null, null);
            using var system = new PerformerRuleSystem(_world, events, commands, defs, instances: null, programs, graphApi, _globals);

                for (int d = 0; d < defCount; d++)
                {
                    int defId = defs.GetOrRegisterId($"bench.scale.{d}");
                    defs.Register($"bench.scale.{d}", new PerformerDefinition
                    {
                        Rules = new[]
                        {
                            new PerformerRule
                            {
                                Event = new EventFilter { Kind = PresentationEventKind.EffectApplied, KeyId = d + 1000 },
                                Condition = ConditionRef.AlwaysTrue,
                                Command = new PerformerCommand
                                {
                                    CommandKind = PerformerCommandKind.CreatePerformer,
                                    PerformerDefinitionId = defId,
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
                Console.WriteLine($"[Benchmark] PerformerRuleSystem.DefinitionScaling defs={defCount}: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms/frame");
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
                Assert.That(_instances.TryAllocate(defId, owner, i + 1, out int handle), Is.True);
                _instances.Get(handle).BehaviorActiveMask = 0b11u;
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
            Console.WriteLine($"[Benchmark] PerformerEmitSystem.InstanceScoped avg frame: {emitAvgMs:F2}ms ({budgetPercent:F1}% of 16.6ms budget)");
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

        private static int RegisterHealthBarDefinition(PerformerDefinitionRegistry defs, int healthAttrId)
        {
            return defs.Register(WellKnownPerformerKeys.EntityHealthBar, new PerformerDefinition
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
                            TargetParamKey = WellKnownPerformerParamKeys.BarFillRatio,
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
                            MaterialParamKey = WellKnownPerformerParamKeys.BarFillRatio,
                            AssetSwapParamKey = -1,
                            VisibilityParamKey = -1,
                            Grounding = GroundingMode.None,
                            GroundingOffset = 0f,
                        }
                    }
                },
                VisibilityCondition = new ConditionRef { Inline = InlineConditionKind.OwnerCullVisible },
                DefaultColor = new Vector4(0f, 1f, 0f, 1f),
                PositionOffset = new Vector3(0f, 1.5f, 0f),
            });
        }

        private static void LoadCorePerformerDefinitions(PerformerDefinitionRegistry defs, int healthAttrId)
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
            var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);

            new PerformerDefinitionConfigLoader(
                pipeline,
                defs,
                resolveAttributeName: name => string.Equals(name, "Health", StringComparison.Ordinal) ? healthAttrId : 0,
                resolveMeshId: meshes.GetId,
                resolveTextTokenId: textCatalog.GetTokenId,
                resolveBehaviorAssetId: (kind, key) => kind switch
                {
                    AssetKind.Mesh => meshes.GetId(key),
                    AssetKind.WorldText => textCatalog.GetTokenId(key),
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
