using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
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
        public void Project_PassesEntityThroughAndFillsSubjectSurface()
        {
            AttributeRegistry.Register("Health");
            TagRegistry.Register("Status.Stunned");

            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.unit.roster",
              "subject": "Entity",
              "graph": "g.card",
              "pins": [
                { "name": "health", "key": "unit.roster.health", "default": 0 },
                { "name": "stunned", "key": "unit.roster.stunned", "default": 0 }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "bind": "displayName" },
                  { "type": "badge", "bind": "stunned", "text": "晕眩", "showWhen": true }
                ]
              }
            }
            """);

            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.list",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                {
                  "name": "units",
                  "collectionKey": "tests.roster",
                  "template": "panel.unit.roster"
                }
              ]
            }
            """);

            var registry = new PanelTemplateRegistry();
            registry.Register(element);
            registry.Register(host);
            registry.Freeze();
            PanelListProjector.BindElements(host, registry);

            using World world = World.Create();
            Entity owner = world.Create();
            Entity high = CreateUnit(world, "A");
            Entity mid = CreateUnit(world, "B");

            var values = new GraphOutputValueStore(new StringIntRegistry(8, 1, 0, StringComparer.Ordinal), 8);
            values.SetFloat(high, "unit.roster.health", 90f);
            values.SetBool(high, "unit.roster.stunned", false);
            values.SetFloat(mid, "unit.roster.health", 70f);
            values.SetBool(mid, "unit.roster.stunned", true);

            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var store = new EntityCollectionStore(keyRegistry, 8, 16);
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.roster",
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.Display);
            store.Replace(owner, descriptor, new[] { high, mid });

            var reader = new PanelProjectionReader(world, values);
            var projector = new PanelListProjector(world, store, reader, graphEvaluator: null);
            IReadOnlyList<PanelListProjection> lists = projector.Project(owner, host);

            Assert.That(lists[0].Items.Count, Is.EqualTo(2));
            Assert.That(lists[0].Items[0].Strings["displayName"], Is.EqualTo("A"));
            Assert.That(lists[0].Items[0].Floats["health"], Is.EqualTo(90f));
            Assert.That(lists[0].Items[1].Strings["displayName"], Is.EqualTo("B"));
            Assert.That(lists[0].Items[1].Bools["stunned"], Is.True);
        }

        private static Entity CreateUnit(World world, string name)
        {
            Entity entity = world.Create();
            world.Add(entity, new Name { Value = name });
            world.Add(entity, new AttributeBuffer());
            world.Add(entity, new GameplayTagContainer());
            return entity;
        }
    }
}
