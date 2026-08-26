using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    [TestFixture]
    public sealed class PanelListProjectorTests
    {
        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
        }

        [Test]
        public void Project_FiltersSortsAndProjectsScalars()
        {
            int healthId = AttributeRegistry.Register("Health");
            int stunnedId = TagRegistry.Register("Status.Stunned");

            const string templateJson = """
            {
              "id": "tests.panel.list",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "lists": [
                {
                  "name": "units",
                  "collectionKey": "tests.roster",
                  "filter": [ { "kind": "attribute", "attribute": "Health", "op": "gt", "value": 0 } ],
                  "sort": [ { "attribute": "Health", "descending": true } ],
                  "item": {
                    "fields": [
                      { "name": "displayName", "kind": "name" },
                      { "name": "health", "kind": "attribute", "attribute": "Health" },
                      { "name": "healthMax", "kind": "attributeBase", "attribute": "Health" },
                      { "name": "stunned", "kind": "tag", "tag": "Status.Stunned" }
                    ]
                  }
                }
              ]
            }
            """;

            PanelTemplate template = PanelTemplateLoader.Load(templateJson);
            PanelListProjector.BindSymbols(template);

            using World world = World.Create();
            Entity owner = world.Create();
            Entity high = CreateUnit(world, "A", healthId, 90f, stunnedId, stunned: false);
            Entity mid = CreateUnit(world, "B", healthId, 70f, stunnedId, stunned: true);
            Entity dead = CreateUnit(world, "C", healthId, 0f, stunnedId, stunned: false);

            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var store = new EntityCollectionStore(keyRegistry, 8, 16);
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.roster",
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.Display);
            store.Replace(owner, descriptor, new[] { high, mid, dead });

            var projector = new PanelListProjector(world, store);
            IReadOnlyList<PanelListProjection> lists = projector.Project(owner, template);

            Assert.That(lists.Count, Is.EqualTo(1));
            Assert.That(lists[0].Items.Count, Is.EqualTo(2));
            Assert.That(lists[0].Items[0].Strings["displayName"], Is.EqualTo("A"));
            Assert.That(lists[0].Items[0].Floats["health"], Is.EqualTo(90f));
            Assert.That(lists[0].Items[1].Strings["displayName"], Is.EqualTo("B"));
            Assert.That(lists[0].Items[1].Bools["stunned"], Is.True);
        }

        private static Entity CreateUnit(World world, string name, int healthId, float health, int stunnedId, bool stunned)
        {
            Entity entity = world.Create();
            world.Add(entity, new Name { Value = name });
            world.Add(entity, new AttributeBuffer());
            ref AttributeBuffer buffer = ref world.Get<AttributeBuffer>(entity);
            buffer.SetBase(healthId, health);
            var tags = new GameplayTagContainer();
            if (stunned)
            {
                tags.AddTag(stunnedId);
            }

            world.Add(entity, tags);
            return entity;
        }
    }
}
