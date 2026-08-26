using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelHosting;
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
        public void Project_PreservesGraphOrderAndFillsColumnsFromItemTemplate()
        {
            int healthId = AttributeRegistry.Register("Health");
            int stunnedId = TagRegistry.Register("Status.Stunned");

            PanelItemTemplate item = PanelItemTemplateLoader.Load("""
            {
              "id": "item.unit.roster",
              "fields": [
                { "name": "displayName", "kind": "name" },
                { "name": "health", "kind": "attribute", "attribute": "Health" },
                { "name": "healthMax", "kind": "attributeBase", "attribute": "Health" },
                { "name": "stunned", "kind": "tag", "tag": "Status.Stunned" }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "bind": "displayName" },
                  { "type": "progressBar", "current": "health", "max": "healthMax" },
                  { "type": "badge", "bind": "stunned", "text": "晕眩", "showWhen": true }
                ]
              }
            }
            """);

            PanelTemplate template = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.list",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                {
                  "name": "units",
                  "collectionKey": "tests.roster",
                  "item": "item.unit.roster"
                }
              ]
            }
            """);

            var items = new PanelItemTemplateRegistry();
            items.Register(item);
            items.Freeze();
            PanelItemTemplateBinder.Bind(template, items);

            using World world = World.Create();
            Entity owner = world.Create();
            Entity high = CreateUnit(world, "A", healthId, 90f, stunnedId, stunned: false);
            Entity mid = CreateUnit(world, "B", healthId, 70f, stunnedId, stunned: true);

            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var store = new EntityCollectionStore(keyRegistry, 8, 16);
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.roster",
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.Display);
            store.Replace(owner, descriptor, new[] { high, mid });

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
