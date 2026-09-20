using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[NonParallelizable]
public sealed class DerivedEntityIndexTests
{
    private static readonly MapId Map = new("query-test");
    private static readonly QueryDescription Query = new QueryDescription().WithAll<MapEntity, Team, EntityTemplateKeyRef>();

    private static Entity Spawn(World world, int team, int template = 7) => world.Create(
        new MapEntity { MapId = Map }, new Team { Id = team }, new EntityTemplateKeyRef { TemplateKeyId = template });

    private static DerivedEntityIndex CreateIndex(World world, int team = 1) => new(world, Map, Query,
        new[] { Component<Team>.ComponentType, Component<EntityTemplateKeyRef>.ComponentType },
        (w, e) => w.Get<Team>(e).Id == team && w.Get<EntityTemplateKeyRef>(e).TemplateKeyId == 7,
        initialCapacity: 16384);

    [Test]
    public void LargeBurstAndReusedIds_MatchTheWorldWithoutLifecycleEventDelivery()
    {
        using var world = World.Create();
        using var index = CreateIndex(world);
        var survivors = new HashSet<Entity>();
        for (int i = 0; i < 10000; i++)
        {
            Entity entity = Spawn(world, i % 2 == 0 ? 1 : 2);
            if (i % 2 == 0) survivors.Add(entity);
        }
        Assert.That(index.Read().ToArray(), Is.EquivalentTo(survivors));
        long evaluated = index.ReevaluatedCount;
        foreach (Entity entity in survivors) world.Destroy(entity);
        survivors.Clear();
        for (int i = 0; i < 3000; i++) survivors.Add(Spawn(world, 1));
        Assert.That(index.Read().ToArray(), Is.EquivalentTo(survivors));
        Assert.That(index.ReevaluatedCount - evaluated, Is.EqualTo(8000));
    }

    [Test]
    public void ChangesAreCoalesced_AndMapsAndQuerySignaturesRemainIsolated()
    {
        using var world = World.Create();
        Entity entity = Spawn(world, 1);
        using var friendly = CreateIndex(world);
        using var enemy = CreateIndex(world, 2);
        for (int i = 0; i < 10; i++) world.Set(entity, new Team { Id = 2 });
        Assert.That(friendly.Read().Length, Is.Zero);
        Assert.That(enemy.Read().Length, Is.EqualTo(1));
        Assert.That(friendly.ReevaluatedCount, Is.EqualTo(1));
        world.Remove<EntityTemplateKeyRef>(entity);
        Assert.That(enemy.Read().Length, Is.Zero);
        world.Add(entity, new EntityTemplateKeyRef { TemplateKeyId = 7 });
        Assert.That(enemy.Read().Length, Is.EqualTo(1));
        world.Set(entity, new MapEntity { MapId = new MapId("other") });
        Assert.That(enemy.Read().Length, Is.Zero);
        world.Set(entity, new MapEntity { MapId = Map });
        Assert.That(enemy.Read().Length, Is.EqualTo(1));
    }

    [Test]
    public void BulkSetAddRemoveAndExplicitRefNotification_InvalidateMembers()
    {
        using var world = World.Create();
        Entity entity = Spawn(world, 1);
        using var index = CreateIndex(world);
        world.Set(Query, new Team { Id = 2 });
        Assert.That(index.Read().Length, Is.Zero);
        world.Get<Team>(entity).Id = 1;
        world.NotifyComponentChanged<Team>(entity);
        Assert.That(index.Read().Length, Is.EqualTo(1));
        world.Remove<EntityTemplateKeyRef>(Query);
        Assert.That(index.Read().Length, Is.Zero);
        var remaining = new QueryDescription().WithAll<MapEntity, Team>();
        world.Add(remaining, new EntityTemplateKeyRef { TemplateKeyId = 7 });
        Assert.That(index.Read().Length, Is.EqualTo(1));
    }

    [Test]
    public void Paging_IsCompleteAndRejectsChangesBetweenPages()
    {
        using var world = World.Create();
        for (int i = 0; i < 1000; i++) Spawn(world, 1);
        using var index = CreateIndex(world);
        Span<Entity> page = stackalloc Entity[256];
        int offset = 0;
        uint revision = index.Revision;
        bool more;
        do { offset += index.CopyPage(offset, revision, page, out more); } while (more);
        Assert.That(offset, Is.EqualTo(1000));
        Spawn(world, 1);
        Assert.Throws<InvalidOperationException>(() => index.CopyPage(256, revision, new Entity[256], out _));
    }

    [Test]
    public void CollectionSource_IsReadOnlyAndMaterializesOnlyAfterMembershipChanges()
    {
        using var world = World.Create();
        Entity owner = Spawn(world, 2);
        using var index = CreateIndex(world);
        var keys = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
        int key = keys.Register("candidates");
        var store = new EntityCollectionStore(keys);
        store.BindSource(owner, key, index);
        store.TryGet(owner, key, out var before);
        Entity entity = Spawn(world, 1);
        store.TryGet(owner, key, out var after);
        Assert.That(after.Revision, Is.Not.EqualTo(before.Revision));
        Assert.That(store.TryGetEntityAt(after, 0, out var member) && member == entity, Is.True);
        store.TryGet(owner, key, out var unchanged);
        Assert.That(unchanged, Is.EqualTo(after));
        Assert.Throws<InvalidOperationException>(() => CollectionWrite.Apply(store, owner, key, CollectionWriteOp.Replace, Array.Empty<Entity>()));
        store.RemoveSource(index);
        Assert.That(store.TryGet(owner, key, out _), Is.False);
    }

    [TestCase(1000)]
    [TestCase(10000)]
    public void WarmSingleEntityChanges_HaveConstantEvaluationCountAndNoAllocations(int population)
    {
        using var world = World.Create();
        Entity target = Spawn(world, 1);
        for (int i = 1; i < population; i++) Spawn(world, 2);
        using var index = CreateIndex(world);
        for (int i = 0; i < 100; i++)
        {
            world.Set(target, new Team { Id = i % 2 });
            index.Read();
        }
        long beforeEvaluations = index.ReevaluatedCount;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 10000; i++)
        {
            world.Set(target, new Team { Id = i % 2 });
            index.Read();
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.That(index.ReevaluatedCount - beforeEvaluations, Is.EqualTo(10000));
        Assert.That(bytes, Is.Zero);
        TestContext.Out.WriteLine($"population={population}, changes=10000, elapsed_ms={elapsed:F4}, bytes={bytes}, evaluated=10000");
    }
}
