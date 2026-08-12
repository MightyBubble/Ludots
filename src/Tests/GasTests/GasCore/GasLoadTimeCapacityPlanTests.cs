using System;
using Ludots.Core.Gameplay.GAS.Capacity;
using Ludots.Core.Gameplay.GAS.Registry;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GasLoadTimeCapacityPlanTests
    {
        [SetUp]
        public void SetUp()
        {
            GasLoadTimeCapacitySession.ClearForTests();
            if (!AttributeRegistry.IsFrozen)
            {
                AttributeRegistry.Clear();
            }

            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GasLoadTimeCapacitySession.ClearForTests();
            if (!AttributeRegistry.IsFrozen)
            {
                AttributeRegistry.Clear();
            }

            TagRegistry.Clear();
        }

        [Test]
        public void FromRegisteredCounts_WordAlignsTagSpace_AndKeepsAttributeExact()
        {
            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(
                registeredAttributeCount: 70,
                registeredTagCount: 260);

            That(plan.AttributeSlotCount, Is.EqualTo(70));
            That(plan.TagIdSpace, Is.EqualTo(320));
            That(plan.TagUlongWordCount, Is.EqualTo(5));
            That(plan.MaxUsableTagId, Is.EqualTo(319));
        }

        [Test]
        public void FromRegisteredCounts_RejectsAbsoluteCeiling()
        {
            Throws<InvalidOperationException>(() =>
                GasLoadTimeCapacityPlan.FromRegisteredCounts(
                    GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots + 1,
                    registeredTagCount: 1));

            Throws<InvalidOperationException>(() =>
                GasLoadTimeCapacityPlan.FromRegisteredCounts(
                    registeredAttributeCount: 1,
                    registeredTagCount: GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace));
        }

        [Test]
        public void Session_FreezeFromRegistries_UsesDenseCounts_AndLegacyBridge()
        {
            AttributeRegistry.Register("cap.a");
            AttributeRegistry.Register("cap.b");
            TagRegistry.Register("Tag.Cap.A");
            TagRegistry.Register("Tag.Cap.B");
            TagRegistry.Register("Tag.Cap.C");

            var plan = GasLoadTimeCapacitySession.FreezeFromRegistries();

            That(GasLoadTimeCapacitySession.IsFrozen, Is.True);
            That(plan.RegisteredAttributeCount, Is.EqualTo(2));
            That(plan.RegisteredTagCount, Is.EqualTo(3));
            That(plan.AttributeSlotCount, Is.EqualTo(2));
            That(plan.TagIdSpace, Is.EqualTo(64));
            Throws<InvalidOperationException>(() => GasLoadTimeCapacitySession.FreezeFromRegistries());
        }

        [Test]
        public void Session_Freeze_RejectsPlanAboveLegacyEmbeddedCeiling()
        {
            var oversized = GasLoadTimeCapacityPlan.FromRegisteredCounts(65, 10);
            var ex = Throws<InvalidOperationException>(() => GasLoadTimeCapacitySession.Freeze(oversized));
            That(ex!.Message, Does.Contain("legacy AttributeBuffer"));
        }

        [Test]
        public void WorldColumnStore_AllocatesSoA_SizedByPlan_AndRefusesHotPathGrow()
        {
            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(8, 10);
            using var store = new GasWorldColumnStore(plan, initialEntityRowCapacity: 2);

            That(store.AllocateEntityRow(), Is.EqualTo(0));
            That(store.AllocateEntityRow(), Is.EqualTo(1));
            Throws<InvalidOperationException>(() => store.AllocateEntityRow());

            store.EnsureEntityRowCapacity(4);
            That(store.AllocateEntityRow(), Is.EqualTo(2));
            That(store.AttributeBaseValues!.Length, Is.EqualTo(4 * 8));
            That(store.TagBitWords!.Length, Is.EqualTo(4 * plan.TagUlongWordCount));
        }
    }
}
