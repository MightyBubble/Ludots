using System.Numerics;
using Arch.Core;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Gas.InteractionInput;

[TestFixture]
public sealed class CommandSourcePointerHitResolverTests
{
    [Test]
    public void PointCandidatesUseOneProjectionSnapshotAndAllocateZeroAtSteadyState()
    {
        using World world = World.Create();
        Entity owner = world.Create();
        for (int i = 0; i < 1024; i++)
        {
            world.Create(
                new VisualTransform { Position = new Vector3(0.9f, 0.9f, 0f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true },
                new CommandSourceSelectableTag(),
                CommandSourceSelectableState.EnabledByDefault);
        }

        Entity expected = world.Create(
            new VisualTransform { Position = new Vector3(0.25f, 0f, 0f), Rotation = Quaternion.Identity, Scale = Vector3.One },
            new CullState { IsVisible = true },
            new CommandSourceSelectableTag(),
            CommandSourceSelectableState.EnabledByDefault);
        var projector = new SnapshotOnlyProjector();
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.ScreenProjector.Name] = projector,
        };
        var pointer = new Vector2(125f, 100f);

        Entity warmup = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
            world,
            globals,
            owner,
            pointer,
            radiusPixels: 2f);
        Assert.That(warmup, Is.EqualTo(expected));

        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            Entity actual = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                world,
                globals,
                owner,
                pointer,
                radiusPixels: 2f);
            Assert.That(actual, Is.EqualTo(expected));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(allocated, Is.EqualTo(0), "Point hit-testing must be allocation free after warmup.");
            Assert.That(projector.SnapshotReadCount, Is.EqualTo(33), "Each hit-test must capture one projection snapshot.");
            Assert.That(projector.WorldToScreenCallCount, Is.EqualTo(0), "Snapshot-capable projectors must not be queried once per entity.");
        });
    }

    private sealed class SnapshotOnlyProjector : IScreenProjector, IProjectionSnapshotProvider
    {
        public int SnapshotReadCount { get; private set; }
        public int WorldToScreenCallCount { get; private set; }
        public int ProjectionRevision => 1;

        public bool TryGetProjectionSnapshot(out ProjectionSnapshot snapshot)
        {
            SnapshotReadCount++;
            snapshot = new ProjectionSnapshot(Matrix4x4.Identity, new Vector2(200f, 200f));
            return true;
        }

        public Vector2 WorldToScreen(Vector3 worldPosition)
        {
            WorldToScreenCallCount++;
            throw new InvalidOperationException("The point fast path must use the captured projection snapshot.");
        }
    }
}
