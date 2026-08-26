using System;
using Ludots.Core.Fields;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class FieldLayerRegistryTests
    {
        [Test]
        public void RegistersSequentialIds_AndDuplicateKeyIsIdempotent()
        {
            var registry = new FieldLayerRegistry();
            FieldLayerId first = registry.Register(
                "layerX", FieldLayerKind.Scalar32, 100, 8, FieldLayerDefaultValue.Scalar32(1f), true, "test.writer", 0);
            FieldLayerId second = registry.Register(
                "layerY", FieldLayerKind.DiscreteId, 50, 4, FieldLayerDefaultValue.None, true, "test.writer", 256);

            Assert.That(first.Value, Is.EqualTo(1));
            Assert.That(second.Value, Is.EqualTo(2));
            Assert.That(registry.GetId("layerX"), Is.EqualTo(first));
            Assert.That(registry.GetId("layerY"), Is.EqualTo(second));

            FieldLayerId duplicate = registry.Register(
                "layerX", FieldLayerKind.Scalar32, 100, 8, FieldLayerDefaultValue.Scalar32(1f), true, "test.writer", 0);
            Assert.That(duplicate, Is.EqualTo(first), "re-registering a known key returns the existing id");
            Assert.That(registry.Count, Is.EqualTo(2));
        }

        [Test]
        public void DefinitionsCarrySemanticMetadata_IncludingMaxRegionIds()
        {
            var registry = new FieldLayerRegistry();
            FieldLayerId id = registry.Register(
                "layerX", FieldLayerKind.DiscreteId, 100, 8, FieldLayerDefaultValue.None, persistent: false, "test.writer", 128);

            Assert.That(registry.TryGet(id, out FieldLayerDefinition definition), Is.True);
            Assert.That(definition.Id, Is.EqualTo(id));
            Assert.That(definition.Key, Is.EqualTo("layerX"));
            Assert.That(definition.Kind, Is.EqualTo(FieldLayerKind.DiscreteId));
            Assert.That(definition.CellSizeCm, Is.EqualTo(100));
            Assert.That(definition.ChunkSizeCells, Is.EqualTo(8));
            Assert.That(definition.Persistent, Is.False);
            Assert.That(definition.WriterDomain, Is.EqualTo("test.writer"));
            Assert.That(definition.MaxRegionIds, Is.EqualTo(128));
        }

        [Test]
        public void UnregisteredKey_ResolvesToInvalidId_AndNoKeyMapsToZero()
        {
            var registry = new FieldLayerRegistry();
            Assert.That(registry.GetId("missing").Value, Is.EqualTo(0), "unregistered keys resolve to the invalid id");

            FieldLayerId registered = registry.Register(
                "layerX", FieldLayerKind.Scalar32, 100, 8, FieldLayerDefaultValue.Scalar32(0f), true, "test.writer", 0);
            Assert.That(registered.Value, Is.GreaterThan(0), "registered keys never map to id 0");
            Assert.That(registry.TryGet(registry.GetId("missing"), out _), Is.False);
        }

        [Test]
        public void Freeze_BlocksFurtherRegistration_WithoutDirtyingTheTable()
        {
            var registry = new FieldLayerRegistry();
            FieldLayerId existing = registry.Register(
                "layerX", FieldLayerKind.Scalar32, 100, 8, FieldLayerDefaultValue.Scalar32(0f), true, "test.writer", 0);
            registry.Freeze();

            Assert.Throws<InvalidOperationException>(() => registry.Register(
                "layerY", FieldLayerKind.Scalar32, 100, 8, FieldLayerDefaultValue.Scalar32(0f), true, "test.writer", 0));
            Assert.That(registry.GetId("layerY").Value, Is.EqualTo(0), "the rejected key stays unregistered");
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.GetId("layerX"), Is.EqualTo(existing));
        }

        [Test]
        public void Get_ThrowsForUnregisteredId()
        {
            var registry = new FieldLayerRegistry();
            Assert.Throws<InvalidOperationException>(() => registry.Get(new FieldLayerId(1)));
        }
    }

    [TestFixture]
    public sealed class FieldRegionIdRegistryTests
    {
        [Test]
        public void RegistersFromOne_AndReservedZeroNeverMapsToAKey()
        {
            var registry = new RegionIdRegistry("layerX", maxRegionIds: 4);
            Assert.That(registry.Register("r1"), Is.EqualTo(1));
            Assert.That(registry.Register("r2"), Is.EqualTo(2));
            Assert.That(registry.Count, Is.EqualTo(2));
            Assert.That(registry.GetId("r1"), Is.EqualTo(1));

            Assert.That(registry.GetId("missing"), Is.EqualTo(0), "unknown keys resolve to the reserved no-region id");
            Assert.That(registry.GetName(0), Is.Empty, "id 0 has no backing key");
            Assert.That(registry.Contains("missing"), Is.False);
            Assert.That(registry.MaxRegionIds, Is.EqualTo(4));
        }

        [Test]
        public void DuplicateRegistration_IsIdempotent()
        {
            var registry = new RegionIdRegistry("layerX", maxRegionIds: 4);
            int first = registry.Register("r1");
            Assert.That(registry.Register("r1"), Is.EqualTo(first));
            Assert.That(registry.Count, Is.EqualTo(1), "idempotent re-registration must not consume capacity");
        }

        [Test]
        public void CapacityExhaustion_ThrowsAtomically()
        {
            var registry = new RegionIdRegistry("layerX", maxRegionIds: 2);
            registry.Register("r1");
            registry.Register("r2");

            var exception = Assert.Throws<InvalidOperationException>(() => registry.Register("r3"));
            Assert.That(exception!.Message, Does.Contain("layerX"), "error must name the layer");
            Assert.That(exception.Message, Does.Contain("2"), "error must state the capacity");
            Assert.That(registry.GetId("r3"), Is.EqualTo(0), "the rejected key stays unregistered");
            Assert.That(registry.Count, Is.EqualTo(2), "a rejected registration must not dirty the table");
        }

        [Test]
        public void Freeze_BlocksNewRegistrations_ButIdempotentLookupStillPasses()
        {
            var registry = new RegionIdRegistry("layerX", maxRegionIds: 4);
            int first = registry.Register("r1");
            registry.Freeze();

            Assert.Throws<InvalidOperationException>(() => registry.Register("r2"));
            Assert.That(registry.GetId("r2"), Is.EqualTo(0));
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.Register("r1"), Is.EqualTo(first), "idempotent passthrough still works after freeze");
        }

        [Test]
        public void Constructor_RejectsNonPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RegionIdRegistry("layerX", maxRegionIds: 0));
        }
    }
}
