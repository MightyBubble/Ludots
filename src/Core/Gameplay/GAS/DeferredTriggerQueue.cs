using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// 延迟触发器队列（下一帧执行）
    /// 使用数组实现，避免GC分配
    /// </summary>
    public class DeferredTriggerQueue
    {
        private static readonly int Capacity = GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME;

        private AttributeChangedTrigger[] _attributeTriggers = new AttributeChangedTrigger[Capacity];
        private AttributeChangedTrigger[] _attributeOverflow = new AttributeChangedTrigger[Capacity];
        private TagChangedTrigger[] _tagTriggers = new TagChangedTrigger[Capacity];
        private TagChangedTrigger[] _tagOverflow = new TagChangedTrigger[Capacity];
        private TagCountChangedTrigger[] _tagCountTriggers = new TagCountChangedTrigger[Capacity];
        private TagCountChangedTrigger[] _tagCountOverflow = new TagCountChangedTrigger[Capacity];
        
        private int _attributeCount = 0;
        private int _tagCount = 0;
        private int _tagCountTriggerCount = 0;
        private int _attributeOverflowCount = 0;
        private int _tagOverflowCount = 0;
        private int _tagCountOverflowCount = 0;

        private bool _attributeBudgetFused;
        private bool _tagBudgetFused;
        private bool _tagCountBudgetFused;
        
        /// <summary>
        /// 入队属性变化触发器
        /// </summary>
        public void EnqueueAttributeChanged(AttributeChangedTrigger trigger)
        {
            if (_attributeCount >= GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME)
            {
                if (!_attributeBudgetFused)
                {
                    _attributeBudgetFused = true;
                    // Budget fused flag set above; no Console.WriteLine to avoid GC in hot path
                }
                if (_attributeOverflowCount >= GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME) return;
                _attributeOverflow[_attributeOverflowCount++] = trigger;
                return;
            }
            
            _attributeTriggers[_attributeCount++] = trigger;
        }
        
        /// <summary>
        /// 入队Tag变化触发器
        /// </summary>
        public void EnqueueTagChanged(TagChangedTrigger trigger)
        {
            if (_tagCount >= GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME)
            {
                if (!_tagBudgetFused)
                {
                    _tagBudgetFused = true;
                    // Budget fused flag set above; no Console.WriteLine to avoid GC in hot path
                }
                if (_tagOverflowCount >= GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME) return;
                _tagOverflow[_tagOverflowCount++] = trigger;
                return;
            }
            
            _tagTriggers[_tagCount++] = trigger;
        }
        
        /// <summary>
        /// 入队TagCount变化触发器
        /// </summary>
        public void EnqueueTagCountChanged(TagCountChangedTrigger trigger)
        {
            if (_tagCountTriggerCount >= GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME)
            {
                if (!_tagCountBudgetFused)
                {
                    _tagCountBudgetFused = true;
                    // Budget fused flag set above; no Console.WriteLine to avoid GC in hot path
                }
                if (_tagCountOverflowCount >= GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME) return;
                _tagCountOverflow[_tagCountOverflowCount++] = trigger;
                return;
            }
            
            _tagCountTriggers[_tagCountTriggerCount++] = trigger;
        }
        
        /// <summary>
        /// 清空队列
        /// </summary>
        public void Clear()
        {
            if (_attributeOverflowCount > 0)
            {
                System.Array.Copy(_attributeOverflow, 0, _attributeTriggers, 0, _attributeOverflowCount);
                _attributeCount = _attributeOverflowCount;
                _attributeOverflowCount = 0;
            }
            else
            {
                _attributeCount = 0;
            }

            if (_tagOverflowCount > 0)
            {
                System.Array.Copy(_tagOverflow, 0, _tagTriggers, 0, _tagOverflowCount);
                _tagCount = _tagOverflowCount;
                _tagOverflowCount = 0;
            }
            else
            {
                _tagCount = 0;
            }

            if (_tagCountOverflowCount > 0)
            {
                System.Array.Copy(_tagCountOverflow, 0, _tagCountTriggers, 0, _tagCountOverflowCount);
                _tagCountTriggerCount = _tagCountOverflowCount;
                _tagCountOverflowCount = 0;
            }
            else
            {
                _tagCountTriggerCount = 0;
            }
            _attributeBudgetFused = false;
            _tagBudgetFused = false;
            _tagCountBudgetFused = false;
        }
        
        /// <summary>
        /// Whether the attribute trigger budget was exceeded this frame.
        /// </summary>
        public bool AttributeBudgetFused => _attributeBudgetFused;

        /// <summary>
        /// Whether the tag trigger budget was exceeded this frame.
        /// </summary>
        public bool TagBudgetFused => _tagBudgetFused;

        /// <summary>
        /// Whether the tag-count trigger budget was exceeded this frame.
        /// </summary>
        public bool TagCountBudgetFused => _tagCountBudgetFused;

        /// <summary>
        /// 获取属性变化触发器数量
        /// </summary>
        public int AttributeTriggerCount => _attributeCount;
        
        /// <summary>
        /// 获取Tag变化触发器数量
        /// </summary>
        public int TagTriggerCount => _tagCount;
        
        /// <summary>
        /// 获取TagCount变化触发器数量
        /// </summary>
        public int TagCountTriggerCount => _tagCountTriggerCount;
        
        /// <summary>
        /// 获取属性变化触发器（只读访问）
        /// </summary>
        public AttributeChangedTrigger GetAttributeTrigger(int index)
        {
            if (index < 0 || index >= _attributeCount)
            {
                return default;
            }
            return _attributeTriggers[index];
        }
        
        /// <summary>
        /// 获取Tag变化触发器（只读访问）
        /// </summary>
        public TagChangedTrigger GetTagTrigger(int index)
        {
            if (index < 0 || index >= _tagCount)
            {
                return default;
            }
            return _tagTriggers[index];
        }
        
        /// <summary>
        /// 获取TagCount变化触发器（只读访问）
        /// </summary>
        public TagCountChangedTrigger GetTagCountTrigger(int index)
        {
            if (index < 0 || index >= _tagCountTriggerCount)
            {
                return default;
            }
            return _tagCountTriggers[index];
        }
    }
}
