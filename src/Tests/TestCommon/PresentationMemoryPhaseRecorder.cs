using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
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
    /// 30k 级 Presentation 场景的分阶段内存观测记录器。
    ///
    /// 线程与结束合同：GCAllocationTick 回调可能在任意触发分配的线程上到达；
    /// 阶段切换（BeginAllocationPhase）与帧采样只允许驱动线程调用；
    /// 汇总（BuildReport/BuildSamplesJsonl/WriteArtifacts）前必须先 StopAllocationSampling
    /// 停事件源，再在锁内取快照；Dispose 负责停事件并释放监听器。
    ///
    /// 活集口径（三者分开报，不得混用）：
    /// - HeapSizeBytes：GC.GetGCMemoryInfo().HeapSizeBytes，微软文档语义为提交堆总量，含碎片；
    /// - ReachableEstimateBytes：HeapSizeBytes - FragmentedBytes，可达对象估算；
    /// - CompactedLiveBytes：LOH 压缩回收（LargeObjectHeapCompactionMode.CompactOnce + 压缩 Gen2）
    ///   之后采的 HeapSizeBytes，可达对象实测，仅在标记 compacting 的边界使用。
    /// </summary>
    public sealed class PresentationMemoryPhaseRecorder : IDisposable
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly AllocationTickListener? _allocationListener;
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();
        private readonly List<PhaseSample> _phases = new();
        private readonly List<FrameSample> _frames = new();
        private readonly List<SubPhasePoint> _points = new();
        private readonly object _allocationGate = new();
        private readonly Dictionary<string, Dictionary<string, AllocationAccumulator>> _allocations = new();
        private readonly List<string> _phaseAllocatedKeysInOrder = new();
        private string? _currentPhaseLabel;
        private long _phaseAllocatedBaseline;
        private long _phaseThreadAllocatedBaseline;
        private bool _samplingStopped;

        public PresentationMemoryPhaseRecorder(string caseId)
        {
            RunId = $"L105-{caseId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{CodeSha[..Math.Min(8, CodeSha.Length)]}";
            MachineInfo = $"{Environment.MachineName} | {Environment.OSVersion.VersionString} | procs={Environment.ProcessorCount} | 64bit={Environment.Is64BitProcess} | runtime={Environment.Version}";
            _allocationListener = AllocationTickListener.TryCreate(OnAllocationTick);
        }

        public string RunId { get; }
        public string CodeSha => ResolveCodeSha();
        public string MachineInfo { get; }

        /// <summary>分配采样是否真正启用过：监听器对象存在且事件源 EnableEvents 成功过。Stop 之后仍保持 true（历史事实）。</summary>
        public bool AllocationSamplingActive => _allocationListener != null && _allocationListener.WasEnabled;

        public long AllocationSamplingErrors => _allocationListener?.SamplingErrors ?? -1;

        public IReadOnlyList<PhaseSample> Phases => _phases;
        public IReadOnlyList<FrameSample> Frames => _frames;
        public IReadOnlyList<SubPhasePoint> Points => _points;

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

        /// <summary>
        /// 阶段边界采样：完整回收后读三个口径的活集。compacting=true 时额外压缩 LOH 再采，
        /// 得到 CompactedLiveBytes（可达对象实测）；该操作会移动 LOH 对象，只用于关键边界。
        /// </summary>
        public PhaseSample CapturePhase(string phaseLabel, GameEngine? engine = null, bool compacting = false)
        {
            if (compacting)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }
            else
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            }

            _process.Refresh();
            var info = GC.GetGCMemoryInfo();
            long heapSize = info.HeapSizeBytes;
            long fragmented = info.FragmentedBytes;
            long reachable = Math.Max(0, heapSize - fragmented);
            var generationSizes = new long[info.GenerationInfo.Length];
            var generationFragmentation = new long[info.GenerationInfo.Length];
            for (int i = 0; i < info.GenerationInfo.Length; i++)
            {
                generationSizes[i] = info.GenerationInfo[i].SizeAfterBytes;
                generationFragmentation[i] = info.GenerationInfo[i].FragmentationAfterBytes;
            }

            var sample = new PhaseSample(
                phaseLabel,
                ElapsedMs(_startTimestamp),
                GC.GetTotalAllocatedBytes(precise: true),
                GC.GetAllocatedBytesForCurrentThread(),
                GC.GetTotalMemory(forceFullCollection: false),
                heapSize,
                fragmented,
                reachable,
                compacting ? heapSize : null,
                generationSizes,
                generationFragmentation,
                _process.WorkingSet64,
                _process.PrivateMemorySize64,
                _process.PeakWorkingSet64,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                CaptureBufferFootprint(engine),
                BuildObjectAccounting(engine),
                compacting);
            _phases.Add(sample);
            BeginAllocationPhase(phaseLabel);
            return sample;
        }

        /// <summary>
        /// 分配热点归因切换 + 阶段分配计数基线重置：帧级"自阶段开始分配"以此刻累计分配为 0 点，
        /// 因此首帧自身的分配也计入阶段总量（修复旧版用帧样本首尾差漏掉首帧、单帧阶段算 0 的问题）。
        /// </summary>
        public void BeginAllocationPhase(string label)
        {
            _phaseAllocatedBaseline = GC.GetTotalAllocatedBytes(precise: true);
            _phaseThreadAllocatedBaseline = GC.GetAllocatedBytesForCurrentThread();
            lock (_allocationGate)
            {
                _currentPhaseLabel = label;
                if (!_allocations.TryGetValue(label, out _))
                {
                    _allocations[label] = new Dictionary<string, AllocationAccumulator>();
                    _phaseAllocatedKeysInOrder.Add(label);
                }
            }
        }

        /// <summary>帧级采样：只读计数器，不做 GC、不打断节奏。</summary>
        public FrameSample CaptureFrame(string phaseLabel, int frameIndex, GameEngine engine)
        {
            _process.Refresh();
            long totalAllocated = GC.GetTotalAllocatedBytes(precise: true);
            var sample = new FrameSample(
                phaseLabel,
                frameIndex,
                ElapsedMs(_startTimestamp),
                totalAllocated,
                GC.GetAllocatedBytesForCurrentThread(),
                Math.Max(0, totalAllocated - _phaseAllocatedBaseline),
                Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _phaseThreadAllocatedBaseline),
                _process.WorkingSet64,
                _process.PrivateMemorySize64,
                CaptureBufferFootprint(engine));
            _frames.Add(sample);
            return sample;
        }

        /// <summary>
        /// 子阶段计数采样点：帧间/阶段间没有边界可采的时点（入队后、稳定判定后、渲染后）补一次计数快照。
        /// 只证明"该时点真实出现过这些占用"；帧内瞬时峰值没有钩子可测，报告中按口径说明处理。
        /// </summary>
        public SubPhasePoint CaptureSubPhasePoint(string pointName, GameEngine engine)
        {
            var point = new SubPhasePoint(
                _currentPhaseLabel ?? "(none)",
                pointName,
                ElapsedMs(_startTimestamp),
                CaptureBufferFootprint(engine));
            _points.Add(point);
            return point;
        }

        public IReadOnlyList<BufferFootprintRow> CaptureBufferFootprint(GameEngine? engine)
        {
            var rows = new List<BufferFootprintRow>();
            if (engine == null)
            {
                return rows;
            }

            AddTyped(rows, engine, CoreServiceKeys.RuntimeEntitySpawnQueue, "RuntimeEntitySpawnQueue", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<RuntimeEntitySpawnRequest>(), "RuntimeEntitySpawnRequest[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.RuntimeEntitySpawnReceiptQueue, "RuntimeEntitySpawnReceiptQueue", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<RuntimeEntitySpawnReceipt>(), "RuntimeEntitySpawnReceipt[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.EffectRequestQueue, "EffectRequestQueue", "GAS effect pipeline", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * 2L * Unsafe.SizeOf<EffectRequest>(), "EffectRequest[cap]×2（items+overflow 双数组）");
            AddTyped(rows, engine, CoreServiceKeys.PresentationEventStream, "PresentationEventStream", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresentationEvent>(), "PresentationEvent[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.PresentationOwnerChangeBuffer, "PresentationOwnerChangeBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresentationOwnerChange>(), "PresentationOwnerChange[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.GasPresentationEventBuffer, "GasPresentationEventBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<GasPresentationEvent>(), "GasPresentationEvent[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.PresenterCommandBuffer, "PresenterCommandBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresenterCommand>(), "PresenterCommand[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.PresentationPrimitiveDrawBuffer, "PresentationPrimitiveDrawBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PrimitiveDrawItem>(), "PrimitiveDrawItem[cap]×1 + 惰性 static slot 字典");
            AddTyped(rows, engine, CoreServiceKeys.PresentationVisualSnapshotBuffer, "PresentationVisualSnapshotBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PrimitiveDrawItem>(), "PrimitiveDrawItem[cap]×1（独立于 draw buffer 的第二个实例）");
            AddTyped(rows, engine, CoreServiceKeys.PresentationFrameReceiptBuffer, "PresentationFrameReceiptBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<PresentationFrameReceiptItem>(), "PresentationFrameReceiptItem[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.PresentationVisualProxyBuffer, "PresentationVisualProxyBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<PresentationVisualProxy>(), "PresentationVisualProxy[cap]×1");
            AddTyped(rows, engine, CoreServiceKeys.PresentationSkinnedVisualBatchBuffer, "SkinnedVisualBatchBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, cap => (long)cap * Unsafe.SizeOf<SkinnedVisualBatchItem>(), "SkinnedVisualBatchItem[cap]×1");
            if (engine.TryGetService(CoreServiceKeys.PresentationStableDrawCache, out var stableDrawCache) && stableDrawCache != null)
            {
                int stableCapacity = engine.MergedConfig?.Presentation?.VisualSnapshotBufferCapacity ?? 0;
                long perEntry = Unsafe.SizeOf<PresentationVisualProxy>() + sizeof(int) + DictEntryApproxBytes;
                rows.Add(new BufferFootprintRow(
                    "StableDrawCache",
                    "GameEngine presentation services",
                    stableCapacity,
                    stableDrawCache.Count,
                    0,
                    (long)stableCapacity * perEntry,
                    $"PresentationVisualProxy[cap]+int[cap]+Dictionary<int,int>(cap)（字典条目 {DictEntryApproxBytes}B/条 近似）；容量来自 MergedConfig.VisualSnapshotBufferCapacity"));
            }

            AddTyped(rows, engine, CoreServiceKeys.PresenterVisualStableIdTable, "PresenterVisualStableIdTable", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => Pow2TableArrayBytes(cap), "5 个 pow2(cap/0.7) 数组 + Dictionary<int,int>(0.7*pow2) 近似");
            AddTyped(rows, engine, CoreServiceKeys.PresentationWorldHudBuffer, "WorldHudBatchBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, WorldHudBufferBytes, "WorldHudItem[cap]×2 + int[cap]×4 + WorldHudOwnerGroup[min1024] + int[min1024]");
            AddTyped(rows, engine, CoreServiceKeys.PresentationScreenHudBuffer, "ScreenHudBatchBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, ScreenHudBufferBytes, "BarItem[cap]×3 + TextItem[cap]×3 + Item[cap] + int[cap]×3 + Dictionary<int,int>(cap)×2 近似");
            AddTyped(rows, engine, CoreServiceKeys.MinimapMarkerBuffer, "MinimapMarkerBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, MinimapMarkerBufferBytes, "SoA 10 数组 x cap");
            AddTyped(rows, engine, CoreServiceKeys.MinimapScreenMarkerBuffer, "MinimapScreenMarkerBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => b.DroppedTotal, MinimapScreenMarkerBufferBytes, "8 live + 8 staged + 6 桶结构 cap 级数组 + 3 个 pow2(2cap) lookup 数组");
            AddTyped(rows, engine, CoreServiceKeys.GroundOverlayBuffer, "GroundOverlayBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * Unsafe.SizeOf<GroundOverlayItem>(), "GroundOverlayItem[cap]×1（dict 惰性增长不计）");
            AddTyped(rows, engine, CoreServiceKeys.SplineRibbonBuffer, "SplineRibbonBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, cap => (long)cap * (sizeof(int) + 22 * sizeof(float)), "int[cap] + 22×float[cap]（dict 惰性增长不计）");
            if (engine.TryGetService(CoreServiceKeys.PresenterAnimatorStateBuffer, out var animatorStates) && animatorStates != null)
            {
                int animatorCapacity = engine.MergedConfig?.Presentation?.PresenterInstanceCapacity ?? 0;
                rows.Add(new BufferFootprintRow(
                    "PresenterAnimatorStateBuffer",
                    "GameEngine presentation services",
                    animatorCapacity,
                    animatorStates.ActiveSlotCount,
                    0,
                    (long)animatorCapacity * (Unsafe.SizeOf<AnimatorPackedState>() + Unsafe.SizeOf<AnimatorRuntimeState>() + Unsafe.SizeOf<AnimatorFeedbackBuffer>() + Unsafe.SizeOf<AnimationOverlayRequest>() + sizeof(int)),
                    "5 个 cap 级数组（packed/runtime/feedback/overlay/slotByEntityId）",
                    animatorStates.AllocatedSlotHighWater));
            }

            AddTyped(rows, engine, CoreServiceKeys.PresentationRequestBuffer, "PresentationRequestBuffer", "GameEngine presentation services", b => b.Capacity, b => b.Count, b => 0L, _ => null, "8 通道数组（逐通道容量见核算表 Section A）");
            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            if (presenters != null)
            {
                rows.Add(new BufferFootprintRow("PresenterEntityRuntime.ActiveCount", "Arch World + PresenterEntityRuntime", null, presenters.ActiveCount, 0, null, "惰性增长（无预分配容量）"));
            }

            return rows;
        }

        private const long DictEntryApproxBytes = 24;

        /// <summary>对象容量核算：实测缓冲（服务可达）+ GAS 事务镜像 + 配置推算 + 未核算候选。配置加载前的边界没有服务与合同，不核算。</summary>
        private IReadOnlyList<ObjectAccountingRow> BuildObjectAccounting(GameEngine? engine)
        {
            var rows = new List<ObjectAccountingRow>();
            if (engine == null || engine.MergedConfig?.Presentation == null)
            {
                return rows;
            }

            var footprint = CaptureBufferFootprint(engine);
            foreach (BufferFootprintRow row in footprint)
            {
                if (row.EstimatedBytes is > 0)
                {
                    rows.Add(new ObjectAccountingRow(
                        "A 实测缓冲",
                        row.Name,
                        row.Holder,
                        row.EstimatedBytes!.Value,
                        row.ShapeNote,
                        "实测容量（服务暴露）"));
                }
            }

            if (engine.TryGetService(CoreServiceKeys.RuntimeEntitySpawnQueue, out var spawnQueue) && spawnQueue != null &&
                engine.TryGetService(CoreServiceKeys.EffectRequestQueue, out var effectQueue) && effectQueue != null &&
                engine.TryGetService(CoreServiceKeys.GasPresentationEventBuffer, out var gasEvents) && gasEvents != null)
            {
                long spawnRequestSize = Unsafe.SizeOf<RuntimeEntitySpawnRequest>();
                long effectRequestSize = Unsafe.SizeOf<EffectRequest>();
                long gasEventSize = Unsafe.SizeOf<GasPresentationEvent>();
                rows.Add(new ObjectAccountingRow(
                    "B GAS 事务镜像",
                    "EffectPhaseSideEffectTransaction._stagedSpawnRequests ×3",
                    "EffectApplicationSystem / EffectLifetimeSystem / EffectProposalProcessingSystem 各持一份",
                    3L * spawnQueue.Capacity * spawnRequestSize,
                    $"3 实例 × {spawnQueue.Capacity} 元素 × {spawnRequestSize}B",
                    "实测容量镜像（构造函数按 spawnRequests.Capacity 复制容量）"));
                rows.Add(new ObjectAccountingRow(
                    "B GAS 事务镜像",
                    "EffectPhaseSideEffectTransaction._stagedEffectRequests ×3",
                    "EffectApplicationSystem / EffectLifetimeSystem / EffectProposalProcessingSystem 各持一份",
                    3L * effectQueue.TotalCapacity * effectRequestSize,
                    $"3 实例 × {effectQueue.TotalCapacity} 元素 × {effectRequestSize}B",
                    "实测容量镜像（构造函数按 effectRequests.TotalCapacity 复制容量）"));
                rows.Add(new ObjectAccountingRow(
                    "B GAS 事务镜像",
                    "EffectPhaseSideEffectTransaction._stagedPresentationEvents ×3",
                    "EffectApplicationSystem / EffectLifetimeSystem / EffectProposalProcessingSystem 各持一份",
                    3L * gasEvents.Capacity * gasEventSize,
                    $"3 实例 × {gasEvents.Capacity} 元素 × {gasEventSize}B",
                    "实测容量镜像（构造函数按 presentationEvents.Capacity 复制容量）"));
                rows.Add(new ObjectAccountingRow(
                    "B GAS 事务镜像",
                    "EffectPhaseSideEffectTransaction attribute 级数组 ×3",
                    "同上三个系统",
                    null,
                    "每实例 20+ 个 attributeEntityCapacity 级数组（Entity/AttributeBuffer/mask/tag 等），未逐项核算",
                    "候选（源码核实存在，元素大小未逐项累加）"));
            }

            var presentation = engine.MergedConfig?.Presentation;
            if (presentation != null)
            {
                var requestChannels = PresentationRequestChannelCapacities.From(presentation);
                rows.Add(new ObjectAccountingRow(
                    "A 实测缓冲",
                    "PresentationRequestBuffer 通道数组",
                    "GameEngine presentation services",
                    (long)requestChannels.VisualProxy * Unsafe.SizeOf<VisualProxyChannelItem>()
                    + (long)requestChannels.GroundOverlay * Unsafe.SizeOf<GroundOverlayChannelItem>()
                    + (long)requestChannels.WorldHud * Unsafe.SizeOf<WorldHudChannelItem>()
                    + (long)requestChannels.SplineRibbon * Unsafe.SizeOf<SplineRibbonChannelItem>()
                    + (long)requestChannels.SurfaceSource * Unsafe.SizeOf<SurfaceSourceChannelItem>()
                    + (long)requestChannels.Removal * Unsafe.SizeOf<PresentationRemovalRequest>()
                    + (long)requestChannels.ClearTransient * Unsafe.SizeOf<Entity>()
                    + (long)requestChannels.TotalOperationCapacity * Unsafe.SizeOf<PresentationRequestOp>(),
                    $"proxy {requestChannels.VisualProxy} + overlay {requestChannels.GroundOverlay} + hud {requestChannels.WorldHud} + ribbon {requestChannels.SplineRibbon} + surface {requestChannels.SurfaceSource} + removal {requestChannels.Removal} + clearTransient {requestChannels.ClearTransient} + ops {requestChannels.TotalOperationCapacity}",
                    "配置容量推算（PresentationRequestChannelCapacities.From 公式）"));
                rows.Add(new ObjectAccountingRow(
                    "C 配置容量池（未逐项核算）",
                    "presentation 其余容量合同",
                    "GameEngine presentation services",
                    null,
                    $"lifecycleQueue={presentation.RuntimeEntityLifecycleQueueCapacity} lifecycleReceipt={presentation.RuntimeEntityLifecycleReceiptQueueCapacity} presenterTimer={presentation.PresenterTimerCapacity} instancedBatchRequest={presentation.InstancedBatchRequestCapacity} instancedBatchOperation={presentation.InstancedBatchOperationCapacity} globalFieldVisual(record/cell/dirtyRect)={presentation.GlobalFieldVisualRecordCapacity}/{presentation.GlobalFieldVisualCellCapacity}/{presentation.GlobalFieldVisualDirtyRectCapacity} navMeshTile={presentation.NavMeshTileCapacity} trailMesh={presentation.TrailMeshCapacity}",
                    "容量合同已知，元素形状未逐项核算"));
            }

            var gas = engine.MergedConfig?.GasRuntimeCapacity;
            if (gas != null)
            {
                rows.Add(new ObjectAccountingRow(
                    "C 配置容量池（未逐项核算）",
                    "gasRuntimeCapacity 容量合同",
                    "GAS runtime services",
                    null,
                    $"orderQueue={gas.OrderQueueCapacity} responseChainOrderQueue={gas.ResponseChainOrderQueueCapacity} orderAdmissionResult={gas.OrderAdmissionResultCapacity} orderAdmissionRejection={gas.OrderAdmissionRejectionCapacity} orderTerminalResult={gas.OrderTerminalResultCapacity} deferredTriggerActive={gas.DeferredTriggerActiveEntityCapacity} deferredTriggerPerFrame={gas.DeferredTriggerPerFrameCapacity} abilityExecSnapshot={gas.AbilityExecSnapshotCapacity} effectLifetimeSnapshot={gas.EffectLifetimeSnapshotCapacity} effectFanOutCommand={gas.EffectFanOutCommandCapacity} graphOutputValue={gas.GraphOutputValueCapacity} projectileCollision={gas.ProjectileCollisionCandidateCapacity} projectileRuntime={gas.ProjectileRuntimeEntityCapacity}",
                    "容量合同已知，元素形状未逐项核算"));
            }

            rows.Add(new ObjectAccountingRow(
                "D 未核算候选",
                "RuntimeEntitySpawnSystem 批车道 scratch",
                "RuntimeEntitySpawnSystem",
                32768L * Unsafe.SizeOf<RuntimeEntitySpawnRequest>(),
                $"32768 × {Unsafe.SizeOf<RuntimeEntitySpawnRequest>()}B（BatchEntityScratchCapacity 常量；另含 internal TemplateBatchSpawnRequest[32768] 未计）",
                "候选（源码核实常量，internal 类型大小未计）"));
            rows.Add(new ObjectAccountingRow(
                "D 未核算候选",
                "Arch World 惰性存储 / 注册表 / 配置树 / JIT 反射元数据",
                "Arch.Core World、各 Registry、ConfigPipeline 解析产物",
                null,
                "Entity[]/Bucket/Entry[ComponentType]/Int32[]/String 等采样热点见分配表；惰性增长，无固定容量合同",
                "候选（类型级采样证据，无逐持有者证据）"));

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

            return (long)pow2 * (Unsafe.SizeOf<PresenterVisualStableKey>() + sizeof(int) + sizeof(byte) + sizeof(int) + sizeof(int))
                + (long)(pow2 * 0.7f) * DictEntryApproxBytes;
        }

        private static long? MinimapMarkerBufferBytes(int capacity)
        {
            return (long)capacity * (sizeof(int) + sizeof(float) + sizeof(float) + Unsafe.SizeOf<System.Numerics.Vector4>() + sizeof(float) + sizeof(float) + sizeof(float) + sizeof(uint) + Unsafe.SizeOf<Entity>() + Unsafe.SizeOf<MinimapMarkerRenderBucketKey>());
        }

        private static long? MinimapScreenMarkerBufferBytes(int capacity)
        {
            long perEntry =
                2L * (sizeof(int) + sizeof(float) + sizeof(float) + Unsafe.SizeOf<System.Numerics.Vector4>() + sizeof(float) + sizeof(float) + sizeof(float) + sizeof(uint))
                + sizeof(int)
                + Unsafe.SizeOf<MinimapMarkerRenderBucketKey>()
                + sizeof(int)
                + sizeof(int)
                + Unsafe.SizeOf<MinimapScreenMarkerBucket>()
                + sizeof(int);
            int lookup = Math.Max(8, capacity * 2);
            int lookupPow2 = 1;
            while (lookupPow2 < lookup)
            {
                lookupPow2 <<= 1;
            }

            long lookupBytes = (long)lookupPow2 * (Unsafe.SizeOf<MinimapMarkerRenderBucketKey>() + sizeof(int) + sizeof(int));
            return (long)capacity * perEntry + lookupBytes;
        }

        private static long? WorldHudBufferBytes(int capacity)
        {
            int capped = Math.Min(capacity, 1024);
            return (long)capacity * (2L * Unsafe.SizeOf<WorldHudItem>() + 4L * sizeof(int))
                + (long)capped * (Unsafe.SizeOf<WorldHudOwnerGroup>() + sizeof(int));
        }

        private static long? ScreenHudBufferBytes(int capacity)
        {
            return (long)capacity * (3L * Unsafe.SizeOf<ScreenHudBarItem>() + 3L * Unsafe.SizeOf<ScreenHudTextItem>() + Unsafe.SizeOf<ScreenHudItem>() + 3L * sizeof(int) + 2L * DictEntryApproxBytes);
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

        /// <summary>把 MergedConfig.GasRuntimeCapacity 的容量合同逐项写出（GAS 侧镜像与池的事实来源层）。</summary>
        public static string DescribeGasRuntimeCapacityConfig(GameEngine engine)
        {
            var config = engine.MergedConfig?.GasRuntimeCapacity;
            if (config == null)
            {
                return "(MergedConfig.GasRuntimeCapacity 缺失)";
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

        /// <summary>停分配采样事件源；汇总前必须调用（回调线程不再进入，锁内快照才安全）。</summary>
        public void StopAllocationSampling()
        {
            if (_samplingStopped)
            {
                return;
            }

            _samplingStopped = true;
            _allocationListener?.Stop();
        }

        public string WriteArtifacts(string artifactRelativeDir)
        {
            StopAllocationSampling();
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
            StopAllocationSampling();
            var sb = new StringBuilder();
            sb.AppendLine("# Presentation Memory Phase Report");
            sb.AppendLine();
            sb.AppendLine($"- runID: `{RunId}`");
            sb.AppendLine($"- code SHA: `{CodeSha}`");
            sb.AppendLine($"- machine: {MachineInfo}");
            sb.AppendLine($"- allocation sampling: {(AllocationSamplingActive ? $"GCAllocationTick (EventListener) events={_allocationListener.EventsSeen} ticks={_allocationListener.AllocationTicksSeen} samplingErrors={_allocationListener.SamplingErrors}" : "DEGRADED：事件源未启用（监听器缺失或 EnableEvents 失败），仅有计数差异与容量表")}");
            if (AllocationSamplingErrors > 0 && _allocationListener != null)
            {
                sb.AppendLine($"- allocation sampling warning: 采样回调捕获异常 {AllocationSamplingErrors} 次（首例 {_allocationListener.FirstSamplingError}），相关事件的字节可能被丢弃计数，热点名次仅供参考");
            }

            sb.AppendLine();
            sb.AppendLine("## 口径说明");
            sb.AppendLine();
            sb.AppendLine("- 活集三口径分开报：`堆含碎片` = GC.GetGCMemoryInfo().HeapSizeBytes（微软文档语义：提交堆总量，含碎片）；`可达估算` = HeapSizeBytes - FragmentedBytes；`压缩实测` = LOH 压缩回收后的 HeapSizeBytes（仅标 compacting 的边界）。`GetTotalMemory` 单列仅作参照，不再当作活集。");
            sb.AppendLine("- 阶段分配：阶段开始重置基线（BeginAllocationPhase），阶段总量 = 阶段末累计分配 - 基线；帧表另给首帧与每帧均值，单帧阶段不为 0。");
            sb.AppendLine("- GCAllocationTick 的 AllocationAmount 在 EventListener 语义下为字节（与 GetTotalAllocatedBytes 互校过）；类型级热点只证明\"该类型发生过分配\"，持有者归因以核算表 Evidence 列为准，标注\"候选\"的不得当作定论。");
            sb.AppendLine("- 帧末/阶段末 Count=0 只说明该时点缓冲已清空，不是帧内瞬时峰值；帧内峰值没有钩子可测。任何\"预算可缩零\"的结论都不成立，预算依据保留不确定性。");
            sb.AppendLine("- 本观测是单生命周期证据：只能证明本生命周期内大量对象可回收，不能裁决跨生命周期无泄漏。");
            sb.AppendLine();
            sb.AppendLine("## 阶段表");
            sb.AppendLine();
            sb.AppendLine("| 阶段 | 累计耗时 ms | 进程累计分配 B | 本阶段分配 B | 驱动线程累计分配 B | 堆含碎片 B | 碎片 B | 可达估算 B | 压缩实测 B | GetTotalMemory B | RSS B | private commit B | gen0/1/2 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            long previousAllocated = 0;
            foreach (PhaseSample phase in _phases)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {phase.Label}{(phase.Compacting ? "（压缩）" : "")} | {phase.ElapsedMs:F0} | {phase.TotalAllocatedBytes} | {phase.TotalAllocatedBytes - previousAllocated} | {phase.ThreadAllocatedBytes} | {phase.GcHeapSizeBytes} | {phase.FragmentedBytes} | {phase.ReachableEstimateBytes} | {phase.CompactedLiveBytes?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | {phase.TotalMemoryBytes} | {phase.WorkingSetBytes} | {phase.PrivateMemoryBytes} | {phase.Gen0Collections}/{phase.Gen1Collections}/{phase.Gen2Collections} |");
                previousAllocated = phase.TotalAllocatedBytes;
            }

            sb.AppendLine();
            foreach (PhaseSample phase in _phases)
            {
                if (phase.GenerationSizeAfterBytes.Length == 0)
                {
                    continue;
                }

                sb.Append(CultureInfo.InvariantCulture, $"- {phase.Label} 分代（SizeAfter/Fragmentation B）：");
                for (int i = 0; i < phase.GenerationSizeAfterBytes.Length; i++)
                {
                    string name = i switch { 0 => "gen0", 1 => "gen1", 2 => "gen2", 3 => "LOH", 4 => "POH", _ => $"gen{i}" };
                    sb.Append(CultureInfo.InvariantCulture, $" {name}={phase.GenerationSizeAfterBytes[i]}/{phase.GenerationFragmentBytes[i]}");
                }

                sb.AppendLine();
            }

            sb.AppendLine();
            string maxFrameRss = _frames.Count > 0 ? _frames.Max(f => f.WorkingSetBytes).ToString(CultureInfo.InvariantCulture) : "n/a";
            string maxPhaseRss = _phases.Count > 0 ? _phases.Max(p => p.WorkingSetBytes).ToString(CultureInfo.InvariantCulture) : "n/a";
            sb.AppendLine($"- 寿命峰值 PeakWorkingSet（仅参考，不当每阶段峰值）: 最大采样 RSS {maxFrameRss} B / 阶段峰值 {maxPhaseRss} B");
            sb.AppendLine();
            AppendFrameAggregates(sb);
            AppendSubPhasePoints(sb);
            AppendAllocationTables(sb);
            AppendFootprintTables(sb);
            AppendAccountingTables(sb);
            AppendConclusionBoundaries(sb);
            return sb.ToString();
        }

        private void AppendFrameAggregates(StringBuilder sb)
        {
            sb.AppendLine("## 帧采样聚合（每阶段；基线在阶段开始重置）");
            sb.AppendLine();
            sb.AppendLine("| 阶段 | 帧数 | 阶段分配总量 B | 每帧均值 B | 首帧分配 B | 除首帧均值 B | RSS min/max B | private min/max B | 缓冲峰值（primitives/snapshot/worldHud/screenHud/skinned/spawnQueue/presenters/animatorActive/animatorHighWater/minimapMarkers） |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---|---|---|");
            foreach (IGrouping<string, FrameSample> group in _frames.GroupBy(f => f.PhaseLabel))
            {
                FrameSample[] frames = group.ToArray();
                long total = frames[^1].AllocatedSincePhaseStartBytes;
                long restMean = frames.Length > 1 ? (total - frames[0].AllocatedSincePhaseStartBytes) / (frames.Length - 1) : 0;
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key} | {frames.Length} | {total} | {(frames.Length > 0 ? total / frames.Length : 0)} | {frames[0].AllocatedSincePhaseStartBytes} | {restMean} | {frames.Min(f => f.WorkingSetBytes)}/{frames.Max(f => f.WorkingSetBytes)} | {frames.Min(f => f.PrivateMemoryBytes)}/{frames.Max(f => f.PrivateMemoryBytes)} | {PeakSummary(frames)} |");
            }

            sb.AppendLine();
        }

        private static string PeakSummary(FrameSample[] frames)
        {
            long peakPrimitives = frames.Max(f => FootprintValue(f.Buffers, "PresentationPrimitiveDrawBuffer"));
            long peakSnapshot = frames.Max(f => FootprintValue(f.Buffers, "PresentationVisualSnapshotBuffer"));
            long peakWorldHud = frames.Max(f => FootprintValue(f.Buffers, "WorldHudBatchBuffer"));
            long peakScreenHud = frames.Max(f => FootprintValue(f.Buffers, "ScreenHudBatchBuffer"));
            long peakSkinned = frames.Max(f => FootprintValue(f.Buffers, "SkinnedVisualBatchBuffer"));
            long peakSpawnQueue = frames.Max(f => FootprintValue(f.Buffers, "RuntimeEntitySpawnQueue"));
            long peakPresenters = frames.Max(f => FootprintValue(f.Buffers, "PresenterEntityRuntime.ActiveCount"));
            long peakAnimatorActive = frames.Max(f => FootprintValue(f.Buffers, "PresenterAnimatorStateBuffer"));
            long peakAnimatorHighWater = frames.Max(f => FootprintHighWater(f.Buffers, "PresenterAnimatorStateBuffer"));
            long peakMarkers = frames.Max(f => FootprintValue(f.Buffers, "MinimapMarkerBuffer"));
            return $"{peakPrimitives}/{peakSnapshot}/{peakWorldHud}/{peakScreenHud}/{peakSkinned}/{peakSpawnQueue}/{peakPresenters}/{peakAnimatorActive}/{peakAnimatorHighWater}/{peakMarkers}";
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

        private static long FootprintHighWater(IReadOnlyList<BufferFootprintRow> rows, string name)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].Name, name, StringComparison.Ordinal))
                {
                    return rows[i].HighWater ?? 0;
                }
            }

            return 0;
        }

        private void AppendSubPhasePoints(StringBuilder sb)
        {
            if (_points.Count == 0)
            {
                return;
            }

            sb.AppendLine("## 子阶段计数采样点（只证明该时点的真实占用，非帧内峰值）");
            sb.AppendLine();
            sb.AppendLine("| 阶段 | 采样点 | 耗时 ms | spawnQueue | presenters | animatorActive | animatorHighWater | minimapMarkers | screenMarkers |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (SubPhasePoint point in _points)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| {point.PhaseLabel} | {point.PointName} | {point.ElapsedMs:F0} | {FootprintValue(point.Buffers, "RuntimeEntitySpawnQueue")} | {FootprintValue(point.Buffers, "PresenterEntityRuntime.ActiveCount")} | {FootprintValue(point.Buffers, "PresenterAnimatorStateBuffer")} | {FootprintHighWater(point.Buffers, "PresenterAnimatorStateBuffer")} | {FootprintValue(point.Buffers, "MinimapMarkerBuffer")} | {FootprintValue(point.Buffers, "MinimapScreenMarkerBuffer")} |");
            }

            sb.AppendLine();
        }

        private void AppendAllocationTables(StringBuilder sb)
        {
            sb.AppendLine("## 分配热点（GCAllocationTick 采样聚合，按阶段）");
            sb.AppendLine();
            if (!AllocationSamplingActive)
            {
                sb.AppendLine("DEGRADED：GCAllocationTick 事件源未启用，本报告不含分配类型热点，仅有计数差异与对象容量表。");
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
                foreach (KeyValuePair<string, AllocationAccumulator> row in top.OrderByDescending(kv => kv.Value.Bytes).Take(15))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {row.Key} | {row.Value.Bytes:F0} | {row.Value.Ticks} |");
                }

                sb.AppendLine();
            }

            sb.AppendLine("- 归因边界：下表类型名只证明分配发生与规模；Int32[] 不能单独证明 Arch 调用栈，Entry[String,Object][] 不能单独证明 GAS/HUD 唯一调用链。持有者定论以对象核算表为准。");
            sb.AppendLine();
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
                sb.AppendLine("| 缓冲 | 持有者 | 容量 | 当前占用 | 高水位 | dropped | 预分配估算 B | 形状 |");
                sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
                foreach (BufferFootprintRow row in phase.Buffers)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {row.Name} | {row.Holder} | {row.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | {row.Count} | {row.HighWater?.ToString(CultureInfo.InvariantCulture) ?? "不可用"} | {row.DroppedTotal} | {row.EstimatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | {row.ShapeNote} |");
                }

                sb.AppendLine();
            }
        }

        private void AppendAccountingTables(StringBuilder sb)
        {
            sb.AppendLine("## 对象容量核算与活集对账（逐缓冲：实例数×元素大小=字节，附持有者与证据级别）");
            sb.AppendLine();
            bool hasAccounting = false;
            foreach (PhaseSample phase in _phases)
            {
                if (phase.Accounting.Count == 0)
                {
                    continue;
                }

                hasAccounting = true;
                long accounted = 0;
                sb.AppendLine($"### {phase.Label}");
                sb.AppendLine();
                sb.AppendLine("| 分区 | 缓冲 | 持有者 | 估算 B | 公式（实例×元素×大小） | 证据 |");
                sb.AppendLine("|---|---|---|---:|---|---|");
                foreach (ObjectAccountingRow row in phase.Accounting)
                {
                    accounted += row.EstimatedBytes ?? 0;
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {row.Section} | {row.Name} | {row.Holder} | {row.EstimatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "未核算"} | {row.Formula} | {row.Evidence} |");
                }

                long residual = Math.Max(0, phase.ReachableEstimateBytes - accounted);
                double coverage = phase.ReachableEstimateBytes > 0 ? 100d * accounted / phase.ReachableEstimateBytes : 0;
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"- 对账：可达估算 {phase.ReachableEstimateBytes} B；已核算合计 {accounted} B（覆盖 {coverage:F1}%）；残差 {residual} B 由 D 分区候选与惰性存储构成（Arch World、注册表、字符串、配置树、字典节点；类型级证据见分配热点表，均为候选归因）。");
                sb.AppendLine();
            }

            if (!hasAccounting)
            {
                sb.AppendLine("（本 run 无带引擎的阶段边界，未做核算。）");
                sb.AppendLine();
            }
        }

        private static void AppendConclusionBoundaries(StringBuilder sb)
        {
            sb.AppendLine("## 结论边界");
            sb.AppendLine();
            sb.AppendLine("- 单生命周期：本 run 只能证明本生命周期内对象大量可回收（dispose 后可达对象大幅回落），不能裁决跨生命周期无泄漏；跨生命周期结论需要连续多 run 对比。");
            sb.AppendLine("- 预算依据不完整：帧末/阶段末计数不是帧内峰值（无帧内钩子）；2097152 级队列预算的实证只有 dropped=0（未溢出）与采样点占用，不足以给出\"缩零\"或精确缩减建议。");
            sb.AppendLine("- 归因分级：核算表\"实测容量镜像\"级可作持有者定论；\"配置推算\"与\"候选\"级只能指导下一步取证，不得写入结论性归因。");
            sb.AppendLine();
        }

        public string BuildSamplesJsonl()
        {
            StopAllocationSampling();
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"run","runId":"{{RunId}}","codeSha":"{{CodeSha}}","machine":"{{MachineInfo.Replace("\"", "'")}}","allocationSampling":{{(AllocationSamplingActive ? "true" : "false")}},"samplingErrors":{{AllocationSamplingErrors}}}""");
            foreach (PhaseSample phase in _phases)
            {
                string gens = string.Join(",", phase.GenerationSizeAfterBytes.Select((size, i) => $"{{\"sizeAfterBytes\":{size},\"fragmentationBytes\":{phase.GenerationFragmentBytes[i]}}}"));
                sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"phase","label":"{{phase.Label}}","elapsedMs":{{phase.ElapsedMs:F3}},"totalAllocatedBytes":{{phase.TotalAllocatedBytes}},"threadAllocatedBytes":{{phase.ThreadAllocatedBytes}},"totalMemoryBytes":{{phase.TotalMemoryBytes}},"heapSizeBytes":{{phase.GcHeapSizeBytes}},"fragmentedBytes":{{phase.FragmentedBytes}},"reachableEstimateBytes":{{phase.ReachableEstimateBytes}},"compactedLiveBytes":{{(phase.CompactedLiveBytes?.ToString(CultureInfo.InvariantCulture) ?? "null")}},"compacting":{{(phase.Compacting ? "true" : "false")}},"generations":[{{gens}}],"workingSetBytes":{{phase.WorkingSetBytes}},"privateMemoryBytes":{{phase.PrivateMemoryBytes}},"peakWorkingSetLifetime":{{phase.PeakWorkingSetLifetime}},"gen0":{{phase.Gen0Collections}},"gen1":{{phase.Gen1Collections}},"gen2":{{phase.Gen2Collections}}}""");
            }

            foreach (FrameSample frame in _frames)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"frame","phase":"{{frame.PhaseLabel}}","frame":{{frame.FrameIndex}},"elapsedMs":{{frame.ElapsedMs:F3}},"totalAllocatedBytes":{{frame.TotalAllocatedBytes}},"threadAllocatedBytes":{{frame.ThreadAllocatedBytes}},"allocatedSincePhaseStartBytes":{{frame.AllocatedSincePhaseStartBytes}},"threadAllocatedSincePhaseStartBytes":{{frame.ThreadAllocatedSincePhaseStartBytes}},"workingSetBytes":{{frame.WorkingSetBytes}},"privateMemoryBytes":{{frame.PrivateMemoryBytes}}}""");
            }

            foreach (SubPhasePoint point in _points)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"point","phase":"{{point.PhaseLabel}}","point":"{{point.PointName}}","elapsedMs":{{point.ElapsedMs:F3}},"spawnQueueCount":{{FootprintValue(point.Buffers, "RuntimeEntitySpawnQueue")}},"presenterActiveCount":{{FootprintValue(point.Buffers, "PresenterEntityRuntime.ActiveCount")}},"animatorActiveSlots":{{FootprintValue(point.Buffers, "PresenterAnimatorStateBuffer")}},"animatorSlotHighWater":{{FootprintHighWater(point.Buffers, "PresenterAnimatorStateBuffer")}},"minimapMarkerCount":{{FootprintValue(point.Buffers, "MinimapMarkerBuffer")}},"minimapScreenMarkerCount":{{FootprintValue(point.Buffers, "MinimapScreenMarkerBuffer")}}}""");
            }

            foreach (PhaseSample phase in _phases)
            {
                foreach (BufferFootprintRow row in phase.Buffers)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"buffer","phase":"{{phase.Label}}","name":"{{row.Name}}","holder":"{{row.Holder}}","capacity":{{(row.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "null")}},"count":{{row.Count}},"highWater":{{(row.HighWater?.ToString(CultureInfo.InvariantCulture) ?? "null")}},"dropped":{{row.DroppedTotal}},"estimatedBytes":{{(row.EstimatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "null")}}}""");
                }

                foreach (ObjectAccountingRow row in phase.Accounting)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"account","phase":"{{phase.Label}}","section":"{{row.Section}}","name":"{{row.Name.Replace("\"", "'")}}","holder":"{{row.Holder.Replace("\"", "'")}}","estimatedBytes":{{(row.EstimatedBytes?.ToString(CultureInfo.InvariantCulture) ?? "null")}},"formula":"{{row.Formula.Replace("\"", "'")}}","evidence":"{{row.Evidence.Replace("\"", "'")}}"}""");
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
                    sb.AppendLine(CultureInfo.InvariantCulture, $$"""{"kind":"alloc","phase":"{{phase}}","type":"{{row.Key.Replace("\"", "'")}}","bytes":{{row.Value.Bytes:F1}},"ticks":{{row.Value.Ticks}}}""");
                }
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            StopAllocationSampling();
            _allocationListener?.Dispose();
        }

        private void OnAllocationTick(string typeName, double sampledBytes)
        {
            lock (_allocationGate)
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

                accumulator.Bytes += sampledBytes;
                accumulator.Ticks++;
            }
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private sealed class AllocationTickListener : EventListener
        {
            private readonly Action<string, double> _onTick;
            private EventSource? _runtimeSource;
            private volatile bool _enabled;
            private bool _wasEnabled;

            public long EventsSeen;
            public long AllocationTicksSeen;
            public long SamplingErrors;
            public string? FirstSamplingError;

            private AllocationTickListener(Action<string, double> onTick)
            {
                _onTick = onTick;
            }

            /// <summary>调度门：停事件后为 false，OnEventWritten 直接返回。</summary>
            public bool IsEnabled => _enabled;

            /// <summary>历史事实：EnableEvents 是否成功过（Stop 之后仍为 true）。</summary>
            public bool WasEnabled => _wasEnabled;

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
                    _runtimeSource = eventSource;
                    EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords)0x1);
                    _enabled = true;
                    _wasEnabled = true;
                }
                catch
                {
                    _enabled = false;
                    _wasEnabled = false;
                }
            }

            /// <summary>停事件并禁用关键字；停完后回调线程不再进入 OnEventWritten。</summary>
            public void Stop()
            {
                _enabled = false;
                try
                {
                    if (_runtimeSource != null)
                    {
                        EnableEvents(_runtimeSource, EventLevel.LogAlways, EventKeywords.None);
                    }
                }
                catch
                {
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
                catch (Exception ex)
                {
                    SamplingErrors++;
                    FirstSamplingError ??= ex.GetType().Name;
                }
            }
        }

        private sealed class AllocationAccumulator
        {
            public double Bytes;
            public long Ticks;
        }

        public sealed record PhaseSample(
            string Label,
            double ElapsedMs,
            long TotalAllocatedBytes,
            long ThreadAllocatedBytes,
            long TotalMemoryBytes,
            long GcHeapSizeBytes,
            long FragmentedBytes,
            long ReachableEstimateBytes,
            long? CompactedLiveBytes,
            long[] GenerationSizeAfterBytes,
            long[] GenerationFragmentBytes,
            long WorkingSetBytes,
            long PrivateMemoryBytes,
            long PeakWorkingSetLifetime,
            int Gen0Collections,
            int Gen1Collections,
            int Gen2Collections,
            IReadOnlyList<BufferFootprintRow> Buffers,
            IReadOnlyList<ObjectAccountingRow> Accounting,
            bool Compacting);

        public sealed record FrameSample(
            string PhaseLabel,
            int FrameIndex,
            double ElapsedMs,
            long TotalAllocatedBytes,
            long ThreadAllocatedBytes,
            long AllocatedSincePhaseStartBytes,
            long ThreadAllocatedSincePhaseStartBytes,
            long WorkingSetBytes,
            long PrivateMemoryBytes,
            IReadOnlyList<BufferFootprintRow> Buffers);

        public sealed record SubPhasePoint(
            string PhaseLabel,
            string PointName,
            double ElapsedMs,
            IReadOnlyList<BufferFootprintRow> Buffers);

        public sealed record BufferFootprintRow(
            string Name,
            string Holder,
            int? Capacity,
            int Count,
            long DroppedTotal,
            long? EstimatedBytes,
            string ShapeNote,
            long? HighWater = null);

        public sealed record ObjectAccountingRow(
            string Section,
            string Name,
            string Holder,
            long? EstimatedBytes,
            string Formula,
            string Evidence);
    }
}
