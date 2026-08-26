using System;
using Arch.Core;
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
        public void Load_ListsAndLayout_ParsesColumnsAndControls()
        {
            const string json = """
            {
              "id": "tests.panel.roster",
              "graph": "tests.graph.roster",
              "pins": [ { "name": "rowCount", "key": "k.count", "mode": "realtime", "default": 0 } ],
              "lists": [
                {
                  "name": "units",
                  "collectionKey": "tests.collection.units",
                  "item": {
                    "fields": [
                      { "name": "displayName", "kind": "name" },
                      { "name": "health", "kind": "attribute", "attribute": "Health" },
                      { "name": "stunned", "kind": "tag", "tag": "Status.Stunned" }
                    ]
                  }
                }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "prefix": "在编 ", "bind": "rowCount" },
                  {
                    "type": "list",
                    "bind": "units",
                    "itemControls": [
                      { "type": "label", "bind": "displayName" },
                      { "type": "badge", "bind": "stunned", "text": "晕眩", "showWhen": true }
                    ]
                  }
                ]
              }
            }
            """;

            PanelTemplate template = PanelTemplateLoader.Load(json);
            Assert.That(template.Lists.Count, Is.EqualTo(1));
            Assert.That(template.Lists[0].Fields[2].Kind, Is.EqualTo(PanelItemFieldKind.Tag));
            Assert.That(template.Layout, Is.Not.Null);
            Assert.That(template.Layout!.Controls[1].Type, Is.EqualTo(PanelLayoutControlType.List));
            Assert.That(template.Layout.Controls[1].ItemControls[1].ShowWhen, Is.True);
        }

        [Test]
        public void Load_ListFilterOrSort_FailsClosed()
        {
            const string json = """
            {
              "id": "tests.panel.badfilter",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "lists": [
                {
                  "name": "units",
                  "collectionKey": "c",
                  "filter": [ { "kind": "attribute", "attribute": "Health", "op": "gt", "value": 0 } ],
                  "item": { "fields": [ { "name": "health", "kind": "attribute", "attribute": "Health" } ] }
                }
              ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("filter"));
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
                  { "type": "list", "bind": "missing", "itemControls": [ { "type": "label", "text": "x" } ] }
                ]
              }
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("list"));
        }
    }
}
