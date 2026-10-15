using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav.Pathing;

/// <summary>一次两点路径请求(格号 + 上下文 + 起端层)。
/// CorridorMask 非空 = 同走廊刷新(S7 队伍反应):不重推走廊,按 padMask ∪ 该掩码建流场。
/// Origins 非空 = 组规划:每个起点各推一条走廊,并进同一张掩码,流场按垫过的并集建。</summary>
public readonly record struct PathQuery(int NavContextId, int StartCell, int GoalCell, int Level)
{
    public byte[]? CorridorMask { get; init; }
    /// <summary>走廊扩展：统计进扩展次数和耗时。建场仍是 padMask ∪ 原掩码。</summary>
    public bool Extension { get; init; }
    /// <summary>组的起点表,按 (格, 层) 成对平铺。数组归调用方,答复取走前不能改。</summary>
    public int[]? Origins { get; init; }
    public int OriginCount { get; init; }
    /// <summary>领队起点(地面层)。它的走廊也并进组掩码;流场描不到它而目标可达时,并入它的 tile 重建一次。-1 = 无。</summary>
    public int LeaderStart { get; init; } = -1;
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

/// <summary>路径服务累计读数。没有答复或没有工人样本的耗时是 NaN。</summary>
internal readonly struct PathServiceStats
{
    public long Requests { get; init; }
    public long Replies { get; init; }
    public int Pending { get; init; }
    public double RoundTripLastMs { get; init; }
    public double RoundTripMeanMs { get; init; }
    public double WorkerLastMs { get; init; }
    public double WorkerMeanMs { get; init; }
    public double NavMeshMs { get; init; }
    public double HpaMs { get; init; }
    public double FlowMs { get; init; }
    public bool Faulted { get; init; }
    public string? FaultMessage { get; init; }
    public double PlanTimeoutMs { get; init; }
    public long ExtendJobs { get; init; }
    public double ExtendMs { get; init; }
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
    private Queue<(int Id, PathQuery Query)> _pending;
    private Queue<(int Id, PathQuery Query)> _keep;
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

    /// <summary>测试钩:两点请求和组请求里的领队走廊按失败处理(模拟 HPA 图与连通域不一致)。</summary>
    public bool TestCorridorFails { get; set; }

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
    public TimeSpan PlanTimeout => _planTimeout;
    public int WorkerCount { get { lock (_gate) return _workerTarget; } }

    private long _requests;
    private long _storedReplies;
    private long _rtSumMs;
    private long _rtLastMs;
    private long _workerSumTicks;
    private long _workerLastTicks;
    private long _workerSamples;
    private long _navTicks;
    private long _hpaTicks;
    private long _flowTicks;
    private long _extendJobs;
    private long _extendTicks;
    private int[] _retired;
    private int _retiredCount;

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
        _keep = new Queue<(int Id, PathQuery Query)>(slots);
        _retired = new int[slots];
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
    private static int InFlightWindow(int perTick, int latencyTicks)
    {
        int n = perTick * latencyTicks;
        return n < 1 ? 1 : n;
    }

    private static int ReplySlotCapacity(CrowdSimulationRuntimeConfig config)
    {
        return InFlightWindow(config.Planning.MaxRefreshesPerTick, config.Planning.RefreshLatencyTicks)
            + InFlightWindow(config.Flowfield.MaxRebuildsPerTick, config.Planning.RefreshLatencyTicks)
            + Ludots.Core.CrowdSimulation.CrowdSimulationLimits.HotPathSlotReserve;
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
            _requests++;
            int id = ++_seq;
            _dueTick[id] = requestTick + _latencyTicks;
            _sentAtMs[id] = Environment.TickCount64;
            _pending.Enqueue((id, query));
            Monitor.PulseAll(_gate);
            return id;
        }
    }

    /// <summary>
    /// 作废一个请求。返回 true 表示工人不会再读这次请求的走廊掩码：
    /// 还在队列里的直接拿掉，已经算完的答复连流场一起丢掉。
    /// 返回 false 表示工人可能正在读掩码，算完后进 <see cref="DrainRetired"/>。
    /// </summary>
    public bool Discard(int id)
    {
        lock (_gate)
        {
            bool queued = PullPending(id);
            bool stored = _replies.Remove(id, out var reply);
            _dueTick.Remove(id);
            _sentAtMs.Remove(id);
            if (stored)
            {
                if (reply != null && reply.Flow != null)
                {
                    var flow = reply.Flow;
                    reply.Flow = null;
                    flow.OriginPool?.Give(flow);
                }

                if (reply != null) ReleaseReply(reply);
                return true;
            }

            if (queued) return true;
            _discarded.Add(id);
            return false;
        }
    }

    /// <summary>工人已经不会再读这些请求的走廊掩码。调用方据此归还掩码副本。</summary>
    internal int DrainRetired(int[] dest)
    {
        lock (_gate)
        {
            int n = _retiredCount;
            if (n > dest.Length) n = dest.Length;
            if (n > 0) Array.Copy(_retired, dest, n);
            int left = _retiredCount - n;
            if (left > 0) Array.Copy(_retired, n, _retired, 0, left);
            _retiredCount = left;
            return n;
        }
    }

    private bool PullPending(int id)
    {
        bool found = false;
        while (_pending.Count > 0)
        {
            var job = _pending.Dequeue();
            if (job.Id == id)
            {
                found = true;
                continue;
            }

            _keep.Enqueue(job);
        }

        var swap = _pending;
        _pending = _keep;
        _keep = swap;
        return found;
    }

    private void RetireLocked(int id)
    {
        if (_retiredCount == _retired.Length)
        {
            var bigger = new int[_retired.Length << 1];
            Array.Copy(_retired, bigger, _retired.Length);
            _retired = bigger;
        }

        _retired[_retiredCount++] = id;
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
        int refreshInFlight = InFlightWindow(_config.Planning.MaxRefreshesPerTick, _config.Planning.RefreshLatencyTicks);
        int extendInFlight = InFlightWindow(_config.Flowfield.MaxRebuildsPerTick, _config.Planning.RefreshLatencyTicks);
        // 缓存里的场不回空闲栈。把缓存容量一起备上，换目标时从栈里取，tick 里不再新分配。
        // 配置的 poolCapacity 是这份工作集之外的空闲余量，不是活场数量的上限。
        int flowCapacity = _config.Flowfield.PoolCapacity
            + _config.Flowfield.CacheCapacity
            + refreshInFlight
            + extendInFlight;
        var pool = new FlowPool(_config.NavCellsPerSide, flowCapacity);
        _pools.Add(pool);
        int navTilesPerSide = _config.NavCellsPerSide / _config.Hpa.NavCellsPerTile;
        int tileCount = navTilesPerSide * navTilesPerSide;
        // 边取边还必须先攒齐。否则下一次 Take 拿回刚还的那一份，池里永远只有一块。
        var flowStock = new FlowField[flowCapacity];
        for (int i = 0; i < flowCapacity; i++)
        {
            flowStock[i] = pool.Take();
            flowStock[i].LkBuf ??= new int[pool.GroundCount];
            flowStock[i].MaskBuf ??= new byte[tileCount];
        }

        for (int i = 0; i < flowCapacity; i++) pool.Give(flowStock[i]);
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
                if (_discarded.Remove(job.Id))
                {
                    RetireLocked(job.Id);
                    continue;
                }
            }

            if (TestDelayPerJob > TimeSpan.Zero) Thread.Sleep(TestDelayPerJob);
            PathResult result;
            long workMark = Stopwatch.GetTimestamp();
            try
            {
                lock (_navGate)
                {
                    result = Compute(job.Query, pool, buffers);
                }
            }
            catch (Exception ex)
            {
                // worker 崩溃 = 服务故障:报错,绝不静默重试或换算法,也不改到主线程重算
                lock (_gate)
                {
                    FailLocked($"路径 worker 内部错误(请求 #{job.Id}): {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }

                return;
            }

            long workTicks = Stopwatch.GetTimestamp() - workMark;
            Interlocked.Add(ref _workerSumTicks, workTicks);
            Interlocked.Exchange(ref _workerLastTicks, workTicks);
            Interlocked.Increment(ref _workerSamples);

            lock (_gate)
            {
                if (_discarded.Remove(job.Id))
                {
                    if (result.Flow != null) pool.Give(result.Flow);
                    result.Flow = null;
                    ReleaseReply(result);
                    RetireLocked(job.Id);
                }
                else
                {
                    long sent = _sentAtMs[job.Id];
                    long rt = Environment.TickCount64 - sent;
                    _rtSumMs += rt;
                    _rtLastMs = rt;
                    _storedReplies++;
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
            long refreshMark = Stopwatch.GetTimestamp();
            var refreshed = FlowFieldBuilder.Build(nav, q.GoalCell, buffers.Union, pool);
            long built = Stopwatch.GetTimestamp() - refreshMark;
            Interlocked.Add(ref _flowTicks, built);
            if (q.Extension)
            {
                Interlocked.Increment(ref _extendJobs);
                Interlocked.Add(ref _extendTicks, built);
            }
            Fill(slot, q, true, CorridorQuery.Branch.None, null, 0, null, 0, refreshed, q.CorridorMask);
            return slot.Result;
        }

        if (q.Origins != null) return ComputeGroup(q, nav, pool, buffers, slot, tiles);

        Array.Clear(buffers.Corridor, 0, tiles);
        buffers.Corridor[NavGridSteps.TileOf(q.GoalCell, navCellsPerSide, navCellsPerTile, navTilesPerSide)] = 1;
        var stages = default(CorridorQuery.CorridorStageTicks);
        var corridor = TestCorridorFails
            ? buffers.Scratch.Publish(CorridorQuery.Branch.None, 0, null, 0, null, null, 0, default)
            : CorridorQuery.CorridorTo(nav, _config, q.StartCell, q.GoalCell, buffers.Corridor, q.Level, buffers.Scratch, ref stages);
        if (stages.NavMesh != 0) Interlocked.Add(ref _navTicks, stages.NavMesh);
        if (stages.Hpa != 0) Interlocked.Add(ref _hpaTicks, stages.Hpa);
        FlowFieldBuilder.PadMaskInto(buffers.Corridor, navTilesPerSide, _config.Flowfield.CorridorPaddingNavTiles, buffers.Pad);
        long flowMark = Stopwatch.GetTimestamp();
        var flow = FlowFieldBuilder.Build(nav, q.GoalCell, buffers.Pad, pool);
        Interlocked.Add(ref _flowTicks, Stopwatch.GetTimestamp() - flowMark);

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

        if (points == null && nav.CanReach(nav.CompAt(q.StartCell, q.Level), nav.Comp[q.GoalCell]))
        {
            flow = RebuildWithStartTile(nav, q.StartCell, q.GoalCell, flow, pool, buffers);
            var retry = CorridorQuery.FlowPoints(flow, q.StartCell, buffers.Scratch);
            if (retry != null)
            {
                points = retry.Value.Points;
                pointCount = retry.Value.PointCount;
                layers = retry.Value.Layers;
                layerCount = retry.Value.LayerCount;
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

    /// <summary>
    /// 组规划:目标 tile 加上每个起点、领队起点的走廊,并成一张掩码,垫一圈后建场。
    /// 组总能拿到场;成员可不可达在规划时逐个按连通域判过。
    /// </summary>
    private PathResult ComputeGroup(PathQuery q, NavContext nav, FlowPool pool, WorkerBuffers buffers, ReplySlot slot, int tiles)
    {
        int navCellsPerSide = nav.NavCellsPerSide;
        int navCellsPerTile = nav.Hpa!.NavCellsPerTile, navTilesPerSide = nav.Hpa.NavTilesPerSide;
        Array.Clear(buffers.Corridor, 0, tiles);
        buffers.Corridor[NavGridSteps.TileOf(q.GoalCell, navCellsPerSide, navCellsPerTile, navTilesPerSide)] = 1;
        var stages = default(CorridorQuery.CorridorStageTicks);
        var origins = q.Origins!;
        for (int k = 0; k < q.OriginCount; k++)
        {
            CorridorQuery.CorridorTo(nav, _config, origins[2 * k], q.GoalCell, buffers.Corridor, origins[2 * k + 1], buffers.Scratch, ref stages);
        }

        bool leaderOnCorridor = true;
        if (q.LeaderStart >= 0)
        {
            bool found = !TestCorridorFails &&
                CorridorQuery.CorridorTo(nav, _config, q.LeaderStart, q.GoalCell, buffers.Corridor, 0, buffers.Scratch, ref stages).Points != null;
            leaderOnCorridor = found && nav.Links == null;
        }

        if (stages.NavMesh != 0) Interlocked.Add(ref _navTicks, stages.NavMesh);
        if (stages.Hpa != 0) Interlocked.Add(ref _hpaTicks, stages.Hpa);
        FlowFieldBuilder.PadMaskInto(buffers.Corridor, navTilesPerSide, _config.Flowfield.CorridorPaddingNavTiles, buffers.Pad);
        long flowMark = Stopwatch.GetTimestamp();
        var flow = FlowFieldBuilder.Build(nav, q.GoalCell, buffers.Pad, pool);
        Interlocked.Add(ref _flowTicks, Stopwatch.GetTimestamp() - flowMark);
        if (!leaderOnCorridor &&
            CorridorQuery.FlowPoints(flow, q.LeaderStart, buffers.Scratch) == null &&
            nav.CanReach(nav.CompAt(q.LeaderStart, 0), nav.Comp[q.GoalCell]))
        {
            flow = RebuildWithStartTile(nav, q.LeaderStart, q.GoalCell, flow, pool, buffers);
        }

        if (slot.Mask.Length != tiles) slot.Mask = new byte[tiles];
        Array.Copy(buffers.Corridor, slot.Mask, tiles);
        Fill(slot, q, true, CorridorQuery.Branch.None, null, 0, null, 0, flow, slot.Mask);
        return slot.Result;
    }

    /// <summary>起点落在走廊掩码外时流场描不到起点，目标却可达:并入起点 tile,按 pad(已垫掩码 ∪ 起点) 重建一次。</summary>
    private FlowField RebuildWithStartTile(NavContext nav, int startCell, int goalCell, FlowField flow, FlowPool pool, WorkerBuffers buffers)
    {
        int navTilesPerSide = nav.Hpa!.NavTilesPerSide;
        int startTile = NavGridSteps.TileOf(startCell, nav.NavCellsPerSide, nav.Hpa.NavCellsPerTile, navTilesPerSide);
        buffers.Corridor[startTile] = 1;
        buffers.Pad[startTile] = 1;
        FlowFieldBuilder.PadMaskInto(buffers.Pad, navTilesPerSide, _config.Flowfield.CorridorPaddingNavTiles, buffers.Union);
        pool.Give(flow);
        long retryMark = Stopwatch.GetTimestamp();
        var rebuilt = FlowFieldBuilder.Build(nav, goalCell, buffers.Union, pool);
        Interlocked.Add(ref _flowTicks, Stopwatch.GetTimestamp() - retryMark);
        return rebuilt;
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

    internal PathServiceStats CopyStats()
    {
        long nav = Interlocked.Read(ref _navTicks);
        long hpa = Interlocked.Read(ref _hpaTicks);
        long flow = Interlocked.Read(ref _flowTicks);
        long extend = Interlocked.Read(ref _extendTicks);
        long extendJobs = Interlocked.Read(ref _extendJobs);
        long workerSum = Interlocked.Read(ref _workerSumTicks);
        long workerLast = Interlocked.Read(ref _workerLastTicks);
        long workerN = Interlocked.Read(ref _workerSamples);
        lock (_gate)
        {
            bool any = _storedReplies > 0;
            bool worked = workerN > 0;
            return new PathServiceStats
            {
                Requests = _requests,
                Replies = _storedReplies,
                Pending = _sentAtMs.Count,
                RoundTripLastMs = any ? _rtLastMs : double.NaN,
                RoundTripMeanMs = any ? (double)_rtSumMs / _storedReplies : double.NaN,
                WorkerLastMs = worked ? CrowdSimulationTelemetry.TicksToMs(workerLast) : double.NaN,
                WorkerMeanMs = worked ? CrowdSimulationTelemetry.TicksToMs(workerSum) / workerN : double.NaN,
                NavMeshMs = CrowdSimulationTelemetry.TicksToMs(nav),
                HpaMs = CrowdSimulationTelemetry.TicksToMs(hpa),
                FlowMs = CrowdSimulationTelemetry.TicksToMs(flow),
                Faulted = Faulted,
                FaultMessage = FaultMessage,
                PlanTimeoutMs = _planTimeout.TotalMilliseconds,
                ExtendJobs = extendJobs,
                ExtendMs = CrowdSimulationTelemetry.TicksToMs(extend),
            };
        }
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
