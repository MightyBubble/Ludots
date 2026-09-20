using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace GasTests.GasCore
{
    /// <summary>
    /// RFC-0067 §3.4 基准对比床：七个固定 MetricId，场景参数固定（ENTITY_COUNT/ITERATIONS），
    /// 禁止漂移。before/after 必须同机同参复跑。
    /// - LUDOTS_EMIT_CAPACITY_BASELINE=1：把本次结果写入 docs/rfcs/gas-loadtime-capacity/benchmark-baseline.json（P0 入库口径）。
    /// - LUDOTS_COMPARE_CAPACITY_BASELINE=1：重跑并对已入库 baseline 逐 MetricId 断言——耗时回归 &gt;10% 或热路径新增托管分配即失败关闭（迁移 Phase 的合入硬门槛）。
    /// 不设环境变量时：只跑场景保活 + 机器无关断言（热路径零分配），不产出/对比 JSON。
    /// </summary>
    [Category("benchmark")]
    public sealed class GasLoadTimeCapacityBenchmarkTests
    {
        private const int EntityCount = 10_000;
        private const int Iterations = 100;
        private const double TimeRegressionThreshold = 1.10;
        // RFC §3.4「可按 metric 覆盖」：全管线 100k 含 GC/解释车道抖动，阈值放宽并留痕。
        private static readonly Dictionary<string, double> TimeThresholdOverrides = new()
        {
            ["gas.pipeline.100k"] = 1.25,
        };
        private const long AllocationToleranceBytes = 64;
        private const string BaselineRelativePath = "docs/rfcs/gas-loadtime-capacity/benchmark-baseline.json";

        private static readonly string EmitEnv = Environment.GetEnvironmentVariable("LUDOTS_EMIT_CAPACITY_BASELINE");
        private static readonly string CompareEnv = Environment.GetEnvironmentVariable("LUDOTS_COMPARE_CAPACITY_BASELINE");

        private static readonly List<MetricResult> Results = new();
        private static readonly string PhaseEnv = Environment.GetEnvironmentVariable("LUDOTS_CAPACITY_BASELINE_PHASE");
        // 基准场景参数：脏队列容量与实体数同参固定（生产为帧级 4096，这里同床同参禁漂移）。
        // 每个测试自建实例——队列/脏注册表是场景状态，禁止跨 metric 串染。
        private static TagOps CreateTagOps()
        {
            return new TagOps(
                new DirtyEntityQueue(EntityCount),
                new TagRuleRegistry(),
                aggregateDirty: new AttributeAggregateDirtyRegistry());
        }

        public sealed record MetricResult(
            string Id,
            double ElapsedMs,
            long AllocatedBytes,
            long Ops,
            double PerEntityBytes,
            string Note);

        private const int SampleCount = 5;

        /// <summary>min-of-N 采样：耗时取最小值（抗调度噪声），分配取最大值（保守口径）。
        /// 单次采样同机抖动可达 ±20%，10% 阈值的门必须建立在 min 之上（RFC §3.4 同机同参复跑）。/  </summary>
        private static (double elapsedMs, long allocatedBytes) Measure(Action action)
        {
            double minMs = double.MaxValue;
            long maxAllocated = 0;
            for (int sample = 0; sample < SampleCount; sample++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                action();
                double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                if (elapsedMs < minMs)
                {
                    minMs = elapsedMs;
                }

                if (allocated > maxAllocated)
                {
                    maxAllocated = allocated;
                }
            }

            return (minMs, maxAllocated);
        }

        private static void Record(MetricResult result)
        {
            Results.Add(result);
            TestContext.Out.WriteLine($"[capacity-bench] {result.Id}: {result.ElapsedMs:F3} ms, alloc {result.AllocatedBytes} B, ops {result.Ops}, perEntity {result.PerEntityBytes:F1} B ({result.Note})");
        }

        private static Entity[] CreateEntitiesWith(World world, Func<Entity[]> factory, out World owned)
        {
            owned = world;
            return factory();
        }

        private static readonly string NoStoreEnv = Environment.GetEnvironmentVariable("LUDOTS_CAPACITY_NO_STORE");

        [SetUp]
        public void BindP1Store()
        {
            if (!string.IsNullOrEmpty(NoStoreEnv))
            {
                return; // baseline 侧：不绑列存，隔离迁移本身的成本（before = 内嵌纯路径）
            }

            // P1 生产形态：列存随容量计划绑定（典型内容 64 槽），镜像探针计入热路径——before/after 同机同参。
            var plan = GasLoadTimeCapacityPlan.Freeze(64, 1, 1024, 256);
            WorldAttributeStoreAmbient.Bind(new WorldAttributeStore(plan, rowCapacity: 16_384));
        }

        [TearDown]
        public void UnbindP1Store()
        {
            WorldAttributeStoreAmbient.Reset();
        }

        [Test]
        public void Metric_AttrFootprint_PerEntity()
        {
            Record(Run_Metric_AttrFootprint_PerEntity());
        }

        private MetricResult Run_Metric_AttrFootprint_PerEntity()
        {
            using World world = World.Create();
            var (ms, alloc) = Measure(() =>
            {
                for (int i = 0; i < EntityCount; i++)
                {
                    world.Create(new AttributeBuffer(), new AttributeLastSnapshot());
                }
            });

            return new MetricResult("attr.footprint.per_entity", ms, alloc, EntityCount,
                alloc / (double)EntityCount, "load: AttributeBuffer+AttributeLastSnapshot 内嵌定长 64 槽");
        }


        [Test]
        public void Metric_AttrSetGetHot()
        {
            Record(Run_Metric_AttrSetGetHot());
        }

        private MetricResult Run_Metric_AttrSetGetHot()
        {
            using World world = World.Create();
            Entity[] entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = world.Create(new AttributeBuffer(), new DirtyFlags(), new GameplayTagContainer());
            }

            TagOps _tagOps = CreateTagOps();
            for (int w = 0; w < 8; w++)
            {
                foreach (ref readonly Entity entity in entities.AsSpan())
                {
                    AttributeMutationOps.SetCurrent(world, entity, 1, w * 1f, _tagOps);
                    world.Get<AttributeBuffer>(entity).GetCurrent(1);
                }
            }

            float sink = 0;
            var (ms, alloc) = Measure(() =>
            {
                foreach (ref readonly Entity entity in entities.AsSpan())
                {
                    for (int it = 0; it < Iterations; it++)
                    {
                        AttributeMutationOps.SetCurrent(world, entity, it & 7, it * 0.5f, _tagOps);
                        sink += world.Get<AttributeBuffer>(entity).GetCurrent(it & 7);
                    }
                }
            });

            That(sink, Is.GreaterThanOrEqualTo(0f), "防 JIT 死码消除");
            That(alloc, Is.EqualTo(0), "写入权威热路径必须零托管分配（当前内嵌实现的既有合同）");
            return new MetricResult("attr.setw.get.hot", ms, alloc, EntityCount * Iterations * 2L,
                0, "load: AttributeMutationOps.SetCurrent + buffer.GetCurrent ×100 迭代");
        }


        [Test]
        public void Metric_AttrAggregateTick()
        {
            Record(Run_Metric_AttrAggregateTick());
        }

        private MetricResult Run_Metric_AttrAggregateTick()
        {
            TagOps _tagOps = CreateTagOps();
            using World world = World.Create();
            Entity[] entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = world.Create(new AttributeBuffer(), new DirtyFlags(), new GameplayTagContainer());
                AttributeMutationOps.SetBase(world, entities[i], i & 7, 100f + i, _tagOps);
            }

            using var aggregator = new AttributeAggregatorSystem(world, tagOps: _tagOps, aggregateDirty: _tagOps.AggregateDirty);
            aggregator.Update(0f);

            const int cyclesPerSample = 10;
            var (ms, alloc) = Measure(() =>
            {
                for (int cycle = 0; cycle < cyclesPerSample; cycle++)
                {
                    for (int i = 0; i < EntityCount; i += 10)
                    {
                        AttributeMutationOps.SetBase(world, entities[i], (i >> 4) & 7, 100f + (i & 15) + cycle, _tagOps);
                    }

                    aggregator.Update(0f);
                }
            });

            return new MetricResult("attr.aggregate.tick", ms / cyclesPerSample, alloc / cyclesPerSample, EntityCount / 10,
                0, $"load: 1/10 实体再脏 + 全量 64 槽基值重算，脏驱动单帧聚合（{cyclesPerSample} 周期/采样取均）");
        }


        [Test]
        public void Metric_TagFootprint_PerEntity()
        {
            Record(Run_Metric_TagFootprint_PerEntity());
        }

        private MetricResult Run_Metric_TagFootprint_PerEntity()
        {
            using World world = World.Create();
            var (ms, alloc) = Measure(() =>
            {
                for (int i = 0; i < EntityCount; i++)
                {
                    world.Create(
                        new GameplayTagContainer(),
                        new GameplayTagSnapshot(),
                        new GameplayTagEffectiveCache());
                }
            });

            return new MetricResult("tag.footprint.per_entity", ms, alloc, EntityCount,
                alloc / (double)EntityCount, "load: GameplayTagContainer+Snapshot+EffectiveCache 内嵌 256 位");
        }


        [Test]
        public void Metric_TagAddHasHot()
        {
            Record(Run_Metric_TagAddHasHot());
        }

        private MetricResult Run_Metric_TagAddHasHot()
        {
            using World world = World.Create();
            Entity[] entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = world.Create(new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
            }

            for (int w = 0; w < 8; w++)
            {
                foreach (ref readonly Entity entity in entities.AsSpan())
                {
                    world.Get<GameplayTagContainer>(entity).AddTag((w % 63) + 1);
                    _ = world.Get<GameplayTagContainer>(entity).HasTag((w % 63) + 1);
                }
            }

            var (ms, alloc) = Measure(() =>
            {
                foreach (ref readonly Entity entity in entities.AsSpan())
                {
                    ref GameplayTagContainer container = ref world.Get<GameplayTagContainer>(entity);
                    for (int it = 0; it < Iterations; it++)
                    {
                        container.AddTag((it % 63) + 1);
                        if (container.HasTag((it % 63) + 1))
                        {
                            container.RemoveTag((it % 63) + 1);
                        }
                    }
                }
            });

            That(alloc, Is.EqualTo(0), "标签位图热路径必须零托管分配（当前内嵌实现的既有合同）");
            return new MetricResult("tag.add.has.hot", ms, alloc, EntityCount * Iterations * 3L,
                0, "load: 容器级 AddTag+HasTag+RemoveTag ×100 迭代");
        }


        [Test]
        public void Metric_TagDirtyCollect()
        {
            Record(Run_Metric_TagDirtyCollect());
        }

        private MetricResult Run_Metric_TagDirtyCollect()
        {
            using World world = World.Create();
            Entity[] entities = new Entity[EntityCount];
            for (int i = 0; i < EntityCount; i++)
            {
                entities[i] = world.Create(new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
                world.Get<GameplayTagContainer>(entities[i]).AddTag((i % 63) + 1);
            }

            TagOps _tagOps = CreateTagOps();
            int marked = 0;
            for (int i = 0; i < EntityCount; i += 10)
            {
                _tagOps.MarkDirtyEntity(world, entities[i]);
                marked++;
            }

            That(marked, Is.EqualTo(EntityCount / 10), "稀疏脏标记预期 1/10 实体");
            long visited = 0;
            var (ms, alloc) = Measure(() =>
            {
                while (_tagOps.DirtyEntities.TryDequeue(out Entity entity))
                {
                    visited++;
                    _ = world.Get<GameplayTagContainer>(entity).HasTag(1);
                }
            });

            That(visited, Is.EqualTo(marked), "队列必须清空");
            return new MetricResult("tag.dirty.collect", ms, alloc, marked,
                0, $"load: {marked} 稀疏脏实体出队 + 容器探测");
        }


        [Test]
        public void Metric_GasPipeline100k()
        {
            if (string.IsNullOrEmpty(EmitEnv) && string.IsNullOrEmpty(CompareEnv))
            {
                Assert.Ignore("100k 全管线场景只在基准门运行（Emit/Compare 模式）时执行，避免常规套件常驻慢路径。");
            }

            var (ms, alloc) = Measure(Ludots.Core.Gameplay.GAS.Benchmarks.GasBenchmark.Run);
            Record(new MetricResult("gas.pipeline.100k", ms, alloc, 100_000,
                0, "load: GasBenchmark.Run 全管线（100k 实体 / 100 帧事件风暴），同参禁漂移"));
        }

        [Test]
        public void Zz_Gate_EmitOrCompareBaseline()
        {
            if (!string.IsNullOrEmpty(EmitEnv))
            {
                string phase = string.IsNullOrWhiteSpace(PhaseEnv) ? "baseline" : PhaseEnv.Trim();
                string emitPath = Path.Combine(
                    FindRepoRoot(),
                    BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar).Replace("benchmark-baseline.json", $"benchmark-{phase}.json"));
                Directory.CreateDirectory(Path.GetDirectoryName(emitPath)!);
                var baseline = new BaselineDocument(
                    GeneratedAtUtc: DateTime.UtcNow,
                    Machine: new MachineInfo(Environment.MachineName, Environment.OSVersion.ToString(), Environment.ProcessorCount),
                    Parameters: new RunParameters(EntityCount, Iterations, CurrentStorageKind()),
                    Metrics: Results.ToArray());
                File.WriteAllText(emitPath, JsonSerializer.Serialize(baseline, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                }));
                TestContext.Out.WriteLine($"[capacity-bench] baseline 写入 {emitPath}（{Results.Count} metrics）");
                That(Results.Count, Is.GreaterThanOrEqualTo(7), "七个 MetricId 必须齐全才算 baseline");
                return;
            }

            string liveEnv = Environment.GetEnvironmentVariable("LUDOTS_COMPARE_LIVE");
            if (!string.IsNullOrEmpty(liveEnv))
            {
                // 活差分：同进程背靠背——解绑列存跑 baseline 侧，与本次 Results（列存侧）同承压对比，
                // 外部负载（IDE/编译服务器）对两侧近似同权，抖动抵消。打满负载机器上的诚实口径。
                WorldAttributeStoreAmbient.Reset();
                var liveBaseline = new List<MetricResult>
                {
                    Run_Metric_AttrFootprint_PerEntity(),
                    Run_Metric_AttrSetGetHot(),
                    Run_Metric_AttrAggregateTick(),
                    Run_Metric_TagFootprint_PerEntity(),
                    Run_Metric_TagAddHasHot(),
                    Run_Metric_TagDirtyCollect(),
                };
                BindP1Store();
                var liveStore = new List<MetricResult>
                {
                    Run_Metric_AttrFootprint_PerEntity(),
                    Run_Metric_AttrSetGetHot(),
                    Run_Metric_AttrAggregateTick(),
                    Run_Metric_TagFootprint_PerEntity(),
                    Run_Metric_TagAddHasHot(),
                    Run_Metric_TagDirtyCollect(),
                };

                var liveFailures = new List<string>();
                foreach (MetricResult after in liveStore)
                {
                    MetricResult? before = liveBaseline.Find(m => m.Id == after.Id);
                    if (before == null)
                    {
                        continue;
                    }

                    double threshold = TimeThresholdOverrides.TryGetValue(after.Id, out double o) ? o : TimeRegressionThreshold;
                    if (after.ElapsedMs > before.ElapsedMs * threshold)
                    {
                        liveFailures.Add($"{after.Id}: 活差分耗时 {after.ElapsedMs:F3} ms vs 无列存 {before.ElapsedMs:F3} ms 超 {threshold:P0}");
                    }

                    long allocTolerance = Math.Max(AllocationToleranceBytes, (long)(before.AllocatedBytes * 0.01));
                    if (after.AllocatedBytes > before.AllocatedBytes + allocTolerance)
                    {
                        liveFailures.Add($"{after.Id}: 活差分分配 {after.AllocatedBytes} B vs {before.AllocatedBytes} B 超 1% 容差");
                    }

                    TestContext.Out.WriteLine($"[capacity-bench:live] {after.Id}: no-store {before.ElapsedMs:F3}ms/{before.AllocatedBytes}B -> store {after.ElapsedMs:F3}ms/{after.AllocatedBytes}B");
                }

                That(liveFailures, Is.Empty, "RFC-0067 §3.4 活差分门未通过——迁移侧在同承压条件下回归。");
                return;
            }

            if (string.IsNullOrEmpty(CompareEnv))
            {
                Assert.Ignore("基准床就绪。生成 baseline：LUDOTS_EMIT_CAPACITY_BASELINE=1；跑对比门：LUDOTS_COMPARE_CAPACITY_BASELINE=1（活差分：+LUDOTS_COMPARE_LIVE=1）。");
                return;
            }

            string comparePath = Path.Combine(FindRepoRoot(), BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));
            That(File.Exists(comparePath), Is.True, $"缺少已入库 baseline：{BaselineRelativePath}（先跑 Emit 模式入库）");
            BaselineDocument compareBaseline = JsonSerializer.Deserialize<BaselineDocument>(
                File.ReadAllText(comparePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            That(compareBaseline.Parameters.EntityCount, Is.EqualTo(EntityCount), "before/after 参数必须一致（RFC §3.4 规则 4）");
            That(compareBaseline.Parameters.Iterations, Is.EqualTo(Iterations), "before/after 参数必须一致（RFC §3.4 规则 4）");
            TestContext.Out.WriteLine($"[capacity-bench] storage kind: baseline={compareBaseline.Parameters.StorageKind}, current={CurrentStorageKind()}");

            var failures = new List<string>();
            foreach (MetricResult result in Results)
            {
                MetricResult? baselineMetric = Array.Find(compareBaseline.Metrics, m => m.Id == result.Id);
                if (baselineMetric == null)
                {
                    failures.Add($"{result.Id}: baseline 缺失");
                    continue;
                }

                double threshold = TimeThresholdOverrides.TryGetValue(result.Id, out double overridden) ? overridden : TimeRegressionThreshold;
                if (result.ElapsedMs > baselineMetric.ElapsedMs * threshold)
                {
                    failures.Add($"{result.Id}: 耗时 {result.ElapsedMs:F3} ms 超过 baseline {baselineMetric.ElapsedMs:F3} ms 的 {threshold:P0} 阈值");
                }

                long allocTolerance = Math.Max(AllocationToleranceBytes, (long)(baselineMetric.AllocatedBytes * 0.01));
                if (result.AllocatedBytes > baselineMetric.AllocatedBytes + allocTolerance)
                {
                    failures.Add($"{result.Id}: 托管分配 {result.AllocatedBytes} B 超过 baseline {baselineMetric.AllocatedBytes} B + 1% 容差（热路径新增分配失败关闭）");
                }
            }

            That(failures, Is.Empty, "RFC-0067 §3.4 对比门未通过——未解释的回归不能合入，例外必须在 Epic #1196 评论留痕并写明新阈值");
        }

        private static string CurrentStorageKind()
        {
            return WorldAttributeStoreAmbient.Current != null
                ? $"world-column-store-mirror(slots={WorldAttributeStoreAmbient.Current.SlotCount})"
                : "embedded-fixed-64-256";
        }

        private static string FindRepoRoot()
        {
            string? dir = Path.GetDirectoryName(typeof(GasLoadTimeCapacityBenchmarkTests).Assembly.Location);
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("未找到仓库根（showcase.registry.json 锚点缺失）。");
        }

        public sealed record BaselineDocument(
            DateTime GeneratedAtUtc,
            MachineInfo Machine,
            RunParameters Parameters,
            MetricResult[] Metrics);

        public sealed record MachineInfo(string Name, string Os, int ProcessorCount);

        public sealed record RunParameters(int EntityCount, int Iterations, string StorageKind);
    }
}
