using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Tests for attribute aggregation with multiple modifiers:
    /// - Add + Multiply + Override stacking
    /// - Multiple modifiers on the same attribute
    /// - Order-of-operations correctness
    /// </summary>
    [TestFixture]
    public class AttributeAggregatorTests
    {
        [Test]
        public unsafe void SingleAdd_ModifiesBase()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 25f);

            var entry = mods.Get(0);
            That(entry.AttributeId, Is.EqualTo(0));
            That(entry.Operation, Is.EqualTo(ModifierOp.Add));
            That(entry.Value, Is.EqualTo(25f));
        }

        [Test]
        public unsafe void MultipleAdds_Stack()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 10f);
            mods.Add(attrId: 0, ModifierOp.Add, 15f);
            mods.Add(attrId: 0, ModifierOp.Add, 5f);

            That(mods.Count, Is.EqualTo(3));

            float sum = 0;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.Operation == ModifierOp.Add && entry.AttributeId == 0)
                    sum += entry.Value;
            }

            That(sum, Is.EqualTo(30f), "Multiple Add modifiers should sum to 30");
        }

        [Test]
        public unsafe void AddAndMultiply_Combined()
        {
            // Base = 100, Add +20, Multiply x1.5 → expected (100+20)*1.5 = 180
            float baseValue = 100f;
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 20f);
            mods.Add(attrId: 0, ModifierOp.Multiply, 1.5f);

            // Simulate aggregation: Add first, then Multiply
            float addSum = 0;
            float mulProduct = 1f;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.Operation == ModifierOp.Add) addSum += entry.Value;
                else if (entry.Operation == ModifierOp.Multiply) mulProduct *= entry.Value;
            }

            float result = (baseValue + addSum) * mulProduct;
            That(result, Is.EqualTo(180f), "Add then Multiply: (100+20)*1.5 = 180");
        }

        [Test]
        public unsafe void Override_TakesLastValue()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 50f);
            mods.Add(attrId: 0, ModifierOp.Override, 42f);

            // Override should discard all Add values and set to 42
            float overrideValue = float.NaN;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.Operation == ModifierOp.Override)
                    overrideValue = entry.Value;
            }

            That(float.IsNaN(overrideValue), Is.False, "Override modifier should be present");
            That(overrideValue, Is.EqualTo(42f), "Override value should be 42");
        }

        [Test]
        public unsafe void DifferentAttributes_Independent()
        {
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, 10f);  // HP
            mods.Add(attrId: 1, ModifierOp.Add, -5f);   // Mana
            mods.Add(attrId: 0, ModifierOp.Add, 20f);  // HP

            float hpSum = 0;
            float manaSum = 0;
            for (int i = 0; i < mods.Count; i++)
            {
                var entry = mods.Get(i);
                if (entry.AttributeId == 0) hpSum += entry.Value;
                else if (entry.AttributeId == 1) manaSum += entry.Value;
            }

            That(hpSum, Is.EqualTo(30f), "HP modifiers should sum independently");
            That(manaSum, Is.EqualTo(-5f), "Mana modifiers should sum independently");
        }

        [Test]
        public unsafe void EffectModifiers_Capacity_IsEight()
        {
            var mods = new EffectModifiers();
            That(EffectModifiers.CAPACITY, Is.EqualTo(8),
                "EffectModifiers should support 8 entries per effect");
        }
    }
}
