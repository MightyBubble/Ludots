using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// 属性读路由（RFC-0067 P1）：槽位 [0,64) 读实体内嵌镜像，[64, Plan) 读世界列存。
    /// 高槽位且列存未绑定时失败关闭——能登记出高 id 就必须有列存。
    /// </summary>
    public static class AttributeReads
    {
        public static float Current(World world, Entity entity, int attributeId)
        {
            if ((uint)attributeId < (uint)AttributeBuffer.MAX_ATTRS)
            {
                return world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
            }

            WorldAttributeStore store = WorldAttributeStoreAmbient.Current
                ?? throw HighLaneUnavailable();
            return store.TryGetRow(entity, out int row)
                ? store.GetCurrent(row, attributeId)
                : 0f;
        }

        public static float Base(World world, Entity entity, int attributeId)
        {
            if ((uint)attributeId < (uint)AttributeBuffer.MAX_ATTRS)
            {
                return world.Get<AttributeBuffer>(entity).GetBase(attributeId);
            }

            WorldAttributeStore store = WorldAttributeStoreAmbient.Current
                ?? throw HighLaneUnavailable();
            return store.TryGetRow(entity, out int row)
                ? store.GetBase(row, attributeId)
                : 0f;
        }

        public static float Cap(World world, Entity entity, int attributeId)
        {
            if ((uint)attributeId < (uint)AttributeBuffer.MAX_ATTRS)
            {
                return world.Get<AttributeBuffer>(entity).GetCap(attributeId);
            }

            WorldAttributeStore store = WorldAttributeStoreAmbient.Current
                ?? throw HighLaneUnavailable();
            return store.TryGetRow(entity, out int row)
                ? store.GetCap(row, attributeId)
                : 0f;
        }

        private static InvalidOperationException HighLaneUnavailable()
        {
            return new InvalidOperationException(
                "GAS.CAPACITY.ERR.HighLaneUnavailable: attributeId ≥ 64 需要世界列存（RFC-0067 P1），但 WorldAttributeStore 未绑定。");
        }
    }
}
