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
/// 按 URI 去重的原生资源存储（#1327）：句柄租约 + 引用计数 + 帧末延迟销毁 + 负缓存版本探测重试 + 链式 fail-loud。
/// 同一 URI 只创建一份物理资源（跨渲染器共享）；引用计数归零后资源进入退役队列，FlushRetired 在帧末真正销毁——
/// 本帧仍在绘制中的引用不会被立即释放。装载失败记录原因与重试次数；文件补齐或内容版本变化（mtime/长度）后
/// 下一次 Acquire 自动重试，不须重启进程。装载/销毁经注入的委托执行，逻辑可无 GL 单测。
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
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<Entry> _retired = new();
    private bool _disposed;

    public RaylibAssetStore(IRenderAssetPathResolver? pathResolver, LoadResource loader, Action<T> destroyer)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _destroyer = destroyer ?? throw new ArgumentNullException(nameof(destroyer));
        _pathResolver = pathResolver;
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
    public bool TryAcquire(string uri, out Lease? lease, out string? failure)
    {
        lease = null;
        failure = null;
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
                    return true;
                }

                if (entry.State == RaylibAssetState.Failed && !VersionChangedSinceLastAttempt(entry))
                {
                    failure = entry.FailureReason;
                    return false;
                }
            }
            else
            {
                entry = new Entry { Uri = uri, State = RaylibAssetState.Unrequested };
                _entries[uri] = entry;
            }

            entry.RetryCount++;
            if (!ProbeVersion(uri, out long version, out bool versionKnown, out string? probeFailure))
            {
                entry.State = RaylibAssetState.Failed;
                entry.FailureReason = probeFailure;
                entry.VersionKnown = versionKnown;
                entry.VersionTicks = version;
                failure = entry.FailureReason;
                return false;
            }

            entry.VersionKnown = versionKnown;
            entry.VersionTicks = version;
            entry.State = RaylibAssetState.Preparing;
            try
            {
                if (_pathResolver == null || !_pathResolver.TryResolveFullPath(uri, out string fullPath))
                {
                    throw new InvalidOperationException("no path resolver or URI does not resolve.");
                }

                entry.Resource = _loader(fullPath);
            }
            catch (Exception ex)
            {
                entry.State = RaylibAssetState.Failed;
                entry.FailureReason = ex.Message;
                failure = entry.FailureReason;
                return false;
            }

            entry.State = RaylibAssetState.Resident;
            entry.FailureReason = null;
            entry.RefCount++;
            lease = new Lease(this, entry);
            return true;
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
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (KeyValuePair<string, Entry> kvp in _entries)
            {
                Entry entry = kvp.Value;
                if (entry.State == RaylibAssetState.Resident && !entry.Destroyed)
                {
                    _destroyer(entry.Resource);
                    entry.Destroyed = true;
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
