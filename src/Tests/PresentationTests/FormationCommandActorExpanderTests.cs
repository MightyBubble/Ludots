using Arch.Core;
using FormationCapabilityShowcaseMod.Runtime;
using FormationCapabilityShowcaseMod.Systems;
using Ludots.Core.Components;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class FormationCommandActorExpanderTests
{
    [Test]
    public void Expand_UsesLiveMembersInStableSlotOrder()
    {
        using var world = World.Create();
        Entity anchor = world.Create(new FormationAnchorState { FormationIndex = 3, SlotCount = 3 });
        Entity slotTwo = world.Create(new FormationMemberState { FormationIndex = 3, SlotIndex = 2 });
        Entity otherFormation = world.Create(new FormationMemberState { FormationIndex = 4, SlotIndex = 0 });
        Entity slotZero = world.Create(new FormationMemberState { FormationIndex = 3, SlotIndex = 0 });
        _ = world.Create(
            new FormationMemberState { FormationIndex = 3, SlotIndex = 1 },
            default(SuspendedTag));
        var expander = new FormationCommandActorExpander(world, maxMembersPerFormation: 3, maxExpandedActorCount: 6);
        var destination = new Entity[3];

        int count = expander.Expand(anchor, destination);

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
    public void Expand_RejectsMemberOutsideAnchorDeclaredSlots()
    {
        using var world = World.Create();
        Entity anchor = world.Create(new FormationAnchorState { FormationIndex = 1, SlotCount = 2 });
        _ = world.Create(new FormationMemberState { FormationIndex = 1, SlotIndex = 2 });
        var expander = new FormationCommandActorExpander(world, maxMembersPerFormation: 3, maxExpandedActorCount: 3);
        var destination = new Entity[3];

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => expander.Expand(anchor, destination))!;

        Assert.That(ex.Message, Does.Contain("exceeds the anchor-declared slot count"));
    }
}
