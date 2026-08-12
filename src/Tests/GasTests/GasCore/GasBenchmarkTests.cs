using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("benchmark")]
    public class GasBenchmarkTests
    {
        private readonly TagOps _tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
        private World _world;
        private const int ENTITY_COUNT = 10000;
        private const int ITERATIONS = 100;
        
        [SetUp]
        public void Setup()
        {
            _world = World.Create();
        }
        
        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }
        
        [Test]
        public void Benchmark_TagOps_AddTag_WorldGet()
        {
            _tagOps.ClearRuleRegistry();

            // Arrange
            var entities = new Entity[ENTITY_COUNT];
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                entities[i] = _world.Create();
                _world.Add(entities[i], GameplayTagContainer.CreateAttached());
                _world.Add(entities[i], new TagCountContainer());
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            long initialMemory = GC.GetTotalMemory(false);
            long initialAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            
            // Act
            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                for (int i = 0; i < ENTITY_COUNT; i++)
                {
                    ref var tags = ref _world.Get<GameplayTagContainer>(entities[i]);
                    ref var counts = ref _world.Get<TagCountContainer>(entities[i]);
                    _tagOps.AddTag(ref tags, ref counts, (i % 255) + 1);
                }
            }
            
            sw.Stop();
            long finalMemory = GC.GetTotalMemory(false);
            long allocatedMemory = finalMemory - initialMemory;
            long finalAlloc = GC.GetAllocatedBytesForCurrentThread();
            long allocatedBytes = finalAlloc - initialAlloc;
            
            // Assert & Log
            double avgTimeMs = sw.ElapsedMilliseconds / (double)ITERATIONS;
            double opsPerSecond = (ENTITY_COUNT * ITERATIONS) / sw.Elapsed.TotalSeconds;
            
            Console.WriteLine($"[Benchmark] _tagOps.AddTag (WorldGet):");
            Console.WriteLine($"  Entities: {ENTITY_COUNT}");
            Console.WriteLine($"  Iterations: {ITERATIONS}");
            Console.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Avg Time per Iteration: {avgTimeMs:F2}ms");
            Console.WriteLine($"  Operations/sec: {opsPerSecond:F0}");
            Console.WriteLine($"  Memory Allocated: {allocatedMemory / 1024.0:F2} KB");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocatedBytes} bytes");
            Console.WriteLine($"  GC Collections: Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}");
        }

        [Test]
        public void Benchmark_TagOps_AddTag_WorldGet_WithRules()
        {
            _tagOps.ClearRuleRegistry();

            var ruleSetA = new TagRuleSet();
            unsafe
            {
                ruleSetA.AttachedTags[0] = 2;
                ruleSetA.AttachedCount = 1;
                ruleSetA.RemovedTags[0] = 3;
                ruleSetA.RemovedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(1, ruleSetA);

            var entities = new Entity[ENTITY_COUNT];
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                entities[i] = _world.Create();
                _world.Add(entities[i], GameplayTagContainer.CreateAttached());
                _world.Add(entities[i], new TagCountContainer());
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long initialMemory = GC.GetTotalMemory(false);
            long initialAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                for (int i = 0; i < ENTITY_COUNT; i++)
                {
                    ref var tags = ref _world.Get<GameplayTagContainer>(entities[i]);
                    ref var counts = ref _world.Get<TagCountContainer>(entities[i]);
                    _tagOps.AddTag(ref tags, ref counts, 1);
                }
            }

            sw.Stop();
            long finalMemory = GC.GetTotalMemory(false);
            long allocatedMemory = finalMemory - initialMemory;
            long finalAlloc = GC.GetAllocatedBytesForCurrentThread();
            long allocatedBytes = finalAlloc - initialAlloc;

            double avgTimeMs = sw.ElapsedMilliseconds / (double)ITERATIONS;
            double opsPerSecond = (ENTITY_COUNT * ITERATIONS) / sw.Elapsed.TotalSeconds;

            Console.WriteLine($"[Benchmark] _tagOps.AddTag (WorldGet WithRules):");
            Console.WriteLine($"  Entities: {ENTITY_COUNT}");
            Console.WriteLine($"  Iterations: {ITERATIONS}");
            Console.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Avg Time per Iteration: {avgTimeMs:F2}ms");
            Console.WriteLine($"  Operations/sec: {opsPerSecond:F0}");
            Console.WriteLine($"  Memory Allocated: {allocatedMemory / 1024.0:F2} KB");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocatedBytes} bytes");
            Console.WriteLine($"  GC Collections: Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}");
        }

        [Test]
        public void Benchmark_TagOps_AddTag_InlineQuery()
        {
            _tagOps.ClearRuleRegistry();

            const int tagId = 1;
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                _world.Create(GameplayTagContainer.CreateAttached(), new TagCountContainer());
            }

            var query = new QueryDescription().WithAll<GameplayTagContainer, TagCountContainer>();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long initialMemory = GC.GetTotalMemory(false);
            long initialAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            var job = new TagAddJob { TagId = tagId, Ops = _tagOps };
            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                _world.InlineEntityQuery<TagAddJob, GameplayTagContainer, TagCountContainer>(in query, ref job);
            }

            sw.Stop();
            long finalMemory = GC.GetTotalMemory(false);
            long allocatedMemory = finalMemory - initialMemory;
            long finalAlloc = GC.GetAllocatedBytesForCurrentThread();
            long allocatedBytes = finalAlloc - initialAlloc;

            double opsPerSecond = (ENTITY_COUNT * ITERATIONS) / sw.Elapsed.TotalSeconds;

            Console.WriteLine($"[Benchmark] _tagOps.AddTag (InlineQuery):");
            Console.WriteLine($"  Entities: {ENTITY_COUNT}");
            Console.WriteLine($"  Iterations: {ITERATIONS}");
            Console.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Operations/sec: {opsPerSecond:F0}");
            Console.WriteLine($"  Memory Allocated: {allocatedMemory / 1024.0:F2} KB");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocatedBytes} bytes");
        }

        [Test]
        public void Benchmark_TagOps_AddTag_InlineQuery_WithRules()
        {
            _tagOps.ClearRuleRegistry();

            var ruleSetA = new TagRuleSet();
            unsafe
            {
                ruleSetA.AttachedTags[0] = 2;
                ruleSetA.AttachedCount = 1;
                ruleSetA.RemovedTags[0] = 3;
                ruleSetA.RemovedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(1, ruleSetA);

            const int tagId = 1;
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                _world.Create(GameplayTagContainer.CreateAttached(), new TagCountContainer());
            }

            var query = new QueryDescription().WithAll<GameplayTagContainer, TagCountContainer>();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long initialMemory = GC.GetTotalMemory(false);
            long initialAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            var job = new TagAddJob { TagId = tagId, Ops = _tagOps };
            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                _world.InlineEntityQuery<TagAddJob, GameplayTagContainer, TagCountContainer>(in query, ref job);
            }

            sw.Stop();
            long finalMemory = GC.GetTotalMemory(false);
            long allocatedMemory = finalMemory - initialMemory;
            long finalAlloc = GC.GetAllocatedBytesForCurrentThread();
            long allocatedBytes = finalAlloc - initialAlloc;

            double opsPerSecond = (ENTITY_COUNT * ITERATIONS) / sw.Elapsed.TotalSeconds;

            Console.WriteLine($"[Benchmark] _tagOps.AddTag (InlineQuery WithRules):");
            Console.WriteLine($"  Entities: {ENTITY_COUNT}");
            Console.WriteLine($"  Iterations: {ITERATIONS}");
            Console.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Operations/sec: {opsPerSecond:F0}");
            Console.WriteLine($"  Memory Allocated: {allocatedMemory / 1024.0:F2} KB");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocatedBytes} bytes");
        }

        private struct TagAddJob : IForEachWithEntity<GameplayTagContainer, TagCountContainer>
        {
            public int TagId;
            public TagOps Ops;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref GameplayTagContainer tags, ref TagCountContainer counts)
            {
                Ops.AddTag(ref tags, ref counts, TagId);
            }
        }
        
        [Test]
        public void Benchmark_DeferredTriggerQueue_Enqueue()
        {
            // Arrange
            var queue = new DeferredTriggerQueue();
            var attributeTriggers = new AttributeChangedTrigger[ENTITY_COUNT];
            for (int i = 0; i < ENTITY_COUNT; i++)
            {
                attributeTriggers[i] = new AttributeChangedTrigger
                {
                    Target = _world.Create(),
                    AttributeId = i % 64,
                    OldValue = i * 0.1f,
                    NewValue = (i + 1) * 0.1f
                };
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            long initialMemory = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            
            // Act
            for (int iter = 0; iter < ITERATIONS; iter++)
            {
                for (int i = 0; i < ENTITY_COUNT; i++)
                {
                    queue.EnqueueAttributeChanged(attributeTriggers[i]);
                }
                queue.Clear();
            }
            
            sw.Stop();
            long finalMemory = GC.GetTotalMemory(false);
            long allocatedMemory = finalMemory - initialMemory;
            
            // Assert & Log
            double avgTimeMs = sw.ElapsedMilliseconds / (double)ITERATIONS;
            double opsPerSecond = (ENTITY_COUNT * ITERATIONS) / sw.Elapsed.TotalSeconds;
            
            Console.WriteLine($"[Benchmark] DeferredTriggerQueue.EnqueueAttributeChanged:");
            Console.WriteLine($"  Triggers: {ENTITY_COUNT}");
            Console.WriteLine($"  Iterations: {ITERATIONS}");
            Console.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Avg Time per Iteration: {avgTimeMs:F2}ms");
            Console.WriteLine($"  Operations/sec: {opsPerSecond:F0}");
            Console.WriteLine($"  Memory Allocated: {allocatedMemory / 1024.0:F2} KB");
            Console.WriteLine($"  GC Collections: Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}");
        }

        [Test]
        public void Benchmark_DeferredTriggerCollectionSystem_SparseDirtyTags()
        {
            const int entityCount = 1000;
            var queue = new DeferredTriggerQueue();
            var system = new DeferredTriggerCollectionSystem(_world, queue, _tagOps);

            for (int i = 0; i < entityCount; i++)
            {
                int tagId = (i % 31) + 1;
                var tags = GameplayTagContainer.CreateAttached();
                var counts = default(TagCountContainer);
                var dirty = default(DirtyFlags);
                tags.AddTag(tagId);
                counts.AddCount(tagId);
                dirty.MarkTagDirty(tagId);
                _world.Create(tags, counts, dirty);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            system.Update(0.016f);

            sw.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - startAlloc;

            That(queue.TagTriggerCount, Is.EqualTo(entityCount));
            That(queue.TagCountTriggerCount, Is.EqualTo(entityCount));

            int previousFullScanChecks = entityCount * GameplayTagContainer.MAX_TAG_ID;
            int dirtyBitVisits = entityCount;
            Console.WriteLine("[Benchmark] DeferredTriggerCollectionSystem.SparseDirtyTags:");
            Console.WriteLine($"  Entities: {entityCount}");
            Console.WriteLine($"  PreviousFullScanTagChecks: {previousFullScanChecks}");
            Console.WriteLine($"  DirtyBitTagVisits: {dirtyBitVisits}");
            Console.WriteLine($"  Total Time: {sw.Elapsed.TotalMilliseconds:F3}ms");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocatedBytes} bytes");

            system.Dispose();
        }
        
        [Test]
        public void Benchmark_TagCountContainer_AddRemove()
        {
            // Arrange
            var entity = _world.Create();
            _world.Add(entity, new TagCountContainer());
            ref var container = ref _world.Get<TagCountContainer>(entity);
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            long initialMemory = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            
            // Act
            for (int iter = 0; iter < ITERATIONS * 100; iter++)
            {
                int tagId = (iter % 16) + 1; // tagId must be > 0
                container.AddCount(tagId, 1);
                container.GetCount(tagId);
                if (iter % 2 == 0)
                {
                    container.RemoveCount(tagId, 1);
                }
            }
            
            sw.Stop();
            long finalMemory = GC.GetTotalMemory(false);
            long allocatedMemory = finalMemory - initialMemory;
            
            // Assert & Log
            double avgTimeMs = sw.ElapsedMilliseconds / (double)(ITERATIONS * 100);
            double opsPerSecond = (ITERATIONS * 100 * 3) / sw.Elapsed.TotalSeconds;
            
            Console.WriteLine($"[Benchmark] TagCountContainer.Add/Remove/Get:");
            Console.WriteLine($"  Operations: {ITERATIONS * 100 * 3}");
            Console.WriteLine($"  Total Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"  Avg Time per Operation: {avgTimeMs:F4}ms");
            Console.WriteLine($"  Operations/sec: {opsPerSecond:F0}");
            Console.WriteLine($"  Memory Allocated: {allocatedMemory / 1024.0:F2} KB");
            Console.WriteLine($"  GC Collections: Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}");
        }
    }
}
