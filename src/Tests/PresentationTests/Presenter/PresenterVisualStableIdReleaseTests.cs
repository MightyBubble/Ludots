using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class PresenterVisualStableIdReleaseTests
{
    [Test]
    public void ReleasePresenter_HandlesWrappedCollisionCluster_AndKeepsOtherIdentities()
    {
        var table = new PresenterVisualStableIdTable(new PresentationStableIdAllocator(), 16);
        var keys = new PresenterVisualStableKey[12];
        var ids = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            int discriminator = 0;
            do
            {
                keys[i] = new PresenterVisualStableKey(i % 3 + 1, i, AssetKind.Mesh, discriminator++);
            } while ((keys[i].GetHashCode() & (table.Capacity - 1)) != table.Capacity - 2);
            ids[i] = table.GetOrAllocate(keys[i]);
        }

        Assert.That(table.Remove(keys[4], out int removed), Is.True);
        Assert.That(removed, Is.EqualTo(ids[4]));
        Assert.That(table.ReleasePresenter(2), Is.EqualTo(3));
        Assert.That(table.ReleasePresenter(2), Is.Zero);
        Assert.That(table.Count, Is.EqualTo(8));
        for (int i = 0; i < keys.Length; i++)
        {
            bool found = table.TryGet(keys[i], out int id);
            Assert.That(found, Is.EqualTo(i % 3 != 1));
            if (found) Assert.That(id, Is.EqualTo(ids[i]));
        }
        int replacement = table.GetOrAllocate(keys[4]);
        Assert.That(replacement, Is.GreaterThan(ids[^1]));
        Assert.That(table.ReleasePresenter(2), Is.EqualTo(1));
        Assert.That(table.ReleasePresenter(1), Is.EqualTo(4));
        Assert.That(table.ReleasePresenter(3), Is.EqualTo(4));
        Assert.That(table.Count, Is.Zero);
    }

    [Test]
    public void MixedRemovalsAndReleases_MatchCompleteKeyReference()
    {
        var table = new PresenterVisualStableIdTable(new PresentationStableIdAllocator(), 512);
        var expected = new Dictionary<PresenterVisualStableKey, int>();
        var random = new Random(1465);
        var removedKeys = new List<PresenterVisualStableKey>();
        for (int step = 0; step < 4000; step++)
        {
            int presenterId = random.Next(1, 25);
            var key = new PresenterVisualStableKey(presenterId, random.Next(4),
                (AssetKind)random.Next(1, 5), random.Next(3));
            switch (random.Next(3))
            {
                case 0:
                    int id = table.GetOrAllocate(key);
                    if (expected.TryGetValue(key, out int oldId)) Assert.That(id, Is.EqualTo(oldId));
                    expected[key] = id;
                    break;
                case 1:
                    Assert.That(table.Remove(key, out int actual), Is.EqualTo(expected.Remove(key, out int prior)));
                    Assert.That(actual, Is.EqualTo(prior));
                    break;
                default:
                    removedKeys.Clear();
                    foreach (var entry in expected)
                        if (entry.Key.PresenterStableId == presenterId) removedKeys.Add(entry.Key);
                    Assert.That(table.ReleasePresenter(presenterId), Is.EqualTo(removedKeys.Count));
                    foreach (var removedKey in removedKeys) expected.Remove(removedKey);
                    break;
            }
            Assert.That(table.Count, Is.EqualTo(expected.Count));
            foreach (var entry in expected)
            {
                Assert.That(table.TryGet(entry.Key, out int actual), Is.True);
                Assert.That(actual, Is.EqualTo(entry.Value));
            }
        }
    }

    [Test]
    public void CapacityExhaustion_DoesNotDamageOwnership_AndReleasedCapacityCanBeReused()
    {
        var table = new PresenterVisualStableIdTable(new PresentationStableIdAllocator(), 4);
        for (int i = 0; i < table.MaxEntries; i++)
            table.GetOrAllocate(new PresenterVisualStableKey(1, i, AssetKind.Mesh, 0));
        Assert.Throws<InvalidOperationException>(() => table.GetOrAllocate(new PresenterVisualStableKey(2, 0, AssetKind.Mesh, 0)));
        Assert.That(table.ReleasePresenter(1), Is.EqualTo(table.MaxEntries));
        Assert.That(table.ReleasePresenter(2), Is.Zero);
        Assert.That(table.GetOrAllocate(new PresenterVisualStableKey(2, 0, AssetKind.Mesh, 0)), Is.Positive);
        Assert.That(table.ReleasePresenter(2), Is.EqualTo(1));
    }

    [Test]
    public void RepeatedOwnershipRecycling_DoesNotAllocate()
    {
        var table = new PresenterVisualStableIdTable(new PresentationStableIdAllocator(), 2048);
        Recycle(table);
        Recycle(table);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int released = Recycle(table);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(released, Is.EqualTo(5000));
        Assert.That(allocated, Is.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Recycle(PresenterVisualStableIdTable table)
    {
        int released = 0;
        for (int pass = 0; pass < 5; pass++)
        {
            for (int i = 1; i <= 1000; i++) table.GetOrAllocate(new PresenterVisualStableKey(i, 0, AssetKind.Mesh, pass));
            for (int i = 1; i <= 1000; i++) released += table.ReleasePresenter(i);
        }
        return released;
    }
}
