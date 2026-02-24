using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Comprehensive performance benchmarks for the unified Performer pipeline.
    ///
    /// Measures:
    /// - PerformerRuleSystem event matching throughput
    /// - PerformerEmitSystem instance-scoped emission throughput
    /// - PerformerEmitSystem entity-scoped emission throughput
    /// - PerformerInstanceBuffer allocate/release throughput
    /// - Full pipeline throughput (Bridge → Rule → Runtime → Emit)
    /// - Parameter resolution overhead isolation
    ///
    /// All benchmarks track wall-clock time, per-thread GC allocations, and GC collections.
    /// Follow the established GasBenchmarkTests pattern for consistency.
    /// </summary>
    [TestFixture]
    public class PerformerBenchmarkTests
    {
        // ── Scale constants ──
        private const int ENTITY_COUNT = 1000;
        private const int INSTANCE_COUNT = 500;
        private const int EVENTS_PER_FRAME = 200;
        private const int FRAMES = 100;

        // ── Shared infrastructure ──
        private World _world;
        private GasPresentationEventBuffer _gasEvents;
        private GameplayEventBus _eventBus;
        private PresentationEventStream _presEvents;
        private PresentationCommandBuffer _commands;
        private PerformerDefinitionRegistry _defs;
        private PerformerInstanceBuffer _instances;
        private GraphProgramRegistry _programs;
        private Dictionary<string, object> _globals;

        // ── Output buffers ──
        private PrimitiveDrawBuffer _primitives;
        private WorldHudBatchBuffer _hud;
        private GroundOverlayBuffer _overlays;

        // ── Systems ──
        private PresentationBridgeSystem _bridge;
        private PerformerRuleSystem _ruleSystem;
        private PerformerRuntimeSystem _runtimeSystem;
        private PerformerEmitSystem _emitSystem;

        private int _healthAttrId;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _gasEvents = new GasPresentationEventBuffer(16384);
            _eventBus = new GameplayEventBus();
            _presEvents = new PresentationEventStream(16384);
            _commands = new PresentationCommandBuffer(16384);
            _defs = new PerformerDefinitionRegistry();
            _instances = new PerformerInstanceBuffer(8192);
            _programs = new GraphProgramRegistry();
            _globals = new Dictionary<string, object>();
            _primitives = new PrimitiveDrawBuffer(16384);
            _hud = new WorldHudBatchBuffer(16384);
            _overlays = new GroundOverlayBuffer(4096);

            _healthAttrId = AttributeRegistry.Register("Health");

            BuiltinPerformerDefinitions.Register(_defs);

            var session = new GameSession();
            var graphApi = new GasGraphRuntimeApi(_world, null, null, null);

            _bridge = new PresentationBridgeSystem(_world, _eventBus, _presEvents, session, _gasEvents);
            _ruleSystem = new PerformerRuleSystem(_world, _presEvents, _commands, _defs, _programs, graphApi, _globals);
            _runtimeSystem = new PerformerRuntimeSystem(_world, new PrefabRegistry(), _commands, _primitives, new TransientMarkerBuffer(), _instances);
            _emitSystem = new PerformerEmitSystem(_world, _instances, _defs, _overlays, _primitives, _hud, _programs, graphApi, _globals);
        }

        [TearDown]
        public void TearDown()
        {
            _emitSystem?.Dispose();
            _runtimeSystem?.Dispose();
            _ruleSystem?.Dispose();
            _bridge?.Dispose();
            _world?.Dispose();
        }

        private void ClearOutputBuffers()
        {
            _hud.Clear();
            _primitives.Clear();
            _overlays.Clear();
        }

        private void TickPipeline(float dt)
        {
            ClearOutputBuffers();
            _bridge.Update(dt);
            _ruleSystem.Update(dt);
            _runtimeSystem.Update(dt);
            _emitSystem.Update(dt);
        }

        // ════════════════════════════════════════════════════════════════════
        // B-1  PerformerRuleSystem — Event Matching Throughput
        //      Measures O(Events × Definitions × Rules) hot loop cost.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_RuleSystem_EventMatching()
        {
            // Register multiple definitions with rules to stress the matching loop
            for (int d = 100; d < 120; d++)
            {
                _defs.Register(d, new PerformerDefinition
                {
                    VisualKind = PerformerVisualKind.Marker3D,
                    MeshOrShapeId = 1,
                    DefaultLifetime = 0.5f,
                    Rules = new[]
                    {
                        new PerformerRule
                        {
                            Event = new EventFilter { Kind = PresentationEventKind.CastCommitted, KeyId = d },
                            Condition = ConditionRef.AlwaysTrue,
                            Command = new PerformerCommand
                            {
                                CommandKind = PresentationCommandKind.CreatePerformer,
                                PerformerDefinitionId = d,
                                ScopeId = -1,
                            }
                        },
                        new PerformerRule
                        {
                            Event = new EventFilter { Kind = PresentationEventKind.EffectApplied, KeyId = -1 },
                            Condition = ConditionRef.AlwaysTrue,
                            Command = new PerformerCommand
                            {
                                CommandKind = PresentationCommandKind.CreatePerformer,
                                PerformerDefinitionId = d,
                                ScopeId = -1,
                            }
                        },
                    }
                });
            }

            var actor = _world.Create(new VisualTransform { Position = Vector3.Zero });

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                // Inject events
                for (int e = 0; e < EVENTS_PER_FRAME; e++)
                {
                    _presEvents.TryAdd(new PresentationEvent
                    {
                        Kind = e % 2 == 0 ? PresentationEventKind.CastCommitted : PresentationEventKind.EffectApplied,
                        KeyId = 100 + (e % 20),
                        Source = actor,
                        Target = actor,
                    });
                }

                _ruleSystem.Update(0.016f);
                _commands.Clear();
                _instances.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            int totalRegisteredDefs = _defs.RegisteredIds.Count;
            int totalEventsDef = FRAMES * EVENTS_PER_FRAME;
            long totalComparisons = (long)totalEventsDef * totalRegisteredDefs * 2; // avg 2 rules per def

            PrintResult("PerformerRuleSystem.EventMatching",
                sw, startAlloc, endAlloc, totalEventsDef,
                $"  Registered Definitions: {totalRegisteredDefs}",
                $"  Events per frame: {EVENTS_PER_FRAME}",
                $"  Total event-def-rule comparisons: {totalComparisons:N0}",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-2  PerformerEmitSystem — Instance-Scoped Emission
        //      Measures per-instance tick + param resolution + draw output.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_EmitSystem_InstanceScoped()
        {
            // Pre-allocate instances
            var entities = new Entity[INSTANCE_COUNT];
            for (int i = 0; i < INSTANCE_COUNT; i++)
            {
                entities[i] = _world.Create(new VisualTransform { Position = new Vector3(i, 0, i) });
                // Allocate to FloatingCombatText (has AlphaFade, YDrift, bindings)
                _instances.TryAllocate(BuiltinPerformerIds.FloatingCombatText, entities[i], -1, out _);
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _emitSystem.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            int totalEmits = INSTANCE_COUNT * FRAMES;

            PrintResult("PerformerEmitSystem.InstanceScoped",
                sw, startAlloc, endAlloc, totalEmits,
                $"  Active instances: {INSTANCE_COUNT}",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms",
                $"  Avg per-instance: {sw.Elapsed.TotalMicroseconds / totalEmits:F2}μs");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-3  PerformerEmitSystem — Entity-Scoped Emission (Health Bars)
        //      Measures chunk iteration + visibility + param resolution.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_EmitSystem_EntityScoped_HealthBars()
        {
            // Create entities with VisualTransform + AttributeBuffer (health bar targets)
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 70f + (i % 30));
                _world.Create(
                    new VisualTransform { Position = new Vector3(i, 0, i) },
                    attrBuf);
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _emitSystem.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            int totalEmits = ENTITY_COUNT * FRAMES;

            PrintResult("PerformerEmitSystem.EntityScoped.HealthBars",
                sw, startAlloc, endAlloc, totalEmits,
                $"  Entities with AttributeBuffer: {ENTITY_COUNT}",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms",
                $"  Avg per-entity: {sw.Elapsed.TotalMicroseconds / totalEmits:F2}μs");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-4  PerformerEmitSystem — Entity-Scoped with CullState (mixed)
        //      Measures visibility check overhead with partial culling.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_EmitSystem_EntityScoped_WithCulling()
        {
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 80f);
                _world.Create(
                    new VisualTransform { Position = new Vector3(i, 0, i) },
                    attrBuf,
                    new CullState { IsVisible = i % 3 != 0 }); // 1/3 culled
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _emitSystem.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();

            PrintResult("PerformerEmitSystem.EntityScoped.WithCulling",
                sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES,
                $"  Entities: {ENTITY_COUNT} (1/3 culled)",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-5  PerformerInstanceBuffer — Allocate/Release Throughput
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_InstanceBuffer_AllocateRelease()
        {
            var buf = new PerformerInstanceBuffer(8192);
            var entity = _world.Create();

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            int totalOps = 0;
            for (int frame = 0; frame < FRAMES; frame++)
            {
                // Allocate a batch
                int batchSize = INSTANCE_COUNT;
                for (int i = 0; i < batchSize; i++)
                {
                    buf.TryAllocate(1, entity, i % 10, out _);
                    totalOps++;
                }

                // Release half individually
                for (int i = 0; i < batchSize / 2; i++)
                {
                    buf.Release(i);
                    totalOps++;
                }

                // Release by scope
                for (int s = 0; s < 5; s++)
                {
                    buf.ReleaseScope(s);
                    totalOps++;
                }

                buf.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();

            PrintResult("PerformerInstanceBuffer.AllocateRelease",
                sw, startAlloc, endAlloc, totalOps,
                $"  Batch size: {INSTANCE_COUNT}",
                $"  Avg per op: {sw.Elapsed.TotalMicroseconds / totalOps:F3}μs");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-6  PerformerInstanceBuffer — ProcessActive with Sparse Slots
        //      Measures the cost of iterating high-water-mark with many dead slots.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_InstanceBuffer_ProcessActive_SparseSlots()
        {
            var buf = new PerformerInstanceBuffer(4096);
            var entity = _world.Create();

            // Allocate 2000 slots then release every other one → 50% sparse
            for (int i = 0; i < 2000; i++)
                buf.TryAllocate(1, entity, 0, out _);
            for (int i = 0; i < 2000; i += 2)
                buf.Release(i);

            int activeCount = buf.ActiveCount;
            int callbackCount = 0;

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES * 10; frame++)
            {
                callbackCount += buf.ProcessActive(0.016f, (int handle, ref PerformerInstance inst) =>
                {
                    // Minimal callback — measure iteration overhead
                });
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();

            PrintResult("PerformerInstanceBuffer.ProcessActive.SparseSlots",
                sw, startAlloc, endAlloc, callbackCount,
                $"  HighWaterMark: 2000, Active: {activeCount} (50% sparse)",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)(FRAMES * 10):F3}ms",
                $"  Avg per-active-slot: {sw.Elapsed.TotalMicroseconds / callbackCount:F3}μs");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-7  Full Pipeline — Bridge → Rule → Runtime → Emit
        //      Simulates realistic gameplay frame with all systems active.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_FullPipeline_RealisticFrame()
        {
            // Create entities for entity-scoped health bars
            for (int i = 0; i < 200; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 80f);
                _world.Create(
                    new VisualTransform { Position = new Vector3(i, 0, i) },
                    attrBuf);
            }

            var actor = _world.Create(new VisualTransform { Position = Vector3.Zero });

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                // Simulate typical frame: a few GAS events
                for (int e = 0; e < 10; e++)
                {
                    _gasEvents.Publish(new GasPresentationEvent
                    {
                        Kind = e % 3 == 0 ? GasPresentationEventKind.CastCommitted : GasPresentationEventKind.EffectApplied,
                        Actor = actor,
                        Target = actor,
                        Delta = -10f,
                        EffectTemplateId = 1,
                        AbilityId = 1,
                    });
                }

                TickPipeline(0.016f);
                _gasEvents.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();

            PrintResult("FullPipeline.RealisticFrame",
                sw, startAlloc, endAlloc, FRAMES,
                $"  Entities: 200 (health bars), GAS events/frame: 10",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms",
                $"  HUD items last frame: {_hud.Count}",
                $"  Primitive items last frame: {_primitives.Count}");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-8  Full Pipeline — Stress Test with High Entity Count
        //      Worst-case scenario: many entities + many events per frame.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_FullPipeline_StressTest()
        {
            // Create many entities for entity-scoped performers
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 60f + (i % 40));
                _world.Create(
                    new VisualTransform { Position = new Vector3(i % 100, 0, i / 100) },
                    attrBuf);
            }

            var actor = _world.Create(new VisualTransform { Position = Vector3.Zero });

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                // Heavy event load
                for (int e = 0; e < 50; e++)
                {
                    _gasEvents.Publish(new GasPresentationEvent
                    {
                        Kind = GasPresentationEventKind.EffectApplied,
                        Actor = actor,
                        Target = actor,
                        Delta = -(e + 1),
                        EffectTemplateId = 1,
                    });
                }

                TickPipeline(0.016f);
                _gasEvents.Clear();
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();

            PrintResult("FullPipeline.StressTest",
                sw, startAlloc, endAlloc, FRAMES,
                $"  Entities: {ENTITY_COUNT}, GAS events/frame: 50",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms",
                $"  16.6ms budget usage: {(sw.ElapsedMilliseconds / (double)FRAMES / 16.6 * 100):F1}%",
                $"  HUD items last frame: {_hud.Count}",
                $"  Primitive items last frame: {_primitives.Count}",
                $"  Active instances: {_instances.ActiveCount}");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-9  ResolveParam Isolation — Measures binding resolution cost
        //      Entity-scoped with many bindings per definition.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_ParamResolution_ManyBindings()
        {
            // Register a definition with many bindings to stress ResolveParam's linear scan
            _defs.Register(200, new PerformerDefinition
            {
                VisualKind = PerformerVisualKind.WorldBar,
                EntityScope = EntityScopeFilter.AllWithAttributes,
                DefaultColor = new Vector4(0, 1, 0, 1),
                PositionOffset = new Vector3(0, 1.5f, 0),
                Bindings = new[]
                {
                    new PerformerParamBinding { ParamKey = 0, Value = ValueRef.FromAttributeRatio(_healthAttrId) },
                    new PerformerParamBinding { ParamKey = 1, Value = ValueRef.FromConstant(50f) },
                    new PerformerParamBinding { ParamKey = 2, Value = ValueRef.FromConstant(8f) },
                    new PerformerParamBinding { ParamKey = 4, Value = ValueRef.FromConstant(0.1f) },
                    new PerformerParamBinding { ParamKey = 5, Value = ValueRef.FromConstant(0.85f) },
                    new PerformerParamBinding { ParamKey = 6, Value = ValueRef.FromConstant(0.15f) },
                    new PerformerParamBinding { ParamKey = 7, Value = ValueRef.FromConstant(1.0f) },
                    new PerformerParamBinding { ParamKey = 8, Value = ValueRef.FromConstant(0.2f) },
                    new PerformerParamBinding { ParamKey = 9, Value = ValueRef.FromConstant(0.0f) },
                    new PerformerParamBinding { ParamKey = 10, Value = ValueRef.FromConstant(0.0f) },
                    new PerformerParamBinding { ParamKey = 11, Value = ValueRef.FromConstant(0.85f) },
                    new PerformerParamBinding { ParamKey = 12, Value = ValueRef.FromConstant(0.02f) },
                }
            });

            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 50f + (i % 50));
                _world.Create(
                    new VisualTransform { Position = new Vector3(i, 0, i) },
                    attrBuf);
            }

            WarmUpGC();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _emitSystem.Update(0.016f);
            }

            sw.Stop();
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();

            // Each entity triggers ~15 ResolveParam calls (WorldBar), each scanning up to 12 bindings
            long estimatedParamLookups = (long)ENTITY_COUNT * FRAMES * 15;

            PrintResult("PerformerEmitSystem.ParamResolution.ManyBindings",
                sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES,
                $"  Entities: {ENTITY_COUNT}, Bindings per def: 12",
                $"  Estimated ResolveParam calls: {estimatedParamLookups:N0}",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms",
                $"  Avg per-entity: {sw.Elapsed.TotalMicroseconds / (ENTITY_COUNT * FRAMES):F2}μs");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-10 PresentationBridgeSystem — Tag Changed Bits Throughput
        //      Measures tag-changed-bit scanning overhead.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_BridgeSystem_TagChangedBits()
        {
            // Create entities with tag changed bits (simulates GAS tag changes)
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var entity = _world.Create(
                    new GameplayTagEffectiveChangedBits(),
                    new GameplayTagEffectiveCache());

                // Set some changed bits to simulate tag changes
                ref var bits = ref _world.Get<GameplayTagEffectiveChangedBits>(entity);
                unsafe
                {
                    fixed (ulong* words = bits.Bits)
                    {
                        words[0] = (ulong)(i % 7 + 1); // a few bits set
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

            PrintResult("PresentationBridgeSystem.TagChangedBits",
                sw, startAlloc, endAlloc, ENTITY_COUNT * FRAMES,
                $"  Entities with TagChanged: {ENTITY_COUNT}",
                $"  Avg frame time: {sw.ElapsedMilliseconds / (double)FRAMES:F2}ms");
        }

        // ════════════════════════════════════════════════════════════════════
        // B-11 PerformerRuleSystem — Scaling Test
        //      How matching cost grows with definition count.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_RuleSystem_DefinitionScaling()
        {
            var actor = _world.Create(new VisualTransform { Position = Vector3.Zero });

            // Test with increasing definition counts
            int[] defCounts = { 5, 20, 50, 100 };

            Console.WriteLine("[Benchmark] PerformerRuleSystem Definition Scaling:");
            Console.WriteLine($"  Events per frame: {EVENTS_PER_FRAME}, Frames: {FRAMES}");
            Console.WriteLine("  -------------------------------------------------");

            foreach (int defCount in defCounts)
            {
                // Reset
                var defs = new PerformerDefinitionRegistry();
                var events = new PresentationEventStream(16384);
                var commands = new PresentationCommandBuffer(16384);
                var programs = new GraphProgramRegistry();
                var graphApi = new GasGraphRuntimeApi(_world, null, null, null);
                var system = new PerformerRuleSystem(_world, events, commands, defs, programs, graphApi, _globals);

                for (int d = 0; d < defCount; d++)
                {
                    defs.Register(d + 1000, new PerformerDefinition
                    {
                        VisualKind = PerformerVisualKind.Marker3D,
                        Rules = new[]
                        {
                            new PerformerRule
                            {
                                Event = new EventFilter { Kind = PresentationEventKind.EffectApplied, KeyId = d + 1000 },
                                Condition = ConditionRef.AlwaysTrue,
                                Command = new PerformerCommand
                                {
                                    CommandKind = PresentationCommandKind.CreatePerformer,
                                    PerformerDefinitionId = d + 1000,
                                    ScopeId = -1,
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
                double avgMs = sw.ElapsedMilliseconds / (double)FRAMES;
                Console.WriteLine($"  Defs={defCount,4}: {avgMs:F2}ms/frame, " +
                    $"comparisons/frame≈{EVENTS_PER_FRAME * defCount:N0}");

                system.Dispose();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // B-12 Comparison: WorldToVisualSync vs PerformerEmit overhead ratio
        //      Shows how much of the frame budget Performer takes relative
        //      to the lightweight sync system.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Benchmark_OverheadComparison_SyncVsEmit()
        {
            // Create entities that participate in both systems
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                var attrBuf = new AttributeBuffer();
                attrBuf.SetBase(_healthAttrId, 100f);
                attrBuf.SetCurrent(_healthAttrId, 80f);
                _world.Create(
                    new VisualTransform { Position = new Vector3(i, 0, i) },
                    attrBuf);
            }

            // Measure PerformerEmitSystem alone
            WarmUpGC();
            var swEmit = Stopwatch.StartNew();
            for (int frame = 0; frame < FRAMES; frame++)
            {
                ClearOutputBuffers();
                _emitSystem.Update(0.016f);
            }
            swEmit.Stop();

            double emitAvgMs = swEmit.ElapsedMilliseconds / (double)FRAMES;
            double budgetPercent = emitAvgMs / 16.6 * 100;

            Console.WriteLine("[Benchmark] Overhead Comparison:");
            Console.WriteLine($"  Entities: {ENTITY_COUNT}");
            Console.WriteLine($"  PerformerEmitSystem avg frame: {emitAvgMs:F2}ms ({budgetPercent:F1}% of 16.6ms budget)");
            Console.WriteLine($"  Estimated per-entity cost: {swEmit.Elapsed.TotalMicroseconds / (ENTITY_COUNT * FRAMES):F2}μs");

            // Warn if over budget
            if (budgetPercent > 10)
            {
                Console.WriteLine($"  ⚠ WARNING: PerformerEmitSystem alone takes >{budgetPercent:F0}% of frame budget!");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

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
                Console.WriteLine(line);
        }
    }
}
