using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
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
          "variables": [
            { "name": "ore.total", "kind": "Float",
              "source": { "sourceKind": "AggregateProjection", "graphOutputKey": "tests.panel.ore.total" } },
            { "name": "gas.total", "kind": "Float",
              "source": { "sourceKind": "AggregateProjection", "graphOutputKey": "tests.panel.gas.total" } }
          ],
          "binds": [
            { "control": "lbl.ore", "variable": "ore.total" },
            { "control": "lbl.gas", "variable": "gas.total" }
          ]
        }
        """;

        [Test]
        public void Load_ValidTemplate_CarriesVariablesAndBinds()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);

            Assert.That(template.Id, Is.EqualTo("tests.panel.resource_bar"));
            Assert.That(template.Variables, Has.Count.EqualTo(2));
            Assert.That(template.Binds, Has.Count.EqualTo(2));
            Assert.That(template.ResolveBinding("ore.total").GraphOutputKey, Is.EqualTo("tests.panel.ore.total"));
        }

        [Test]
        public void Load_MissingGraphOutputKey_FailsNamingVariable()
        {
            const string json = """
            {
              "id": "tests.panel.bad",
              "variables": [
                { "name": "ore", "kind": "Float", "source": { "sourceKind": "AggregateProjection" } }
              ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.Exception.With.Message.Contains("ore"));
        }

        [Test]
        public void Load_UnknownSourceKind_FailsNamingKind()
        {
            const string json = """
            {
              "id": "tests.panel.bad",
              "variables": [
                { "name": "ore", "kind": "Float", "source": { "sourceKind": "Magic" } }
              ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("Magic"));
        }

        [Test]
        public void Load_UnknownRootField_FailsNamingField()
        {
            const string json = """
            {
              "id": "tests.panel.bad",
              "frobnicate": true,
              "variables": [
                { "name": "ore", "kind": "Float", "source": { "sourceKind": "SingleAttribute", "attributeId": "a" } }
              ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("frobnicate"));
        }

        [Test]
        public void Load_BindToUndeclaredVariable_FailsNamingBind()
        {
            const string json = """
            {
              "id": "tests.panel.bad",
              "variables": [
                { "name": "ore", "kind": "Float", "source": { "sourceKind": "AggregateProjection", "graphOutputKey": "k" } }
              ],
              "binds": [ { "control": "lbl.x", "variable": "ghost" } ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.ArgumentException.With.Message.Contains("ghost"));
        }

        [Test]
        public void Evaluate_AggregateProjection_EqualsGraphOutput()
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
        public void Evaluate_MissingGraphOutput_FailsNoSilentZero()
        {
            PanelTemplate template = PanelTemplateLoader.Load(AggregateTemplateJson);
            using World world = World.Create();
            Entity owner = world.Create();

            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);

            var reader = new PanelProjectionReader(world, outputs);

            Assert.That(
                () => new PanelInstance(template, owner).Evaluate(reader),
                Throws.InvalidOperationException.With.Message.Contains("tests.panel.ore.total"));
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
        public void Evaluate_SingleAttribute_ReadsOwnerBuffer()
        {
            AttributeRegistry.Clear();
            int attrId = AttributeRegistry.Register("tests.panel.queue");
            try
            {
                const string json = """
                {
                  "id": "tests.panel.queue_card",
                  "variables": [
                    { "name": "queue.length", "kind": "Float",
                      "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.panel.queue" } }
                  ]
                }
                """;
                PanelTemplate template = PanelTemplateLoader.Load(json);
                using World world = World.Create();
                Entity owner = world.Create();
                world.Add(owner, new AttributeBuffer());
                world.Get<AttributeBuffer>(owner).SetBase(attrId, 3f);

                var reader = new PanelProjectionReader(world);
                PanelVariableSet result = new PanelInstance(template, owner).Evaluate(reader);

                Assert.That(result.Get("queue.length"), Is.EqualTo(3f));
            }
            finally
            {
                AttributeRegistry.Clear();
            }
        }

        [Test]
        public void Load_AttributeBase_WithoutAttributeId_FailsNamingVariable()
        {
            const string json = """
            {
              "id": "tests.panel.pool_card",
              "variables": [
                { "name": "pool.base", "kind": "Float", "source": { "sourceKind": "AttributeBase" } }
              ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.Exception.With.Message.Contains("pool.base"));
        }

        [Test]
        public void Load_AttributeBase_WithGraphOutputKey_FailsClosed()
        {
            const string json = """
            {
              "id": "tests.panel.pool_card",
              "variables": [
                { "name": "pool.base", "kind": "Float",
                  "source": { "sourceKind": "AttributeBase", "attributeId": "tests.panel.pool", "graphOutputKey": "k" } }
              ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.Exception.With.Message.Contains("graphOutputKey"));
        }

        [Test]
        public void Evaluate_AttributeBase_ReadsBaseWhileCurrentDrifts()
        {
            AttributeRegistry.Clear();
            int attrId = AttributeRegistry.Register("tests.panel.pool");
            try
            {
                const string json = """
                {
                  "id": "tests.panel.pool_card",
                  "variables": [
                    { "name": "pool.current", "kind": "Float", "realtime": true,
                      "source": { "sourceKind": "SingleAttribute", "attributeId": "tests.panel.pool" } },
                    { "name": "pool.base", "kind": "Float",
                      "source": { "sourceKind": "AttributeBase", "attributeId": "tests.panel.pool" } }
                  ]
                }
                """;
                PanelTemplate template = PanelTemplateLoader.Load(json);
                using World world = World.Create();
                Entity owner = world.Create();
                world.Add(owner, new AttributeBuffer());
                world.Get<AttributeBuffer>(owner).SetBase(attrId, 80f);
                world.Get<AttributeBuffer>(owner).SetCurrent(attrId, 55f);

                var reader = new PanelProjectionReader(world);
                PanelVariableSet result = new PanelInstance(template, owner).Evaluate(reader);

                Assert.That(result.Get("pool.current"), Is.EqualTo(55f));
                Assert.That(result.Get("pool.base"), Is.EqualTo(80f));
            }
            finally
            {
                AttributeRegistry.Clear();
            }
        }
    }
}
