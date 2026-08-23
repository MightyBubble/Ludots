using Arch.Core;
using Ludots.Core.EntityHistory;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace GasTests.EntityHistory;

public sealed class EntityHistoryContractsTests
{
    [Test]
    public void EntityRef_keeps_generation_when_numeric_id_is_reused()
    {
        World world = World.Create();
        Entity first = world.Create();
        EntityRef oldRef = EntityRef.From(first);
        world.Destroy(first);
        Entity second = world.Create();

        Assert.That(second.Id, Is.EqualTo(first.Id));
        Assert.That(EntityRef.From(second), Is.Not.EqualTo(oldRef));
        Assert.That(oldRef.ToEntity(), Is.Not.EqualTo(second));
        world.Destroy(second);
    }

    [Test]
    public void Snapshot_store_rejects_capacity_without_replacing_existing_identity()
    {
        World world = World.Create();
        Entity first = world.Create();
        Entity second = world.Create();
        var store = new EntitySnapshotStore(1);
        EntitySnapshot firstSnapshot = CreateSnapshot(first, 1);
        EntitySnapshot secondSnapshot = CreateSnapshot(second, 2);

        Assert.That(store.Upsert(in firstSnapshot), Is.EqualTo(EntityHistoryStoreResult.Added));
        Assert.That(store.Upsert(in secondSnapshot), Is.EqualTo(EntityHistoryStoreResult.CapacityRejected));
        Assert.That(store.TryGet(EntityRef.From(first), out EntitySnapshot resolved), Is.True);
        Assert.That(resolved.CapturedTick, Is.EqualTo(1));
        world.Destroy(first);
        world.Destroy(second);
    }

    [Test]
    public void Last_known_resolution_is_explicitly_stale_after_ttl()
    {
        World world = World.Create();
        Entity viewer = world.Create();
        Entity target = world.Create();
        var entityStore = new EntitySnapshotStore(4);
        var knowledgeStore = new KnowledgeSnapshotStore(4);
        EntityRef viewerRef = EntityRef.From(viewer);
        EntityRef targetRef = EntityRef.From(target);
        KnowledgeSnapshot snapshot = new()
        {
            Viewer = viewerRef,
            Target = targetRef,
            Presence = KnowledgePresence.Known,
            PositionAccess = KnowledgePositionAccess.LastKnown,
            Position = Fix64Vec2.FromInt(4, 6),
            HasPosition = 1,
            ObservedTick = 2,
            ExpiryTick = 4,
            Revision = 7,
        };
        Assert.That(knowledgeStore.Upsert(in snapshot), Is.EqualTo(EntityHistoryStoreResult.Added));
        var targetRefSpec = new EffectTargetRef(in targetRef, in viewerRef, EffectTargetResolutionMode.LastKnown, 2, 7, 4, Fix64Vec2.Zero, 0);

        EffectTargetResolveOutput live = EffectTargetResolver.Resolve(world, in targetRefSpec, 3, entityStore, knowledgeStore);
        EffectTargetResolveOutput stale = EffectTargetResolver.Resolve(world, in targetRefSpec, 4, entityStore, knowledgeStore);

        Assert.That(live.Result, Is.EqualTo(EffectTargetResolveResult.LastKnown));
        Assert.That(stale.Result, Is.EqualTo(EffectTargetResolveResult.Stale));
        world.Destroy(viewer);
        world.Destroy(target);
    }

    [Test]
    public void Destroy_event_captures_snapshot_before_entity_is_removed()
    {
        World world = World.Create();
        Entity entity = world.Create();
        var store = new EntitySnapshotStore(2);
        using var capture = new EntitySnapshotCapture(world, store, new TestSnapshotReader());

        world.Destroy(entity);

        Assert.That(store.TryGet(EntityRef.From(entity), out EntitySnapshot snapshot), Is.True);
        Assert.That(snapshot.State, Is.EqualTo(EntitySnapshotState.Destroyed));
    }

    private static EntitySnapshot CreateSnapshot(Entity entity, int tick)
        => new() { Identity = EntityRef.From(entity), CapturedTick = tick, State = EntitySnapshotState.Live };

    private sealed class TestSnapshotReader : IEntitySnapshotReader
    {
        public bool TryCapture(World world, in Entity entity, int tick, out EntitySnapshot snapshot)
        {
            snapshot = new EntitySnapshot { Identity = EntityRef.From(entity), CapturedTick = tick };
            return true;
        }
    }
}
