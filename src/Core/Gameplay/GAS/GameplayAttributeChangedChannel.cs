using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// 非结构化属性变更广播通道：GAS 写方 Mark（实体 → 64 位属性变更位图），
    /// 表现消费方（投影、清理）按 tick 读后 Clear，替代结构组件的每事件原型搬迁。
    /// 形态对齐 AttributeAggregateDirtyRegistry：不感知 World——实体存活与
    /// AttributeBuffer 齐备性由消费方惰性过滤；无引用计数，回滚语义由事务以
    /// （原位覆盖 / 摘除条目）显式补偿。Version 在任何可见状态变化时递增，
    /// 消费方以版本对比跳过无变化窗口的遍历。
    /// </summary>
    public sealed class GameplayAttributeChangedChannel
    {
        private Entity[] _entities;
        private ulong[] _bits;
        private int _count;
        private readonly Dictionary<Entity, int> _index;

        public GameplayAttributeChangedChannel(int initialCapacity = 0)
        {
            int capacity = initialCapacity > 0 ? initialCapacity : 0;
            _entities = new Entity[capacity];
            _bits = new ulong[capacity];
            _index = new Dictionary<Entity, int>(capacity);
        }

        /// <summary>消费方门控用：自通道创建以来的可见状态变化计数。</summary>
        public int Version { get; private set; }

        public int Count => _count;

        public ReadOnlySpan<Entity> Entities => new(_entities, 0, _count);

        public ReadOnlySpan<ulong> Bits => new(_bits, 0, _count);

        public bool Contains(Entity entity) => _index.ContainsKey(entity);

        public bool TryGetBits(Entity entity, out ulong bits)
        {
            if (_index.TryGetValue(entity, out int row))
            {
                bits = _bits[row];
                return true;
            }

            bits = 0UL;
            return false;
        }

        /// <summary>标一个属性变更位；越界属性 id 静默忽略（对齐旧组件 Mark 的防御边界）。</summary>
        public bool Mark(Entity entity, int attributeId)
        {
            if ((uint)attributeId >= (uint)AttributeBuffer.MAX_ATTRS)
            {
                return false;
            }

            return MarkMask(entity, 1UL << attributeId);
        }

        public bool MarkMask(Entity entity, ulong mask)
        {
            if (mask == 0UL)
            {
                return false;
            }

            if (_index.TryGetValue(entity, out int row))
            {
                ulong merged = _bits[row] | mask;
                if (merged == _bits[row])
                {
                    return false;
                }

                _bits[row] = merged;
                Version++;
                return true;
            }

            if (_count == _entities.Length)
            {
                int next = _entities.Length == 0 ? 64 : _entities.Length * 2;
                Array.Resize(ref _entities, next);
                Array.Resize(ref _bits, next);
            }

            _index[entity] = _count;
            _entities[_count] = entity;
            _bits[_count] = mask;
            _count++;
            Version++;
            return true;
        }

        /// <summary>事务回滚：把实体位图恢复为进入事务前的原值（原位覆盖，不改行序）。</summary>
        public void SetBits(Entity entity, ulong bits)
        {
            if (!_index.TryGetValue(entity, out int row))
            {
                if (bits == 0UL)
                {
                    return;
                }

                MarkMask(entity, bits);
                return;
            }

            if (_bits[row] == bits)
            {
                return;
            }

            _bits[row] = bits;
            Version++;
        }

        /// <summary>事务回滚：摘除本事务新增的条目（swap-remove），既有条目不受影响。</summary>
        public bool Remove(Entity entity)
        {
            if (!_index.Remove(entity, out int row))
            {
                return false;
            }

            int last = _count - 1;
            if (row != last)
            {
                Entity moved = _entities[last];
                _entities[row] = moved;
                _bits[row] = _bits[last];
                _index[moved] = row;
            }

            _entities[last] = default;
            _bits[last] = 0UL;
            _count--;
            Version++;
            return true;
        }

        /// <summary>表现清理尾部整批清空（对齐旧 ClearPresentationFlagsSystem 的整 tick 清除合同）。</summary>
        public void Clear()
        {
            if (_count == 0)
            {
                return;
            }

            Array.Clear(_entities, 0, _count);
            Array.Clear(_bits, 0, _count);
            _index.Clear();
            _count = 0;
            Version++;
        }
    }
}
