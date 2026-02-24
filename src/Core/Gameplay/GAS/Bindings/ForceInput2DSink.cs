using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    /// <summary>
    /// GAS → Physics2D 力输入绑定。
    /// 属性值 (float) → Fix64Vec2 转换在此边界层完成。
    /// </summary>
    public sealed class ForceInput2DSink : IAttributeSink
    {
        private readonly QueryDescription _query = new QueryDescription().WithAll<AttributeBuffer, ForceInput2D>();

        public void Apply(World world, AttributeBindingEntry[] entries, int start, int count)
        {
            bool resetX = false;
            bool resetY = false;
            for (int i = 0; i < count; i++)
            {
                var e = entries[start + i];
                if (e.ResetPolicy != AttributeBindingResetPolicy.ResetToZeroPerLogicFrame) continue;
                if (e.Channel == 0) resetX = true;
                if (e.Channel == 1) resetY = true;
            }

            var job = new ApplyJob
            {
                Entries = entries,
                Start = start,
                Count = count,
                ResetX = resetX,
                ResetY = resetY
            };
            world.InlineEntityQuery<ApplyJob, AttributeBuffer, ForceInput2D>(in _query, ref job);
        }

        private struct ApplyJob : IForEachWithEntity<AttributeBuffer, ForceInput2D>
        {
            public AttributeBindingEntry[] Entries;
            public int Start;
            public int Count;
            public bool ResetX;
            public bool ResetY;

            public void Update(Entity entity, ref AttributeBuffer attr, ref ForceInput2D force)
            {
                // Fix64Vec2 是 readonly struct，需要拆分为分量操作
                Fix64 vx = force.Force.X;
                Fix64 vy = force.Force.Y;
                if (ResetX) vx = Fix64.Zero;
                if (ResetY) vy = Fix64.Zero;

                for (int i = 0; i < Count; i++)
                {
                    var b = Entries[Start + i];
                    // 属性值从 float 转换为 Fix64（GAS → Physics 边界）
                    Fix64 value = Fix64.FromFloat(attr.GetCurrent(b.AttributeId) * b.Scale);
                    switch (b.Channel)
                    {
                        case 0:
                            vx = Apply(vx, value, b.Mode);
                            break;
                        case 1:
                            vy = Apply(vy, value, b.Mode);
                            break;
                    }
                }

                force.Force = new Fix64Vec2(vx, vy);
            }

            private static Fix64 Apply(Fix64 current, Fix64 value, AttributeBindingMode mode)
            {
                return mode == AttributeBindingMode.Override ? value : current + value;
            }
        }
    }
}
