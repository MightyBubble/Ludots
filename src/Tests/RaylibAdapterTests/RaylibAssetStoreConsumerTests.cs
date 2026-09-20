using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Tests.RaylibAdapter;

/// <summary>
/// RaylibAssetStore 的消费者级回归（#1327 复核补测）：Dispose 后获取、材质部分绑定失败的租约回收、
/// 蒙皮缓存链式回退聚合。装载/销毁全部走假委托，不触碰 GL。
/// </summary>
[TestFixture]
public sealed class RaylibAssetStoreConsumerTests
{
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

    private sealed class FakeMaterialAssets : IRenderMaterialAssets
    {
        public ResolvedMaterialAsset Resolved;

        public FakeMaterialAssets(ResolvedMaterialAsset resolved)
        {
            Resolved = resolved;
        }

        public bool TryGet(int id, out MaterialAssetDescriptor descriptor)
        {
            descriptor = new MaterialAssetDescriptor();
            return false;
        }

        public bool TryResolve(int id, out ResolvedMaterialAsset material)
        {
            material = Resolved;
            return true;
        }

        public int GetId(string key)
        {
            return 1;
        }

        public string GetName(int id)
        {
            return "fake-material";
        }
    }

    private string _root = null!;
    private List<int> _destroyed = null!;
    private int _nextTextureId;
    private HashSet<string>? _failingUris;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"raylib-asset-consumer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _destroyed = new List<int>();
        _nextTextureId = 10;
        _failingUris = null;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RaylibAssetStore<Texture2D> CreateTextureStore()
    {
        return new RaylibAssetStore<Texture2D>(
            new FakeResolver(_root),
            _ =>
            {
                if (_failingUris != null)
                {
                    foreach (string failing in _failingUris)
                    {
                        if (_.EndsWith(failing))
                        {
                            throw new InvalidOperationException("fake texture failure");
                        }
                    }
                }

                return new Texture2D { id = (uint)_nextTextureId++, width = 4, height = 4 };
            },
            texture => _destroyed.Add((int)texture.id));
    }

    private void WriteAsset(string uri)
    {
        string path = Path.Combine(_root, uri.Replace("mod:", "").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
    }

    [Test]
    public void TryAcquire_AfterDispose_ThrowsEvenWithoutVersionProbe()
    {
        var store = CreateTextureStore();
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.TryAcquire("mod:any.png", out _, out _));
        Assert.Throws<ObjectDisposedException>(() => store.Acquire("mod:any.png"));
    }

    [Test]
    public void MaterialBinding_PartialFailure_RecoversAcquiredLeases()
    {
        using var store = CreateTextureStore();
        WriteAsset("mod:albedo.png");
        WriteAsset("mod:roughness.png");
        _failingUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "roughness.png" };

        var resolved = new ResolvedMaterialAsset(
            id: 1,
            shaderKey: "raylib/lit",
            domain: MaterialAssetDomain.Surface,
            flags: MaterialAssetFlags.None,
            floats: new Dictionary<string, float>(),
            colors: new Dictionary<string, System.Numerics.Vector4>(),
            textureUris: new Dictionary<string, string>
            {
                ["albedo"] = "mod:albedo.png",
                ["roughness"] = "mod:roughness.png",
            });
        using var library = new RaylibMaterialLibrary(new FakeResolver(_root), new FakeMaterialAssets(resolved), store);

        Assert.Throws<InvalidOperationException>(
            () => library.TryGetPbrParams(1, out _, out _, out _, out _, out _));
        Assert.That(store.RetiredCount, Is.EqualTo(1), "部分绑定失败后 albedo 租约应被回收进入退役队列");

        store.FlushRetired();
        Assert.That(_destroyed.Count, Is.EqualTo(1), "回收的 albedo 贴图在冲刷时销毁，不随库 Dispose 泄漏");
    }

    [Test]
    public void SkinnedCache_ChainFallback_AggregatesUriFailures()
    {
        WriteAsset("mod:first.glb");
        WriteAsset("mod:second.glb");
        _failingUris = null;
        var modelStore = new RaylibAssetStore<Model>(
            new FakeResolver(_root),
            fullPath => throw new InvalidOperationException($"fake model failure for {Path.GetFileName(fullPath)}"),
            _ => _destroyed.Add(-1));

        using var cache = new RaylibGpuSkinnedModelCache(new FakeResolver(_root), modelStore);
        var descriptor = new MeshAssetDescriptor
        {
            Type = MeshAssetType.Model,
            SourceUris = new[] { "mod:first.glb", "mod:second.glb" },
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => cache.GetOrLoad(7, descriptor));
        Assert.That(error.Message, Does.Contain("mod:first.glb"), "聚合异常应列出首个 URI 的失败原因");
        Assert.That(error.Message, Does.Contain("mod:second.glb"), "链式回退应尝试并记录第二个 URI");
    }

    [Test]
    public void SkinnedCache_ColdRequestReturnsInFlightWithoutAdvancingTheUriChain()
    {
        WriteAsset("mod:first.glb");
        WriteAsset("mod:second.glb");
        using var workerStarted = new ManualResetEventSlim(false);
        using var releaseWorker = new ManualResetEventSlim(false);
        int prepareCalls = 0;
        using var modelStore = new RaylibAssetStore<Model>(
            new FakeResolver(_root),
            _ => default,
            _ => { },
            cpuPrepare: fullPath =>
            {
                Interlocked.Increment(ref prepareCalls);
                workerStarted.Set();
                releaseWorker.Wait();
                return fullPath;
            },
            uploader: _ => default);
        using var cache = new RaylibGpuSkinnedModelCache(new FakeResolver(_root), modelStore);
        var descriptor = new MeshAssetDescriptor
        {
            Type = MeshAssetType.Model,
            SourceUris = new[] { "mod:first.glb", "mod:second.glb" },
        };

        RaylibGpuSkinnedModelAcquireOutcome first = cache.TryGetOrLoad(7, descriptor, out _, out _);
        Assert.That(first, Is.EqualTo(RaylibGpuSkinnedModelAcquireOutcome.InFlight));
        Assert.That(workerStarted.Wait(TimeSpan.FromSeconds(2)), Is.True);

        RaylibGpuSkinnedModelAcquireOutcome second = cache.TryGetOrLoad(7, descriptor, out _, out _);
        Assert.That(second, Is.EqualTo(RaylibGpuSkinnedModelAcquireOutcome.InFlight));
        Assert.That(Volatile.Read(ref prepareCalls), Is.EqualTo(1), "同一候选仍在装载时不得并行启动后续 URI");

        releaseWorker.Set();
        Assert.That(SpinWait.SpinUntil(
            () => modelStore.TryGetState("mod:first.glb", out RaylibAssetState state, out _, out _) &&
                state == RaylibAssetState.CpuReady,
            TimeSpan.FromSeconds(2)),
            Is.True);
    }
}
