using System.Collections.Concurrent;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using Sango.Core;
using Sango.Runtime;
using Sango.WebUi;

namespace Ludots.Tests.SangoWebUi;

/// <summary>
/// M1.d 可选增量验收:sango.save / sango.load 命令走 Ludots 正式持久化管线
/// (WorldSnapshotService → 引擎 ISaveStorage 槽位 → WorldRestoreService),
/// sango.sim 域在引擎存档注册表里承载世界态。headless 下用真实 GameEngine +
/// 内存 ISaveStorage;存储服务缺席时命令必须类型化失败(无 fallback)。
/// </summary>
[TestFixture]
public sealed class SangoSaveCommandTests
{
    private const int Seed = 20260902;

    private static string RepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate Ludots repo root (showcase.registry.json).");
    }

    private static readonly WebUiCommandRequest SaveRequest = new(
        SangoSaveCommandHandler.CommandName, ClientSeq: 0, EntityRefs: Array.Empty<WebUiEntityRef>(), Payload: default);
    private static readonly WebUiCommandRequest LoadRequest = new(
        SangoLoadCommandHandler.CommandName, ClientSeq: 0, EntityRefs: Array.Empty<WebUiEntityRef>(), Payload: default);

    private static List<string> ResolveMods(params string[] modIds)
    {
        var discovered = ModDiscovery.DiscoverMods(new[] { Path.Combine(RepoRoot(), "mods") });
        var byName = discovered.ToDictionary(mod => mod.Manifest.Name, mod => mod.DirectoryPath);
        return modIds.Select(id => byName[id]).ToList();
    }

    private static GameEngine CreateEngine()
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            ResolveMods("LudotsCoreMod", "SangoContentMod"),
            Path.Combine(RepoRoot(), "assets"));
        engine.LoadStartupMap();
        return engine;
    }

    [Test]
    public void SaveCommand_PersistsSangoDomainThroughEngineStorage()
    {
        using GameEngine engine = CreateEngine();
        engine.GetService(CoreServiceKeys.SaveParticipants)!.Register(
            new SangoSaveParticipant(engine.VFS!, "SangoContentMod"));
        var storage = new MemorySaveStorage();
        engine.SetService(CoreServiceKeys.SaveStorage, storage);

        SangoKernelBoot.Boot(engine.VFS!, "SangoContentMod", Seed, "Scenario/Scenario.json");
        SangoTurnDriver.AdvanceTurn();

        var handler = new SangoSaveCommandHandler(engine);
        WebUiCommandResult result = handler.HandleAsync(SaveRequest).AsTask().GetAwaiter().GetResult();
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(storage.ListFileKeys("saves/").Count, Is.EqualTo(1), "exactly one manual slot must exist");

        string digestAtSave = SangoTurnDriver.WorldDigest();
        SangoTurnDriver.AdvanceTurn();
        SangoTurnDriver.AdvanceTurn();
        Assert.That(SangoTurnDriver.WorldDigest(), Is.Not.EqualTo(digestAtSave), "world must move past the save point");

        var loadHandler = new SangoLoadCommandHandler(engine);
        WebUiCommandResult loadResult = loadHandler.HandleAsync(LoadRequest).AsTask().GetAwaiter().GetResult();
        Assert.That(loadResult.Success, Is.True, loadResult.Message);
        Assert.That(SangoTurnDriver.WorldDigest(), Is.EqualTo(digestAtSave),
            "load must restore the kernel to the saved point through the formal pipeline");
    }

    [Test]
    public void SaveCommand_WithoutKernelOrStorage_FailsWithTypeErrors()
    {
        using GameEngine engine = CreateEngine();
        engine.GetService(CoreServiceKeys.SaveParticipants)!.Register(
            new SangoSaveParticipant(engine.VFS!, "SangoContentMod"));

        // 进程内内核单例可能被兄弟 fixture 启动过;按 Player.Quit 语义先关停,
        // 使 kernel_not_booted 分支可断言。
        Scenario.Cur?.OnGameShutdown();

        WebUiCommandResult noKernel = new SangoSaveCommandHandler(engine).HandleAsync(SaveRequest).AsTask().GetAwaiter().GetResult();
        Assert.That(noKernel.Success, Is.False);
        Assert.That(noKernel.ErrorCode, Is.EqualTo("kernel_not_booted"));

        // 内核启动但宿主未注册 ISaveStorage:类型化失败,不落任何文件。
        SangoKernelBoot.Boot(engine.VFS!, "SangoContentMod", Seed, "Scenario/Scenario.json");
        WebUiCommandResult noStorage = new SangoSaveCommandHandler(engine).HandleAsync(SaveRequest).AsTask().GetAwaiter().GetResult();
        Assert.That(noStorage.Success, Is.False);
        Assert.That(noStorage.ErrorCode, Is.EqualTo("storage_unavailable"));

        WebUiCommandResult noSlot = new SangoLoadCommandHandler(engine).HandleAsync(LoadRequest).AsTask().GetAwaiter().GetResult();
        Assert.That(noSlot.Success, Is.False);
        Assert.That(noSlot.ErrorCode, Is.EqualTo("storage_unavailable"));
    }

    private sealed class MemorySaveStorage : ISaveStorage
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public IReadOnlyList<string> ListFileKeys(string prefix)
        {
            return _files.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
        }

        public bool Exists(string key)
        {
            return _files.ContainsKey(key);
        }

        public byte[] ReadAllBytes(string key)
        {
            return _files.TryGetValue(key, out byte[]? bytes)
                ? bytes.ToArray()
                : throw new FileNotFoundException(key);
        }

        public void WriteAllBytes(string key, byte[] bytes)
        {
            _files[key] = bytes.ToArray();
        }

        public void CommitTempFile(string tempKey, string finalKey)
        {
            _files[finalKey] = ReadAllBytes(tempKey);
            _files.Remove(tempKey);
        }

        public void Delete(string key)
        {
            _files.Remove(key);
        }
    }
}
