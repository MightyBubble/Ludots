using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// 非结构化的属性聚合脏注册表：写方 MarkDirty，聚合器整批 Drain 消费，
    /// 替代结构 tag 的 add/remove archetype 搬迁。不感知 World——实体存活与
    /// 组件齐备性由消费方惰性过滤；无引用计数，Unmark 语义对齐旧 tag 的
    /// 事务回滚（仅本事务新标脏的实体回滚为不脏）。
    /// </summary>
    public sealed class AttributeAggregateDirtyRegistry
    {
        public const string MissingRegistryError = "GAS.AGGREGATE.ERR.MissingDirtyRegistry";

        private readonly List<Entity> _pending;
        private readonly HashSet<Entity> _marked;

        public AttributeAggregateDirtyRegistry(int initialCapacity = 0)
        {
            _pending = new List<Entity>(initialCapacity);
            _marked = new HashSet<Entity>();
            if (initialCapacity > 0)
            {
                _marked.EnsureCapacity(initialCapacity);
            }
        }

        public int Count => _marked.Count;

        public bool Contains(Entity entity) => _marked.Contains(entity);

        /// <summary>返回 true 表示本次把实体从非脏翻为脏；事务据此区分“本事务新标脏”与既有脏。</summary>
        public bool MarkDirty(Entity entity)
        {
            if (!_marked.Add(entity))
            {
                return false;
            }

            _pending.Add(entity);
            return true;
        }

        /// <summary>把待聚合集搬入 destination（追加，调用方复用前自清）；两端列表预分配复用，稳态零分配。</summary>
        public void Drain(List<Entity> destination)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                destination.Add(_pending[i]);
            }

            _pending.Clear();
            _marked.Clear();
        }

        /// <summary>回滚专用：等价旧 tag 模型里“existed=false 才 Remove”的迁移补偿，不做来源计数。</summary>
        public void Unmark(Entity entity)
        {
            if (!_marked.Remove(entity))
            {
                return;
            }

            _pending.Remove(entity);
        }

        public void Clear()
        {
            _pending.Clear();
            _marked.Clear();
        }
    }
}
