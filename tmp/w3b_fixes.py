path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibAssetStore.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

# --- 阻断1: TryAcquire routes through TryAcquireOrBegin and completes in-flight entries synchronously ---
old_sig = "    public bool TryAcquire(string uri, out Lease? lease, out string? failure)"
idx = t.find(old_sig)
assert idx >= 0
ls = t.rfind("\n", 0, idx) + 1
ob = t.find("{", idx)
depth = 0
end = None
for i in range(ob, len(t)):
    if t[i] == "{":
        depth += 1
    elif t[i] == "}":
        depth -= 1
        if depth == 0:
            end = i + 1
            break
while end < len(t) and t[end] == "\n":
    end += 1
new_ta = '''    /// <summary>同步获取：与 TryAcquireOrBegin 共用状态机；遇到 InFlight 条目时等待 worker 并在本线程完成上传
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
'''
t = t[:ls] + new_ta + t[end:]

# --- 应改2: single RetryCount per attempt (kick site no longer increments; sync path and worker-failure each once) ---
old = """            entry.RetryCount++;
            entry.VersionKnown = versionKnown;
            entry.VersionTicks = version;

            if (_cpuPrepare == null || _uploader == null)
            {
                entry.State = RaylibAssetState.Preparing;"""
new = """            entry.VersionKnown = versionKnown;
            entry.VersionTicks = version;

            if (_cpuPrepare == null || _uploader == null)
            {
                entry.RetryCount++;
                entry.State = RaylibAssetState.Preparing;"""
assert old in t
t = t.replace(old, new, 1)

# --- 应改3: reject half-configured async delegates ---
old = """        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _destroyer = destroyer ?? throw new ArgumentNullException(nameof(destroyer));
        _pathResolver = pathResolver;
        _cpuPrepare = cpuPrepare;
        _uploader = uploader;
    }"""
new = """        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
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
    }"""
assert old in t
t = t.replace(old, new, 1)

# ctor signature gains payloadDisposer
old = """    public RaylibAssetStore(
        IRenderAssetPathResolver? pathResolver,
        LoadResource loader,
        Action<T> destroyer,
        Func<string, object?>? cpuPrepare = null,
        Func<object?, T>? uploader = null)"""
new = """    public RaylibAssetStore(
        IRenderAssetPathResolver? pathResolver,
        LoadResource loader,
        Action<T> destroyer,
        Func<string, object?>? cpuPrepare = null,
        Func<object?, T>? uploader = null,
        Action<object?>? payloadDisposer = null)"""
assert old in t
t = t.replace(old, new, 1)

old = """    private readonly Func<string, object?>? _cpuPrepare;
    private readonly Func<object?, T>? _uploader;"""
new = """    private readonly Func<string, object?>? _cpuPrepare;
    private readonly Func<object?, T>? _uploader;
    private readonly Action<object?>? _payloadDisposer;"""
assert old in t
t = t.replace(old, new, 1)

# --- 应改1: Dispose waits workers + disposes pending payloads (native leak) ---
old = """    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
"""
new = """    public void Dispose()
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
"""
assert old in t
t = t.replace(old, new, 1)

old = """            foreach (KeyValuePair<string, Entry> kvp in _entries)
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
    }"""
new = """            foreach (KeyValuePair<string, Entry> kvp in _entries)
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
    }"""
assert old in t
t = t.replace(old, new, 1)

# RunCpuPhase: after dispose, dispose the payload it produced (worker completed during dispose wait is handled by main loop above;
# but worker completing AFTER the dispose-wait snapshot: entry.Destroyed never set for non-resident... guard via _disposed check + payload disposer)
old = """        catch (Exception ex)
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
        }"""
new = """        catch (Exception ex)
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

        static void DisposeIfOrphaned(RaylibAssetStore<T> store, Entry entry)
        {
            if (store._disposed && entry.PreparedPayload != null)
            {
                store._payloadDisposer?.Invoke(entry.PreparedPayload);
                entry.PreparedPayload = null;
            }
        }"""
# simpler: in RunCpuPhase success path, after setting payload, check disposed
old2 = """                entry.PreparedPayload = payload;
                entry.State = RaylibAssetState.CpuReady;
            }
        }
        catch"""
new2 = """                if (_disposed || entry.Destroyed)
                {
                    _payloadDisposer?.Invoke(payload);
                    return;
                }

                entry.PreparedPayload = payload;
                entry.State = RaylibAssetState.CpuReady;
            }
        }
        catch"""
assert old2 in t
t = t.replace(old2, new2, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("store fixes applied")
