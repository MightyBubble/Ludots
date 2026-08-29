using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibAssetStoreAsyncTests
{
    private readonly struct FakeResource
    {
        public FakeResource(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    private sealed class FakeResolver : IRenderAssetPathResolver
    {
        private readonly string _root;

        public FakeResolver(string root)
        {
            _root = root;
        }

        public bool TryResolveFullPath(string uri, out string fullPath)
        {
            fullPath = Path.Combine(_root, uri.Replace("mod:", "").Replace('/', Path.DirectorySeparatorChar));
            return true;
        }
    }

    private string _root = null!;
    private List<int> _destroyed = null!;
    private ManualResetEventSlim _cpuPhaseGate = null!;
    private Func<string, object?>? _cpuPrepare;
    private Func<object?, FakeResource>? _uploader;
    private int _workerAttempts;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"raylib-asset-async-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _destroyed = new List<int>();
        _cpuPhaseGate = new ManualResetEventSlim(initialState: true);
        _workerAttempts = 0;
        _cpuPrepare = fullPath =>
        {
            _workerAttempts++;
            _cpuPhaseGate.Wait();
            return $"payload:{fullPath}";
        };
        _uploader = payload => new FakeResource(((string)payload!).Length);
    }

    [TearDown]
    public void TearDown()
    {
        _cpuPhaseGate.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RaylibAssetStore<FakeResource> CreateAsyncStore()
    {
        return new RaylibAssetStore<FakeResource>(
            new FakeResolver(_root),
            _ => new FakeResource(-1),
            resource => _destroyed.Add(resource.Id),
            _cpuPrepare,
            _uploader);
    }

    private void WriteAsset(string uri)
    {
        string path = Path.Combine(_root, uri.Replace("mod:", "").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "asset");
    }

    [Test]
    public void AsyncLifecycle_KickInFlight_CpuReadyThenPumpBecomesResident()
    {
        using var store = CreateAsyncStore();
        WriteAsset("mod:a.png");
        _cpuPhaseGate.Reset();

        RaylibAssetAcquireOutcome first = store.TryAcquireOrBegin("mod:a.png", out RaylibAssetStore<FakeResource>.Lease? _, out string? status1);
        Assert.That(first, Is.EqualTo(RaylibAssetAcquireOutcome.InFlight));
        Assert.That(status1, Is.EqualTo(RaylibAssetState.Preparing.ToString()));

        store.PumpUploads();
        RaylibAssetAcquireOutcome stillInFlight = store.TryAcquireOrBegin("mod:a.png", out _, out string? status2);
        Assert.That(stillInFlight, Is.EqualTo(RaylibAssetAcquireOutcome.InFlight), "CPU 相未完成时帧泵不得产出 Resident");
        Assert.That(store.InFlightCount, Is.EqualTo(1));

        _cpuPhaseGate.Set();
        Assert.That(SpinWait.SpinUntil(() => store.TryGetState("mod:a.png", out RaylibAssetState cpuReady, out _, out _) && cpuReady == RaylibAssetState.CpuReady, 5000),
            "worker 完成后应进入 CpuReady");

        store.PumpUploads();
        RaylibAssetAcquireOutcome resident = store.TryAcquireOrBegin("mod:a.png", out RaylibAssetStore<FakeResource>.Lease? lease, out _);
        Assert.That(resident, Is.EqualTo(RaylibAssetAcquireOutcome.Resident));
        Assert.That(lease!.Resource.Id, Is.GreaterThan(0));
        Assert.That(store.InFlightCount, Is.EqualTo(0));
        lease.Dispose();
    }

    [Test]
    public void WorkerFailure_BecomesFailed_FailLoudOnNextAsk_NoRepeatUntilVersionChange()
    {
        using var store = CreateAsyncStore();
        WriteAsset("mod:bad.png");
        int throwingAttempts = 0;
        var failing = new RaylibAssetStore<FakeResource>(
            new FakeResolver(_root),
            _ => new FakeResource(-1),
            r => _destroyed.Add(r.Id),
            cpuPrepare: _ =>
            {
                throwingAttempts++;
                throw new InvalidOperationException("worker explode");
            },
            uploader: _uploader);

        Assert.That(failing.TryAcquireOrBegin("mod:bad.png", out _, out _), Is.EqualTo(RaylibAssetAcquireOutcome.InFlight));
        Assert.That(SpinWait.SpinUntil(() =>
        {
            _ = failing.TryGetState("mod:bad.png", out RaylibAssetState s, out _, out _);
            return s == RaylibAssetState.Failed;
        }, 5000), "worker 异常应落到 Failed");

        Assert.That(failing.TryAcquireOrBegin("mod:bad.png", out _, out string? failure), Is.EqualTo(RaylibAssetAcquireOutcome.Failed));
        Assert.That(failure, Does.Contain("worker explode"));
        int failuresObserved = 0;
        for (int i = 0; i < 3; i++)
        {
            if (failing.TryAcquireOrBegin("mod:bad.png", out _, out _) == RaylibAssetAcquireOutcome.Failed)
            {
                failuresObserved++;
            }
        }

        Assert.That(failuresObserved, Is.EqualTo(3), "负缓存期间重复询问全部命中 Failed");
        Assert.That(throwingAttempts, Is.EqualTo(1), "负缓存期间不得重复 kick worker");
    }

    [Test]
    public void SyncFallback_WithoutAsyncDelegates_CompletesOnCallerThread()
    {
        using var store = new RaylibAssetStore<FakeResource>(
            new FakeResolver(_root),
            _ => new FakeResource(7),
            resource => _destroyed.Add(resource.Id));
        WriteAsset("mod:sync.png");

        RaylibAssetAcquireOutcome outcome = store.TryAcquireOrBegin("mod:sync.png", out RaylibAssetStore<FakeResource>.Lease? lease, out _);
        Assert.That(outcome, Is.EqualTo(RaylibAssetAcquireOutcome.Resident), "无异步委托时必须同步完成（bootstrap 通道）");
        Assert.That(lease!.Resource.Id, Is.EqualTo(7));
        lease.Dispose();
    }

    [Test]
    public void PumpUploads_UploadFailure_BecomesFailedWithReason()
    {
        _uploader = _ => throw new InvalidOperationException("gl upload explode");
        using var store = CreateAsyncStore();
        WriteAsset("mod:upfail.png");
        Assert.That(store.TryAcquireOrBegin("mod:upfail.png", out _, out _), Is.EqualTo(RaylibAssetAcquireOutcome.InFlight));
        Assert.That(SpinWait.SpinUntil(() =>
        {
            _ = store.TryGetState("mod:upfail.png", out RaylibAssetState s, out _, out _);
            return s == RaylibAssetState.CpuReady;
        }, 5000), Is.True);

        store.PumpUploads();
        Assert.That(store.TryGetState("mod:upfail.png", out RaylibAssetState state, out string? reason, out _), Is.True);
        Assert.That(state, Is.EqualTo(RaylibAssetState.Failed));
        Assert.That(reason, Does.Contain("gl upload explode"));
    }

    [Test]
    public void HalfConfiguredAsyncDelegates_RejectedFailLoud()
    {
        Assert.Throws<ArgumentException>(() => new RaylibAssetStore<FakeResource>(
            new FakeResolver(_root),
            _ => new FakeResource(-1),
            _ => { },
            cpuPrepare: _ => "payload"));
    }

    [Test]
    public void SyncTryAcquire_ResolvesInFlightEntryOnCallerThread()
    {
        using var store = CreateAsyncStore();
        WriteAsset("mod:syncwait.png");
        Assert.That(store.TryAcquireOrBegin("mod:syncwait.png", out _, out _), Is.EqualTo(RaylibAssetAcquireOutcome.InFlight));
        Assert.That(store.TryAcquire("mod:syncwait.png", out RaylibAssetStore<FakeResource>.Lease? lease, out string? failure), Is.True,
            "sync API 必须等待 worker 并在本线程完成上传（蒙皮/材质/VFX 合同）");
        Assert.That(failure, Is.Null);
        Assert.That(lease!.Resource.Id, Is.GreaterThan(0));
        lease.Dispose();
    }

    [Test]
    public void Dispose_WaitsWorkersAndDisposesPendingPayloads()
    {
        bool payloadDisposed = false;
        var gate = new ManualResetEventSlim(false);
        var store = new RaylibAssetStore<FakeResource>(
            new FakeResolver(_root),
            _ => new FakeResource(-1),
            _ => { },
            cpuPrepare: fullPath =>
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                return $"payload:{fullPath}";
            },
            uploader: payload => new FakeResource(1),
            payloadDisposer: _ => payloadDisposed = true);
        WriteAsset("mod:dispose-wait.png");
        Assert.That(store.TryAcquireOrBegin("mod:dispose-wait.png", out _, out _), Is.EqualTo(RaylibAssetAcquireOutcome.InFlight));
        Thread poolRelease = new(() =>
        {
            Thread.Sleep(50);
            gate.Set();
        });
        poolRelease.Start();
        store.Dispose();
        Assert.That(payloadDisposed, Is.True, "Dispose 等待 worker 后必须清理未上传的 CPU payload（native Image 防泄漏）");
    }
}
