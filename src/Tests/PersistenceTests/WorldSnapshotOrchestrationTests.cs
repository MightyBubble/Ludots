using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class WorldSnapshotOrchestrationTests
{
    [Test]
    public void SnapshotRestoreRoundTripRestoresWorldAndDomainState()
    {
        using GameEngine source = CreateInitializedEngine();
        using GameEngine target = CreateInitializedEngine();
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();

        Entity savedEntity = source.World.Create(
            new Name { Value = "saved-actor" },
            WorldPositionCm.FromCm(100, 200),
            new GameplayTagContainer());
        source.GameSession.Globals["score"] = 9;
        source.GameSession.FixedUpdate();

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        ref WorldPositionCm sourcePosition = ref source.World.Get<WorldPositionCm>(savedEntity);
        sourcePosition = WorldPositionCm.FromCm(777, 888);
        source.GameSession.Globals["score"] = 42;
        target.World.Create(new Name { Value = "target-only" }, WorldPositionCm.FromCm(-1, -2));

        restoreService.Restore(target, snapshot);

        Entity restored = FindSingleByName(target.World, "saved-actor");
        ref readonly WorldPositionCm restoredPosition = ref target.World.Get<WorldPositionCm>(restored);
        Assert.That(restoredPosition.ToWorldCmInt2(), Is.EqualTo(new Ludots.Core.Mathematics.WorldCmInt2(100, 200)));
        Assert.That(target.GameSession.CurrentTick, Is.EqualTo(snapshot.Header.Tick));
        Assert.That(target.GameSession.Globals["score"], Is.EqualTo(9));
        Assert.That(FindByName(target.World, "target-only"), Is.EqualTo(Entity.Null));
    }

    [Test]
    public void RestoreRejectsDamagedWorldBlobWithoutMutatingCurrentWorld()
    {
        using GameEngine source = CreateInitializedEngine();
        using GameEngine target = CreateInitializedEngine();
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();

        source.World.Create(new Name { Value = "saved-actor" }, WorldPositionCm.FromCm(100, 200));
        target.World.Create(new Name { Value = "target-survives" }, WorldPositionCm.FromCm(3, 4));
        int targetTickBeforeRestore = target.GameSession.CurrentTick;

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
        byte[] damagedWorld = snapshot.WorldBytes[..(snapshot.WorldBytes.Length / 2)];
        var damaged = snapshot with { WorldBytes = damagedWorld };

        Assert.Throws<SaveContextException>(() => restoreService.Restore(target, damaged));

        Entity preserved = FindSingleByName(target.World, "target-survives");
        ref readonly WorldPositionCm preservedPosition = ref target.World.Get<WorldPositionCm>(preserved);
        Assert.That(preservedPosition.ToWorldCmInt2(), Is.EqualTo(new Ludots.Core.Mathematics.WorldCmInt2(3, 4)));
        Assert.That(target.GameSession.CurrentTick, Is.EqualTo(targetTickBeforeRestore));
    }

    [Test]
    public void SnapshotExcludesEntitiesOutsideSaveInclusionPolicy()
    {
        using GameEngine source = CreateInitializedEngine();
        using GameEngine target = CreateInitializedEngine();
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();

        source.World.Create(new Name { Value = "saved-actor" }, WorldPositionCm.FromCm(1, 2));
        source.World.Create(new Name { Value = "excluded-actor" }, new SaveExcludedTag());

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        restoreService.Restore(target, snapshot);

        Assert.That(FindByName(target.World, "saved-actor"), Is.Not.EqualTo(Entity.Null));
        Assert.That(FindByName(target.World, "excluded-actor"), Is.EqualTo(Entity.Null));
    }

    [Test]
    public void RestoredEngineContinuesFixedStepDeterministically()
    {
        using GameEngine continuous = CreateInitializedEngine();
        using GameEngine restored = CreateInitializedEngine();
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();

        UseTurnBasedPacemaker(continuous);
        UseTurnBasedPacemaker(restored);
        RunFixedSteps(continuous, 2);

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            continuous,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        restoreService.Restore(restored, snapshot);

        string[] continuousTrace = RunFixedSteps(continuous, 3);
        string[] restoredTrace = RunFixedSteps(restored, 3);

        Assert.That(restoredTrace, Is.EqualTo(continuousTrace));
    }

    private static Entity FindSingleByName(World world, string name)
    {
        Entity entity = FindByName(world, name);
        Assert.That(entity, Is.Not.EqualTo(Entity.Null));
        return entity;
    }

    private static Entity FindByName(World world, string name)
    {
        Entity found = Entity.Null;
        int count = 0;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name entityName) =>
        {
            if (string.Equals(entityName.Value, name, StringComparison.Ordinal))
            {
                found = entity;
                count++;
            }
        });

        Assert.That(count, Is.LessThanOrEqualTo(1));
        return found;
    }

    private static GameEngine CreateInitializedEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
            Path.Combine(repoRoot, "assets"));
        engine.LoadMap(engine.MergedConfig.StartupMapId);
        Assert.That(engine.GetService(CoreServiceKeys.SaveParticipants), Is.Not.Null);
        return engine;
    }

    private static void UseTurnBasedPacemaker(GameEngine engine)
    {
        engine.Pacemaker = new TurnBasedPacemaker();
        engine.SimulationBudgetMsPerFrame = int.MaxValue;
        engine.SimulationMaxSlicesPerLogicFrame = 1000;
        engine.Start();
    }

    private static string[] RunFixedSteps(GameEngine engine, int count)
    {
        var trace = new string[count];
        var pacemaker = (TurnBasedPacemaker)engine.Pacemaker;
        IClock clock = engine.GetService(CoreServiceKeys.Clock);
        for (int i = 0; i < count; i++)
        {
            pacemaker.Step();
            engine.Tick(1f);
            trace[i] = $"tick={engine.GameSession.CurrentTick};fixedFrame={clock.Now(ClockDomainId.FixedFrame)}";
        }

        return trace;
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            string gitPath = Path.Combine(dir, ".git");
            if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                Directory.Exists(Path.Combine(dir, "src")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }
}
