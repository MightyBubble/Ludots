using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Tests;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

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
        Assert.That(restoredPosition.ToWorldCmInt2(), Is.EqualTo(new Ludots.Platform.Abstractions.WorldCmInt2(100, 200)));
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
        Assert.That(preservedPosition.ToWorldCmInt2(), Is.EqualTo(new Ludots.Platform.Abstractions.WorldCmInt2(3, 4)));
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
    public void SnapshotRestoreRoundTripRestoresTaskEntityAndRuntimeIndex()
    {
        using GameEngine source = CreateInitializedEngine();
        using GameEngine target = CreateInitializedEngine();
        RegisterTaskDefinition(source);
        RegisterTaskDefinition(target);
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();

        TaskRuntimeService sourceTasks = source.GetService(CoreServiceKeys.TaskRuntimeService);
        Entity taskEntity = sourceTasks.OfferOrStart("Task.Test.Persistence");
        Assert.That(taskEntity, Is.Not.EqualTo(Entity.Null));
        sourceTasks.EmitSignal("task.persistence.resolved");

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        restoreService.Restore(target, snapshot);

        TaskRuntimeService targetTasks = target.GetService(CoreServiceKeys.TaskRuntimeService);
        Assert.That(targetTasks.TryResolveInstance("Task.Test.Persistence", Entity.Null, out Entity restoredTask), Is.True);
        Assert.That(restoredTask, Is.Not.EqualTo(Entity.Null));
        Assert.That(target.World.Has<TaskInstanceCm>(restoredTask), Is.True);
        Assert.That(targetTasks.TryGetState("Task.Test.Persistence", out TaskInstanceState state), Is.True);
        Assert.That(state, Is.EqualTo(TaskInstanceState.Completed));
        Assert.That(targetTasks.Signals.TryGetValue("task.persistence.resolved", out int count), Is.True);
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void SnapshotRestoreRoundTripRestoresScopedTaskEntityAndRuntimeIndex()
    {
        using GameEngine source = CreateInitializedEngine();
        using GameEngine target = CreateInitializedEngine();
        RegisterTaskDefinition(source);
        RegisterTaskDefinition(target);
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();

        Entity scopeHost = source.World.Create(new Name { Value = "task-scope-host" });
        TaskRuntimeService sourceTasks = source.GetService(CoreServiceKeys.TaskRuntimeService);
        Entity taskEntity = sourceTasks.OfferOrStart("Task.Test.Persistence", scopeHost);
        Assert.That(taskEntity, Is.Not.EqualTo(Entity.Null));

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        restoreService.Restore(target, snapshot);

        Entity restoredHost = FindSingleByName(target.World, "task-scope-host");
        TaskRuntimeService targetTasks = target.GetService(CoreServiceKeys.TaskRuntimeService);
        Assert.That(targetTasks.TryResolveInstance("Task.Test.Persistence", restoredHost, out Entity restoredTask), Is.True);
        Assert.That(restoredTask, Is.Not.EqualTo(Entity.Null));
        Assert.That(target.World.Get<TaskInstanceCm>(restoredTask).ScopeHost, Is.EqualTo(restoredHost));
    }

    [Test]
    public void SnapshotRestoreRoundTripRestoresRelationshipEntityAndRuntimeIndex()
    {
        using GameEngine source = CreateInitializedEngine();
        using GameEngine target = CreateInitializedEngine();
        int typeId = RegisterRelationshipType(source, target, "Tests.Relationship.Persistence");
        int pressureId = EnsureAttribute("Health");
        int tagId = EnsureTag("Role.Core.Researcher");
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();

        Entity sourceEntity = source.World.Create(new Name { Value = "relationship-source" });
        Entity targetEntity = source.World.Create(new Name { Value = "relationship-target" });
        RelationshipRuntime sourceRelationships = source.GetService(CoreServiceKeys.RelationshipRuntime);
        sourceRelationships.EnsureLink(sourceEntity, targetEntity, typeId);
        Assert.That(sourceRelationships.TryResolveRelationshipEntity(sourceEntity, targetEntity, typeId, out Entity relationEntity), Is.True);
        ref AttributeBuffer attributes = ref source.World.Get<AttributeBuffer>(relationEntity);
        attributes.SetBase(pressureId, 4f);
        ref GameplayTagContainer tags = ref source.World.Get<GameplayTagContainer>(relationEntity);
        tags.AddTag(tagId);

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        restoreService.Restore(target, snapshot);

        Entity restoredSource = FindSingleByName(target.World, "relationship-source");
        Entity restoredTarget = FindSingleByName(target.World, "relationship-target");
        RelationshipRuntime targetRelationships = target.GetService(CoreServiceKeys.RelationshipRuntime);
        Assert.That(targetRelationships.HasLink(restoredSource, restoredTarget, typeId), Is.True);
        Assert.That(targetRelationships.TryResolveRelationshipEntity(restoredSource, restoredTarget, typeId, out Entity restoredRelation), Is.True);
        Assert.That(target.World.Has<RelationshipInstanceCm>(restoredRelation), Is.True);
        Assert.That(target.World.Has<AttributeBuffer>(restoredRelation), Is.True);
        Assert.That(target.World.Has<GameplayTagContainer>(restoredRelation), Is.True);
        Assert.That(target.World.Has<TagCountContainer>(restoredRelation), Is.True);
        Assert.That(target.World.Has<ActiveEffectContainer>(restoredRelation), Is.True);
        Assert.That(target.World.Get<AttributeBuffer>(restoredRelation).GetCurrent(pressureId), Is.EqualTo(4f));
        Assert.That(target.World.Get<GameplayTagContainer>(restoredRelation).HasTag(tagId), Is.True);

        ref readonly RelationshipInstanceCm restoredInstance = ref target.World.Get<RelationshipInstanceCm>(restoredRelation);
        Assert.That(restoredInstance.Source, Is.EqualTo(restoredSource));
        Assert.That(restoredInstance.Target, Is.EqualTo(restoredTarget));
        Assert.That(restoredInstance.TypeId, Is.EqualTo(typeId));
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

    [Test]
    public void RestoreDuringAdmissionStepAbortsVolatileGenerationBeforeNextLogicStep()
    {
        using GameEngine source = CreateInitializedEngine();
        using GameEngine target = CreateInitializedEngine();
        var snapshotService = new WorldSnapshotService();
        var restoreService = new WorldRestoreService();
        OrderAdmissionResultBuffer admission = target.GetService(CoreServiceKeys.OrderAdmissionResultBuffer);
        OrderQueue orderQueue = target.GetService(CoreServiceKeys.OrderQueue);

        WorldSaveSnapshot snapshot = snapshotService.Capture(
            source,
            SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));

        admission.BeginLogicStep();
        var beforeRestore = new Order { OrderTypeId = 3 };
        orderQueue.EnsureOrderId(ref beforeRestore);
        Assert.That(admission.TryWrite(new OrderAdmissionOutcome(
            orderId: beforeRestore.OrderId,
            orderTypeId: 3,
            OrderAdmissionStage.EntityIntake,
            OrderSubmitResult.Activated)), Is.True);

        restoreService.Restore(target, snapshot);

        Assert.That(admission.LogicStepActive, Is.False);
        Assert.That(admission.EntityIntakeOpen, Is.False);
        Assert.That(admission.Count, Is.Zero);
        Assert.That(admission.TryGet(beforeRestore.OrderId, OrderAdmissionStage.EntityIntake, out _), Is.False);
        var afterRestore = new Order { OrderTypeId = 3 };
        orderQueue.EnsureOrderId(ref afterRestore);
        Assert.That(afterRestore.OrderId, Is.GreaterThan(beforeRestore.OrderId));
        Assert.DoesNotThrow(admission.BeginLogicStep);
        admission.EndEntityIntake();
        admission.EndLogicStep();
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
        engine.LoadStartupMap();
        Assert.That(engine.GetService(CoreServiceKeys.SaveParticipants), Is.Not.Null);
        return engine;
    }

    private static void RegisterTaskDefinition(GameEngine engine)
    {
        TaskDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.TaskDefinitionRegistry);
        definitions.Register("Task.Test.Persistence", new TaskDefinition
        {
            DisplayName = "Persistence Task",
            Tags = { "task.persistence" },
            StartPolicy = TaskStartPolicy.Automatic,
            Objectives =
            {
                new TaskObjectiveDefinition
                {
                    Id = "resolve",
                    Kind = TaskObjectiveKind.Signal,
                    Title = "Resolve",
                    SignalKey = "task.persistence.resolved"
                }
            }
        });
    }

    private static int RegisterRelationshipType(GameEngine first, GameEngine second, string typeName)
    {
        int firstId = first.GetService(CoreServiceKeys.RelationshipTypeRegistry).Register(typeName);
        int secondId = second.GetService(CoreServiceKeys.RelationshipTypeRegistry).Register(typeName);
        Assert.That(secondId, Is.EqualTo(firstId));
        return firstId;
    }

    private static int EnsureAttribute(string attribute)
    {
        // Engine init freezes the ambient registry (attribute registration is config-load time only),
        // so tests resolve ids that the core config already registered instead of adding new ones.
        return AttributeRegistry.RequireId(attribute);
    }

    private static int EnsureTag(string tag)
    {
        int id = TagRegistry.GetId(tag);
        Assert.That(id, Is.Not.EqualTo(TagRegistry.InvalidId), $"Tag '{tag}' must be registered by core config.");
        return id;
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
        return SaveContinuationTrace.RunFixedSteps(engine, count, 1f);
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
