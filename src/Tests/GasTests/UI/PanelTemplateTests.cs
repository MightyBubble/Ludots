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
            { "name": "ore.total", "key": "tests.panel.ore.total", "mode": "realtime", "type": "Float", "default": 0 },
            { "name": "gas.total", "key": "tests.panel.gas.total", "mode": "snapshot", "type": "Float", "default": -1 }
          ]
        }
        """;

        [Test]
        public void Load_ValidTemplate_CarriesGraphAndTypedPins()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);

            Assert.That(template.Id, Is.EqualTo("tests.panel.resource_bar"));
            Assert.That(template.Graph, Is.EqualTo("tests.graph.resource_bar"));
            Assert.That(template.Pins.Count, Is.EqualTo(2));
            Assert.That(template.Pins[0].Key, Is.EqualTo("tests.panel.ore.total"));
            Assert.That(template.Pins[0].Realtime, Is.True);
            Assert.That(template.Pins[0].Kind, Is.EqualTo(PanelValueKind.Float));
            Assert.That(template.Pins[0].FloatDefault, Is.EqualTo(0f));
            Assert.That(template.Pins[1].Realtime, Is.False);
            Assert.That(template.Pins[1].FloatDefault, Is.EqualTo(-1f));
        }

        [Test]
        public void Load_PinWithoutType_DefaultsToFloat()
        {
            const string json = """
            { "id": "tests.panel.notype", "graph": "g",
              "pins": [ { "name": "hp", "key": "k", "mode": "realtime", "default": 5 } ] }
            """;

            PanelTemplate template = PanelTemplateLoader.Load(json);
            Assert.That(template.Pins[0].Kind, Is.EqualTo(PanelValueKind.Float), "absent type keeps legacy templates readable as Float");
            Assert.That(template.Pins[0].FloatDefault, Is.EqualTo(5f));
        }

        [Test]
        public void Load_TypedDefaults_IntAndBoolAndEntity()
        {
            const string json = """
            {
              "id": "tests.panel.typed", "graph": "g",
              "pins": [
                { "name": "stage", "key": "k1", "type": "Int", "default": 7 },
                { "name": "ready", "key": "k2", "type": "Bool", "default": true },
                { "name": "selection", "key": "k3", "type": "Entity" }
              ]
            }
            """;

            PanelTemplate template = PanelTemplateLoader.Load(json);
            Assert.That(template.Pins[0].Kind, Is.EqualTo(PanelValueKind.Int));
            Assert.That(template.Pins[0].IntDefault, Is.EqualTo(7));
            Assert.That(template.Pins[1].Kind, Is.EqualTo(PanelValueKind.Bool));
            Assert.That(template.Pins[1].BoolDefault, Is.True);
            Assert.That(template.Pins[2].Kind, Is.EqualTo(PanelValueKind.Entity));
        }

        [Test]
        public void Load_UnknownType_FailsNamingType()
        {
            const string json = """
            { "id": "tests.panel.badtype", "graph": "g",
              "pins": [ { "name": "hp", "key": "k", "type": "Decimal" } ] }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("Decimal"));
        }

        [Test]
        public void Load_DefaultTypeMismatch_FailsClosed()
        {
            Assert.That(
                () => PanelTemplateLoader.Load("""
                { "id": "tests.panel.bad_int_default", "graph": "g",
                  "pins": [ { "name": "stage", "key": "k", "type": "Int", "default": 1.5 } ] }
                """),
                Throws.InvalidOperationException.With.Message.Contains("Int"));

            Assert.That(
                () => PanelTemplateLoader.Load("""
                { "id": "tests.panel.bad_bool_default", "graph": "g",
                  "pins": [ { "name": "ready", "key": "k", "type": "Bool", "default": 1 } ] }
                """),
                Throws.InvalidOperationException.With.Message.Contains("Bool"));

            Assert.That(
                () => PanelTemplateLoader.Load("""
                { "id": "tests.panel.bad_entity_default", "graph": "g",
                  "pins": [ { "name": "selection", "key": "k", "type": "Entity", "default": 0 } ] }
                """),
                Throws.InvalidOperationException.With.Message.Contains("Entity"),
                "Entity pins cannot declare a default; entity values come only from the graph output");
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
        public void Evaluate_PinsReadTypedGraphOutputs()
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

            Assert.That(result.Get("ore.total").Kind, Is.EqualTo(PanelValueKind.Float));
            Assert.That(result.Get("ore.total").FloatValue, Is.EqualTo(1200f));
            Assert.That(result.Get("gas.total").FloatValue, Is.EqualTo(450.5f).Within(0.0001f));
        }

        [Test]
        public void Evaluate_MissingGraphOutput_FailsLoudly()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);
            using World world = World.Create();
            Entity owner = world.Create();

            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);

            var reader = new PanelProjectionReader(world, outputs);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => new PanelInstance(template, owner).Evaluate(reader))!;

            Assert.That(error.Message, Does.Contain("ore.total"), "the first missing pin is named; no silent default");
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

            Assert.That(setA.Get("ore.total").FloatValue, Is.EqualTo(100f));
            Assert.That(setB.Get("ore.total").FloatValue, Is.EqualTo(900f));
        }
    }
}
