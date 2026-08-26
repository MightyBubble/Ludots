using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Platform.Desktop;
using Ludots.Tests;
using NUnit.Framework;
using SavePanelMod;
using SavePanelMod.Runtime;

namespace Ludots.Tests.Persistence;

/// <summary>
/// save_load showcase acceptance: UI must reuse SavePanelMod (zero private save panel).
/// </summary>
[TestFixture]
public sealed class SaveLoadShowcaseAcceptanceTests
{
    private string _root = null!;
    private GameEngine _engine = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ludots-save-load-show-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _engine = Boot(_root);
    }

    [TearDown]
    public void TearDown()
    {
        _engine.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void ReusesSavePanel_SaveRestoreRoundTrip()
    {
        Assert.That(_engine.TryGetService(SavePanelModEntry.RuntimeKey, out SavePanelRuntime? panel), Is.True);
        Assert.That(panel, Is.Not.Null);
        panel!.Show(_engine);
        Assert.That(panel.IsVisible(_engine), Is.True);

        panel.SetManualName("showcase");
        panel.RequestManualSave(_engine);
        Step(_engine);
        panel.DrainPendingAfterFixedStep(_engine);
        Assert.That(panel.Error, Is.Null, panel.Status);
        Assert.That(panel.SelectedSlot, Is.EqualTo("manual/showcase"));

        Step(_engine);
        int after = _engine.GameSession.CurrentTick;
        panel.RestoreSelected(_engine);
        Assert.That(panel.Error, Is.Null, panel.Status);
        Assert.That(_engine.GameSession.CurrentTick, Is.LessThanOrEqualTo(after));
        Assert.That(panel.BuildPanelState(_engine).StorageRoot, Is.EqualTo(Path.GetFullPath(_root)));
    }

    [Test]
    public void TamperFailsVisibly_OnSharedPanel()
    {
        var panel = _engine.GetService(SavePanelModEntry.RuntimeKey)!;
        panel.SetManualName("showcase");
        panel.RequestManualSave(_engine);
        Step(_engine);
        panel.DrainPendingAfterFixedStep(_engine);

        string path = Path.Combine(_root, "saves", "manual", "showcase.ldsave");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^3] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        panel.SelectSlot("manual/showcase");
        panel.RestoreSelected(_engine);
        Assert.That(panel.Error, Is.Not.Null);
        Assert.That(panel.Error!, Does.Contain("hash").IgnoreCase);
    }

    private static void Step(GameEngine engine)
    {
        ((TurnBasedPacemaker)engine.Pacemaker).Step();
        engine.Tick(1f);
    }

    private static GameEngine Boot(string storageRoot)
    {
        string repo = FindRepo();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repo, new[] { "LudotsCoreMod", "CoreInputMod", "SavePanelMod" }),
            Path.Combine(repo, "assets"));
        engine.SetService(CoreServiceKeys.SaveStorage, (ISaveStorage)new DesktopSaveStorage(storageRoot));
        engine.LoadStartupMap();
        engine.Pacemaker = new TurnBasedPacemaker();
        engine.SimulationBudgetMsPerFrame = int.MaxValue;
        engine.SimulationMaxSlicesPerLogicFrame = 1000;
        engine.Start();
        Step(engine);
        if (!engine.TryGetService(SavePanelModEntry.RuntimeKey, out _))
        {
            var runtime = new SavePanelRuntime();
            runtime.BindEngine(engine);
            engine.SetService(SavePanelModEntry.RuntimeKey, runtime);
        }

        return engine;
    }

    private static string FindRepo()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if ((Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                && Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "mods")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("repo root");
    }
}
