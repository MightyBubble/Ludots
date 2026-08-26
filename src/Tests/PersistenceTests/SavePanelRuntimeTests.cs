using System.IO;
using System.Linq;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Platform.Abstractions;
using Ludots.Platform.Desktop;
using Ludots.Tests;
using NUnit.Framework;
using SavePanelMod;
using SavePanelMod.Runtime;

namespace Ludots.Tests.Persistence;

/// <summary>
/// SavePanelMod runtime contracts: formal SaveSlotStore path, checkpoint-boundary write,
/// visible fail-fast on missing storage / tampered slots, ShowPanel activation.
/// </summary>
[TestFixture]
public sealed class SavePanelRuntimeTests
{
    private string _root = null!;
    private GameEngine _engine = null!;
    private SavePanelRuntime _runtime = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ludots-save-panel-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _engine = CreateEngine(_root, withStorage: true);
        UseTurnBased(_engine);
        _runtime = new SavePanelRuntime();
        _runtime.BindEngine(_engine);
        _runtime.Show(_engine);
    }

    [TearDown]
    public void TearDown()
    {
        _runtime.UnbindEngine(_engine);
        _engine.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void SaveListRestoreDelete_RoundTripThroughFormalSlots()
    {
        _runtime.SetManualName("panel-a");
        _runtime.RequestManualSave(_engine);
        Assert.That(_runtime.HasPendingCapture, Is.True);

        StepOnce(_engine);
        _runtime.DrainPendingAfterFixedStep(_engine);

        Assert.That(_runtime.Error, Is.Null, _runtime.Status);
        Assert.That(_runtime.SelectedSlot, Is.EqualTo("manual/panel-a"));
        Assert.That(_runtime.BuildPanelState(_engine).Slots.Count, Is.EqualTo(1));
        Assert.That(_runtime.BuildPanelState(_engine).StorageRoot, Is.EqualTo(Path.GetFullPath(_root)));

        StepOnce(_engine);
        int tickAfterPlay = _engine.GameSession.CurrentTick;

        _runtime.RestoreSelected(_engine);
        Assert.That(_runtime.Error, Is.Null, _runtime.Status);
        Assert.That(_engine.GameSession.CurrentTick, Is.LessThan(tickAfterPlay));

        _runtime.DeleteSelected(_engine);
        Assert.That(_runtime.Error, Is.Null, _runtime.Status);
        Assert.That(_runtime.BuildPanelState(_engine).Slots.Count, Is.EqualTo(0));
    }

    [Test]
    public void TamperedSlot_RestoreFailsWithVisibleError()
    {
        _runtime.SetManualName("tamper");
        _runtime.RequestManualSave(_engine);
        StepOnce(_engine);
        _runtime.DrainPendingAfterFixedStep(_engine);
        Assert.That(_runtime.Error, Is.Null, _runtime.Status);

        string path = Path.Combine(_root, "saves", "manual", "tamper.ldsave");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^3] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        _runtime.SelectSlot("manual/tamper");
        _runtime.RestoreSelected(_engine);
        Assert.That(_runtime.Error, Is.Not.Null.And.Contain("hash"));
        Assert.That(_runtime.Status, Does.StartWith("失败："));
    }

    [Test]
    public void MissingStorage_FailsClosedWithoutSilentFallback()
    {
        using GameEngine bare = CreateEngine(_root, withStorage: false);
        UseTurnBased(bare);
        var runtime = new SavePanelRuntime();
        runtime.BindEngine(bare);

        Assert.That(runtime.Error, Is.Not.Null.And.Contain("ISaveStorage"));
        runtime.RequestManualSave(bare);
        Assert.That(runtime.Error, Is.Not.Null.And.Contain("ISaveStorage"));
        Assert.That(runtime.HasPendingCapture, Is.False);
    }

    [Test]
    public void ShowPanelActivation_TogglesVisibility()
    {
        Assert.That(_runtime.IsVisible(_engine), Is.True);
        _runtime.ToggleVisible(_engine);
        Assert.That(_runtime.IsVisible(_engine), Is.False);
        _runtime.ToggleVisible(_engine);
        Assert.That(_runtime.IsVisible(_engine), Is.True);
        Assert.That(
            _engine.GetService(CoreServiceKeys.PanelActivationStore)!.IsVisible(SavePanelIds.PanelType),
            Is.True);
    }

    [Test]
    public void Autosave_RotatesWithoutDeletingManual()
    {
        _runtime.SetManualName("keep-me");
        _runtime.RequestManualSave(_engine);
        StepOnce(_engine);
        _runtime.DrainPendingAfterFixedStep(_engine);

        for (int i = 0; i < 5; i++)
        {
            _runtime.RequestAutosave(_engine);
            StepOnce(_engine);
            _runtime.DrainPendingAfterFixedStep(_engine);
            Assert.That(_runtime.Error, Is.Null, _runtime.Status);
        }

        var state = _runtime.BuildPanelState(_engine);
        Assert.That(state.Slots.Count(s => s.Kind == "manual"), Is.EqualTo(1));
        Assert.That(state.Slots.Count(s => s.Kind == "autosave"), Is.EqualTo(3));
    }

    private static void StepOnce(GameEngine engine)
    {
        ((TurnBasedPacemaker)engine.Pacemaker).Step();
        engine.Tick(1f);
    }

    private static void UseTurnBased(GameEngine engine)
    {
        engine.Pacemaker = new TurnBasedPacemaker();
        engine.SimulationBudgetMsPerFrame = int.MaxValue;
        engine.SimulationMaxSlicesPerLogicFrame = 1000;
        engine.Start();
        StepOnce(engine);
    }

    private static GameEngine CreateEngine(string storageRoot, bool withStorage)
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string gitPath = Path.Combine(dir, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                Directory.Exists(Path.Combine(dir, "src")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                break;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(dir!, new[] { "LudotsCoreMod" }),
            Path.Combine(dir!, "assets"));
        engine.LoadStartupMap();
        if (withStorage)
        {
            engine.SetService(CoreServiceKeys.SaveStorage, (ISaveStorage)new DesktopSaveStorage(storageRoot));
        }

        return engine;
    }
}
