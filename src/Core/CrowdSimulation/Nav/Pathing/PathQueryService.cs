using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Nav.Pathing;

/// <summary>一次两点路径请求(格号 + 上下文 + 起端层)。</summary>
public readonly record struct PathQuery(int NavContextId, int StartCell, int GoalCell, int Level);

/// <summary>路径答复:折线 + 方向图(流场);不可达时 Points 为 null。</summary>
public sealed class PathResult
{
    public required PathQuery Query { get; init; }
    public required bool Reachable { get; init; }
    public required CorridorQuery.Branch Branch { get; init; }
    /// <summary>拉直折线(格单位,x, y 平铺);链接层来自流场路点链。</summary>
    public required Fix64[]? Points { get; init; }
    /// <summary>逐路点层号(null = 全地面)。</summary>
    public required int[]? PointLayers { get; init; }
    /// <summary>方向图(调用方持有;弃用经 PathQueryService.Recycle 还池)。</summary>
    public required FlowField? Flow { get; init; }
    public required byte[]? CorridorMask { get; init; }
}

/// <summary>路径服务故障:请求超过 planTimeoutMs 未返回(报错,绝不重试或换算法)。</summary>
public sealed class PathServiceFaultException : InvalidOperationException
{
    public PathServiceFaultException(string message) : base(message) { }
}

/// <summary>
/// 固定生效帧路径服务(planning/localPathService.js + workers/workerPathService.js 契约):
/// 请求在 requestTick 发出,生效帧 = requestTick + latencyTicks;仿真在生效帧取答复,
/// 没算完就等待(仿真时钟不走,等待次数计数),超过 planTimeoutMs 未返回即故障报错。
/// 后台 worker 数 1 / 2 / 4 可切换:任务是镜像静态导航状态的纯函数,每个 worker 持有
/// 自己的搜索缓冲(RT-03),结果与线程时序无关。
/// </summary>
public sealed class PathQueryService : IDisposable
{
    private readonly IReadOnlyDictionary<int, NavContext> _navs;
    private readonly CrowdSimulationRuntimeConfig _config;
    private readonly int _latencyTicks;
    private readonly TimeSpan _planTimeout;

    private readonly object _gate = new();
    private readonly Queue<(int Id, PathQuery Query)> _pending = new();
    private readonly Dictionary<int, PathResult> _replies = new();
    private readonly Dictionary<int, int> _dueTick = new();
    private readonly Dictionary<int, long> _sentAtMs = new();
    private readonly List<Thread> _workers = new();
    private readonly System.Collections.Concurrent.ConcurrentBag<FlowPool> _pools = new();
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
    public bool Faulted { get; private set; }
    public string? FaultMessage { get; private set; }
    public int WorkerCount { get { lock (_gate) return _workerTarget; } }

    public PathQueryService(
        IReadOnlyDictionary<int, NavContext> navs,
        CrowdSimulationRuntimeConfig config,
        int workerThreads,
        TimeSpan planTimeout)
    {
        _navs = navs;
        _config = config;
        _latencyTicks = config.Planning.LatencyTicks;
        _planTimeout = planTimeout;
        if (workerThreads < 1) throw new ArgumentOutOfRangeException(nameof(workerThreads));
        SetWorkerCount(workerThreads);
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

    public int DueTick(int id)
    {
        lock (_gate) return _dueTick[id];
    }

    /// <summary>生效帧取答复:没算完就阻塞等待(仿真暂停,结果仍于生效帧生效);超时即故障。</summary>
    public PathResult AwaitDue(int id)
    {
        var sw = Stopwatch.StartNew();
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

    /// <summary>轮询(不就绪立刻返回 false;超时检测与 AwaitDue 同契约)。</summary>
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

    private void WorkerLoop(object? state)
    {
        int workerIndex = (int)state!;
        var pool = new FlowPool(_config.NavCellCount, _config.Flowfield.PoolCapacity);
        _pools.Add(pool);
        var scratch = new CorridorQuery.Scratch(_config.Hpa.ClusterSize * _config.Hpa.ClusterSize);
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

            if (TestDelayPerJob > TimeSpan.Zero) Thread.Sleep(TestDelayPerJob);
            PathResult result;
            try
            {
                result = Compute(job.Query, pool, scratch);
            }
            catch (Exception ex)
            {
                // worker 崩溃 = 服务故障(navClient 契约:报错,绝不静默重试或换算法)
                lock (_gate)
                {
                    FailLocked($"路径 worker 内部错误(请求 #{job.Id}): {ex.GetType().Name}: {ex.Message}");
                }

                return;
            }

            lock (_gate)
            {
                _replies[job.Id] = result;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private PathResult Compute(PathQuery q, FlowPool pool, CorridorQuery.Scratch scratch)
    {
        var nav = _navs[q.NavContextId];
        int n = nav.CellCount;
        int cs = nav.Hpa!.ClusterSize, cc = nav.Hpa.ClustersPerSide;
        var mask = new byte[cc * cc];
        mask[NavGridSteps.ClusterOf(q.GoalCell, n, cs, cc)] = 1;
        var corridor = CorridorQuery.CorridorTo(nav, _config, q.StartCell, q.GoalCell, mask, q.Level, scratch);
        var padded = FlowFieldBuilder.PadMask(mask, cc, _config.Flowfield.CorridorPadding);
        var flow = FlowFieldBuilder.Build(nav, q.GoalCell, padded, pool);

        Fix64[]? points;
        int[]? layers = null;
        if (corridor.Points != null && nav.Links == null)
        {
            points = corridor.Points;
        }
        else
        {
            // 链接层 / 无走廊:领队折线从流场路点链描出(D33)
            var fp = CorridorQuery.FlowPoints(flow, q.StartCell);
            points = fp?.Points;
            layers = fp?.Layers;
        }

        if (points == null)
        {
            pool.Give(flow);
            flow = null;
        }

        return new PathResult
        {
            Query = q,
            Reachable = points != null,
            Branch = corridor.Kind,
            Points = points,
            PointLayers = layers,
            Flow = flow,
            CorridorMask = mask,
        };
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
