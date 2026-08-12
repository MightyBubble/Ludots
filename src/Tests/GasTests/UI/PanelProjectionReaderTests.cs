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
    public sealed class PanelProjectionReaderTests
    {
        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
        }

        [Test]
        public void ResolveFloat_SingleAttribute_ReadsCurrent()
        {
            int attrId = AttributeRegistry.Register("tests.panel.hp");
            using World world = World.Create();
            Entity owner = world.Create();
            world.Add(owner, AttributeBuffer.CreateAttached());
            ref AttributeBuffer buffer = ref world.Get<AttributeBuffer>(owner);
            buffer.SetBase(attrId, 77f);

            var reader = new PanelProjectionReader(world);
            var binding = new PanelVariableBinding(
                "hp",
                PanelBindingSourceKind.SingleAttribute,
                attributeId: "tests.panel.hp",
                graphOutputKey: null);

            Assert.That(reader.ResolveFloat(owner, in binding), Is.EqualTo(77f));
        }

        [Test]
        public void ResolveFloat_AggregateProjection_ReadsSummaryFloat()
        {
            using World world = World.Create();
            Entity owner = world.Create();
            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);
            outputs.SetFloat(owner, "panel.player.resource.ore.total", 1200f);

            var reader = new PanelProjectionReader(world, outputs);
            var binding = new PanelVariableBinding(
                "oreTotal",
                PanelBindingSourceKind.AggregateProjection,
                attributeId: null,
                graphOutputKey: "panel.player.resource.ore.total");

            Assert.That(reader.ResolveFloat(owner, in binding), Is.EqualTo(1200f));
        }

        [Test]
        public void ResolveFloat_MissingSummaryKey_Throws()
        {
            using World world = World.Create();
            Entity owner = world.Create();
            var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);
            var reader = new PanelProjectionReader(world, outputs);
            var binding = new PanelVariableBinding(
                "oreTotal",
                PanelBindingSourceKind.GraphOutput,
                attributeId: null,
                graphOutputKey: "panel.player.resource.ore.total");

            Assert.That(
                () => reader.ResolveFloat(owner, in binding),
                Throws.InvalidOperationException.With.Message.Contains("Silent zero is forbidden"));
        }

        [Test]
        public void ResolveFloat_MissingAttribute_Throws()
        {
            AttributeRegistry.Register("tests.panel.hp");
            using World world = World.Create();
            Entity owner = world.Create();
            world.Add(owner, AttributeBuffer.CreateAttached());

            var reader = new PanelProjectionReader(world);
            var binding = new PanelVariableBinding(
                "hp",
                PanelBindingSourceKind.SingleAttribute,
                attributeId: "tests.panel.hp",
                graphOutputKey: null);

            Assert.That(
                () => reader.ResolveFloat(owner, in binding),
                Throws.InvalidOperationException.With.Message.Contains("not defined"));
        }

        [Test]
        public void Binding_RejectsConflictingRefs()
        {
            Assert.That(
                () => new PanelVariableBinding(
                    "hp",
                    PanelBindingSourceKind.SingleAttribute,
                    attributeId: "tests.panel.hp",
                    graphOutputKey: "panel.x"),
                Throws.ArgumentException);
        }
    }
}
