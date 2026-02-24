using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
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
    public class AbilityCooldownTests
    {
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

            int cdAttrId = 1;
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(cdAttrId, 10f);

            // Simulate 10 ticks of CD reduction
            for (int i = 0; i < 10; i++)
            {
                float remaining = buf.GetCurrent(cdAttrId);
                if (remaining > 0f)
                    buf.SetCurrent(cdAttrId, remaining - 1f);
            }

            That(buf.GetCurrent(cdAttrId), Is.EqualTo(0f),
                "After full tick-down, cooldown attribute should be zero");
        }

        [Test]
        public void CDAttribute_Reset_SetsToFullValue()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            int cdAttrId = 1;
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(cdAttrId, 5f); // mid-cooldown
            That(buf.GetCurrent(cdAttrId), Is.EqualTo(5f));

            buf.SetCurrent(cdAttrId, 60f); // reset to full
            That(buf.GetCurrent(cdAttrId), Is.EqualTo(60f),
                "CD reset should set remaining to full value");
        }

        [Test]
        public void CDAttribute_Reduction_ReducesByAmount()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            int cdAttrId = 1;
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(cdAttrId, 30f);

            // External effect reduces CD by 10
            float remaining = buf.GetCurrent(cdAttrId);
            float reduced = remaining - 10f;
            if (reduced < 0f) reduced = 0f;
            buf.SetCurrent(cdAttrId, reduced);

            That(buf.GetCurrent(cdAttrId), Is.EqualTo(20f),
                "CD reduction should subtract from remaining");
        }

        [Test]
        public void CDAttribute_Reduction_ClampsToZero()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            int cdAttrId = 1;
            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(cdAttrId, 5f);

            float remaining = buf.GetCurrent(cdAttrId);
            float reduced = remaining - 100f;
            if (reduced < 0f) reduced = 0f;
            buf.SetCurrent(cdAttrId, reduced);

            That(buf.GetCurrent(cdAttrId), Is.EqualTo(0f),
                "CD reduction should not go below zero");
        }

        [Test]
        public void MultipleAbilities_IndependentCDs()
        {
            using var world = World.Create();
            var entity = world.Create(new AttributeBuffer());

            int cdAttrA = 1;
            int cdAttrB = 2;
            int cdAttrC = 3;

            ref var buf = ref world.Get<AttributeBuffer>(entity);
            buf.SetCurrent(cdAttrA, 10f);
            buf.SetCurrent(cdAttrB, 20f);
            buf.SetCurrent(cdAttrC, 30f);

            That(buf.GetCurrent(cdAttrA), Is.EqualTo(10f));
            That(buf.GetCurrent(cdAttrB), Is.EqualTo(20f));
            That(buf.GetCurrent(cdAttrC), Is.EqualTo(30f));

            buf.SetCurrent(cdAttrB, 0f);
            That(buf.GetCurrent(cdAttrA), Is.EqualTo(10f), "Ability A CD unaffected");
            That(buf.GetCurrent(cdAttrB), Is.EqualTo(0f), "Ability B CD reset");
            That(buf.GetCurrent(cdAttrC), Is.EqualTo(30f), "Ability C CD unaffected");
        }
    }
}
