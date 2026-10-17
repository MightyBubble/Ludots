using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests
{
    /// <summary>
    /// 30k 级 Presentation 场景的分阶段内存观测记录器。阶段边界做完整 GC 后采 live set；
    /// 帧采样只取计数器不打断运行。分配热点依赖 GCAllocationTick 采样（EventListener），
    /// 事件源不可用时自动降级为纯计数差异并在报告中标注。
    /// </summary>
    public sealed class PresentationMemoryPhaseRecorder : IDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly AllocationTickListener? _allocationListener;
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();
        private readonly List<PhaseSample> _phases = new();
        private readonly List<FrameSample> _frames = new();
        private readonly Dictionary<string, Dictionary<string, AllocationAccumulator>> _allocations = new();
        private readonly List<string> _phaseAllocatedKeysInOrder = new();
        private string? _currentPhaseLabel;

        public PresentationMemoryPhaseRecorder(string caseId)
        {
            RunId = $"L105-{caseId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{CodeSha[..Math.Min(8, CodeSha.Length)]}";
            MachineInfo = $"{Environment.MachineName} | {Environment.OSVersion.VersionString} | procs={Environment.ProcessorCount} | 64bit={Environment.Is64BitProcess} | runtime={Environment.Version}";
            _allocationListener = AllocationTickListener.TryCreate(OnAllocationTick);
        }

        public string RunId { get; }
        public string CodeSha => ResolveCodeSha();
        public string MachineInfo { get; }
        public bool AllocationSamplingActive => _allocationListener != null;
        public IReadOnlyList<PhaseSample> Phases => _phases;
        public IReadOnlyList<FrameSample> Frames => _frames;

        public static string ResolveCodeSha()
        {
            try
            {
                string? dir = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(dir))
                {
                    string marker = Path.Combine(dir, "showcase.registry.json");
                    if (File.Exists(marker))
                    {
                        return ReadGitSha(dir);
                    }

                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch
            {
            }

            return "unknown";
        }

        private static string ReadGitSha(string repoRoot)
        {
            string gitPath = Path.Combine(repoRoot, ".git");
            string gitDir = gitPath;
            if (File.Exists(gitPath))
            {
                string? redirect = File.ReadAllLines(gitPath).FirstOrDefault();
                if (redirect == null || !redirect.StartsWith("gitdir:", StringComparison.Ordinal))
                {
                    return "unknown";
                }

                gitDir = redirect.Substring("gitdir:".Length).Trim();
                if (!Path.IsPathRooted(gitDir))
                {
                    gitDir = Path.GetFullPath(Path.Combine(repoRoot, gitDir));
                }
            }

            string refRoot = gitDir;
            string commonFile = Path.Combine(gitDir, "commondir");
            if (File.Exists(commonFile))
            {
                string? common = File.ReadAllText(commonFile).Trim();
                if (!string.IsNullOrEmpty(common))
                {
                    refRoot = Path.IsPathRooted(common)
                        ? common
                        : Path.GetFullPath(Path.Combine(gitDir, common));
                }
            }

            string headFile = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headFile))
            {
                return "unknown";
            }

            string head = File.ReadAllText(headFile).Trim();
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                string refFile = Path.Combine(refRoot, head.Substring("ref:".Length).Trim().Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refFile))
                {
                    return File.ReadAllText(refFile).Trim();
                }

                string packedRefs = Path.Combine(refRoot, "packed-refs");
                if (File.Exists(packedRefs))
                {
                    string refName = head.Substring("ref:".Length).Trim();
                    foreach (string line in File.ReadAllLines(packedRefs))
                    {
                        if (line.EndsWith(" " + refName, StringComparison.Ordinal))
                        {
                            return line.Split(' ')[0];
                        }
                    }
                }

                return "unknown";
            }

            return head;
        }

        /// <summary>阶段边界采样：先完整回收再读 live set，避免把上一阶段的死对象算进活集。</summary>
        public PhaseSample CapturePhase(string phaseLabel, GameEngine? engine = null)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

            _process.Refresh();
            var info = GC.GetGCMemoryInfo();
            var sample = new PhaseSample(
                phaseLabel,
                ElapsedMs(_startTimestamp),
                GC.GetTotalAllocatedBytes(precise: true),
                GC.GetAllocatedBytesForCurrentThread(),
                GC.GetTotalMemory(forceFullCollection: false),
                info.HeapSizeBytes,
                info.HeapSizeBytes,
                info.FragmentedBytes,
                info.GenerationInfo.Length > 0 ? info.GenerationInfo[0].SizeAfterBytes : 0,
                _process.WorkingSet64,
                _process.PrivateMemorySize64,
                _process.PeakWorkingSet64,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                CaptureBufferFootprint(engine));
            _phases.Add(sample);
            BeginAllocationPhase(phaseLabel);
            return sample;
        }

        /// <summary>分配热点归因切换：帧循环开始前调用，使 GCAllocationTick 落到正确的阶段桶。</summary>
        public void BeginAllocationPhase(string label)
        {
            _currentPhaseLabel = label;
            if (!_allocations.TryGetValue(label, out _))
            {
                _allocations[label] = new Dictionary<string, AllocationAccumulator>();
                _phaseAllocatedKeysInOrder.Add(label);
            }
        }

        /// <summary>帧级采样：只读计数器，不做 GC、不打断节奏；用于阶段内 RSS/分配曲线与缓冲高水位。</summary>
        public FrameSample CaptureFrame(string phaseLabel, int frameIndex, GameEngine engine)
        {
            _process.Refresh();
            var sample = new FrameSample(
                phaseLabel,
                frameIndex,
                ElapsedMs(_startTimestamp),
                GC.GetTotalAllocatedBytes(precise: true),
                GC.GetAllocatedBytesForCurrentThread(),
                _process.WorkingSet64,
                _process.PrivateMemorySize64,
                CaptureBufferFootprint(engine));
            _frames.Add(sample);
            return sample;
        }

        public IReadOnlyList<BufferFootprintRow> CaptureBufferFootprint(GameEngine? engine)
        {
            var rows = new List<BufferFootprintRow>();
            if (engine == null)
            {
                return rows;
            }

            AddTyped(rows, engine, CoreServiceKeys.RuntimeEntitySpawnQueue, "RuntimeEntitySpawnQueue", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<RuntimeEntitySpawnRequest>(), "RuntimeEntitySpawnRequest[cap]");
            AddTyped(rows, engine, CoreServiceKeys.RuntimeEntitySpawnReceiptQueue, "RuntimeEntitySpawnReceiptQueue", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<RuntimeEntitySpawnReceipt>(), "RuntimeEntitySpawnReceipt[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationEventStream, "PresentationEventStream", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresentationEvent>(), "PresentationEvent[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationOwnerChangeBuffer, "PresentationOwnerChangeBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresentationOwnerChange>(), "PresentationOwnerChange[cap]");
            AddTyped(rows, engine, CoreServiceKeys.GasPresentationEventBuffer, "GasPresentationEventBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<GasPresentationEvent>(), "GasPresentationEvent[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresenterCommandBuffer, "PresenterCommandBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresenterCommand>(), "PresenterCommand[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationPrimitiveDrawBuffer, "PresentationPrimitiveDrawBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PrimitiveDrawItem>(), "PrimitiveDrawItem[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationVisualSnapshotBuffer, "PresentationVisualSnapshotBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PrimitiveDrawItem>(), "PrimitiveDrawItem[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationFrameReceiptBuffer, "PresentationFrameReceiptBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<PresentationFrameReceiptItem>(), "PresentationFrameReceiptItem[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationVisualProxyBuffer, "PresentationVisualProxyBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresentationVisualProxy>(), "PresentationVisualProxy[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationSkinnedVisualBatchBuffer, "SkinnedVisualBatchBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<SkinnedVisualBatchItem>(), "SkinnedVisualBatchItem[cap]");
            if (engine.TryGetService(CoreServiceKeys.PresentationStableDrawCache, out var stableDrawCache) && stableDrawCache != null)
            {
                int stableCapacity = engine.MergedConfig?.Presentation?.VisualSnapshotBufferCapacity ?? 0;
                rows.Add(new BufferFootprintRow(
                    "StableDrawCache",
                    "GameEngine presentation services",
                    stableCapacity,
                    stableDrawCache.Count,
                    0,
                    (long)stableCapacity * (Unsafe.SizeOf<PresentationVisualProxy>() + sizeof(int)) + (long)stableCapacity * 3 * sizeof(int) / 2,
                    "PresentationVisualProxy[cap]+int[cap]+Dictionary<int,int>(cap) 条目近似；容量来自 MergedConfig.VisualSnapshotBufferCapacity"));
            }

            AddTyped(rows, engine, CoreServiceKeys.PresenterVisualStableIdTable, "PresenterVisualStableIdTable", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => Pow2TableArrayBytes(cap), "5 个 pow2(cap/0.7) 数组 + Dictionary<int,int>(0.7*pow2)");
            AddTyped(rows, engine, CoreServiceKeys.PresentationWorldHudBuffer, "WorldHudBatchBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<WorldHudItem>(), "WorldHudItem[cap]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationScreenHudBuffer, "ScreenHudBatchBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * (2L * Unsafe.SizeOf<ScreenHudBarItem>() + 2L * Unsafe.SizeOf<ScreenHudTextItem>() + Unsafe.SizeOf<ScreenHudItem>()), "ScreenHudBarItem[cap]x2+ScreenHudTextItem[cap]x2+ScreenHudItem[cap]");
            AddTyped(rows, engine, CoreServiceKeys.MinimapMarkerBuffer, "MinimapMarkerBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, MinimapMarkerBufferBytes, "SoA 10 数组 x cap");
            AddTyped(rows, engine, CoreServiceKeys.MinimapScreenMarkerBuffer, "MinimapScreenMarkerBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => null, "内部多数组（容量未公开逐项枚举）");
            AddTyped(rows, engine, CoreServiceKeys.GroundOverlayBuffer, "GroundOverlayBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<GroundOverlayItem>(), "GroundOverlayItem[cap]（dict 惰性增长不计）");
            AddTyped(rows, engine, CoreServiceKeys.SplineRibbonBuffer, "SplineRibbonBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => null, "内部多数组（容量未公开逐项枚举）");
            if (engine.TryGetService(CoreServiceKeys.PresenterAnimatorStateBuffer, out var animatorStates) && animatorStates != null)
            {
                int animatorCapacity = engine.MergedConfig?.Presentation?.PresenterInstanceCapacity ?? 0;
                rows.Add(new BufferFootprintRow(
                    "PresenterAnimatorStateBuffer",
                    "GameEngine presentation services",
                    animatorCapacity,
                    0,
                    0,
                    (long)animatorCapacity * (Unsafe.SizeOf<AnimatorPackedState>() + Unsafe.SizeOf<AnimatorRuntimeState>() + Unsafe.SizeOf<AnimatorFeedbackBuffer>() + Unsafe.SizeOf<AnimationOverlayRequest>() + sizeof(int)),
                    "5 个 cap 级数组（packed/runtime/feedback/overlay/slotByEntityId）；槽位计数未公开，Animator 槽只对应带动画实体"));
            }

            AddTyped(rows, engine, CoreServiceKeys.PresentationRequestBuffer, "PresentationRequestBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => null, "8 个通道数组（通道容量=min(各缓冲容量, presentationRequestCapacity)）");
            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            if (presenters != null)
            {
                rows.Add(new BufferFootprintRow("PresenterEntityRuntime.ActiveCount", "Arch World + PresenterEntityRuntime", null, presenters.ActiveCount, 0, null, "惰性增长（无预分配容量）"));
            }

            return rows;
        }

        private static long Pow2TableArrayBytes(int capacity)
        {
            int pow2 = 8;
            int target = (int)(capacity / 0.7f) + 1;
            while (pow2 < target)
            {
                pow2 <<= 1;
            }

            return (long)pow2 * (Unsafe.SizeOf<PresenterVisualStableKey>() + sizeof(int) + sizeof(byte) + sizeof(int) + sizeof(int));
        }

        private static long? MinimapMarkerBufferBytes(int capacity)
        {
            return (long)capacity * (sizeof(int) + sizeof(float) + sizeof(float) + Unsafe.SizeOf<System.Numerics.Vector4>() + sizeof(float) + sizeof(float) + sizeof(float) + sizeof(uint) + Unsafe.SizeOf<Entity>() + Unsafe.SizeOf<MinimapMarkerRenderBucketKey>());
        }

        private static void AddTyped<TBuffer>(
            List<BufferFootprintRow> rows,
            GameEngine engine,
            ServiceKey<TBuffer> key,
            string name,
            string holder,
            Func<TBuffer, int> capacity,
            Func<TBuffer, int> count,
            Func<TBuffer, long> dropped,
            Func<int, long?> estimatedBytes,
            string shapeNote)
        {
            if (!engine.TryGetService(key, out TBuffer buffer) || buffer is null)
            {
                return;
            }

            int cap = capacity(buffer);
            rows.Add(new BufferFootprintRow(name, holder, cap, count(buffer), dropped(buffer), estimatedBytes(cap), shapeNote));
        }

        /// <summary>把 MergedConfig.Presentation 的容量合同逐项写出行；这是预分配数组的事实来源层。</summary>
        public static string DescribePresentationConfig(GameEngine engine)
        {
            var config = engine.MergedConfig?.Presentation;
            if (config == null)
            {
                return "(MergedConfig.Presentation 缺失)";
            }

            var sb = new StringBuilder();
            foreach (var property in config.GetType().GetProperties().OrderBy(p => p.Name))
            {
                if (property.GetIndexParameters().Length > 0 || property.PropertyType != typeof(int))
                {
                    continue;
                }

                sb.Append(property.Name).Append('=').Append(property.GetValue(config)).Append(' ');
            }

            return sb.ToString().TrimEnd();
        }

        public string WriteArtifacts(string artifactRelativeDir)
        {
            string artifactDir = CommittedTestArtifacts.Resolve(artifactRelativeDir);
            Directory.CreateDirectory(artifactDir);
            string reportPath = Path.Combine(artifactDir, "memory-phase-report.md");
            string samplesPath = Path.Combine(artifactDir, "memory-phase-samples.jsonl");
            File.WriteAllText(reportPath, BuildReport());
            File.WriteAllText(samplesPath, BuildSamplesJsonl());
            return artifactDir;
        }

        public string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Presentation Memory Phase Report");
            sb.AppendLine();
            sb.AppendLine($"- runID: `{RunId}`");
            sb.AppendLine($"- code SHA: `{CodeSha}`");
            sb.AppendLine($"- machine: {MachineInfo}");
            sb.AppendLine($"- allocation sampling: {(AllocationSamplingActive ? $"GCAllocationTick (EventListener) events={_allocationListener.EventsSeen} ticks={_allocationListener.AllocationTicksSeen}" : "DEGRADED：事件源不可用，仅有计数差异与容量表")}");
            sb.AppendLine();
            sb.AppendLine("## 阶段表");
            sb.AppendLine();
            sb.AppendLine("| 阶段 | 累计耗时 ms | 进程累计分配 B | 本阶段分配 B | 驱动线程累计分配 B | live heap B | GC 总堆 B | 碎片 B | RSS B | private commit B | gen0/1/2 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            long previousAllocated = 0;
            foreach (PhaseSample phase in _phases)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {phase.Label} | {phase.ElapsedMs:F0} | {phase.TotalAllocatedBytes} | {phase.TotalAllocatedBytes - previousAllocated} | {phase.ThreadAllocatedBytes} | {phase.LiveHeapBytes} | {phase.GcHeapSizeBytes} | {phase.FragmentedBytes} | {phase.WorkingSetBytes} | {phase.PrivateMemoryBytes} | {phase.Gen0Collections}/{phase.Gen1Collections}/{phase.Gen2Collections} |");
                previousAllocated = phase.TotalAllocatedBytes;
            }

            sb.AppendLine();
            string maxFrameRss = _frames.Count > 0 ? _frames.Max(f => f.WorkingSetBytes).ToString(CultureInfo.InvariantCulture) : "n/a";
            string maxPhaseRss = _phases.Count > 0 ? _phases.Max(p => p.WorkingSetBytes).ToString(CultureInfo.InvariantCulture) : "n/a";
            sb.AppendLine($"- 寿命峰值 PeakWorkingSet（仅参考，不当每阶段峰值）: 最大采样 RSS {maxFrameRss} B / 阶段峰值 {maxPhaseRss} B");
            sb.AppendLine();
            AppendFrameAggregates(sb);
            AppendAllocationTables(sb);
            AppendFootprintTables(sb);
            return sb.ToString();
        }

        private void AppendFrameAggregates(StringBuilder sb)
        {
            sb.AppendLine("## 帧采样聚合（每阶段）");
            sb.AppendLine();
            sb.AppendLine("| 阶段 | 帧数 | RSS min/max B | private min/max B | 阶段内分配 B | 缓冲峰值（primitives/snapshot/worldHud/screenHud/skinned/spawnQueue/presenters） |");
            sb.AppendLine("|---|---:|---|---|---:|---|");
            foreach (IGrouping<string, FrameSample> group in _frames.GroupBy(f => f.PhaseLabel))
            {
                FrameSample[] frames = group.ToArray();
                long allocated = frames[^1].TotalAllocatedBytes - frames[0].TotalAllocatedBytes;
                long peakPrimitives = frames.Max(f => FootprintValue(f.Buffers, "PresentationPrimitiveDrawBuffer"));
                long peakSnapshot = frames.Max(f => FootprintValue(f.Buffers, "PresentationVisualSnapshotBuffer"));
                long peakWorldHud = frames.Max(f => FootprintValue(f.Buffers, "WorldHudBatchBuffer"));
                long peakScreenHud = frames.Max(f => FootprintValue(f.Buffers, "ScreenHudBatchBuffer"));
                long peakSkinned = frames.Max(f => FootprintValue(f.Buffers, "SkinnedVisualBatchBuffer"));
                long peakSpawnQueue = frames.Max(f => FootprintValue(f.Buffers, "RuntimeEntitySpawnQueue"));
                long peakPresenters = frames.Max(f => FootprintValue(f.Buffers, "PresenterEntityRuntime.ActiveCount"));
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key} | {frames.Length} | {frames.Min(f => f.WorkingSetBytes)}/{frames.Max(f => f.WorkingSetBytes)} | {frames.Min(f => f.PrivateMemoryBytes)}/{frames.Max(f => f.PrivateMemoryBytes)} | {allocated} | {peakPrimitives}/{peakSnapshot}/{peakWorldHud}/{peakScreenHud}/{peakSkinned}/{peakSpawnQueue}/{peakPresenters} |");
            }

            sb.AppendLine();
        }

        private static long FootprintValue(IReadOnlyList<BufferFootprintRow> rows, string name)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].Name, name, StringComparison.Ordinal))
                {
                    return rows[i].Count;
                }
            }

            return 0;
        }

        private void AppendAllocationTables(StringBuilder sb)
        {
            sb.AppendLine("## 分配热点（GCAllocationTick 采样聚合，按阶段）");
            sb.AppendLine();
            if (!AllocationSamplingActive)
            {
                sb.AppendLine("DEGRADED：GCAllocationTick 事件源不可用，本报告不含分配类型热点，仅有计数差异与对象容量表。");
                sb.AppendLine();
                return;
            }

            foreach (string phase in _phaseAllocatedKeysInOrder)
            {
                if (!_allocations.TryGetValue(phase, out var top) || top.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"### {phase}");
                sb.AppendLine();
                sb.AppendLine("| 类型 | 采样分配 B（AllocationAmount 原值，EventListener 下为字节） | 采样次数 |");
                sb.AppendLine("|---|---:|---:|");
                foreach (KeyValuePair<string, AllocationAccumulator> row in top.OrderByDescending(kv => kv.Value.Kilobytes).Take(15))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {row.Key} | {row.Value.Kilobytes:F0} | {row.Value.Ticks} |");
                }

                sb.AppendLine();
            }
        }

        private void AppendFootprintTables(StringBuilder sb)
        {
            sb.AppendLine("## 缓冲容量/占用/预分配表（阶段末快照）");
            sb.AppendLine();
            foreach (PhaseSample phase in _phases)
            {
                if (phase.Buffers.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"### {phase.Label}");
                sb.AppendLine();
                sb.AppendLine("| 缓冲 | 持有者 | 容量 | 当前占用 | dropped | 预分配估算 B | 形状 |");
                sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
                foreach (BufferFootprintRow row in phase.Buffers)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {row.Name} | {row.Holder} | {row.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | {row.Count} | {row.DroppedTotal} | {row.EstimatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | {row.ShapeNote} |");
                }

                sb.AppendLine();
            }
        }

        public string BuildSamplesJsonl()
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"run","runId":"{{RunId}}","codeSha":"{{CodeSha}}","machine":"{{MachineInfo.Replace("\"", "'")}}","allocationSampling":{{(AllocationSamplingActive ? "true" : "false")}}}""");
            foreach (PhaseSample phase in _phases)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"phase","label":"{{phase.Label}}","elapsedMs":{{phase.ElapsedMs:F3}},"totalAllocatedBytes":{{phase.TotalAllocatedBytes}},"threadAllocatedBytes":{{phase.ThreadAllocatedBytes}},"totalMemoryBytes":{{phase.TotalMemoryBytes}},"liveHeapBytes":{{phase.LiveHeapBytes}},"gcHeapSizeBytes":{{phase.GcHeapSizeBytes}},"fragmentedBytes":{{phase.FragmentedBytes}},"gen0SizeBytes":{{phase.Gen0SizeBytes}},"workingSetBytes":{{phase.WorkingSetBytes}},"privateMemoryBytes":{{phase.PrivateMemoryBytes}},"peakWorkingSetLifetime":{{phase.PeakWorkingSetLifetime}},"gen0":{{phase.Gen0Collections}},"gen1":{{phase.Gen1Collections}},"gen2":{{phase.Gen2Collections}}}""");
            }

            foreach (FrameSample frame in _frames)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"frame","phase":"{{frame.PhaseLabel}}","frame":{{frame.FrameIndex}},"elapsedMs":{{frame.ElapsedMs:F3}},"totalAllocatedBytes":{{frame.TotalAllocatedBytes}},"threadAllocatedBytes":{{frame.ThreadAllocatedBytes}},"workingSetBytes":{{frame.WorkingSetBytes}},"privateMemoryBytes":{{frame.PrivateMemoryBytes}}}""");
            }

            foreach (PhaseSample phase in _phases)
            {
                foreach (BufferFootprintRow row in phase.Buffers)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"buffer","phase":"{{phase.Label}}","name":"{{row.Name}}","holder":"{{row.Holder}}","capacity":{{(row.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "null")}},"count":{{row.Count}},"dropped":{{row.DroppedTotal}},"estimatedBytes":{{(row.EstimatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "null")}}}""");
                }
            }

            foreach (string phase in _phaseAllocatedKeysInOrder)
            {
                if (!_allocations.TryGetValue(phase, out var top))
                {
                    continue;
                }

                foreach (KeyValuePair<string, AllocationAccumulator> row in top)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"alloc","phase":"{{phase}}","type":"{{row.Key.Replace("\"", "'")}}","kb":{{row.Value.Kilobytes:F1}},"ticks":{{row.Value.Ticks}}}""");
                }
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            _allocationListener?.Dispose();
        }

        private void OnAllocationTick(string typeName, double sampledBytes)
        {
            if (_currentPhaseLabel == null)
            {
                return;
            }

            if (!_allocations.TryGetValue(_currentPhaseLabel, out var bucket))
            {
                bucket = new Dictionary<string, AllocationAccumulator>();
                _allocations[_currentPhaseLabel] = bucket;
                _phaseAllocatedKeysInOrder.Add(_currentPhaseLabel);
            }

            if (!bucket.TryGetValue(typeName, out AllocationAccumulator accumulator))
            {
                accumulator = new AllocationAccumulator();
                bucket[typeName] = accumulator;
            }

            accumulator.Kilobytes += sampledBytes;
            accumulator.Ticks++;
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private sealed class AllocationTickListener : EventListener
        {
            private readonly Action<string, double> _onTick;
            private bool _enabled;

            public long EventsSeen;
            public long AllocationTicksSeen;

            private AllocationTickListener(Action<string, double> onTick)
            {
                _onTick = onTick;
            }

            public static AllocationTickListener? TryCreate(Action<string, double> onTick)
            {
                try
                {
                    return new AllocationTickListener(onTick);
                }
                catch
                {
                    return null;
                }
            }

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                if (eventSource.Name != "Microsoft-Windows-DotNETRuntime")
                {
                    return;
                }

                try
                {
                    EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords)0x1);
                    _enabled = true;
                }
                catch
                {
                    _enabled = false;
                }
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                if (!_enabled)
                {
                    return;
                }

                EventsSeen++;
                if (eventData.EventName == null || !eventData.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal))
                {
                    return;
                }

                AllocationTicksSeen++;
                try
                {
                    ReadOnlyCollection<string> names = eventData.PayloadNames;
                    IList<object?>? payload = eventData.Payload;
                    if (payload == null)
                    {
                        return;
                    }

                    double kilobytes = 0;
                    string typeName = "(unknown)";
                    for (int i = 0; i < names.Count && i < payload.Count; i++)
                    {
                        if (string.Equals(names[i], "AllocationAmount", StringComparison.Ordinal) && payload[i] != null)
                        {
                            kilobytes = Convert.ToDouble(payload[i], CultureInfo.InvariantCulture);
                        }
                        else if (string.Equals(names[i], "TypeName", StringComparison.Ordinal) && payload[i] is string type)
                        {
                            typeName = type;
                        }
                    }

                    if (kilobytes > 0)
                    {
                        _onTick(typeName, kilobytes);
                    }
                }
                catch
                {
                }
            }
        }

        private sealed class AllocationAccumulator
        {
            public double Kilobytes;
            public long Ticks;
        }

        public sealed record PhaseSample(
            string Label,
            double ElapsedMs,
            long TotalAllocatedBytes,
            long ThreadAllocatedBytes,
            long TotalMemoryBytes,
            long LiveHeapBytes,
            long GcHeapSizeBytes,
            long FragmentedBytes,
            long Gen0SizeBytes,
            long WorkingSetBytes,
            long PrivateMemoryBytes,
            long PeakWorkingSetLifetime,
            int Gen0Collections,
            int Gen1Collections,
            int Gen2Collections,
            IReadOnlyList<BufferFootprintRow> Buffers);

        public sealed record FrameSample(
            string PhaseLabel,
            int FrameIndex,
            double ElapsedMs,
            long TotalAllocatedBytes,
            long ThreadAllocatedBytes,
            long WorkingSetBytes,
            long PrivateMemoryBytes,
            IReadOnlyList<BufferFootprintRow> Buffers);

        public sealed record BufferFootprintRow(
            string Name,
            string Holder,
            int? Capacity,
            int Count,
            long DroppedTotal,
            long? EstimatedBytes,
            string ShapeNote);
    }
}
