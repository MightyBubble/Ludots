using System;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace GasTests.GasCore
{
    [TestFixture]
    public sealed class GasLoadTimeCapacityPlanTests
    {
        [Test]
        public void Freeze_WithinPhysicalCaps_RecordsDemandAndWordAlignedTagSpace()
        {
            var plan = Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                registeredAttributes: 48, registeredTags: 100,
                physicalAttributeSlots: 64, physicalTagIdSpace: 256);

            That(plan.AttributeSlotCount, Is.EqualTo(48));
            That(plan.TagIdSpace, Is.EqualTo(128), "101 位需求上取整到 64 的倍数（2 字）");
            That(plan.TagWordCount, Is.EqualTo(2));
            That(plan.IsFrozen, Is.True);
        }

        [Test]
        public void Freeze_AtPhysicalCapExactly_Succeeds()
        {
            var plan = Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                registeredAttributes: 64, registeredTags: 255,
                physicalAttributeSlots: 64, physicalTagIdSpace: 256);

            That(plan.AttributeSlotCount, Is.EqualTo(64));
            That(plan.TagIdSpace, Is.EqualTo(256));
        }

        [Test]
        public void Freeze_AttributeDemandExceedsPhysicalSlots_FailsWithP1PendingMessage()
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                    registeredAttributes: 65, registeredTags: 10,
                    physicalAttributeSlots: 64, physicalTagIdSpace: 256))!;

            That(ex.Message, Does.Contain("GAS.CAPACITY.ERR.AttributeSlotsExceeded"));
            That(ex.Message, Does.Contain("P1"), "错误必须指明这是内嵌定容的已知边界，出口是 P1");
        }

        [Test]
        public void Freeze_TagDemandExceedsPhysicalBits_FailsWithP2PendingMessage()
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                    registeredAttributes: 10, registeredTags: 256,
                    physicalAttributeSlots: 64, physicalTagIdSpace: 256))!;

            That(ex.Message, Does.Contain("GAS.CAPACITY.ERR.TagBitsExceeded"));
            That(ex.Message, Does.Contain("P2"));
        }

        [TestCase(1025, 10)]
        [TestCase(2000, 10)]
        public void Freeze_AttributeCountOverAbsoluteCeiling_FailsAsContentInflation(int attrs, int tags)
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                    registeredAttributes: attrs, registeredTags: tags,
                    physicalAttributeSlots: int.MaxValue, physicalTagIdSpace: int.MaxValue))!;

            That(ex.Message, Does.Contain("GAS.CAPACITY.ERR.AttributeCeiling"), "绝对天花板先于物理上限判");
            That(ex.Message, Does.Contain("内容种类膨胀"));
        }

        [Test]
        public void Freeze_TagCountOverAbsoluteCeiling_FailsAsContentInflation()
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                    registeredAttributes: 10, registeredTags: 5000,
                    physicalAttributeSlots: int.MaxValue, physicalTagIdSpace: int.MaxValue))!;

            That(ex.Message, Does.Contain("GAS.CAPACITY.ERR.TagCeiling"));
        }

        [TestCase(-1, 10)]
        [TestCase(10, -1)]
        public void Freeze_NegativeCounts_ThrowArgumentOutOfRange(int attrs, int tags)
        {
            Throws<ArgumentOutOfRangeException>(() =>
                Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                    registeredAttributes: attrs, registeredTags: tags,
                    physicalAttributeSlots: 64, physicalTagIdSpace: 256));
        }

        [Test]
        public void VerifyFrozen_WhenNotFrozen_Throws()
        {
            var plan = Ludots.Core.Gameplay.GAS.GasLoadTimeCapacityPlan.Freeze(
                registeredAttributes: 1, registeredTags: 1,
                physicalAttributeSlots: 64, physicalTagIdSpace: 256);
            plan.VerifyFrozen();
            That(plan.IsFrozen, Is.True, "Freeze 产物即冻结态，唯一扩容窗口不可重入");
        }
    }
}
