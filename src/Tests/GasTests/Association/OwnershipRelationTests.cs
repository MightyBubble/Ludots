using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
    public sealed class OwnershipRelationTests
    {
        [Test]
        public void InventoryOwnership_UsesTransitiveOwnsChainAcrossPlayerCityContainerAndItems()
        {
            using World world = World.Create();
            var relationshipTypes = new RelationshipTypeRegistry();
            var relationshipMetrics = new RelationshipMetricRegistry();
            var relationshipFlags = new RelationshipFlagRegistry();
            var relationshipBands = new RelationshipBandRegistry();
            var relationshipReasons = new RelationshipReasonRegistry();
            var relationships = new RelationshipRuntime(
                world,
                relationshipTypes,
                relationshipMetrics,
                relationshipFlags,
                relationshipBands,
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
            RelationshipCatalogInstaller.RegisterCatalog(
                new RelationshipCatalogConfig
                {
                    Types = { new RelationshipTypeConfig { Id = "Owns" } },
                },
                relationshipTypes,
                relationshipMetrics,
                relationshipFlags,
                relationshipBands,
                relationshipReasons);
            int ownsTypeId = relationships.TypeRegistry.GetId("Owns");
            var ownership = new OwnershipResolver(relationships, ownsTypeId);

            var shapes = new ItemShapeRegistry();
            var layouts = new ItemLayoutRegistry();
            var definitions = new ItemDefinitionRegistry();
            int stashLayout = RegisterOneByOneItemSetup(shapes, layouts, definitions);
            var inventory = new InventoryRuntimeService(world, shapes, layouts, definitions, ownership);

            Entity player = world.Create();
            Entity city = world.Create();
            ownership.EnsureOwnership(player, city);

            Entity stash = inventory.CreateContainer(city, stashLayout, ItemContainerPurpose.Stash);
            Entity artifact = inventory.CreateItem(definitionId: 1);

            Assert.That(inventory.TryMoveItemToGrid(artifact, stash, 0, 0), Is.True);
            Assert.That(inventory.CountStackUnits(player, 1), Is.EqualTo(1));
            Assert.That(inventory.TryFindOwnedContainer(player, ItemContainerPurpose.Stash, out Entity resolvedStash), Is.True);
            Assert.That(resolvedStash, Is.EqualTo(stash));
            Assert.That(inventory.TryFindOwnedItem(player, 1, ItemContainerPurpose.Stash, out Entity resolvedItem), Is.True);
            Assert.That(resolvedItem, Is.EqualTo(artifact));

            Span<Entity> scratch = stackalloc Entity[4];
            int containerOwners = relationships.CollectIncoming(stash, ownsTypeId, scratch);
            Assert.That(containerOwners, Is.EqualTo(1));
            Assert.That(scratch[0], Is.EqualTo(city));

            int cityOwners = relationships.CollectIncoming(city, ownsTypeId, scratch);
            Assert.That(cityOwners, Is.EqualTo(1));
            Assert.That(scratch[0], Is.EqualTo(player));
        }

        private static int RegisterOneByOneItemSetup(
            ItemShapeRegistry shapes,
            ItemLayoutRegistry layouts,
            ItemDefinitionRegistry definitions)
        {
            int shapeId = shapes.Register("shape_1x1", new ItemShapeDefinition
            {
                Id = "shape_1x1",
                Rotations = new[] { new ItemShapeRotation(1, 1, new[] { true }) }
            });

            int layoutId = layouts.Register("layout_stash", new ItemLayoutDefinition
            {
                Id = "layout_stash",
                Purpose = ItemContainerPurpose.Stash,
                Width = 2,
                Height = 2,
            }.InitializeBlockedMask(new bool[4]));

            definitions.Register("artifact", new ItemDefinition
            {
                Id = "artifact",
                DisplayName = "Artifact",
                ShapeId = shapeId,
            });

            return layoutId;
        }
    }
}
