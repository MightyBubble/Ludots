using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    [TestFixture]
    public sealed class PanelTemplateTests
    {
        private const string AggregateTemplateJson = """
        {
          "id": "tests.panel.resource_bar",
          "graph": "tests.graph.resource_bar",
          "pins": [
            { "name": "ore.total", "key": "tests.panel.ore.total", "mode": "realtime", "default": 0 },
            { "name": "gas.total", "key": "tests.panel.gas.total", "mode": "snapshot", "default": -1 }
          ]
        }
        """;

        [Test]
        public void Load_ValidTemplate_CarriesGraphAndPins()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);

            Assert.That(template.Id, Is.EqualTo("tests.panel.resource_bar"));
            Assert.That(template.Graph, Is.EqualTo("tests.graph.resource_bar"));
            Assert.That(template.Pins.Count, Is.EqualTo(2));
            Assert.That(template.Pins[0].Key, Is.EqualTo("tests.panel.ore.total"));
            Assert.That(template.Pins[0].Realtime, Is.True);
            Assert.That(template.Pins[1].Realtime, Is.False);
            Assert.That(template.Pins[1].Default, Is.EqualTo(-1f));
        }

        [Test]
        public void Load_MissingGraph_FailsNamingTemplate()
        {
            const string json = """
            { "id": "tests.panel.nograph", "pins": [ { "name": "hp", "key": "k", "default": 0 } ] }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.Exception.With.Message.Contains("graph"));
        }

        [Test]
        public void Load_EmptyPins_Fails()
        {
            const string json = """
            { "id": "tests.panel.nopins", "graph": "g", "pins": [] }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("pins"));
        }

        [Test]
        public void Load_UnknownPinMode_FailsNamingMode()
        {
            const string json = """
            {
              "id": "tests.panel.badmode", "graph": "g",
              "pins": [ { "name": "hp", "key": "k", "mode": "sometimes" } ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("sometimes"));
        }

        [Test]
        public void Load_DuplicatePin_FailsNamingPin()
        {
            const string json = """
            {
              "id": "tests.panel.dup", "graph": "g",
              "pins": [ { "name": "hp", "key": "k1" }, { "name": "hp", "key": "k2" } ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.Exception.With.Message.Contains("hp"));
        }

        [Test]
        public void Load_UnknownRootField_FailsNamingField()
        {
            const string json = """
            {
              "id": "tests.panel.unknown", "graph": "g",
              "pins": [ { "name": "hp", "key": "k" } ],
              "variables": []
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("variables"));
        }

        [Test]
        public void Evaluate_PinsReadGraphOutputs()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);
            using World world = World.Create();
            Entity owner = world.Create();

            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);
            outputs.SetFloat(owner, "tests.panel.ore.total", 1200f);
            outputs.SetFloat(owner, "tests.panel.gas.total", 450.5f);

            var reader = new PanelProjectionReader(world, outputs);
            PanelVariableSet result = new PanelInstance(template, owner).Evaluate(reader);

            Assert.That(result.Get("ore.total"), Is.EqualTo(1200f));
            Assert.That(result.Get("gas.total"), Is.EqualTo(450.5f).Within(0.0001f));
        }

        [Test]
        public void Evaluate_MissingGraphOutput_ShowsDeclaredDefault()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);
            using World world = World.Create();
            Entity owner = world.Create();

            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);

            var reader = new PanelProjectionReader(world, outputs);
            PanelVariableSet result = new PanelInstance(template, owner).Evaluate(reader);

            Assert.That(result.Get("ore.total"), Is.EqualTo(0f), "missing output shows pin default, no error");
            Assert.That(result.Get("gas.total"), Is.EqualTo(-1f));
        }

        [Test]
        public void Evaluate_TwoInstancesSameTemplate_IndependentScopes()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);
            using World world = World.Create();
            Entity ownerA = world.Create();
            Entity ownerB = world.Create();

            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);
            outputs.SetFloat(ownerA, "tests.panel.ore.total", 100f);
            outputs.SetFloat(ownerA, "tests.panel.gas.total", 10f);
            outputs.SetFloat(ownerB, "tests.panel.ore.total", 900f);
            outputs.SetFloat(ownerB, "tests.panel.gas.total", 90f);

            var reader = new PanelProjectionReader(world, outputs);
            PanelVariableSet setA = new PanelInstance(template, ownerA).Evaluate(reader);
            PanelVariableSet setB = new PanelInstance(template, ownerB).Evaluate(reader);

            Assert.That(setA.Get("ore.total"), Is.EqualTo(100f));
            Assert.That(setB.Get("ore.total"), Is.EqualTo(900f));
        }

        [Test]
        public void Load_CollectionsReferenceElementTemplateId()
        {
            const string json = """
            {
              "id": "tests.panel.roster",
              "graph": "tests.graph.roster",
              "pins": [ { "name": "rowCount", "key": "k.count", "mode": "realtime", "default": 0 } ],
              "collections": [
                {
                  "name": "units",
                  "source": "selfGraph",
                  "collectionKey": "tests.collection.units",
                  "template": "panel.unit.roster"
                }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "prefix": "在编 ", "bind": "rowCount" },
                  { "type": "list", "bind": "units" }
                ]
              }
            }
            """;

            PanelTemplate template = PanelTemplateLoader.Load(json);
            Assert.That(template.Collections.Count, Is.EqualTo(1));
            Assert.That(template.Collections[0].TemplateId, Is.EqualTo("panel.unit.roster"));
            Assert.That(template.Subject, Is.EqualTo(PanelSubjectKind.None));
            Assert.That(template.Layout!.Controls[1].Type, Is.EqualTo(PanelLayoutControlType.List));
        }

        [Test]
        public void Load_ElementDeclaresSubjectAndGraph()
        {
            const string json = """
            {
              "id": "panel.unit.roster",
              "subject": "Entity",
              "graph": "Graph.Unit.RosterCard",
              "pins": [
                { "name": "health", "key": "unit.roster.health", "default": 0 }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "bind": "displayName" },
                  { "type": "progressBar", "current": "health", "max": "health" }
                ]
              }
            }
            """;

            PanelTemplate element = PanelTemplateLoader.Load(json);
            Assert.That(element.Subject, Is.EqualTo(PanelSubjectKind.Entity));
            Assert.That(element.Collections.Count, Is.EqualTo(0));
        }

        [Test]
        public void Load_UnknownSubject_FailsClosed()
        {
            const string json = """
            {
              "id": "panel.bad",
              "subject": "Widget",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("subject"));
        }

        [Test]
        public void Load_InlineItemControls_FailsClosed()
        {
            const string json = """
            {
              "id": "tests.panel.inline",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                { "name": "units", "source": "selfGraph", "collectionKey": "c", "template": "panel.unit.roster" }
              ],
              "layout": {
                "controls": [
                  {
                    "type": "list",
                    "bind": "units",
                    "itemControls": [ { "type": "label", "text": "x" } ]
                  }
                ]
              }
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("itemControls"));
        }

        [Test]
        public void Load_LegacyListsRoot_FailsClosed()
        {
            const string json = """
            {
              "id": "tests.panel.legacy",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "lists": []
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("lists"));
        }

        [Test]
        public void Load_ListControlBindUnknown_FailsClosed()
        {
            const string json = """
            {
              "id": "tests.panel.badlist",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "layout": {
                "controls": [
                  { "type": "list", "bind": "missing" }
                ]
              }
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("list"));
        }

        [Test]
        public void BindElements_RequiresMatchingSubject()
        {
            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.unit.roster",
              "subject": "Entity",
              "graph": "g",
              "pins": [ { "name": "health", "key": "k" } ],
              "layout": { "controls": [ { "type": "label", "text": "x" } ] }
            }
            """);

            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.roster",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                { "name": "units", "source": "selfGraph", "collectionKey": "c", "template": "panel.unit.roster" }
              ],
              "layout": { "controls": [ { "type": "list", "bind": "units" } ] }
            }
            """);

            var registry = new Ludots.Core.UI.PanelHosting.PanelTemplateRegistry();
            registry.Register(element);
            registry.Register(host);
            registry.Freeze();
            PanelListProjector.BindElements(host, registry);
            Assert.That(host.Collections[0].Template, Is.SameAs(element));
        }
    }
}
