using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render;

/// <summary>
/// 资产句柄状态机（#1327）。CpuReady/UploadQueued 是 #1328 异步两阶段装载的状态；
/// 同步装载路径为 Unrequested→Preparing→Resident / Failed。
/// </summary>
public enum RaylibAssetState
{
    Unrequested,
    Preparing,
    CpuReady,
    UploadQueued,
    Resident,
    Failed,
}

/// <summary>
/// TryAcquireOrBegin 的结果：Resident=立即可租；InFlight=两阶段装载进行中（本帧不可用、下帧再问）；
/// Failed=负缓存命中（按合同 fail-loud 由调用方抛出）。
/// </summary>
public enum RaylibAssetAcquireOutcome
{
    Resident,
    InFlight,
    Failed,
}

/// <summary>
/// 按 URI 去重的原生资源存储（#1327/#1328）：句柄租约 + 引用计数 + 帧末延迟销毁 + 负缓存版本探测重试 + 链式 fail-loud。
/// 同一 URI 只创建一份物理资源（跨渲染器共享）；引用计数归零后资源进入退役队列，FlushRetired 在帧末真正销毁——
/// 本帧仍在绘制中的引用不会被立即释放。装载失败记录原因与重试次数；文件补齐或内容版本变化（mtime/长度）后
/// 下一次 Acquire 自动重试，不须重启进程。
/// 两阶段异步（#1328）：提供 cpuPrepare（worker 线程：文件 IO/Assimp 转换/图像解码）与 uploader（渲染线程：GL 创建）
/// 后，TryAcquireOrBegin 在未命中时 kick worker 并返回 InFlight；PumpUploads 每帧把 CpuReady 条目上传为 Resident。
/// 未提供两阶段委托时退化为纯同步装载（bootstrap/验收通道，LUDOTS_RAYLIB_SYNC_ASSET_LOAD=1 强制）。
/// </summary>
public sealed class RaylibAssetStore<T> : IDisposable
    where T : struct
{
    public delegate T LoadResource(string fullPath);

    internal sealed class Entry
    {
        public string Uri = string.Empty;
        public RaylibAssetState State;
        public T Resource;
        public int RefCount;
        public string? FailureReason;
        public int RetryCount;
        public long VersionTicks;
        public bool VersionKnown;
        public bool Retired;
        public bool Destroyed;
        public object? PreparedPayload;
        public System.Threading.Tasks.Task? WorkerTask;
    }

    public sealed class Lease : IDisposable
    {
        private readonly RaylibAssetStore<T> _store;
        private readonly Entry _entry;
        private bool _released;

        internal Lease(RaylibAssetStore<T> store, Entry entry)
        {
            _store = store;
            _entry = entry;
        }

        public T Resource => _entry.Resource;

        public string Uri => _entry.Uri;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _store.Release(_entry);
        }
    }

    private readonly IRenderAssetPathResolver? _pathResolver;
    private readonly LoadResource _loader;
    private readonly Action<T> _destroyer;
    private readonly Func<string, object?>? _cpuPrepare;
    private readonly Func<object?, T>? _uploader;
    private readonly Action<object?>? _payloadDisposer;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<Entry> _retired = new();
    private bool _disposed;

    public RaylibAssetStore(
        IRenderAssetPathResolver? pathResolver,
        LoadResource loader,
        Action<T> destroyer,
        Func<string, object?>? cpuPrepare = null,
        Func<object?, T>? uploader = null,
        Action<object?>? payloadDisposer = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _destroyer = destroyer ?? throw new ArgumentNullException(nameof(destroyer));
        _pathResolver = pathResolver;
        if ((cpuPrepare == null) != (uploader == null))
        {
            throw new ArgumentException(
                $"{nameof(RaylibAssetStore<T>)} requires both {nameof(cpuPrepare)} and {nameof(uploader)} for two-phase loading, or neither for synchronous loading.");
        }

        _cpuPrepare = cpuPrepare;
        _uploader = uploader;
        _payloadDisposer = payloadDisposer;
    }

    public int ResidentCount
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (KeyValuePair<string, Entry> kvp in _entries)
                {
                    if (kvp.Value.State == RaylibAssetState.Resident)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    public int RetiredCount
    {
        get
        {
            lock (_gate)
            {
                return _retired.Count;
            }
        }
    }

    /// <summary>按 URI 链获取（或共享）一个驻留资源的租约；链内逐个尝试，全部失败时抛出聚合异常。</summary>
    public Lease Acquire(IReadOnlyList<string> uris)
    {
        if (uris == null || uris.Count == 0)
        {
            throw new InvalidOperationException($"{nameof(RaylibAssetStore<T>)} Acquire called with no URIs.");
        }

        List<string>? failures = null;
        for (int i = 0; i < uris.Count; i++)
        {
            string uri = uris[i];
            if (string.IsNullOrWhiteSpace(uri))
            {
                continue;
            }

            if (TryAcquire(uri, out RaylibAssetStore<T>.Lease? lease, out string? failure))
            {
                return lease!;
            }

            failures ??= new List<string>();
            failures.Add($"'{uri}': {failure}");
        }

        throw new InvalidOperationException(
            $"{nameof(RaylibAssetStore<T>)} could not load any URI [{string.Join(", ", uris)}]: {string.Join("; ", failures ?? new List<string>())})");
    }

    public Lease Acquire(string uri)
    {
        return Acquire(new[] { uri });
    }

    /// <summary>单 URI 获取；失败返回 false 并带出原因（失败已记入负缓存，含重试计数）。装载在锁内同步执行（冷路径）。</summary>
    /// <summary>同步获取：与 TryAcquireOrBegin 共用状态机；遇到 InFlight 条目时等待 worker 并在本线程完成上传
    /// （蒙皮/材质/VFX 等 sync 消费者的合同入口；bootstrap 语义），不与异步路径产生双装载。</summary>
    public bool TryAcquire(string uri, out Lease? lease, out string? failure)
    {
        for (int spin = 0; spin < 1000; spin++)
        {
            RaylibAssetAcquireOutcome outcome = TryAcquireOrBegin(uri, out lease, out failure);
            if (outcome != RaylibAssetAcquireOutcome.InFlight)
            {
                return outcome == RaylibAssetAcquireOutcome.Resident;
            }

            System.Threading.Tasks.Task? worker = null;
            lock (_gate)
            {
                if (_entries.TryGetValue(uri, out Entry? entry))
                {
                    if (entry.State == RaylibAssetState.CpuReady)
                    {
                        PumpUploads();
                        continue;
                    }

                    worker = entry.WorkerTask;
                }
            }

            worker?.Wait();
        }

        throw new InvalidOperationException(
            $"{nameof(RaylibAssetStore<T>)} sync acquire of '{uri}' did not settle after in-flight resolution; worker pipeline is wedged.");
    }
    /// <summary>两阶段获取：Resident 立即租借；未命中且配置了异步委托时 kick worker 并返回 InFlight（本帧不可用，不记负缓存）；
    /// Failed 返回负缓存原因（调用方按合同 fail-loud 抛出）。未配置异步委托时在调用线程同步完成（bootstrap/验收通道）。</summary>
    public RaylibAssetAcquireOutcome TryAcquireOrBegin(string uri, out Lease? lease, out string? status)
    {
        lease = null;
        status = null;
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibAssetStore<T>));
            }

            Entry entry;
            if (_entries.TryGetValue(uri, out Entry? existing))
            {
                entry = existing;
                if (entry.State == RaylibAssetState.Resident)
                {
                    entry.Retired = false;
                    _retired.Remove(entry);
                    entry.RefCount++;
                    lease = new Lease(this, entry);
                    return RaylibAssetAcquireOutcome.Resident;
                }

                if (entry.State == RaylibAssetState.Failed && !VersionChangedSinceLastAttempt(entry))
                {
                    status = entry.FailureReason;
                    return RaylibAssetAcquireOutcome.Failed;
                }

                if (entry.State == RaylibAssetState.Preparing ||
                    entry.State == RaylibAssetState.CpuReady ||
                    entry.State == RaylibAssetState.UploadQueued)
                {
                    status = entry.State.ToString();
                    return RaylibAssetAcquireOutcome.InFlight;
                }
            }
            else
            {
                entry = new Entry { Uri = uri, State = RaylibAssetState.Unrequested };
                _entries[uri] = entry;
            }

            if (!ProbeVersion(uri, out long version, out bool versionKnown, out string? probeFailure))
            {
                entry.RetryCount++;
                entry.State = RaylibAssetState.Failed;
                entry.FailureReason = probeFailure;
                entry.VersionKnown = versionKnown;
                entry.VersionTicks = version;
                status = entry.FailureReason;
                return RaylibAssetAcquireOutcome.Failed;
            }

            entry.VersionKnown = versionKnown;
            entry.VersionTicks = version;

            if (_cpuPrepare == null || _uploader == null)
            {
                entry.RetryCount++;
                entry.State = RaylibAssetState.Preparing;
                try
                {
                    entry.Resource = _loader(_pathResolver!.TryResolveFullPath(uri, out string syncPath) ? syncPath : uri);
                }
                catch (Exception ex)
                {
                    entry.State = RaylibAssetState.Failed;
                    entry.FailureReason = ex.Message;
                    status = entry.FailureReason;
                    return RaylibAssetAcquireOutcome.Failed;
                }

                entry.State = RaylibAssetState.Resident;
                entry.FailureReason = null;
                entry.RefCount++;
                lease = new Lease(this, entry);
                return RaylibAssetAcquireOutcome.Resident;
            }

            if (!_pathResolver!.TryResolveFullPath(uri, out string fullPath))
            {
                entry.State = RaylibAssetState.Failed;
                entry.FailureReason = $"URI '{uri}' does not resolve to a file path.";
                status = entry.FailureReason;
                return RaylibAssetAcquireOutcome.Failed;
            }

            entry.State = RaylibAssetState.Preparing;
            entry.WorkerTask = System.Threading.Tasks.Task.Run(() => RunCpuPhase(entry, fullPath));
            status = RaylibAssetState.Preparing.ToString();
            return RaylibAssetAcquireOutcome.InFlight;
        }
    }

    /// <summary>渲染线程每帧调用：把 CpuReady 条目上传为 Resident（GL 创建只在渲染线程，#1328 两阶段的第二相）。</summary>
    public void PumpUploads()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (KeyValuePair<string, Entry> kvp in _entries)
            {
                Entry entry = kvp.Value;
                if (entry.State != RaylibAssetState.CpuReady)
                {
                    continue;
                }

                entry.State = RaylibAssetState.UploadQueued;
                try
                {
                    entry.Resource = _uploader!(entry.PreparedPayload);
                    entry.State = RaylibAssetState.Resident;
                    entry.FailureReason = null;
                }
                catch (Exception ex)
                {
                    entry.State = RaylibAssetState.Failed;
                    entry.FailureReason = ex.Message;
                }
                finally
                {
                    entry.PreparedPayload = null;
                    entry.WorkerTask = null;
                }
            }
        }
    }

    public int InFlightCount
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (KeyValuePair<string, Entry> kvp in _entries)
                {
                    RaylibAssetState state = kvp.Value.State;
                    if (state == RaylibAssetState.Preparing ||
                        state == RaylibAssetState.CpuReady ||
                        state == RaylibAssetState.UploadQueued)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    private void RunCpuPhase(Entry entry, string fullPath)
    {
        try
        {
            object? payload = _cpuPrepare!(fullPath);
            lock (_gate)
            {
                if (_disposed || entry.Destroyed)
                {
                    _payloadDisposer?.Invoke(payload);
                    return;
                }

                entry.PreparedPayload = payload;
                entry.State = RaylibAssetState.CpuReady;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_disposed || entry.Destroyed)
                {
                    return;
                }

                entry.RetryCount++;
                entry.State = RaylibAssetState.Failed;
                entry.FailureReason = ex.Message;
                entry.WorkerTask = null;
            }
        }
    }

    /// <summary>状态查询；retryCount 为已提交的装载尝试次数（含首次），负缓存命中不计。</summary>
    public bool TryGetState(string uri, out RaylibAssetState state, out string? failureReason, out int retryCount)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(uri, out Entry? entry))
            {
                state = entry.State;
                failureReason = entry.FailureReason;
                retryCount = entry.RetryCount;
                return true;
            }

            state = RaylibAssetState.Unrequested;
            failureReason = null;
            retryCount = 0;
            return false;
        }
    }

    /// <summary>清除负缓存：下一次 Acquire 无条件重试（文件补齐/内容变化在版本探测下本就自动重试，此为显式入口）。</summary>
    public void Invalidate(string uri)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(uri, out Entry? entry) && entry.State == RaylibAssetState.Failed)
            {
                entry.State = RaylibAssetState.Unrequested;
                entry.FailureReason = null;
                entry.VersionKnown = false;
            }
        }
    }

    /// <summary>帧末调用：销毁引用归零的退役资源；在退役后被重新租用的条目自动复活。</summary>
    public void FlushRetired()
    {
        lock (_gate)
        {
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                Entry entry = _retired[i];
                if (entry.RefCount != 0 || entry.Retired == false)
                {
                    continue;
                }

                _destroyer(entry.Resource);
                entry.Destroyed = true;
                _entries.Remove(entry.Uri);
                _retired.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        System.Threading.Tasks.Task[] workers;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            workers = new System.Threading.Tasks.Task[_entries.Count];
            int w = 0;
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (pair.Value.WorkerTask != null)
                {
                    workers[w++] = pair.Value.WorkerTask;
                }
            }
        }

        foreach (System.Threading.Tasks.Task? worker in workers)
        {
            worker?.Wait();
        }

        lock (_gate)
        {

            foreach (KeyValuePair<string, Entry> kvp in _entries)
            {
                Entry entry = kvp.Value;
                if (entry.State == RaylibAssetState.Resident && !entry.Destroyed)
                {
                    _destroyer(entry.Resource);
                    entry.Destroyed = true;
                }

                if (entry.PreparedPayload != null)
                {
                    _payloadDisposer?.Invoke(entry.PreparedPayload);
                    entry.PreparedPayload = null;
                }
            }

            _entries.Clear();
            _retired.Clear();
        }
    }

    private void Release(Entry entry)
    {
        lock (_gate)
        {
            if (entry.Destroyed || _disposed)
            {
                return;
            }

            entry.RefCount--;
            if (entry.RefCount <= 0 && entry.State == RaylibAssetState.Resident)
            {
                entry.Retired = true;
                if (!_retired.Contains(entry))
                {
                    _retired.Add(entry);
                }
            }
        }
    }

    private bool VersionChangedSinceLastAttempt(Entry entry)
    {
        if (!ProbeVersion(entry.Uri, out long version, out bool versionKnown, out _))
        {
            return false;
        }

        return versionKnown != entry.VersionKnown || version != entry.VersionTicks;
    }

    private bool ProbeVersion(string uri, out long version, out bool versionKnown, out string? failure)
    {
        version = 0;
        versionKnown = false;
        if (_pathResolver == null)
        {
            failure = "no IRenderAssetPathResolver wired; cannot resolve asset URIs.";
            return false;
        }

        if (!_pathResolver.TryResolveFullPath(uri, out string fullPath))
        {
            failure = $"URI '{uri}' does not resolve to a file path.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            failure = $"file missing: '{fullPath}'.";
            return false;
        }

        FileInfo info = new(fullPath);
        version = info.LastWriteTimeUtc.Ticks ^ info.Length;
        versionKnown = true;
        failure = null;
        return true;
    }
}
