using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
[NonParallelizable]
public sealed class SaveSystemUatTests
{
    [Test]
    public void CrossLoadContextSaveShowcaseRestoresNamedActorAndContinues()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "save-system-cross-load-context");
        Directory.CreateDirectory(artifactDir);

        var loadContext = new CrossLoadContextTypeTestHarness.DuplicateAssemblyLoadContext();
        try
        {
            Type duplicateNameType = CrossLoadContextTypeTestHarness.LoadDuplicateType(loadContext, typeof(Name));
            Assert.That(duplicateNameType, Is.Not.SameAs(typeof(Name)));

            var typeFormatter = new CrossLoadContextTypeTestHarness.SubstitutingTypeFormatter(
                typeof(Name),
                duplicateNameType);
            var serializer = new LudotsBinaryWorldSerializer(typeFormatter);
            var snapshotService = new WorldSnapshotService(serializer);
            var restoreService = new WorldRestoreService(serializer);
            var slots = new SaveSlotStore(new MemorySaveStorage());

            using GameEngine continuous = CreateCoreSaveShowcaseEngine(repoRoot);
            using GameEngine restored = CreateCoreSaveShowcaseEngine(repoRoot);
            UseTurnBasedPacemaker(continuous);
            UseTurnBasedPacemaker(restored);

            Entity pilot = continuous.World.Create(
                new Name { Value = "HAN Save Pilot" },
                WorldPositionCm.FromCm(3200, 6400),
                new GameplayTagContainer());
            continuous.GameSession.Globals["missionObjective"] = "Hold the northern gate";
            RunFixedSteps(continuous, 2);

            SaveShowcaseTrace savePoint = CaptureSaveShowcaseTrace(continuous, pilot, "save-point");
            WorldSaveSnapshot snapshot = snapshotService.Capture(
                continuous,
                SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
            slots.WriteSlot(SaveSlotId.Manual("cross-load-context-showcase"), snapshot);

            restored.World.Create(
                new Name { Value = "Unsaved Target Draft" },
                WorldPositionCm.FromCm(-100, -200));
            restored.GameSession.Globals["missionObjective"] = "Target world was mutated";

            WorldSaveSnapshot storedSnapshot = slots.ReadSlot(SaveSlotId.Manual("cross-load-context-showcase"));
            restoreService.Restore(restored, storedSnapshot);

            Entity restoredPilot = FindSingleByName(restored.World, "HAN Save Pilot");
            SaveShowcaseTrace restoredPoint = CaptureSaveShowcaseTrace(restored, restoredPilot, "loaded");
            string[] continuousTrace = RunFixedSteps(continuous, 3);
            string[] restoredTrace = RunFixedSteps(restored, 3);

            Assert.Multiple(() =>
            {
                Assert.That(storedSnapshot.Header.MapId, Is.EqualTo(continuous.CurrentMapSession.MapId.Value));
                Assert.That(restoredPoint.ActorName, Is.EqualTo(savePoint.ActorName));
                Assert.That(restoredPoint.ActorPosition, Is.EqualTo(savePoint.ActorPosition));
                Assert.That(restoredPoint.MissionObjective, Is.EqualTo("Hold the northern gate"));
                Assert.That(restoredPoint.GameSessionTick, Is.EqualTo(savePoint.GameSessionTick));
                Assert.That(restoredPoint.FixedFrame, Is.EqualTo(savePoint.FixedFrame));
                Assert.That(TryFindByName(restored.World, "Unsaved Target Draft"), Is.EqualTo(Entity.Null));
                Assert.That(restoredTrace, Is.EqualTo(continuousTrace));
                Assert.That(typeFormatter.SubstitutionHitCount, Is.GreaterThan(0));
            });

            File.WriteAllText(
                Path.Combine(artifactDir, "trace.jsonl"),
                BuildCrossLoadContextTraceJsonl(savePoint, restoredPoint, continuousTrace, restoredTrace),
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(artifactDir, "scenario-card.md"),
                BuildCrossLoadContextScenarioCard(savePoint, restoredPoint),
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(artifactDir, "manual-uat.md"),
                BuildCrossLoadContextManualUatScript(),
                Encoding.UTF8);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Test]
    public void CrossLoadContextSaveShowcaseRejectsMismatchedComponentContractWithoutMutatingWorld()
    {
        string repoRoot = FindRepoRoot();
        using GameEngine source = CreateCoreSaveShowcaseEngine(repoRoot);
        using GameEngine target = CreateCoreSaveShowcaseEngine(repoRoot);
        var snapshotService = new WorldSnapshotService();
        var typeFormatter = new CrossLoadContextTypeTestHarness.SubstitutingTypeFormatter(
            typeof(Name),
            typeof(WorldPositionCm));
        var restoreService = new WorldRestoreService(new LudotsBinaryWorldSerializer(typeFormatter));

        source.World.Create(
            new Name { Value = "Saved Contract Pilot" },
            WorldPositionCm.FromCm(11, 22));
        target.World.Create(
            new Name { Value = "Current Mission Survives" },
            WorldPositionCm.FromCm(33, 44));

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        var error = Assert.Throws<SaveContextException>(() => restoreService.Restore(target, snapshot));

        Assert.That(error!.Message, Does.Contain("Save world.bin is invalid"));
        Assert.That(error.Message, Does.Contain("does not match archetype component type"));
        Assert.Multiple(() =>
        {
            Assert.That(typeFormatter.SubstitutionHitCount, Is.GreaterThan(0));
            Assert.That(TryFindByName(target.World, "Saved Contract Pilot"), Is.EqualTo(Entity.Null));
            Entity preserved = FindSingleByName(target.World, "Current Mission Survives");
            ref readonly WorldPositionCm preservedPosition = ref target.World.Get<WorldPositionCm>(preserved);
            Assert.That(preservedPosition.ToWorldCmInt2(), Is.EqualTo(new Ludots.Platform.Abstractions.WorldCmInt2(33, 44)));
        });
    }

    [Test]
    public void RtsTrainingShowcaseSaveLoadFlowWritesAcceptanceArtifacts()
    {
        string repoRoot = FindRepoRoot();
        string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "save-system");
        Directory.CreateDirectory(artifactDir);

        using GameEngine continuous = CreateRtsTrainingEngine(repoRoot);
        using GameEngine restored = CreateRtsTrainingEngine(repoRoot);
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();
        var storage = new MemorySaveStorage();
        var slots = new SaveSlotStore(storage);

        UseTurnBasedPacemaker(continuous);
        UseTurnBasedPacemaker(restored);

        Entity warFactory = FindSingleByName(continuous.World, "War Factory");
        Entity armorDisplay = FindSingleByName(continuous.World, "Armor Display");
        Entity marker = continuous.World.Create(
            new Name { Value = "Save UAT Marker" },
            WorldPositionCm.FromCm(12345, 23456),
            new GameplayTagContainer());

        continuous.GameSession.Globals["uatStage"] = "saved";
        continuous.GameSession.FixedUpdate();
        RunFixedSteps(continuous, 2);

        SavePointTrace savePoint = CaptureTrace(continuous, warFactory, armorDisplay, marker, "save-point");
        WorldSaveSnapshot snapshot = snapshotService.Capture(
            continuous,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
        slots.WriteSlot(SaveSlotId.Manual("rts-training-uat"), snapshot);

        restored.World.Create(
            new Name { Value = "Restore Target Mutation" },
            WorldPositionCm.FromCm(777, 888));
        restored.GameSession.Globals["uatStage"] = "target-mutated";

        WorldSaveSnapshot storedSnapshot = slots.ReadSlot(SaveSlotId.Manual("rts-training-uat"));
        restoreService.Restore(restored, storedSnapshot);

        Entity restoredWarFactory = FindSingleByName(restored.World, "War Factory");
        Entity restoredArmorDisplay = FindSingleByName(restored.World, "Armor Display");
        Entity restoredMarker = FindSingleByName(restored.World, "Save UAT Marker");
        SavePointTrace restoredPoint = CaptureTrace(
            restored,
            restoredWarFactory,
            restoredArmorDisplay,
            restoredMarker,
            "restored");

        string[] continuousTrace = RunFixedSteps(continuous, 3);
        string[] restoredTrace = RunFixedSteps(restored, 3);

        Assert.Multiple(() =>
        {
            Assert.That(storedSnapshot.Header.MapId, Is.EqualTo("rts_cnc_training"));
            Assert.That(restoredPoint.MarkerPosition, Is.EqualTo(savePoint.MarkerPosition));
            Assert.That(restoredPoint.GameSessionTick, Is.EqualTo(savePoint.GameSessionTick));
            Assert.That(restoredPoint.FixedFrame, Is.EqualTo(savePoint.FixedFrame));
            Assert.That(restoredPoint.UatStage, Is.EqualTo("saved"));
            Assert.That(restoredPoint.WarFactoryAlive, Is.True);
            Assert.That(restoredPoint.ArmorDisplayAlive, Is.True);
            Assert.That(restoredTrace, Is.EqualTo(continuousTrace));
        });

        File.WriteAllText(
            Path.Combine(artifactDir, "trace.jsonl"),
            BuildTraceJsonl(savePoint, restoredPoint, continuousTrace, restoredTrace),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(artifactDir, "battle-report.md"),
            BuildBattleReport(savePoint, restoredPoint),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(artifactDir, "manual-uat.md"),
            BuildManualUatScript(),
            Encoding.UTF8);
    }

    private static GameEngine CreateRtsTrainingEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "CoreInputMod",
                "EntityCommandPanelMod",
                "RtsDemoMod",
                "RtsCncTrainingShowcaseMod"
            }),
            Path.Combine(repoRoot, "assets"));
        engine.LoadMap("rts_cnc_training");
        Assert.That(engine.GetService(CoreServiceKeys.SaveParticipants), Is.Not.Null);
        return engine;
    }

    private static GameEngine CreateCoreSaveShowcaseEngine(string repoRoot)
    {
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
            Path.Combine(repoRoot, "assets"));
        engine.LoadStartupMap();
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
        return SaveContinuationTrace.RunFixedSteps(engine, count, 1f / 60f);
    }

    private static SavePointTrace CaptureTrace(
        GameEngine engine,
        Entity warFactory,
        Entity armorDisplay,
        Entity marker,
        string stage)
    {
        IClock clock = engine.GetService(CoreServiceKeys.Clock);
        ref readonly WorldPositionCm markerPosition = ref engine.World.Get<WorldPositionCm>(marker);
        string uatStage = engine.GameSession.Globals.TryGetValue("uatStage", out object? value)
            ? (string)value
            : string.Empty;
        return new SavePointTrace(
            stage,
            engine.CurrentMapSession.MapId.Value,
            engine.GameSession.CurrentTick,
            clock.Now(ClockDomainId.FixedFrame),
            engine.World.IsAlive(warFactory),
            engine.World.IsAlive(armorDisplay),
            markerPosition.ToWorldCmInt2().ToString(),
            uatStage);
    }

    private static SaveShowcaseTrace CaptureSaveShowcaseTrace(
        GameEngine engine,
        Entity actor,
        string stage)
    {
        IClock clock = engine.GetService(CoreServiceKeys.Clock);
        ref readonly Name actorName = ref engine.World.Get<Name>(actor);
        ref readonly WorldPositionCm actorPosition = ref engine.World.Get<WorldPositionCm>(actor);
        string missionObjective = engine.GameSession.Globals.TryGetValue("missionObjective", out object? value)
            ? (string)value
            : string.Empty;
        return new SaveShowcaseTrace(
            stage,
            engine.CurrentMapSession.MapId.Value,
            actorName.Value,
            actorPosition.ToWorldCmInt2().ToString(),
            missionObjective,
            engine.GameSession.CurrentTick,
            clock.Now(ClockDomainId.FixedFrame));
    }

    private static Entity FindSingleByName(World world, string name)
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

        Assert.That(count, Is.EqualTo(1), $"Expected exactly one entity named '{name}'.");
        return found;
    }

    private static Entity TryFindByName(World world, string name)
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

        Assert.That(count, Is.LessThanOrEqualTo(1), $"Expected at most one entity named '{name}'.");
        return found;
    }

    private static string BuildTraceJsonl(
        SavePointTrace savePoint,
        SavePointTrace restoredPoint,
        string[] continuousTrace,
        string[] restoredTrace)
    {
        object[] rows =
        {
            new { event_id = "save-point", state = savePoint },
            new { event_id = "restored-point", state = restoredPoint },
            new { event_id = "continuous-next", trace = continuousTrace },
            new { event_id = "restored-next", trace = restoredTrace }
        };

        return string.Join(Environment.NewLine, rows.Select(row => JsonSerializer.Serialize(row))) + Environment.NewLine;
    }

    private static string BuildCrossLoadContextTraceJsonl(
        SaveShowcaseTrace savePoint,
        SaveShowcaseTrace restoredPoint,
        string[] continuousTrace,
        string[] restoredTrace)
    {
        object[] rows =
        {
            new { event_id = "player-save-point", state = savePoint },
            new { event_id = "player-loaded-point", state = restoredPoint },
            new { event_id = "continuous-next", trace = continuousTrace },
            new { event_id = "loaded-next", trace = restoredTrace }
        };

        return string.Join(Environment.NewLine, rows.Select(row => JsonSerializer.Serialize(row))) + Environment.NewLine;
    }

    private static string BuildBattleReport(SavePointTrace savePoint, SavePointTrace restoredPoint)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: save-system");
        sb.AppendLine();
        sb.AppendLine("## Intent");
        sb.AppendLine("- Player goal: prove the Core save system can persist an existing RTS training showcase state, mutate it, reload it, and continue deterministically.");
        sb.AppendLine("- Gameplay domain: `rts_cnc_training` via LudotsCoreMod, CoreInputMod, EntityCommandPanelMod, RtsDemoMod, and RtsCncTrainingShowcaseMod.");
        sb.AppendLine();
        sb.AppendLine("## Action Script");
        sb.AppendLine("1. Load the existing C&C RTS training showcase.");
        sb.AppendLine("2. Add an observable save marker and domain state.");
        sb.AppendLine("3. Save through `WorldSnapshotService` + `SaveSlotStore`.");
        sb.AppendLine("4. Mutate the live world and domain state.");
        sb.AppendLine("5. Restore into a fresh engine and compare state plus deterministic continuation trace.");
        sb.AppendLine();
        sb.AppendLine("## Expected Outcomes");
        sb.AppendLine("- `War Factory` and `Armor Display` survive restore from the existing showcase map.");
        sb.AppendLine("- `Save UAT Marker` returns to the saved position.");
        sb.AppendLine("- GameSession globals and Core clock continue from the save point.");
        sb.AppendLine("- Post-restore trace equals the continuous trace.");
        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine($"- save point: `{savePoint}`");
        sb.AppendLine($"- restored point: `{restoredPoint}`");
        sb.AppendLine("- `artifacts/acceptance/save-system/trace.jsonl`");
        sb.AppendLine("- `artifacts/acceptance/save-system/manual-uat.md`");
        return sb.ToString();
    }

    private static string BuildCrossLoadContextScenarioCard(
        SaveShowcaseTrace savePoint,
        SaveShowcaseTrace restoredPoint)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Card: cross-load-context-save-system");
        sb.AppendLine();
        sb.AppendLine("## Player Promise");
        sb.AppendLine("- A player can name an actor, save the mission, reload the slot, and continue from the same visible state even when the host reloads Ludots assemblies.");
        sb.AppendLine();
        sb.AppendLine("## Showcase Beat");
        sb.AppendLine("1. Enter the core save showcase map.");
        sb.AppendLine("2. Create `HAN Save Pilot` at the northern gate and set the mission objective.");
        sb.AppendLine("3. Save into a manual slot.");
        sb.AppendLine("4. Mutate the target world to prove load replaces unsaved state.");
        sb.AppendLine("5. Load the slot and continue simulation.");
        sb.AppendLine();
        sb.AppendLine("## Pass Signals");
        sb.AppendLine("- `HAN Save Pilot` keeps the same name and position after load.");
        sb.AppendLine("- The mission objective returns to the saved value.");
        sb.AppendLine("- Unsaved target-world actors disappear after load.");
        sb.AppendLine("- The post-load continuation trace matches the original mission.");
        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine($"- saved: `{savePoint}`");
        sb.AppendLine($"- loaded: `{restoredPoint}`");
        sb.AppendLine("- `artifacts/acceptance/save-system-cross-load-context/trace.jsonl`");
        sb.AppendLine("- `artifacts/acceptance/save-system-cross-load-context/manual-uat.md`");
        return sb.ToString();
    }

    private static string BuildManualUatScript()
    {
        return """
# Manual UAT: Core Save/Load

## Target

- Showcase: `rts_cnc_training`
- Existing mod chain: `LudotsCoreMod`, `CoreInputMod`, `EntityCommandPanelMod`, `RtsDemoMod`, `RtsCncTrainingShowcaseMod`
- Automated evidence: `dotnet test src\Tests\PersistenceTests\PersistenceTests.csproj --filter SaveSystemUatTests`

## Steps

1. Launch the C&C RTS training showcase with the Raylib launcher preset or an equivalent local launcher path.
2. Confirm the map loads with `War Factory` and `Armor Display` visible.
3. Trigger a manual save at a clean tick boundary.
4. Change the world state visibly: advance simulation, move the camera, or create/modify units through the existing RTS controls.
5. Trigger load for the saved slot.
6. Confirm the map returns to the saved state: entity count, positions, current tick, and visible presentation all match the save point.
7. Continue simulation for several ticks and confirm no invalid entity references, missing presenters, or stale UI rows appear.

## Pass Criteria

- Load rejects incompatible schema/mod/registry state fail-fast.
- Existing save remains readable if a later write is interrupted.
- Autosave slots rotate without deleting manual slots.
- Restore is deterministic: the continuation trace from the restored state equals the original world's continuation trace.
""";
    }

    private static string BuildCrossLoadContextManualUatScript()
    {
        return """
# Manual UAT: Cross-Load-Context Save/Load

Feature: Player saves and reloads a named actor after the host reloads game assemblies

  Scenario: Saved named actor returns and the mission continues
    Given I am playing the core save showcase map
    And the actor "HAN Save Pilot" is visible at the northern gate
    And the mission objective says "Hold the northern gate"
    When I save the mission into a manual slot
    And the host reloads the game assemblies before reading the slot
    And I load that manual slot
    Then I see "HAN Save Pilot" with the same name and position as the save point
    And the mission objective says "Hold the northern gate"
    And actors created after the save point are gone
    And the mission continues without missing names, invalid entity references, or save errors

  Scenario: Incompatible component contracts stop the load
    Given I have a save slot from the core save showcase map
    When the saved component contract does not match the current component contract
    Then the load is rejected with a clear save error
    And no partial world is applied to the current mission
""";
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

    private sealed record SavePointTrace(
        string Stage,
        string MapId,
        int GameSessionTick,
        int FixedFrame,
        bool WarFactoryAlive,
        bool ArmorDisplayAlive,
        string MarkerPosition,
        string UatStage);

    private sealed record SaveShowcaseTrace(
        string Stage,
        string MapId,
        string ActorName,
        string ActorPosition,
        string MissionObjective,
        int GameSessionTick,
        int FixedFrame);

    private sealed class MemorySaveStorage : ISaveStorage
    {
        public string DisplayRoot => string.Empty;

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
