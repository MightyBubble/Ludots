using System;
using Ludots.Core.Gameplay.GAS.Capacity;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Arch.Core;
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
            GasLoadTimeCapacitySession.EnsureLegacyPlanAndStore();
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
        public void Session_FreezeFromRegistries_UsesDenseCounts_AndAllowsAttributeSlotsAboveLegacy64()
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
        public void Session_Freeze_AllowsAttributeSlotsAbove64_ButRejectsTagLegacyCeiling()
        {
            var attrsOk = GasLoadTimeCapacityPlan.FromRegisteredCounts(70, 10);
            That(GasLoadTimeCapacitySession.Freeze(attrsOk).AttributeSlotCount, Is.EqualTo(70));

            GasLoadTimeCapacitySession.ClearForTests();
            var tagsTooWide = GasLoadTimeCapacityPlan.FromRegisteredCounts(8, 300);
            var ex = Throws<InvalidOperationException>(() => GasLoadTimeCapacitySession.Freeze(tagsTooWide));
            That(ex!.Message, Does.Contain("legacy GameplayTagContainer"));
        }

        [Test]
        public void WorldColumnStore_AllocatesSoA_SizedByPlan_AndRefusesHotPathGrow()
        {
            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(8, 10);
            using var store = new GasWorldColumnStore(plan, initialEntityRowCapacity: 2);

            That(store.AllocateEntityRow(), Is.EqualTo(1));
            That(store.AllocateEntityRow(), Is.EqualTo(2));
            Throws<InvalidOperationException>(() => store.AllocateEntityRow());

            store.EnsureEntityRowCapacity(4);
            That(store.AllocateEntityRow(), Is.EqualTo(3));
            That(store.AttributeBaseValues!.Length, Is.EqualTo((4 + 1) * 8));
            That(store.TagBitWords!.Length, Is.EqualTo((4 + 1) * plan.TagUlongWordCount));
        }

        [Test]
        public void AttributeStore_SupportsMoreThan64_AndOobThrows()
        {
            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(80, 10);
            GasLoadTimeCapacitySession.Freeze(plan);
            GasLoadTimeCapacitySession.EnsureStore(plan, entityRowCapacity: 16);

            for (int i = 0; i < 80; i++)
            {
                AttributeRegistry.Register($"attr.p1.{i}");
            }

            var buffer = AttributeBuffer.CreateAttached();
            buffer.SetBase(70, 12.5f);
            That(buffer.GetCurrent(70), Is.EqualTo(12.5f));
            That(buffer.HasAttribute(70), Is.True);
            Throws<ArgumentOutOfRangeException>(() => buffer.GetCurrent(80));
            Throws<ArgumentOutOfRangeException>(() => buffer.SetCurrent(80, 1f));

            AttributeBuffer.Release(ref buffer);
        }

        [Test]
        public void AttributeStore_FreezeThenRegisterBeyondPlan_FailsClosed()
        {
            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(2, 1);
            GasLoadTimeCapacitySession.Freeze(plan);
            AttributeRegistry.Register("a0");
            AttributeRegistry.Register("a1");
            var ex = Throws<InvalidOperationException>(() => AttributeRegistry.Register("a2"));
            That(ex!.Message, Does.Contain("frozen GasLoadTimeCapacityPlan"));
        }

        [Test]
        public void AttributeAggregator_WorksWithWorldStoreRows()
        {
            var plan = GasLoadTimeCapacityPlan.CreateLegacyEmbeddedBaseline();
            GasLoadTimeCapacitySession.Freeze(plan);
            GasLoadTimeCapacitySession.EnsureStore(plan, entityRowCapacity: 64);

            int health = AttributeRegistry.Register("Health.P1Agg");
            using var world = World.Create();
            var tagOps = new Ludots.Core.Gameplay.GAS.TagOps(
                new Ludots.Core.Gameplay.GAS.DirtyEntityQueue(Ludots.Core.Gameplay.GAS.GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new Ludots.Core.Gameplay.GAS.TagRuleRegistry());

            var entity = world.Create(
                AttributeBuffer.CreateAttached(),
                new ActiveEffectContainer(),
                new AttributeAggregateDirty(),
                new DirtyFlags());
            ref var attrs = ref world.Get<AttributeBuffer>(entity);
            attrs.SetBase(health, 100f);
            attrs.SetCurrent(health, 100f);

            var agg = new AttributeAggregatorSystem(world, tagOps: tagOps);
            agg.Update(0.016f);
            That(attrs.GetCurrent(health), Is.EqualTo(100f));
            That(world.Has<AttributeAggregateDirty>(entity), Is.False);

            GasAttributeRows.ReleaseIfPresent(world, entity);
        }

        [Test]
        public void EnsureEntityRowCapacity_ThrowsAfterGameplaySeal()
        {
            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(4, 1);
            using var store = new GasWorldColumnStore(plan, initialEntityRowCapacity: 2);
            store.SealGameplay();
            Throws<InvalidOperationException>(() => store.EnsureEntityRowCapacity(8));
        }

        [Test]
        public void ReleaseEntityRow_ReturnsToFreelist_WithoutGrowing()
        {
            var plan = GasLoadTimeCapacityPlan.FromRegisteredCounts(4, 1);
            using var store = new GasWorldColumnStore(plan, initialEntityRowCapacity: 2);
            int a = store.AllocateEntityRow();
            int b = store.AllocateEntityRow();
            store.ReleaseEntityRow(a);
            That(store.AllocateEntityRow(), Is.EqualTo(a));
            Throws<InvalidOperationException>(() =>
            {
                store.AllocateEntityRow();
                store.AllocateEntityRow();
            });
            That(b, Is.EqualTo(2));
        }
    }
}
