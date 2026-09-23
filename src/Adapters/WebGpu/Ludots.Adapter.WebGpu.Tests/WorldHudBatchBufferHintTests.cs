using System.Numerics;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class WorldHudBatchBufferHintTests
{
    [Test]
    public void TryAdd_WithValidHint_UpdatesInPlaceWithoutDuplicateSlots()
    {
        var hinted = new WorldHudBatchBuffer(4);
        var baseline = new WorldHudBatchBuffer(4);
        int retainedIndexPlusOne = 0;
        WorldHudItem initial = CreateItem(stableId: 101, value: 0.25f);
        WorldHudItem updated = CreateItem(stableId: 101, value: 0.75f);

        Assert.That(hinted.TryAdd(in initial, ref retainedIndexPlusOne), Is.True);
        Assert.That(baseline.TryAdd(in initial), Is.True);
        Assert.That(hinted.TryAdd(in updated, ref retainedIndexPlusOne), Is.True);
        Assert.That(baseline.TryAdd(in updated), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(retainedIndexPlusOne, Is.EqualTo(1));
            Assert.That(hinted.Count, Is.EqualTo(1));
            Assert.That(hinted.GetSpan()[0].Value0, Is.EqualTo(baseline.GetSpan()[0].Value0));
            Assert.That(hinted.ContentRevision, Is.EqualTo(baseline.ContentRevision));
            Assert.That(hinted.ProjectionRevision, Is.EqualTo(baseline.ProjectionRevision));
            Assert.That(hinted.ContentOnlyRevision, Is.EqualTo(baseline.ContentOnlyRevision));
        });
    }

    [Test]
    public void TryAdd_WithStaleHintAfterSwapRemoval_RepairsHintWithoutDuplicateSlots()
    {
        var buffer = new WorldHudBatchBuffer(4);
        int firstHint = 0;
        int secondHint = 0;
        int movedHint = 0;
        WorldHudItem first = CreateItem(stableId: 101, value: 0.1f);
        WorldHudItem second = CreateItem(stableId: 102, value: 0.2f);
        WorldHudItem moved = CreateItem(stableId: 103, value: 0.3f);

        Assert.That(buffer.TryAdd(in first, ref firstHint), Is.True);
        Assert.That(buffer.TryAdd(in second, ref secondHint), Is.True);
        Assert.That(buffer.TryAdd(in moved, ref movedHint), Is.True);
        buffer.Remove(first.StableId, ref firstHint);

        WorldHudItem movedUpdate = CreateItem(stableId: moved.StableId, value: 0.9f);
        Assert.That(buffer.TryAdd(in movedUpdate, ref movedHint), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(firstHint, Is.Zero);
            Assert.That(movedHint, Is.EqualTo(1));
            Assert.That(secondHint, Is.EqualTo(2));
            Assert.That(buffer.Count, Is.EqualTo(2));
            Assert.That(buffer.TryGetByStableId(moved.StableId, out WorldHudItem actual), Is.True);
            Assert.That(actual.Value0, Is.EqualTo(0.9f));
        });
    }

    [Test]
    public void TryAdd_WithWarmHint_AllocatesZeroManagedBytes()
    {
        var buffer = new WorldHudBatchBuffer(4);
        int retainedIndexPlusOne = 0;
        WorldHudItem item = CreateItem(stableId: 101, value: 0f);
        Assert.That(buffer.TryAdd(in item, ref retainedIndexPlusOne), Is.True);

        for (int i = 0; i < 32; i++)
        {
            item.Value0 = i;
            buffer.TryAdd(in item, ref retainedIndexPlusOne);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
        {
            item.Value0 = i;
            buffer.TryAdd(in item, ref retainedIndexPlusOne);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    private static WorldHudItem CreateItem(int stableId, float value)
    {
        return new WorldHudItem
        {
            StableId = stableId,
            Kind = WorldHudItemKind.Bar,
            WorldPosition = new Vector3(10f, 2f, 20f),
            Color0 = Vector4.One,
            Color1 = new Vector4(0.1f, 0.2f, 0.3f, 1f),
            Width = 40f,
            Height = 6f,
            Value0 = value,
            Value1 = 1f,
        };
    }
}
