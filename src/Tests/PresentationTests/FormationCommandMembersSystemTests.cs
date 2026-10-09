using System.Collections.Generic;
using Arch.Core;
using FormationCapabilityShowcaseMod.Runtime;
using FormationCapabilityShowcaseMod.Systems;
using Ludots.Core.EntityCollections;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Tests.TestCommon;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class FormationCommandMembersSystemTests
{
    private const string CommandSourceKey = "collection.command.source";
    private const string MembersKey = "formation.command.members";

    [Test]
    public void Update_PublishesSoldiersOfSelectedAnchorsAndKeepsPlainSelections()
    {
        using var world = World.Create();
        Entity player = world.Create();
        Entity anchor = world.Create(new FormationAnchorState { FormationIndex = 2, SlotCount = 2 });
        Entity slotOne = world.Create(new FormationMemberState { FormationIndex = 2, SlotIndex = 1 });
        Entity slotZero = world.Create(new FormationMemberState { FormationIndex = 2, SlotIndex = 0 });
        Entity loneUnit = world.Create();
        (Dictionary<string, object> globals, EntityCollectionStore collections) = CreateGlobals(world, player);
        var system = new FormationCommandMembersSystem(world, globals, CommandSourceKey, MembersKey, maxMembersPerFormation: 2, maxMemberCount: 4);

        SetCommandSource(collections, player, anchor, loneUnit);
        system.Update(0f);

        Assert.That(ReadMembers(collections, player), Is.EqualTo(new[] { slotZero, slotOne, loneUnit }));
    }

    [Test]
    public void Update_RepublishesOnlyWhenResolvedMembersChange()
    {
        using var world = World.Create();
        Entity player = world.Create();
        Entity anchor = world.Create(new FormationAnchorState { FormationIndex = 1, SlotCount = 1 });
        Entity soldier = world.Create(new FormationMemberState { FormationIndex = 1, SlotIndex = 0 });
        (Dictionary<string, object> globals, EntityCollectionStore collections) = CreateGlobals(world, player);
        var system = new FormationCommandMembersSystem(world, globals, CommandSourceKey, MembersKey, maxMembersPerFormation: 1, maxMemberCount: 2);

        SetCommandSource(collections, player, anchor);
        system.Update(0f);
        uint firstRevision = ReadRevision(collections, player);
        system.Update(0f);
        uint unchangedRevision = ReadRevision(collections, player);

        SetCommandSource(collections, player);
        system.Update(0f);

        Assert.Multiple(() =>
        {
            Assert.That(unchangedRevision, Is.EqualTo(firstRevision));
            Assert.That(ReadMembers(collections, player), Is.Empty);
            Assert.That(ReadRevision(collections, player), Is.Not.EqualTo(firstRevision));
            Assert.That(world.IsAlive(soldier), Is.True);
        });
    }

    private static (Dictionary<string, object> Globals, EntityCollectionStore Collections) CreateGlobals(World world, Entity player)
    {
        var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        keys.Register(CommandSourceKey);
        keys.Register(MembersKey);
        var collections = new EntityCollectionStore(keys, 8, 32);
        var globals = new Dictionary<string, object>
        {
            [CoreServiceKeys.EntityCollectionStore.Name] = collections,
            [CoreServiceKeys.EntityCollectionKeyRegistry.Name] = keys,
        };
        ClientLocalSeatTestBindings.BindSoleSeat(globals, player, 1, "seat.0");
        return (globals, collections);
    }

    private static void SetCommandSource(EntityCollectionStore collections, Entity player, params Entity[] entities)
    {
        var descriptor = EntityCollectionDescriptor.Create(
            CommandSourceKey,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            contextEntity: player,
            primaryEntity: entities.Length > 0 ? entities[0] : Entity.Null,
            title: "Formation test command source",
            summary: "Test-owned command source collection.");
        collections.Replace(player, in descriptor, entities, player);
    }

    private static Entity[] ReadMembers(EntityCollectionStore collections, Entity player)
    {
        var buffer = new Entity[8];
        int count = collections.CopyEntities(player, MembersKey, buffer);
        return buffer[..count];
    }

    private static uint ReadRevision(EntityCollectionStore collections, Entity player)
    {
        Assert.That(collections.TryGetView(player, MembersKey, out EntityCollectionView view), Is.True);
        return view.Revision;
    }
}
