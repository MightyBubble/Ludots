using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Platform.Desktop;
using Ludots.Tests;
using NUnit.Framework;
using SaveLoadShowcaseMod;
using SaveLoadShowcaseMod.Runtime;
using SavePanelMod;
using SavePanelMod.Runtime;

namespace Ludots.Tests.Persistence;

/// <summary>
/// Player story: move patrol → save → move farther → load → back at saved point.
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
    public void PatrolMoveSaveMoveLoad_ReturnsToSavedPoint()
    {
        Assert.That(_engine.TryGetService(SavePanelModEntry.RuntimeKey, out SavePanelRuntime? panel), Is.True);
        panel!.Show(_engine);
        panel.SetManualName(SaveLoadShowcaseIds.SlotName);

        Entity patrol = SpawnPatrol(_engine, 1000, 2000);
        int spawnX = 1000, spawnY = 2000;

        MoveEntity(_engine, patrol, 400, 0);
        AssertPosition(_engine, patrol, spawnX + 400, spawnY);

        panel.RequestManualSave(_engine);
        Step(_engine);
        panel.DrainPendingAfterFixedStep(_engine);
        Assert.That(panel.Error, Is.Null, panel.Status);
        int savedX = spawnX + 400;
        int savedY = spawnY;

        MoveEntity(_engine, patrol, 800, 200);
        AssertPosition(_engine, patrol, savedX + 800, savedY + 200);

        panel.SelectSlot($"manual/{SaveLoadShowcaseIds.SlotName}");
        panel.RestoreSelected(_engine);
        Assert.That(panel.Error, Is.Null, panel.Status);
        AssertPosition(_engine, patrol, savedX, savedY);
    }

    [Test]
    public void AblateReset_ReturnsToFactory_NotSavedPoint()
    {
        var panel = _engine.GetService(SavePanelModEntry.RuntimeKey)!;
        panel.SetManualName(SaveLoadShowcaseIds.SlotName);
        Entity patrol = SpawnPatrol(_engine, 500, 500);
        WorldSaveSnapshot factory = new WorldSnapshotService().Capture(
            _engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        MoveEntity(_engine, patrol, 400, 0);
        panel.RequestManualSave(_engine);
        Step(_engine);
        panel.DrainPendingAfterFixedStep(_engine);
        MoveEntity(_engine, patrol, 400, 0);

        new WorldRestoreService().Restore(_engine, factory);
        Assert.That(TryFindByName(_engine, SaveLoadShowcaseIds.PatrolName, out Entity restored), Is.True);
        AssertPosition(_engine, restored, 500, 500);
    }

    [Test]
    public void TamperFailsVisibly_OnSharedPanel()
    {
        var panel = _engine.GetService(SavePanelModEntry.RuntimeKey)!;
        panel.SetManualName(SaveLoadShowcaseIds.SlotName);
        SpawnPatrol(_engine, 100, 100);
        panel.RequestManualSave(_engine);
        Step(_engine);
        panel.DrainPendingAfterFixedStep(_engine);

        string path = Path.Combine(_root, "saves", "manual", $"{SaveLoadShowcaseIds.SlotName}.ldsave");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^3] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        panel.SelectSlot($"manual/{SaveLoadShowcaseIds.SlotName}");
        panel.RestoreSelected(_engine);
        Assert.That(panel.Error, Is.Not.Null);
        Assert.That(panel.Error!, Does.Contain("hash").IgnoreCase);
    }

    [Test]
    public void StoryPanel_ExposesActorLoopCopy()
    {
        var runtime = new SaveLoadShowcaseRuntime();
        var state = runtime.BuildPanelState();
        Assert.That(state.Hook, Does.Contain("巡逻兵"));
        Assert.That(state.StepGuide, Does.Contain("挪"));
        Assert.That(state.Controls, Does.Contain("存这一档").Or.Contain("F"));
    }

    private static Entity SpawnPatrol(GameEngine engine, int x, int y)
    {
        return engine.World.Create(
            new Name { Value = SaveLoadShowcaseIds.PatrolName },
            WorldPositionCm.FromCm(x, y));
    }

    private static void MoveEntity(GameEngine engine, Entity entity, int dx, int dy)
    {
        ref WorldPositionCm pos = ref engine.World.Get<WorldPositionCm>(entity);
        var cm = pos.ToWorldCmInt2();
        pos = WorldPositionCm.FromCm(cm.X + dx, cm.Y + dy);
        Step(engine);
    }

    private static void AssertPosition(GameEngine engine, Entity entity, int x, int y)
    {
        // After restore, entity handle may be stale — resolve by name.
        if (!engine.World.IsAlive(entity) || !engine.World.Has<Name>(entity))
        {
            Assert.That(TryFindByName(engine, SaveLoadShowcaseIds.PatrolName, out entity), Is.True);
        }

        var cm = engine.World.Get<WorldPositionCm>(entity).ToWorldCmInt2();
        Assert.That(cm.X, Is.EqualTo(x), "patrol X");
        Assert.That(cm.Y, Is.EqualTo(y), "patrol Y");
    }

    private static bool TryFindByName(GameEngine engine, string name, out Entity entity)
    {
        entity = default;
        Entity found = default;
        bool ok = false;
        var q = new QueryDescription().WithAll<Name, WorldPositionCm>();
        engine.World.Query(in q, (Entity e, ref Name n, ref WorldPositionCm _) =>
        {
            if (ok) return;
            if (!string.Equals(n.Value, name, StringComparison.Ordinal)) return;
            found = e;
            ok = true;
        });
        entity = found;
        return ok;
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
