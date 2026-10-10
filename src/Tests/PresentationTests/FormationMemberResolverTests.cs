using Arch.Core;
using FormationCapabilityShowcaseMod.Runtime;
using FormationCapabilityShowcaseMod.Systems;
using Ludots.Core.Components;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class FormationMemberResolverTests
{
    [Test]
    public void Resolve_UsesLiveMembersInStableSlotOrder()
    {
        using var world = World.Create();
        Entity anchor = world.Create(new FormationAnchorState { FormationIndex = 3, SlotCount = 3 });
        Entity slotTwo = world.Create(new FormationMemberState { FormationIndex = 3, SlotIndex = 2 });
        Entity otherFormation = world.Create(new FormationMemberState { FormationIndex = 4, SlotIndex = 0 });
        Entity slotZero = world.Create(new FormationMemberState { FormationIndex = 3, SlotIndex = 0 });
        _ = world.Create(
            new FormationMemberState { FormationIndex = 3, SlotIndex = 1 },
            default(SuspendedTag));
        var resolver = new FormationMemberResolver(world, maxMembersPerFormation: 3);
        var destination = new Entity[3];

        int count = resolver.Resolve(anchor, destination);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(2));
            Assert.That(destination[0], Is.EqualTo(slotZero));
            Assert.That(destination[1], Is.EqualTo(slotTwo));
            Assert.That(destination[2], Is.EqualTo(Entity.Null));
            Assert.That(destination.Contains(otherFormation), Is.False);
        });
    }

    [Test]
    public void Resolve_NonAnchorStandsForItself()
    {
        using var world = World.Create();
        Entity soldier = world.Create(new FormationMemberState { FormationIndex = 1, SlotIndex = 0 });
        var resolver = new FormationMemberResolver(world, maxMembersPerFormation: 3);
        var destination = new Entity[3];

        int count = resolver.Resolve(soldier, destination);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(destination[0], Is.EqualTo(soldier));
        });
    }

    [Test]
    public void Resolve_RejectsMemberOutsideAnchorDeclaredSlots()
    {
        using var world = World.Create();
        Entity anchor = world.Create(new FormationAnchorState { FormationIndex = 1, SlotCount = 2 });
        _ = world.Create(new FormationMemberState { FormationIndex = 1, SlotIndex = 2 });
        var resolver = new FormationMemberResolver(world, maxMembersPerFormation: 3);
        var destination = new Entity[3];

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(anchor, destination))!;

        Assert.That(ex.Message, Does.Contain("exceeds the anchor-declared slot count"));
    }
}
