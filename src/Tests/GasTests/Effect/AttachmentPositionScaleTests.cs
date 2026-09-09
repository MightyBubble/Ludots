using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Attachment;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class AttachmentPositionScaleTests
{
    [Test]
    public void StableAttachments_ReuseOrderButConsumeChangedLocalPose()
    {
        using var world = World.Create();
        Entity root = world.Create(WorldPositionCm.FromCm(10, 0));
        Entity child = world.Create(WorldPositionCm.FromCm(0, 0));
        AttachmentOps.Attach(world, null, child, root, Offset());
        using var system = new AttachmentPositionSyncSystem(world);
        system.Update(1f / 60f);
        long builds = system.TopologyBuildCount;

        world.Get<AttachedLocalPose>(child).OffsetCm = Fix64Vec2.FromInt(20, 0);
        world.Get<WorldPositionCm>(root) = WorldPositionCm.FromCm(100, 0);
        system.Update(1f / 60f);

        Assert.That(system.TopologyBuildCount, Is.EqualTo(builds));
        Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(120, 0)));
    }

    [Test]
    public void CapacityFailureThenReplacement_RebuildsTheOrder()
    {
        using var world = World.Create();
        Entity root = world.Create(WorldPositionCm.FromCm(10, 0));
        Entity first = world.Create(WorldPositionCm.FromCm(0, 0));
        Entity second = world.Create(WorldPositionCm.FromCm(0, 0));
        AttachmentOps.Attach(world, null, first, root, Offset());
        AttachmentOps.Attach(world, null, second, first, Offset());
        using var system = new AttachmentPositionSyncSystem(world, scratchCapacity: 2);
        system.Update(1f / 60f);
        Entity overflow = world.Create(WorldPositionCm.FromCm(0, 0));
        AttachmentOps.Attach(world, null, overflow, root, Offset());
        Assert.Throws<InvalidOperationException>(() => system.Update(1f / 60f));

        AttachmentOps.Detach(world, null, second, DetachPlacement.KeepWorldPose, 0);
        AttachmentOps.Attach(world, null, first, overflow, Offset());
        world.Get<WorldPositionCm>(root) = WorldPositionCm.FromCm(100, 0);
        system.Update(1f / 60f);

        Assert.That(world.Get<WorldPositionCm>(first).Value, Is.EqualTo(Fix64Vec2.FromInt(102, 0)));
        Assert.That(system.LastAppliedCount, Is.EqualTo(2));
    }

    [Test]
    public void LogicalChildrenWithoutAttachment_DoNotEnterThePositionPass()
    {
        using var world = World.Create();
        Entity root = world.Create(WorldPositionCm.FromCm(0, 0));
        Entity child = world.Create(WorldPositionCm.FromCm(20, 0));
        RelationOps.SetParent(world, child, root);
        using var system = new AttachmentPositionSyncSystem(world);
        system.Update(1f / 60f);
        long builds = system.TopologyBuildCount;
        world.Get<WorldPositionCm>(root) = WorldPositionCm.FromCm(100, 0);
        system.Update(1f / 60f);

        Assert.That(system.TopologyBuildCount, Is.EqualTo(builds));
        Assert.That(system.LastAppliedCount, Is.Zero);
        Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(20, 0)));
    }

    [Test]
    public void LogicalAncestors_DoNotBecomeSpatialDependencies()
    {
        using var world = World.Create();
        Entity ancestor = world.Create(WorldPositionCm.FromCm(100, 0));
        Entity parent = world.Create(WorldPositionCm.FromCm(500, 0));
        RelationOps.SetParent(world, parent, ancestor);
        Entity child = world.Create(WorldPositionCm.FromCm(0, 0));
        AttachmentOps.Attach(world, null, child, parent, Offset());
        using var system = new AttachmentPositionSyncSystem(world);

        world.Get<WorldPositionCm>(ancestor) = WorldPositionCm.FromCm(900, 0);
        system.Update(1f / 60f);

        Assert.Multiple(() =>
        {
            Assert.That(world.Get<WorldPositionCm>(parent).Value, Is.EqualTo(Fix64Vec2.FromInt(500, 0)));
            Assert.That(world.Get<WorldPositionCm>(child).Value, Is.EqualTo(Fix64Vec2.FromInt(501, 0)));
            Assert.That(system.LastAppliedCount, Is.EqualTo(1));
            Assert.That(system.LastMaxDepth, Is.Zero);
        });
    }

    [Test]
    public void ReparentedChain_ReordersEvenWhenEntityIterationOrderIsUnchanged()
    {
        using var world = World.Create();
        Entity root = world.Create(WorldPositionCm.FromCm(10, 0));
        Entity first = world.Create(WorldPositionCm.FromCm(0, 0));
        Entity second = world.Create(WorldPositionCm.FromCm(0, 0));
        Entity leaf = world.Create(WorldPositionCm.FromCm(0, 0));
        AttachmentOps.Attach(world, null, first, root, Offset());
        AttachmentOps.Attach(world, null, second, root, Offset());
        AttachmentOps.Attach(world, null, leaf, first, Offset());
        using var system = new AttachmentPositionSyncSystem(world);
        system.Update(1f / 60f);

        AttachmentOps.Attach(world, null, first, second, Offset());
        world.Get<WorldPositionCm>(root) = WorldPositionCm.FromCm(100, 0);
        system.Update(1f / 60f);

        Assert.That(world.Get<WorldPositionCm>(leaf).Value, Is.EqualTo(Fix64Vec2.FromInt(103, 0)));
    }

    [Test]
    public void SpatialCycle_RejectsEntireUpdateBeforeAnyPositionWrite()
    {
        using var world = World.Create();
        Entity root = world.Create(WorldPositionCm.FromCm(10, 0));
        Entity valid = world.Create(WorldPositionCm.FromCm(0, 0));
        Entity first = world.Create(WorldPositionCm.FromCm(20, 0));
        Entity second = world.Create(WorldPositionCm.FromCm(30, 0));
        AttachmentOps.Attach(world, null, valid, root, Offset());
        AttachmentOps.Attach(world, null, first, root, Offset());
        AttachmentOps.Attach(world, null, second, first, Offset());
        world.Get<ChildOf>(first).Parent = second;
        world.Get<WorldPositionCm>(root) = WorldPositionCm.FromCm(100, 0);
        var original = world.Get<WorldPositionCm>(valid).Value;
        using var system = new AttachmentPositionSyncSystem(world);

        Assert.Throws<InvalidOperationException>(() => system.Update(1f / 60f));
        Assert.That(world.Get<WorldPositionCm>(valid).Value, Is.EqualTo(original));
    }

    [TestCase(1000, 1)]
    [TestCase(1000, 32)]
    [TestCase(1000, 128)]
    [TestCase(5000, 1)]
    [TestCase(5000, 32)]
    [TestCase(5000, 128)]
    [TestCase(10000, 1)]
    [TestCase(10000, 32)]
    [TestCase(10000, 128)]
    [Category("benchmark")]
    public void MovingAssemblies_ReportScalingAndAllocations(int count, int chainLength)
    {
        using var world = World.Create();
        var roots = new Entity[(count + chainLength - 1) / chainLength];
        var leaves = new Entity[roots.Length];
        var lengths = new int[roots.Length];
        int remaining = count;
        for (int i = 0; i < roots.Length; i++)
        {
            Entity root = world.Create(WorldPositionCm.FromCm(0, 0));
            roots[i] = root;
            Entity parent = root;
            int length = Math.Min(remaining, chainLength);
            lengths[i] = length;
            for (int childIndex = 0; childIndex < length; childIndex++)
            {
                Entity child = world.Create(WorldPositionCm.FromCm(0, 0));
                AttachmentOps.Attach(world, null, child, parent, Offset());
                parent = child;
            }

            leaves[i] = parent;
            remaining -= length;
        }

        using var system = new AttachmentPositionSyncSystem(world, scratchCapacity: count);
        for (int i = 0; i < 16; i++) system.Update(1f / 60f);
        var samples = new double[33];
        long allocated = 0;
        for (int sample = 0; sample < samples.Length; sample++)
        {
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                world.Get<WorldPositionCm>(roots[rootIndex]) = WorldPositionCm.FromCm(sample, 0);

            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            system.Update(1f / 60f);
            long elapsed = Stopwatch.GetTimestamp() - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            samples[sample] = elapsed * 1000d / Stopwatch.Frequency;
        }

        Assert.That(system.LastAppliedCount, Is.EqualTo(count));
        Assert.That(allocated, Is.Zero);
        for (int i = 0; i < leaves.Length; i++)
            Assert.That(world.Get<WorldPositionCm>(leaves[i]).Value,
                Is.EqualTo(Fix64Vec2.FromInt(samples.Length - 1 + lengths[i], 0)));

        Array.Sort(samples);
        string csv = "count,chain_length,median_ms,p95_ms,allocated_bytes\n" +
            string.Create(CultureInfo.InvariantCulture,
                $"{count},{chainLength},{samples[16]:F6},{samples[31]:F6},{allocated}\n");
        TestContext.Progress.WriteLine(csv);
        string? output = Environment.GetEnvironmentVariable("LUDOTS_ATTACHMENT_POSITION_BENCHMARK_OUTPUT");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"assemblies-{count}-depth-{chainLength}.csv"), csv);
        }
    }

    private static AttachedLocalPose Offset() => new() { OffsetCm = Fix64Vec2.FromInt(1, 0) };
}
