using System.Runtime.CompilerServices;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Static utility for applying effect modifiers to attribute buffers.
    /// Extracted from <see cref="EffectModifiers.ApplyTo"/> to follow ECS best practice:
    /// components are data containers, systems/utilities own all behavior.
    /// </summary>
    public static class EffectModifierOps
    {
        /// <summary>
        /// Apply all modifiers in the set to the target AttributeBuffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Apply(in EffectModifiers modifiers, ref AttributeBuffer buffer)
        {
            ApplyInternal(in modifiers, ref buffer, clampToCapacity: true);
        }

        /// <summary>
        /// Apply aggregated modifiers while bypassing ClampCurrentToBase.
        /// Used by attribute recomputation to rebuild dynamic caps before persistent current values are restored.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyAggregated(in EffectModifiers modifiers, ref AttributeBuffer buffer)
        {
            ApplyInternal(in modifiers, ref buffer, clampToCapacity: false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyInternal(in EffectModifiers modifiers, ref AttributeBuffer buffer, bool clampToCapacity)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                var mod = modifiers.Get(i);
                if ((uint)mod.AttributeId >= (uint)AttributeBuffer.MAX_ATTRS)
                {
                    continue; // 高槽位（≥64）只存在于 WorldAttributeStore，由调用方的高车道处理（RFC-0067 P1）
                }

                float current = buffer.GetCurrent(mod.AttributeId);

                switch (mod.Operation)
                {
                    case ModifierOp.Add:
                        if (clampToCapacity)
                        {
                            buffer.SetCurrent(mod.AttributeId, current + mod.Value);
                        }
                        else
                        {
                            buffer.SetAggregatedCurrent(mod.AttributeId, current + mod.Value);
                        }
                        break;
                    case ModifierOp.Multiply:
                        if (clampToCapacity)
                        {
                            buffer.SetCurrent(mod.AttributeId, current * mod.Value);
                        }
                        else
                        {
                            buffer.SetAggregatedCurrent(mod.AttributeId, current * mod.Value);
                        }
                        break;
                    case ModifierOp.Override:
                        if (clampToCapacity)
                        {
                            buffer.SetCurrent(mod.AttributeId, mod.Value);
                        }
                        else
                        {
                            buffer.SetAggregatedCurrent(mod.AttributeId, mod.Value);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 聚合车道的高槽位（≥64）应用：直接写列存，绕过 ClampCurrentToBase
        ///（与 <see cref="ApplyAggregated"/> 同语义；持久 current 恢复由聚合器统一做）。
        /// </summary>
        public static void ApplyAggregatedHigh(
            in EffectModifiers modifiers,
            WorldAttributeStore store,
            int row)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                var mod = modifiers.Get(i);
                if (mod.AttributeId < AttributeBuffer.MAX_ATTRS)
                {
                    continue;
                }

                float current = store.GetCurrent(row, mod.AttributeId);
                float value = mod.Operation switch
                {
                    ModifierOp.Add => current + mod.Value,
                    ModifierOp.Multiply => current * mod.Value,
                    _ => mod.Value,
                };
                store.SetCurrentRaw(row, mod.AttributeId, value);
            }
        }
    }
}
