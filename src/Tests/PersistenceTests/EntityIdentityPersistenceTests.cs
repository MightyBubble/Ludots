using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Map;
using Ludots.Core.Persistence;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class EntityIdentityPersistenceTests
{
    [Test]
    public void CoreBinarySerializerRestoresQueryableEntitiesAsAlive()
    {
        using World world = World.Create();
        world.Create(new Name { Value = "alive-after-restore" });

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<Name>(restored);

        Assert.That(restored.IsAlive(restoredEntity), Is.True);
    }

    [Test]
    public void MapEntityMapIdIsPreservedAfterRoundTrip()
    {
        using World world = World.Create();
        world.Create(new MapEntity { MapId = new MapId("rts_cnc_training") });

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<MapEntity>(restored);

        Assert.That(restored.Get<MapEntity>(restoredEntity).MapId.Value, Is.EqualTo("rts_cnc_training"));
    }

    [Test]
    public void EntityReferenceWorldIdMatchesRestoredWorldAfterSourceWorldIsDestroyed()
    {
        var serializer = new LudotsBinaryWorldSerializer();
        byte[] bytes;

        using (World source = World.Create())
        {
            Entity target = source.Create(new Name { Value = "target-after-destroy" });
            var refs = new BlackboardEntityBuffer();
            refs.Set(7, target);
            source.Create(refs);
            bytes = serializer.Serialize(source);
        }

        using World restored = serializer.Deserialize(bytes);
        Entity restoredOwner = FindSingle<BlackboardEntityBuffer>(restored);
        ref readonly BlackboardEntityBuffer refsAfterRestore = ref restored.Get<BlackboardEntityBuffer>(restoredOwner);

        Assert.That(refsAfterRestore.TryGet(7, out Entity restoredTarget), Is.True);
        Assert.That(restoredTarget.WorldId, Is.EqualTo(restored.Id));
        AssertAliveName(restored, restoredTarget, "target-after-destroy");
    }

    [Test]
    public void EntityReferenceAuditFailsFastWhenPersistentComponentReferencesExcludedEntity()
    {
        using World world = World.Create();
        Entity excluded = world.Create(new Name { Value = "excluded" }, new SaveExcludedTag());
        var refs = new BlackboardEntityBuffer();
        refs.Set(7, excluded);
        world.Create(new Name { Value = "persistent-owner" }, refs);

        var error = Assert.Throws<SaveContextException>(
            () => SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default));

        Assert.That(error!.Message, Does.Contain("excluded entity"));
        Assert.That(error.Message, Does.Contain(nameof(BlackboardEntityBuffer)));
    }

    [Test]
    public void BlackboardEntityBufferReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity target = world.Create(new Name { Value = "target" });
        var refs = new BlackboardEntityBuffer();
        refs.Set(7, target);
        world.Create(refs);

        using World restored = CoreRoundTrip(world);
        Entity restoredRefOwner = FindSingle<BlackboardEntityBuffer>(restored);
        ref readonly BlackboardEntityBuffer restoredRefs = ref restored.Get<BlackboardEntityBuffer>(restoredRefOwner);

        Assert.That(restoredRefs.TryGet(7, out Entity restoredTarget), Is.True);
        AssertAliveName(restored, restoredTarget, "target");
    }

    [Test]
    public void ChildrenBufferReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity firstChild = world.Create(new Name { Value = "child-a" });
        Entity secondChild = world.Create(new Name { Value = "child-b" });
        var children = new ChildrenBuffer();
        Assert.That(children.Add(firstChild), Is.True);
        Assert.That(children.Add(secondChild), Is.True);
        world.Create(children);

        using World restored = CoreRoundTrip(world);
        Entity restoredParent = FindSingle<ChildrenBuffer>(restored);
        ref readonly ChildrenBuffer restoredChildren = ref restored.Get<ChildrenBuffer>(restoredParent);

        AssertAliveName(restored, restoredChildren.Get(0), "child-a");
        AssertAliveName(restored, restoredChildren.Get(1), "child-b");
    }

    [Test]
    public void ActiveEffectContainerReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity firstEffect = world.Create(new Name { Value = "effect-a" });
        Entity secondEffect = world.Create(new Name { Value = "effect-b" });
        var activeEffects = new ActiveEffectContainer();
        Assert.That(activeEffects.Add(firstEffect), Is.True);
        Assert.That(activeEffects.Add(secondEffect), Is.True);
        world.Create(activeEffects);

        using World restored = CoreRoundTrip(world);
        Entity restoredOwner = FindSingle<ActiveEffectContainer>(restored);
        ref readonly ActiveEffectContainer restoredActiveEffects = ref restored.Get<ActiveEffectContainer>(restoredOwner);

        AssertAliveName(restored, restoredActiveEffects.GetEntity(0), "effect-a");
        AssertAliveName(restored, restoredActiveEffects.GetEntity(1), "effect-b");
    }

    [Test]
    public void AbilityStateBufferTemplateReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity template = world.Create(new Name { Value = "ability-template" });
        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(template);
        world.Create(abilities);

        using World restored = CoreRoundTrip(world);
        Entity restoredActor = FindSingle<AbilityStateBuffer>(restored);
        AbilitySlotState slot = restored.Get<AbilityStateBuffer>(restoredActor).Get(0);
        Entity restoredTemplate = EntityUtil.Reconstruct(
            slot.TemplateEntityId,
            slot.TemplateEntityWorldId,
            slot.TemplateEntityVersion);

        AssertAliveName(restored, restoredTemplate, "ability-template");
    }

    [Test]
    public void TeamEntityRefValueIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity team = world.Create(new Name { Value = "team" });
        world.Create(new TeamEntityRef { Value = team });

        using World restored = CoreRoundTrip(world);
        Entity restoredOwner = FindSingle<TeamEntityRef>(restored);
        Entity restoredTeam = restored.Get<TeamEntityRef>(restoredOwner).Value;

        AssertAliveName(restored, restoredTeam, "team");
    }

    [Test]
    public void TaskInstanceScopeHostIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity scopeHost = world.Create(new Name { Value = "task-scope" });
        world.Create(new TaskInstanceCm
        {
            DefinitionId = 1,
            InstanceId = 1,
            State = TaskInstanceState.Active,
            ScopeHost = scopeHost,
            Revision = 1
        });

        using World restored = CoreRoundTrip(world);
        Entity restoredTask = FindSingle<TaskInstanceCm>(restored);
        Entity restoredScopeHost = restored.Get<TaskInstanceCm>(restoredTask).ScopeHost;

        Assert.That(restoredScopeHost.WorldId, Is.EqualTo(restored.Id));
        AssertAliveName(restored, restoredScopeHost, "task-scope");
    }

    [Test]
    public void EntityReferenceAuditFailsFastWhenTaskScopeHostReferencesExcludedEntity()
    {
        using World world = World.Create();
        Entity excludedScopeHost = world.Create(new Name { Value = "excluded-task-scope" }, new SaveExcludedTag());
        world.Create(new TaskInstanceCm
        {
            DefinitionId = 1,
            InstanceId = 1,
            State = TaskInstanceState.Active,
            ScopeHost = excludedScopeHost,
            Revision = 1
        });

        var error = Assert.Throws<SaveContextException>(
            () => SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default));

        Assert.That(error!.Message, Does.Contain("excluded entity"));
        Assert.That(error.Message, Does.Contain(nameof(TaskInstanceCm)));
        Assert.That(error.Message, Does.Contain(nameof(TaskInstanceCm.ScopeHost)));
    }

    [Test]
    public void ImportIntoKeepsTargetStableAfterImportedSourceIsDisposedAndWorldsAllocate()
    {
        using World source = World.Create();
        Entity childA = source.Create(new Name { Value = "child-a" });
        Entity childB = source.Create(new Name { Value = "child-b" });
        var children = new ChildrenBuffer();
        Assert.That(children.Add(childA), Is.True);
        Assert.That(children.Add(childB), Is.True);
        source.Create(new Name { Value = "parent" }, children, WorldPositionCm.FromCm(25, 75));
        source.Create(new Name { Value = "excluded" }, new SaveExcludedTag());

        using World target = World.Create();
        using (World normalized = new LudotsBinaryWorldSerializer().CloneIncludedWorld(source))
        {
            LudotsWorldStateImporter.ImportOwnedSnapshotInto(normalized, target);
        }

        for (int i = 0; i < 128; i++)
        {
            source.Create(new Name { Value = $"source-noise-{i}" }, WorldPositionCm.FromCm(i, -i));
        }

        for (int i = 0; i < 128; i++)
        {
            using World pressure = World.Create();
            pressure.Create(new Name { Value = $"pressure-{i}" }, WorldPositionCm.FromCm(i * 2, i * 3));
        }

        Entity restoredParent = FindSingle<ChildrenBuffer>(target);
        ref readonly ChildrenBuffer restoredChildren = ref target.Get<ChildrenBuffer>(restoredParent);
        Entity restoredChildA = restoredChildren.Get(0);
        Entity restoredChildB = restoredChildren.Get(1);

        Assert.Multiple(() =>
        {
            AssertAliveName(target, restoredChildA, "child-a");
            AssertAliveName(target, restoredChildB, "child-b");
            Assert.That(FindByName(target, "excluded"), Is.EqualTo(Entity.Null));
            Assert.That(target.Get<WorldPositionCm>(restoredParent).ToWorldCmInt2(), Is.EqualTo(new Ludots.Platform.Abstractions.WorldCmInt2(25, 75)));
        });
    }

    private static World CoreRoundTrip(World world)
    {
        var serializer = new LudotsBinaryWorldSerializer();
        byte[] bytes = serializer.Serialize(world);
        return serializer.Deserialize(bytes);
    }

    private static void AssertAliveName(World world, Entity entity, string expectedName)
    {
        Assert.That(world.IsAlive(entity), Is.True);
        Assert.That(world.Has<Name>(entity), Is.True);
        Assert.That(world.Get<Name>(entity).Value, Is.EqualTo(expectedName));
    }

    private static Entity FindSingle<T>(World world)
    {
        var query = new QueryDescription().WithAll<T>();
        Entity found = Entity.Null;
        int matches = 0;

        world.Query(in query, entity =>
        {
            found = entity;
            matches++;
        });

        Assert.That(matches, Is.EqualTo(1));
        return found;
    }

    private static Entity FindByName(World world, string name)
    {
        var query = new QueryDescription().WithAll<Name>();
        Entity found = Entity.Null;
        int matches = 0;

        world.Query(in query, (Entity entity, ref Name entityName) =>
        {
            if (string.Equals(entityName.Value, name, StringComparison.Ordinal))
            {
                found = entity;
                matches++;
            }
        });

        Assert.That(matches, Is.LessThanOrEqualTo(1));
        return found;
    }
}
