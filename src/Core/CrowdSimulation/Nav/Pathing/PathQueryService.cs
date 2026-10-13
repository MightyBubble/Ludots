using System;
using System.Collections.Generic;
using System.Threading;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav.Pathing;

/// <summary>一次两点路径请求(格号 + 上下文 + 起端层)。
/// CorridorMask 非空 = 同走廊刷新(S7 队伍反应):不重推走廊,按 padMask ∪ 该掩码建流场。</summary>
public readonly record struct PathQuery(int NavContextId, int StartCell, int GoalCell, int Level)
{
    public byte[]? CorridorMask { get; init; }
}

/// <summary>路径答复:折线 + 方向图(流场);不可达时 Points 为 null。</summary>
public sealed class PathResult
{
    public PathQuery Query { get; set; }
    public bool Reachable { get; set; }
    public CorridorQuery.Branch Branch { get; set; }
    /// <summary>拉直折线(格单位,x, y 平铺)。活前缀是 <see cref="PointCount"/>；链接层来自流场路点链。</summary>
    public Fix64[]? Points { get; set; }
    public int PointCount { get; set; }
    /// <summary>逐路点层号(null = 全地面)。活前缀是 <see cref="LayerCount"/>。</summary>
    public int[]? PointLayers { get; set; }
    public int LayerCount { get; set; }
    /// <summary>方向图(调用方持有;弃用经 PathQueryService.Recycle 还池)。</summary>
    public FlowField? Flow { get; set; }
    public byte[]? CorridorMask { get; set; }
}

/// <summary>路径服务故障:请求超过 planTimeoutMs 未返回(报错,绝不重试或换算法)。</summary>
public sealed class PathServiceFaultException : InvalidOperationException
{
    public PathServiceFaultException(string message) : base(message) { }
}

/// <summary>
/// 路径服务:请求在 requestTick 发出,调用方约定的生效帧 = requestTick + latencyTicks。
/// 取答复的方法不读生效帧。超过 planTimeoutMs 未返回即故障。
/// 后台 worker 数 1 / 2 / 4 可切换:worker 在导航锁下读同一份 NavContext;每个 worker 只独占搜索缓冲。
/// 结果不依赖线程时序,因为计算和重烘互斥。
/// </summary>
public sealed class PathQueryService : IDisposable
{
    private readonly IReadOnlyDictionary<int, NavContext> _navs;
    /// <summary>认知变体号(≥ BeliefStride)的解析器;null = 纯真相会话。</summary>
    private readonly Func<int, NavContext>? _variantResolver;
    private readonly CrowdSimulationRuntimeConfig _config;
    private readonly int _latencyTicks;
    private readonly TimeSpan _planTimeout;

    private readonly object _gate = new();
    /// <summary>导航态互斥:worker 计算与仿真侧结构变更重烘共用同一份
    /// 导航上下文,经本锁串行——任何在途计算恰好看见其请求之前生效的全部结构 op,时序不进结果。</summary>
    private readonly object _navGate = new();
    private readonly Queue<(int Id, PathQuery Query)> _pending;
    private readonly Dictionary<int, PathResult> _replies;
    private readonly Dictionary<int, int> _dueTick;
    private readonly Dictionary<int, long> _sentAtMs;
    private readonly HashSet<int> _discarded;
    private readonly List<Thread> _workers = new();
    private readonly System.Collections.Concurrent.ConcurrentBag<FlowPool> _pools = new();
    private readonly Dictionary<PathResult, ReplySlot> _slotOf;
    private readonly object _slotGate = new();
    private int _workerTarget;
    private bool _disposed;
    private int _seq;

    /// <summary>池统计(浸泡验收:预热后 allocated 必须平台化)。</summary>
    public (long Allocated, long Reused, int Free) GetPoolStats()
    {
        long allocated = 0, reused = 0;
        int free = 0;
        foreach (var p in _pools) { allocated += p.Allocated; reused += p.Reused; free += p.FreeCount; }
        return (allocated, reused, free);
    }

    /// <summary>测试钩:每个任务计算前注入的额外耗时(模拟后台变慢)。</summary>
    public TimeSpan TestDelayPerJob { get; set; } = TimeSpan.Zero;

    public int Waits { get; private set; }

    /// <summary>还没取走的请求数。组消失或状态序对不上时也要取走，否则这里会涨。</summary>
    public int OutstandingReplyCount
    {
        get
        {
            lock (_gate) return _sentAtMs.Count;
        }
    }
    public bool Faulted { get; private set; }
    public string? FaultMessage { get; private set; }
    public int WorkerCount { get { lock (_gate) return _workerTarget; } }

    public PathQueryService(
        IReadOnlyDictionary<int, NavContext> navs,
        CrowdSimulationRuntimeConfig config,
        int workerThreads,
        TimeSpan planTimeout,
        Func<int, NavContext>? variantResolver = null)
    {
        _navs = navs;
        _variantResolver = variantResolver;
        _config = config;
        _latencyTicks = config.Planning.LatencyTicks;
        _planTimeout = planTimeout;
        int slots = ReplySlotCapacity(config);
        _pending = new Queue<(int Id, PathQuery Query)>(slots);
        _replies = new Dictionary<int, PathResult>(slots);
        _dueTick = new Dictionary<int, int>(slots);
        _sentAtMs = new Dictionary<int, long>(slots);
        _discarded = new HashSet<int>(slots);
        _slotOf = new Dictionary<PathResult, ReplySlot>(slots);
        if (workerThreads < 1) throw new ArgumentOutOfRangeException(nameof(workerThreads));
        SetWorkerCount(workerThreads);
    }

    /// <summary>
    /// 同时在途的答复上限:刷新预算占满延迟窗,再加一组规划突发。
    /// 槽和请求表按这个数一次备好,稳态不再因为多一个在途请求而涨容。
    /// </summary>
    private static int ReplySlotCapacity(CrowdSimulationRuntimeConfig config)
    {
        int refresh = config.Planning.MaxRefreshesPerTick * config.Planning.RefreshLatencyTicks;
        if (refresh < 1) refresh = 1;
        return refresh + Ludots.Core.CrowdSimulation.CrowdSimulationLimits.HotPathSlotReserve;
    }

    /// <summary>切换后台线程数(1 / 2 / 4);在途任务继续,答复内容不变。</summary>
    public void SetWorkerCount(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        lock (_gate)
        {
            _workerTarget = count;
            while (_workers.Count < _workerTarget)
            {
                int index = _workers.Count;
                var thread = new Thread(WorkerLoop) { IsBackground = true, Name = $"crowd-path-{index}" };
                _workers.Add(thread);
                thread.Start(index);
            }

            Monitor.PulseAll(_gate); // 多余的 worker 看到目标变小后自行退出
        }
    }

    /// <summary>发请求;生效帧 = requestTick + latencyTicks。</summary>
    public int Request(PathQuery query, int requestTick)
    {
        lock (_gate)
        {
            if (Faulted) throw new PathServiceFaultException(FaultMessage!);
            int id = ++_seq;
            _dueTick[id] = requestTick + _latencyTicks;
            _sentAtMs[id] = Environment.TickCount64;
            _pending.Enqueue((id, query));
            Monitor.PulseAll(_gate);
            return id;
        }
    }

    /// <summary>作废一个请求(被更新的起终点取代):未开工的不再算,已算完的答复直接丢弃。</summary>
    public void Discard(int id)
    {
        lock (_gate)
        {
            _discarded.Add(id);
            _replies.Remove(id);
            _dueTick.Remove(id);
            _sentAtMs.Remove(id);
        }
    }

    public int DueTick(int id)
    {
        lock (_gate) return _dueTick[id];
    }

    /// <summary>取答复。不读生效帧,算完就返回。没算完就阻塞,超时抛 PathServiceFaultException。</summary>
    public PathResult AwaitDue(int id)
    {
        bool waited = false;
        lock (_gate)
        {
            for (; ; )
            {
                if (Faulted) throw new PathServiceFaultException(FaultMessage!);
                if (_replies.Remove(id, out var reply))
                {
                    _dueTick.Remove(id);
                    _sentAtMs.Remove(id);
                    return reply;
                }

                long sentAt = _sentAtMs[id];
                if (Environment.TickCount64 - sentAt > _planTimeout.TotalMilliseconds)
                {
                    FailLocked($"路径规划请求 #{id} 超过 {_planTimeout.TotalMilliseconds:0} ms 未返回。");
                    throw new PathServiceFaultException(FaultMessage!);
                }

                if (!waited) { Waits++; waited = true; }
                Monitor.Wait(_gate, 10);
            }
        }
    }

    /// <summary>轮询。超时阈值与 AwaitDue 相同,并且都会 FailLocked。超时返回 false,不抛。</summary>
    public bool Ready(int id)
    {
        lock (_gate)
        {
            if (Faulted) return false;
            if (_replies.ContainsKey(id)) return true;
            if (Environment.TickCount64 - _sentAtMs[id] > _planTimeout.TotalMilliseconds)
            {
                FailLocked($"路径规划请求 #{id} 超过 {_planTimeout.TotalMilliseconds:0} ms 未返回。");
            }

            return false;
        }
    }

    /// <summary>弃用一个答复的流场(缓冲物归原池,稳态零分配)。</summary>
    public void Recycle(FlowField flow)
    {
        flow.OriginPool?.Give(flow);
    }

    /// <summary>折线缓冲归还 worker。流场另走 <see cref="Recycle(FlowField)"/>，两者寿命不同。</summary>
    public void ReleaseReply(PathResult result)
    {
        ReplySlot? slot;
        lock (_slotGate)
        {
            if (!_slotOf.TryGetValue(result, out slot) || !slot.InUse) return;
            slot.InUse = false;
        }

        lock (slot.Owner.Gate) slot.Owner.Free.Push(slot);
    }

    private void Track(ReplySlot slot)
    {
        lock (_slotGate)
        {
            if (!_slotOf.ContainsKey(slot.Result)) _slotOf.Add(slot.Result, slot);
        }
    }

    private void WorkerLoop(object? state)
    {
        int workerIndex = (int)state!;
        int refreshInFlight = _config.Planning.MaxRefreshesPerTick * _config.Planning.RefreshLatencyTicks;
        if (refreshInFlight < 1) refreshInFlight = 1;
        int flowCapacity = _config.Flowfield.PoolCapacity + refreshInFlight;
        var pool = new FlowPool(_config.NavCellsPerSide, flowCapacity);
        _pools.Add(pool);
        int navTilesPerSide = _config.NavCellsPerSide / _config.Hpa.NavCellsPerTile;
        int tileCount = navTilesPerSide * navTilesPerSide;
        for (int i = 0; i < flowCapacity; i++)
        {
            var primedFlow = pool.Take();
            primedFlow.LkBuf ??= new int[pool.GroundCount];
            primedFlow.MaskBuf ??= new byte[tileCount];
            pool.Give(primedFlow);
        }
        var buffers = new WorkerBuffers(_config.Hpa.NavCellsPerTile * _config.Hpa.NavCellsPerTile);
        buffers.EnsureMasks(tileCount);
        for (int i = 0; i < ReplySlotCapacity(_config); i++)
        {
            var primed = new ReplySlot(buffers) { Mask = new byte[tileCount] };
            Track(primed);
            lock (buffers.Gate) buffers.Free.Push(primed);
        }

        for (; ; )
        {
            (int Id, PathQuery Query) job;
            lock (_gate)
            {
                for (; ; )
                {
                    if (_disposed) return;
                    if (_workers.Count > _workerTarget && _workers[workerIndex] == _workers[^1]) { _workers.RemoveAt(_workers.Count - 1); return; }
                    if (_pending.Count > 0) break;
                    Monitor.Wait(_gate);
                }

                job = _pending.Dequeue();
            }

            lock (_gate)
            {
                if (_discarded.Remove(job.Id)) continue;
            }

            if (TestDelayPerJob > TimeSpan.Zero) Thread.Sleep(TestDelayPerJob);
            PathResult result;
            try
            {
                lock (_navGate)
                {
                    result = Compute(job.Query, pool, buffers);
                }
            }
            catch (Exception ex)
            {
                // worker 崩溃 = 服务故障:报错,绝不静默重试或换算法
                lock (_gate)
                {
                    FailLocked($"路径 worker 内部错误(请求 #{job.Id}): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }

                return;
            }

            lock (_gate)
            {
                if (_discarded.Remove(job.Id))
                {
                    if (result.Flow != null) pool.Give(result.Flow);
                    result.Flow = null;
                    ReleaseReply(result);
                }
                else
                {
                    _replies[job.Id] = result;
                }

                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>导航态互斥执行(结构变更重烘在锁内应用,见 _navGate)。</summary>
    public void RunExclusive(Action action)
    {
        lock (_navGate) action();
    }

    public T RunExclusive<T>(Func<T> action)
    {
        lock (_navGate) return action();
    }

    /// <summary>
    /// 状态是值类型，委托在调用方做成静态字段。tick 里不要传捕获变量的 lambda。
    /// </summary>
    public T RunExclusive<TState, T>(TState state, Func<TState, T> action)
    {
        lock (_navGate) return action(state);
    }

    private PathResult Compute(PathQuery q, FlowPool pool, WorkerBuffers buffers)
    {
        // 变体号经会话的解析器(持有 _navGate 的线程内调用,Monitor 可重入)
        var nav = q.NavContextId < Fog.CrowdBeliefNavs.BeliefStride
            ? _navs[q.NavContextId]
            : _variantResolver!(q.NavContextId);
        int navCellsPerSide = nav.NavCellsPerSide;
        int navCellsPerTile = nav.Hpa!.NavCellsPerTile, navTilesPerSide = nav.Hpa.NavTilesPerSide;
        int tiles = navTilesPerSide * navTilesPerSide;
        buffers.EnsureMasks(tiles);
        var slot = buffers.Rent();
        Track(slot);
        if (q.CorridorMask != null)
        {
            // 同走廊刷新:场按 padMask ∪ 原掩码建,走廊不重推,领队折线不产出(领队与槽位不动)
            FlowFieldBuilder.PadMaskInto(q.CorridorMask, navTilesPerSide, _config.Flowfield.CorridorPaddingNavTiles, buffers.Pad);
            FlowFieldBuilder.OrMaskInto(buffers.Pad, q.CorridorMask, buffers.Union);
            var refreshed = FlowFieldBuilder.Build(nav, q.GoalCell, buffers.Union, pool);
            Fill(slot, q, true, CorridorQuery.Branch.None, null, 0, null, 0, refreshed, q.CorridorMask);
            return slot.Result;
        }

        Array.Clear(buffers.Corridor, 0, tiles);
        buffers.Corridor[NavGridSteps.TileOf(q.GoalCell, navCellsPerSide, navCellsPerTile, navTilesPerSide)] = 1;
        var corridor = CorridorQuery.CorridorTo(nav, _config, q.StartCell, q.GoalCell, buffers.Corridor, q.Level, buffers.Scratch);
        FlowFieldBuilder.PadMaskInto(buffers.Corridor, navTilesPerSide, _config.Flowfield.CorridorPaddingNavTiles, buffers.Pad);
        var flow = FlowFieldBuilder.Build(nav, q.GoalCell, buffers.Pad, pool);

        Fix64[]? points = null;
        int pointCount = 0;
        int[]? layers = null;
        int layerCount = 0;
        if (corridor.Points != null && nav.Links == null)
        {
            points = corridor.Points;
            pointCount = corridor.PointCount;
        }
        else
        {
            // 链接层 / 无走廊:领队折线从流场路点链描出
            var fp = CorridorQuery.FlowPoints(flow, q.StartCell, buffers.Scratch);
            if (fp != null)
            {
                points = fp.Value.Points;
                pointCount = fp.Value.PointCount;
                layers = fp.Value.Layers;
                layerCount = fp.Value.LayerCount;
            }
        }

        if (points == null)
        {
            pool.Give(flow);
            flow = null;
            pointCount = 0;
            layerCount = 0;
            layers = null;
        }

        if (slot.Mask.Length != tiles) slot.Mask = new byte[tiles];
        Array.Copy(buffers.Corridor, slot.Mask, tiles);
        Fill(slot, q, points != null, corridor.Kind, points, pointCount, layers, layerCount, flow, slot.Mask);
        return slot.Result;
    }

    private void Fill(
        ReplySlot slot,
        PathQuery query,
        bool reachable,
        CorridorQuery.Branch branch,
        Fix64[]? points,
        int pointCount,
        int[]? layers,
        int layerCount,
        FlowField? flow,
        byte[]? mask)
    {
        if (pointCount > 0)
        {
            if (slot.Points.Length < pointCount)
                slot.Points = new Fix64[Math.Max(pointCount, slot.Points.Length * 2)];

            Array.Copy(points!, slot.Points, pointCount);
        }

        if (layerCount > 0)
        {
            if (slot.Layers.Length < layerCount)
            {
                slot.Layers = new int[Math.Max(layerCount, slot.Layers.Length * 2)];
            }

            Array.Copy(layers!, slot.Layers, layerCount);
        }

        var result = slot.Result;
        result.Query = query;
        result.Reachable = reachable;
        result.Branch = branch;
        result.PointCount = pointCount;
        result.Points = pointCount == 0 ? null : slot.Points;
        result.LayerCount = layerCount;
        result.PointLayers = layerCount == 0 ? null : slot.Layers;
        result.Flow = flow;
        result.CorridorMask = mask;
    }

    /// <summary>一份 worker 私有暂存。答复槽跨线程交回主线程，算完即与暂存脱开。</summary>
    private sealed class WorkerBuffers
    {
        public WorkerBuffers(int tileCellCount)
        {
            Scratch = new CorridorQuery.Scratch(tileCellCount);
        }

        public readonly object Gate = new();
        public readonly CorridorQuery.Scratch Scratch;
        public readonly Stack<ReplySlot> Free = new();
        public byte[] Corridor = Array.Empty<byte>();
        public byte[] Pad = Array.Empty<byte>();
        public byte[] Union = Array.Empty<byte>();

        public void EnsureMasks(int tiles)
        {
            if (Corridor.Length == tiles) return;
            Corridor = new byte[tiles];
            Pad = new byte[tiles];
            Union = new byte[tiles];
        }

        public ReplySlot Rent()
        {
            ReplySlot slot;
            lock (Gate)
            {
                if (Free.Count > 0) slot = Free.Pop();
                else slot = new ReplySlot(this);
            }

            slot.InUse = true;
            return slot;
        }
    }

    private sealed class ReplySlot
    {
        public ReplySlot(WorkerBuffers owner)
        {
            Owner = owner;
            Result = new PathResult();
        }

        public readonly WorkerBuffers Owner;
        public readonly PathResult Result;
        public bool InUse;
        public Fix64[] Points = new Fix64[4096];
        public int[] Layers = new int[2048];
        public byte[] Mask = Array.Empty<byte>();
    }

    private void FailLocked(string message)
    {
        if (Faulted) return;
        Faulted = true;
        FaultMessage = message;
        Monitor.PulseAll(_gate);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Monitor.PulseAll(_gate);
        }

        foreach (var w in _workers.ToArray()) w.Join();
    }
}
