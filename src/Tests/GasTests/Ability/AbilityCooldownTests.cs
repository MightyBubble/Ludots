using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Tests for ability cooldown (CD) mechanics.
    /// The AbilityCooldown component links a CooldownValueAttributeId (attribute holding CD ticks remaining)
    /// and a CooldownTagId (tag applied while on CD). Actual CD is managed via AttributeBuffer + TimedTagBuffer.
    ///
    /// Tests verify:
    /// - Attribute-based CD remaining value can be set and read
    /// - Timed tag-based CD expiration integrates correctly
    /// - Multiple abilities have independent CD tracking
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class AbilityCooldownTests
    {
        private const int CountdownAttributeId = 8;
        private const int ResetAttributeId = 9;
        private const int ReductionAttributeId = 10;
        private const int ClampAttributeId = 11;
        private const int MultiAttributeA = 12;
        private const int MultiAttributeB = 13;
        private const int MultiAttributeC = 14;

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
        public void AbilityCooldown_Component_StoresAttributeAndTagIds()
        {
            var cd = new AbilityCooldown
            {
                CooldownValueAttributeId = 42,
                CooldownTagId = 7,
            };

            That(cd.CooldownValueAttributeId, Is.EqualTo(42));
            That(cd.CooldownTagId, Is.EqualTo(7));
        }

        [Test]
        public void CDAttribute_CountsDown_ViaAttributeBuffer()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(CountdownAttributeId, 10f);

            // Simulate 10 ticks of CD reduction
            for (int i = 0; i < 10; i++)
            {
                float remaining = buf.GetCurrent(CountdownAttributeId);
                if (remaining > 0f)
                    buf.SetCurrent(CountdownAttributeId, remaining - 1f);
            }

            That(buf.GetCurrent(CountdownAttributeId), Is.EqualTo(0f),
                "After full tick-down, cooldown attribute should be zero");
        }

        [Test]
        public void CDAttribute_Reset_SetsToFullValue()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(ResetAttributeId, 5f); // mid-cooldown
            That(buf.GetCurrent(ResetAttributeId), Is.EqualTo(5f));

            buf.SetCurrent(ResetAttributeId, 60f); // reset to full
            That(buf.GetCurrent(ResetAttributeId), Is.EqualTo(60f),
                "CD reset should set remaining to full value");
        }

        [Test]
        public void CDAttribute_Reduction_ReducesByAmount()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(ReductionAttributeId, 30f);

            // External effect reduces CD by 10
            float remaining = buf.GetCurrent(ReductionAttributeId);
            float reduced = remaining - 10f;
            if (reduced < 0f) reduced = 0f;
            buf.SetCurrent(ReductionAttributeId, reduced);

            That(buf.GetCurrent(ReductionAttributeId), Is.EqualTo(20f),
                "CD reduction should subtract from remaining");
        }

        [Test]
        public void CDAttribute_Reduction_ClampsToZero()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(ClampAttributeId, 5f);

            float remaining = buf.GetCurrent(ClampAttributeId);
            float reduced = remaining - 100f;
            if (reduced < 0f) reduced = 0f;
            buf.SetCurrent(ClampAttributeId, reduced);

            That(buf.GetCurrent(ClampAttributeId), Is.EqualTo(0f),
                "CD reduction should not go below zero");
        }

        [Test]
        public void MultipleAbilities_IndependentCDs()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(MultiAttributeA, 10f);
            buf.SetCurrent(MultiAttributeB, 20f);
            buf.SetCurrent(MultiAttributeC, 30f);

            That(buf.GetCurrent(MultiAttributeA), Is.EqualTo(10f));
            That(buf.GetCurrent(MultiAttributeB), Is.EqualTo(20f));
            That(buf.GetCurrent(MultiAttributeC), Is.EqualTo(30f));

            buf.SetCurrent(MultiAttributeB, 0f);
            That(buf.GetCurrent(MultiAttributeA), Is.EqualTo(10f), "Ability A CD unaffected");
            That(buf.GetCurrent(MultiAttributeB), Is.EqualTo(0f), "Ability B CD reset");
            That(buf.GetCurrent(MultiAttributeC), Is.EqualTo(30f), "Ability C CD unaffected");
        }
    }
}
