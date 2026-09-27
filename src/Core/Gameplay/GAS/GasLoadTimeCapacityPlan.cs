using System;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// RFC-0067 装载期容量计划：本局属性槽与标签 id 空间的唯一容量真相。
    /// Freeze 只发生在装载登记全部完成之后（GameEngine.InitializeCoreSystems 尾部，
    /// 与 AttributeRegistry.Freeze 同窗口）；对局内只读，扩容窗口仅此一次。
    /// P0 阶段存储仍是内嵌定长（AttributeBuffer 64 槽 / GameplayTagContainer 256 位），
    /// 计划先记录需求并做天花板与物理上限校验；P1/P2 起存储改为按本计划分配。
    /// </summary>
    public sealed class GasLoadTimeCapacityPlan
    {
        public const int AbsoluteMaxAttributeSlots = 1024;
        public const int AbsoluteMaxTagIdSpace = 4096;

        public int AttributeSlotCount { get; private set; }
        public int TagIdSpace { get; private set; }
        public int TagWordCount { get; private set; }
        public bool IsFrozen { get; private set; }

        public static GasLoadTimeCapacityPlan Freeze(
            int registeredAttributes,
            int registeredTags,
            int physicalAttributeSlots,
            int physicalTagIdSpace)
        {
            if (registeredAttributes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(registeredAttributes));
            }

            if (registeredTags < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(registeredTags));
            }

            if (registeredAttributes > AbsoluteMaxAttributeSlots)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.AttributeCeiling: 本局登记属性 {registeredAttributes} 种，超过绝对天花板 {AbsoluteMaxAttributeSlots}。这是内容种类膨胀，不是运行时扩容失败；削减属性种类或抬升天花板（RFC-0067 §3.1）。");
            }

            int requiredTagIdSpace = registeredTags + 1;
            if (requiredTagIdSpace > AbsoluteMaxTagIdSpace)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.TagCeiling: 本局登记标签 {registeredTags} 种（含保留 id 0 共需 {requiredTagIdSpace} 位），超过绝对天花板 {AbsoluteMaxTagIdSpace}。这是内容种类膨胀，不是运行时扩容失败；削减标签种类或抬升天花板（RFC-0067 §3.1）。");
            }

            if (registeredAttributes > physicalAttributeSlots)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.AttributeSlotsExceeded: 本局登记属性 {registeredAttributes} 种，超过本局实现上限 {physicalAttributeSlots} 槽。P1 前属性存储仍为内嵌定容（RFC-0067 §3.3）；削减本局属性种类，或等待 P1 装载期定容列存落地。");
            }

            if (requiredTagIdSpace > physicalTagIdSpace)
            {
                throw new InvalidOperationException(
                    $"GAS.CAPACITY.ERR.TagBitsExceeded: 本局登记标签 {registeredTags} 种（含保留 id 0 共需 {requiredTagIdSpace} 位），超过本局实现上限 {physicalTagIdSpace} 位。P2 前标签存储仍为内嵌定容（RFC-0067 §3.3）；削减本局标签种类，或等待 P2 装载期定容位列落地。");
            }

            int tagWordCount = (requiredTagIdSpace + 63) / 64;
            return new GasLoadTimeCapacityPlan
            {
                AttributeSlotCount = registeredAttributes,
                TagIdSpace = tagWordCount * 64,
                TagWordCount = tagWordCount,
                IsFrozen = true,
            };
        }

        /// <summary>冻结后再次 Freeze 即契约违规（唯一扩容窗口）。</summary>
        public void VerifyFrozen()
        {
            if (!IsFrozen)
            {
                throw new InvalidOperationException(
                    "GAS.CAPACITY.ERR.NotFrozen: 容量计划未冻结——装载序列未走到 GasLoadTimeCapacityPlan.Freeze 就有消费方读取容量。");
            }
        }
    }
}
