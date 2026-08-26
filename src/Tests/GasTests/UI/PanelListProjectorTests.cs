using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.Core.TypedCollections;
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
            EffectTemplateIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
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
                  "source": "selfGraph",
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
            var intIdStore = new IntIdCollectionStore(keyRegistry, 8, 16);
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.roster",
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.Display);
            store.Replace(owner, descriptor, new[] { high, mid });

            var reader = new PanelProjectionReader(world, values);
            var projector = new PanelListProjector(
                world,
                store,
                intIdStore,
                new ItemDefinitionRegistry(),
                reader,
                graphEvaluator: null);
            IReadOnlyList<PanelListProjection> lists = projector.Project(owner, host);

            Assert.That(lists[0].Items.Count, Is.EqualTo(2));
            Assert.That(lists[0].Items[0].Strings["displayName"], Is.EqualTo("A"));
            Assert.That(lists[0].Items[0].Floats["health"], Is.EqualTo(90f));
            Assert.That(lists[0].Items[1].Strings["displayName"], Is.EqualTo("B"));
            Assert.That(lists[0].Items[1].Bools["stunned"], Is.True);
        }

        [Test]
        public void Project_EffectTemplateCollection_UsesIntIdStoreAndRegistryName()
        {
            int effectTemplateId = EffectTemplateIdRegistry.Register("效果.祝福");
            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.effect.template.chip",
              "subject": "EffectTemplate",
              "graph": "g.effect.template",
              "pins": [ { "name": "selected", "key": "effect.template.selected", "default": 0 } ],
              "layout": { "controls": [ { "type": "label", "bind": "displayName" } ] }
            }
            """);
            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.effect.templates",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                {
                  "name": "templates",
                  "source": "selfGraph",
                  "collectionKey": "tests.effect.templates",
                  "template": "panel.effect.template.chip"
                }
              ]
            }
            """);
            Bind(host, element);

            using World world = World.Create();
            Entity owner = world.Create();
            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var entityStore = new EntityCollectionStore(keyRegistry, 8, 16);
            var intIdStore = new IntIdCollectionStore(keyRegistry, 8, 16);
            var descriptor = IntIdCollectionDescriptor.Create(
                "tests.effect.templates",
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.Display);
            intIdStore.Replace(owner, descriptor, new[] { effectTemplateId });
            var values = new GraphOutputValueStore(
                new StringIntRegistry(8, 1, 0, StringComparer.Ordinal),
                8);
            var projector = new PanelListProjector(
                world,
                entityStore,
                intIdStore,
                new ItemDefinitionRegistry(),
                new PanelProjectionReader(world, values));

            IReadOnlyList<PanelListProjection> lists = projector.Project(owner, host);

            Assert.That(lists[0].Items, Has.Count.EqualTo(1));
            Assert.That(lists[0].Items[0].MemberIntId, Is.EqualTo(effectTemplateId));
            Assert.That(lists[0].Items[0].Strings["displayName"], Is.EqualTo("效果.祝福"));
        }

        [Test]
        public void Project_SourceInput_UsesParentOutputKeyOnHostScope()
        {
            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.input.unit",
              "subject": "Entity",
              "graph": "g.input.unit",
              "pins": [ { "name": "selected", "key": "input.unit.selected", "default": 0 } ],
              "layout": { "controls": [ { "type": "label", "bind": "displayName" } ] }
            }
            """);
            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.input",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "inputs": [
                {
                  "name": "visibleUnits",
                  "from": { "space": "parent", "output": "parent.visible.units" },
                  "type": "EntityCollection"
                }
              ],
              "collections": [
                {
                  "name": "units",
                  "source": "input",
                  "input": "visibleUnits",
                  "template": "panel.input.unit"
                }
              ]
            }
            """);
            Bind(host, element);

            using World world = World.Create();
            Entity hostScope = world.Create();
            Entity unit = CreateUnit(world, "来源输入");
            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var entityStore = new EntityCollectionStore(keyRegistry, 8, 16);
            var intIdStore = new IntIdCollectionStore(keyRegistry, 8, 16);
            entityStore.Replace(
                hostScope,
                EntityCollectionDescriptor.Create(
                    "parent.visible.units",
                    EntityCollectionSourceKind.GasGraphResult,
                    EntityCollectionRoleKind.Display),
                new[] { unit });
            var values = new GraphOutputValueStore(
                new StringIntRegistry(8, 1, 0, StringComparer.Ordinal),
                8);
            var projector = new PanelListProjector(
                world,
                entityStore,
                intIdStore,
                new ItemDefinitionRegistry(),
                new PanelProjectionReader(world, values));

            IReadOnlyList<PanelListProjection> lists = projector.Project(hostScope, host);

            Assert.That(lists[0].Items, Has.Count.EqualTo(1));
            Assert.That(lists[0].Items[0].Strings["displayName"], Is.EqualTo("来源输入"));
        }

        [Test]
        public void Project_AbilitySlotGraph_UsesOwnerAndSubjectIntId()
        {
            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.ability.slot",
              "subject": "AbilitySlot",
              "graph": "g.ability.slot",
              "pins": [ { "name": "ready", "key": "ability.slot.ready", "default": 0 } ],
              "layout": { "controls": [ { "type": "label", "bind": "displayName" } ] }
            }
            """);
            element.GraphId = 42;
            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.ability.slots",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                {
                  "name": "slots",
                  "source": "selfGraph",
                  "collectionKey": "ability.slots",
                  "template": "panel.ability.slot"
                }
              ]
            }
            """);
            Bind(host, element);

            using World world = World.Create();
            Entity owner = world.Create();
            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var entityStore = new EntityCollectionStore(keyRegistry, 8, 16);
            var intIdStore = new IntIdCollectionStore(keyRegistry, 8, 16);
            intIdStore.Replace(
                owner,
                IntIdCollectionDescriptor.Create(
                    "ability.slots",
                    EntityCollectionSourceKind.GasGraphResult,
                    EntityCollectionRoleKind.Display),
                new[] { 3 });
            var values = new GraphOutputValueStore(
                new StringIntRegistry(8, 1, 0, StringComparer.Ordinal),
                8);
            var evaluator = new RecordingPanelGraphEvaluator();
            var projector = new PanelListProjector(
                world,
                entityStore,
                intIdStore,
                new ItemDefinitionRegistry(),
                new PanelProjectionReader(world, values),
                evaluator);

            IReadOnlyList<PanelListProjection> lists = projector.Project(owner, host);

            Assert.That(evaluator.GraphId, Is.EqualTo(42));
            Assert.That(evaluator.Owner, Is.EqualTo(owner));
            Assert.That(evaluator.SubjectIntId, Is.EqualTo(3));
            Assert.That(lists[0].Items[0].Strings["displayName"], Is.EqualTo("3"));
        }

        [Test]
        public void Project_NestedCollection_StoresChildProjectionOnParentItem()
        {
            PanelTemplate child = PanelTemplateLoader.Load("""
            {
              "id": "panel.nested.child",
              "subject": "Entity",
              "graph": "g.nested.child",
              "pins": [ { "name": "value", "key": "nested.child.value", "default": 0 } ],
              "layout": { "controls": [ { "type": "label", "bind": "displayName" } ] }
            }
            """);
            PanelTemplate parent = PanelTemplateLoader.Load("""
            {
              "id": "panel.nested.parent",
              "subject": "Entity",
              "graph": "g.nested.parent",
              "pins": [ { "name": "value", "key": "nested.parent.value", "default": 0 } ],
              "inputs": [
                {
                  "name": "sharedChildren",
                  "from": { "space": "parent", "output": "nested.shared.children" },
                  "type": "EntityCollection"
                }
              ],
              "collections": [
                {
                  "name": "children",
                  "source": "selfGraph",
                  "collectionKey": "nested.children",
                  "template": "panel.nested.child"
                },
                {
                  "name": "sharedChildren",
                  "source": "input",
                  "input": "sharedChildren",
                  "template": "panel.nested.child"
                }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "bind": "displayName" },
                  { "type": "list", "bind": "children" },
                  { "type": "list", "bind": "sharedChildren" }
                ]
              }
            }
            """);
            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.nested",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                {
                  "name": "parents",
                  "source": "selfGraph",
                  "collectionKey": "nested.parents",
                  "template": "panel.nested.parent"
                }
              ]
            }
            """);
            var registry = new PanelTemplateRegistry();
            registry.Register(child);
            registry.Register(parent);
            registry.Register(host);
            registry.Freeze();
            PanelListProjector.BindElements(host, registry);

            using World world = World.Create();
            Entity hostScope = world.Create();
            Entity parentEntity = CreateUnit(world, "父");
            Entity childEntity = CreateUnit(world, "子");
            Entity sharedChildEntity = CreateUnit(world, "共享子");
            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var entityStore = new EntityCollectionStore(keyRegistry, 8, 16);
            var intIdStore = new IntIdCollectionStore(keyRegistry, 8, 16);
            entityStore.Replace(
                hostScope,
                EntityCollectionDescriptor.Create(
                    "nested.parents",
                    EntityCollectionSourceKind.GasGraphResult,
                    EntityCollectionRoleKind.Display),
                new[] { parentEntity });
            entityStore.Replace(
                parentEntity,
                EntityCollectionDescriptor.Create(
                    "nested.children",
                    EntityCollectionSourceKind.GasGraphResult,
                    EntityCollectionRoleKind.Display),
                new[] { childEntity });
            entityStore.Replace(
                hostScope,
                EntityCollectionDescriptor.Create(
                    "nested.shared.children",
                    EntityCollectionSourceKind.GasGraphResult,
                    EntityCollectionRoleKind.Display),
                new[] { sharedChildEntity });
            var values = new GraphOutputValueStore(
                new StringIntRegistry(8, 1, 0, StringComparer.Ordinal),
                8);
            var projector = new PanelListProjector(
                world,
                entityStore,
                intIdStore,
                new ItemDefinitionRegistry(),
                new PanelProjectionReader(world, values));

            IReadOnlyList<PanelListProjection> lists = projector.Project(hostScope, host);

            PanelListItemProjection parentItem = lists[0].Items[0];
            Assert.That(parentItem.NestedLists, Has.Count.EqualTo(2));
            Assert.That(parentItem.NestedLists[0].Name, Is.EqualTo("children"));
            Assert.That(parentItem.NestedLists[0].Items, Has.Count.EqualTo(1));
            Assert.That(
                parentItem.NestedLists[0].Items[0].Strings["displayName"],
                Is.EqualTo("子"));
            Assert.That(parentItem.NestedLists[1].Name, Is.EqualTo("sharedChildren"));
            Assert.That(
                parentItem.NestedLists[1].Items[0].Strings["displayName"],
                Is.EqualTo("共享子"));
        }

        private static void Bind(PanelTemplate host, PanelTemplate element)
        {
            var registry = new PanelTemplateRegistry();
            registry.Register(element);
            registry.Register(host);
            registry.Freeze();
            PanelListProjector.BindElements(host, registry);
        }

        private sealed class RecordingPanelGraphEvaluator : IPanelGraphEvaluator
        {
            public int GraphId { get; private set; }
            public Entity Owner { get; private set; }
            public int SubjectIntId { get; private set; }

            public void Evaluate(int graphId, Entity owner, int subjectIntId = 0)
            {
                GraphId = graphId;
                Owner = owner;
                SubjectIntId = subjectIntId;
            }
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
