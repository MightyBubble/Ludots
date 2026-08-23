using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public class DeferredTriggerQueue
    {
        public const string CapacityExceededError = "GAS.DEFERRED_TRIGGER.ERR.CapacityExceeded";

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

        public void EnqueueAttributeChanged(AttributeChangedTrigger trigger)
        {
            if (_attributeCount < Capacity)
            {
                _attributeTriggers[_attributeCount++] = trigger;
                return;
            }

            if (_attributeOverflowCount >= Capacity)
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: source=AttributeChanged, capacity={Capacity}, overflowCapacity={Capacity}, attributeId={trigger.AttributeId}.");
            }

            _attributeOverflow[_attributeOverflowCount++] = trigger;
        }

        public void EnqueueTagChanged(TagChangedTrigger trigger)
        {
            if (_tagCount < Capacity)
            {
                _tagTriggers[_tagCount++] = trigger;
                return;
            }

            if (_tagOverflowCount >= Capacity)
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: source=TagChanged, capacity={Capacity}, overflowCapacity={Capacity}, tagId={trigger.TagId}.");
            }

            _tagOverflow[_tagOverflowCount++] = trigger;
        }

        public void EnqueueTagCountChanged(TagCountChangedTrigger trigger)
        {
            if (_tagCountTriggerCount < Capacity)
            {
                _tagCountTriggers[_tagCountTriggerCount++] = trigger;
                return;
            }

            if (_tagCountOverflowCount >= Capacity)
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: source=TagCountChanged, capacity={Capacity}, overflowCapacity={Capacity}, tagId={trigger.TagId}.");
            }

            _tagCountOverflow[_tagCountOverflowCount++] = trigger;
        }

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
        }

        public int AttributeTriggerCount => _attributeCount;
        public int TagTriggerCount => _tagCount;
        public int TagCountTriggerCount => _tagCountTriggerCount;

        public AttributeChangedTrigger GetAttributeTrigger(int index)
        {
            if (index < 0 || index >= _attributeCount)
            {
                return default;
            }
            return _attributeTriggers[index];
        }

        public TagChangedTrigger GetTagTrigger(int index)
        {
            if (index < 0 || index >= _tagCount)
            {
                return default;
            }
            return _tagTriggers[index];
        }

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
