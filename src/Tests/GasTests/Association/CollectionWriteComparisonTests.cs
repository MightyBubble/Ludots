using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[NonParallelizable]
public sealed class CollectionWriteComparisonTests
{
    [TestCase(100)]
    [TestCase(1000)]
    [TestCase(10000)]
    public void WarmAddSubtract_ReportsTimeAndAllocation(int count)
    {
        using var world = World.Create();
        Entity owner = world.Create();
        var keys = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
        int key = keys.Register("selection");
        var store = new EntityCollectionStore(keys, initialRowCapacity: count * 4);
        var entities = new Entity[count];
        for (int i = 0; i < count; i++) entities[i] = world.Create();
        CollectionWrite.Apply(store, owner, key, CollectionWriteOp.Replace, entities);
        ReadOnlySpan<Entity> removed = entities.AsSpan(0, count / 2);
        for (int warm = 0; warm < 3; warm++)
        {
            CollectionWrite.Apply(store, owner, key, CollectionWriteOp.Subtract, removed);
            CollectionWrite.Apply(store, owner, key, CollectionWriteOp.Add, removed);
        }
        var samples = new double[15];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int run = 0; run < samples.Length; run++)
        {
            long start = Stopwatch.GetTimestamp();
            CollectionWrite.Apply(store, owner, key, CollectionWriteOp.Subtract, removed);
            CollectionWrite.Apply(store, owner, key, CollectionWriteOp.Add, removed);
            samples[run] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(samples);
        store.TryGet(owner, key, out var handle);
        store.TryGetView(handle, out var view);
        Assert.That(view.Count, Is.EqualTo(count));
        TestContext.Out.WriteLine(FormattableString.Invariant(
            $"members={count}, pairs=15, p50_ms={samples[7]:F6}, p95_ms={samples[14]:F6}, allocated_bytes={bytes}"));
    }
}
