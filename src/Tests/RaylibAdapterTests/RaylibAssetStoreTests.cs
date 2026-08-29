using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibAssetStoreTests
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
    private int _nextId;
    private int _loadAttempts;
    private Func<string, FakeResource>? _loaderOverride;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"raylib-asset-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _destroyed = new List<int>();
        _nextId = 1;
        _loadAttempts = 0;
        _loaderOverride = null;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RaylibAssetStore<FakeResource> CreateStore()
    {
        return new RaylibAssetStore<FakeResource>(
            new FakeResolver(_root),
            path =>
            {
                _loadAttempts++;
                if (_loaderOverride != null)
                {
                    return _loaderOverride(path);
                }

                return new FakeResource(_nextId++);
            },
            resource => _destroyed.Add(resource.Id));
    }

    private void WriteAsset(string uri, string content = "asset")
    {
        string path = Path.Combine(_root, uri.Replace("mod:", "").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Test]
    public void SameUriAcrossConsumers_LoadsOnce_AndSharesOnePhysicalResource()
    {
        using var store = CreateStore();
        WriteAsset("mod:a.png");

        using RaylibAssetStore<FakeResource>.Lease first = store.Acquire("mod:a.png");
        using RaylibAssetStore<FakeResource>.Lease second = store.Acquire("mod:a.png");

        Assert.That(_loadAttempts, Is.EqualTo(1), "同 URI 两次租约只允许一次物理装载");
        Assert.That(first.Resource.Id, Is.EqualTo(second.Resource.Id));
        Assert.That(store.ResidentCount, Is.EqualTo(1));
    }

    [Test]
    public void ReleaseToZero_RetiresUntilFlush_FrameDelayedDestruction()
    {
        using var store = CreateStore();
        WriteAsset("mod:a.png");

        RaylibAssetStore<FakeResource>.Lease leaseA = store.Acquire("mod:a.png");
        RaylibAssetStore<FakeResource>.Lease leaseB = store.Acquire("mod:a.png");
        int resourceId = leaseA.Resource.Id;

        leaseA.Dispose();
        Assert.That(_destroyed, Is.Empty, "仍有一个租约引用，不得销毁");
        leaseB.Dispose();
        Assert.That(_destroyed, Is.Empty, "引用归零后进入退役队列，延迟到帧末销毁");
        Assert.That(store.RetiredCount, Is.EqualTo(1));

        store.FlushRetired();
        Assert.That(_destroyed, Is.EqualTo(new[] { resourceId }), "帧末 Flush 销毁且仅一次");
        Assert.That(store.ResidentCount, Is.EqualTo(0));
    }

    [Test]
    public void RetiredResource_ReacquiredBeforeFlush_RevivesWithoutReload()
    {
        using var store = CreateStore();
        WriteAsset("mod:a.png");

        RaylibAssetStore<FakeResource>.Lease lease = store.Acquire("mod:a.png");
        int resourceId = lease.Resource.Id;
        lease.Dispose();
        using RaylibAssetStore<FakeResource>.Lease revived = store.Acquire("mod:a.png");

        Assert.That(_loadAttempts, Is.EqualTo(1), "退役未销毁的资源复活不得重新装载");
        Assert.That(revived.Resource.Id, Is.EqualTo(resourceId));
        store.FlushRetired();
        Assert.That(_destroyed, Is.Empty, "复活后 Flush 不得销毁在租资源");
    }

    [Test]
    public void LoadFailure_NegativeCacheHolds_UntilSourceVersionChanges()
    {
        using var store = CreateStore();
        _loaderOverride = _ => throw new InvalidOperationException("corrupt payload");

        WriteAsset("mod:a.png");
        Assert.That(store.TryAcquire("mod:a.png", out _, out string? firstFailure), Is.False);
        Assert.That(firstFailure, Does.Contain("corrupt payload"));

        Assert.That(store.TryAcquire("mod:a.png", out _, out string? secondFailure), Is.False, "版本未变化：负缓存直接命中，不再尝试");
        Assert.That(_loadAttempts, Is.EqualTo(1), "负缓存期间不得重复装载尝试");
        Assert.That(store.TryGetState("mod:a.png", out RaylibAssetState state, out string? reason, out int retryCount), Is.True);
        Assert.That(state, Is.EqualTo(RaylibAssetState.Failed));
        Assert.That(reason, Does.Contain("corrupt payload"));
        Assert.That(retryCount, Is.EqualTo(1), "负缓存命中不计新重试");

        _loaderOverride = null;
        string assetPath = Path.Combine(_root, "a.png");
        File.WriteAllText(assetPath, "repaired content");
        File.SetLastWriteTimeUtc(assetPath, DateTime.UtcNow.AddMinutes(1));

        using RaylibAssetStore<FakeResource>.Lease repaired = store.Acquire("mod:a.png");
        Assert.That(_loadAttempts, Is.EqualTo(2), "文件版本变化后自动重试并成功");
        Assert.That(store.TryGetState("mod:a.png", out RaylibAssetState repairedState, out _, out int finalRetryCount), Is.True);
        Assert.That(repairedState, Is.EqualTo(RaylibAssetState.Resident));
        Assert.That(finalRetryCount, Is.EqualTo(2));
    }

    [Test]
    public void MissingFile_FailsAndRetries_WhenFileAppears_NoRestartNeeded()
    {
        using var store = CreateStore();
        Assert.That(store.TryAcquire("mod:late.png", out _, out string? missing), Is.False);
        Assert.That(missing, Does.Contain("file missing"));

        WriteAsset("mod:late.png");
        using RaylibAssetStore<FakeResource>.Lease lease = store.Acquire("mod:late.png");
        Assert.That(lease.Resource.Id, Is.GreaterThan(0), "文件补齐后同进程内可重入装载");
    }

    [Test]
    public void ChainAcquire_FallsThroughFailedUri_AndThrowsWhenAllFail()
    {
        using var store = CreateStore();
        WriteAsset("mod:good.png");
        WriteAsset("mod:bad.png");
        _loaderOverride = path => path.EndsWith("bad.png")
            ? throw new InvalidOperationException("bad content")
            : new FakeResource(_nextId++);

        using RaylibAssetStore<FakeResource>.Lease lease =
            store.Acquire(new[] { "mod:bad.png", "mod:good.png" });
        Assert.That(lease.Uri, Is.EqualTo("mod:good.png"), "链内失败 URI 应继续尝试下一个");
        Assert.That(store.TryGetState("mod:bad.png", out RaylibAssetState badState, out string? badReason, out _), Is.True);
        Assert.That(badState, Is.EqualTo(RaylibAssetState.Failed));
        Assert.That(badReason, Does.Contain("bad content"));

        InvalidOperationException aggregate = Assert.Throws<InvalidOperationException>(() =>
            store.Acquire(new[] { "mod:bad.png", "mod:worse.png" }));
        Assert.That(aggregate.Message, Does.Contain("mod:bad.png"));
        Assert.That(aggregate.Message, Does.Contain("mod:worse.png"));
    }

    [Test]
    public void Dispose_DestroysEachResidentExactlyOnce_NoDoubleFree()
    {
        var store = CreateStore();
        WriteAsset("mod:a.png");
        WriteAsset("mod:b.png");

        RaylibAssetStore<FakeResource>.Lease a = store.Acquire("mod:a.png");
        RaylibAssetStore<FakeResource>.Lease b = store.Acquire("mod:b.png");
        a.Dispose();
        store.Dispose();
        b.Dispose();
        store.FlushRetired();

        Assert.That(_destroyed.Count, Is.EqualTo(2), "每个驻留资源恰好销毁一次");
        Assert.That(_destroyed, Is.Unique);
    }

    [Test]
    public void Invalidate_ForcesRetryWithoutVersionChange()
    {
        using var store = CreateStore();
        _loaderOverride = _ => throw new InvalidOperationException("always failing");

        WriteAsset("mod:a.png");
        _ = store.TryAcquire("mod:a.png", out _, out _);
        _ = store.TryAcquire("mod:a.png", out _, out _);
        Assert.That(_loadAttempts, Is.EqualTo(1));

        store.Invalidate("mod:a.png");
        _ = store.TryAcquire("mod:a.png", out _, out string? failure);
        Assert.That(_loadAttempts, Is.EqualTo(2), "显式 Invalidate 后无视版本立即重试");
        Assert.That(failure, Does.Contain("always failing"));
    }
}
