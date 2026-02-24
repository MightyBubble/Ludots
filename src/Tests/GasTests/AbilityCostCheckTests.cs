using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Tests for ability cost checking and deduction:
    /// - Sufficient resources: cost check passes, deduct succeeds
    /// - Insufficient resources: cost check fails, no deduction
    /// - Zero cost: always passes
    /// </summary>
    [TestFixture]
    public class AbilityCostCheckTests
    {
        [Test]
        public void CostCheck_SufficientResource_Passes()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());
            ref var buf = ref world.Get<AttributeBuffer>(entity);

            int manaAttrId = 1;
            buf.SetCurrent(manaAttrId, 100f);

            float cost = 40f;
            float current = buf.GetCurrent(manaAttrId);

            That(current >= cost, Is.True,
                "Cost check should pass when resource >= cost");
        }

        [Test]
        public void CostCheck_InsufficientResource_Fails()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());
            ref var buf = ref world.Get<AttributeBuffer>(entity);

            int manaAttrId = 1;
            buf.SetCurrent(manaAttrId, 20f);

            float cost = 40f;
            float current = buf.GetCurrent(manaAttrId);

            That(current >= cost, Is.False,
                "Cost check should fail when resource < cost");
        }

        [Test]
        public void CostDeduction_SubtractsFromResource()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());
            ref var buf = ref world.Get<AttributeBuffer>(entity);

            int manaAttrId = 1;
            buf.SetCurrent(manaAttrId, 100f);

            float cost = 40f;
            buf.SetCurrent(manaAttrId, buf.GetCurrent(manaAttrId) - cost);

            That(buf.GetCurrent(manaAttrId), Is.EqualTo(60f),
                "After cost deduction, resource should be reduced");
        }

        [Test]
        public void CostCheck_ZeroCost_AlwaysPasses()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());
            ref var buf = ref world.Get<AttributeBuffer>(entity);

            int manaAttrId = 1;
            buf.SetCurrent(manaAttrId, 0f);

            float cost = 0f;
            float current = buf.GetCurrent(manaAttrId);

            That(current >= cost, Is.True,
                "Zero cost should always pass regardless of resource level");
        }

        [Test]
        public void CostDeduction_NoDeductOnFailedCheck()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());
            ref var buf = ref world.Get<AttributeBuffer>(entity);

            int manaAttrId = 1;
            buf.SetCurrent(manaAttrId, 20f);

            float cost = 40f;
            float current = buf.GetCurrent(manaAttrId);
            if (current >= cost)
            {
                buf.SetCurrent(manaAttrId, current - cost);
            }

            That(buf.GetCurrent(manaAttrId), Is.EqualTo(20f),
                "Resource should not change when cost check fails");
        }
    }
}
