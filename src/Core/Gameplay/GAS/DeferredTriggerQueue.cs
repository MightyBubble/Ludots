using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public class DeferredTriggerQueue
    {
        public const string CapacityExceededError = "GAS.DEFERRED_TRIGGER.ERR.CapacityExceeded";

        private readonly int _capacity;

        private AttributeChangedTrigger[] _attributeTriggers;
        private AttributeChangedTrigger[] _attributeOverflow;
        private TagChangedTrigger[] _tagTriggers;
        private TagChangedTrigger[] _tagOverflow;
        private TagCountChangedTrigger[] _tagCountTriggers;
        private TagCountChangedTrigger[] _tagCountOverflow;

        public DeferredTriggerQueue(int capacity = GasConstants.MAX_DEFERRED_TRIGGERS_PER_FRAME)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "DeferredTriggerQueue capacity must be positive.");
            }

            _capacity = capacity;
            _attributeTriggers = new AttributeChangedTrigger[capacity];
            _attributeOverflow = new AttributeChangedTrigger[capacity];
            _tagTriggers = new TagChangedTrigger[capacity];
            _tagOverflow = new TagChangedTrigger[capacity];
            _tagCountTriggers = new TagCountChangedTrigger[capacity];
            _tagCountOverflow = new TagCountChangedTrigger[capacity];
        }

        public int Capacity => _capacity;

        private int _attributeCount = 0;
        private int _tagCount = 0;
        private int _tagCountTriggerCount = 0;
        private int _attributeOverflowCount = 0;
        private int _tagOverflowCount = 0;
        private int _tagCountOverflowCount = 0;

        public void EnqueueAttributeChanged(AttributeChangedTrigger trigger)
        {
            if (_attributeCount < _capacity)
            {
                _attributeTriggers[_attributeCount++] = trigger;
                return;
            }

            if (_attributeOverflowCount >= _capacity)
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: source=AttributeChanged, capacity={_capacity}, overflowCapacity={_capacity}, attributeId={trigger.AttributeId}.");
            }

            _attributeOverflow[_attributeOverflowCount++] = trigger;
        }

        public void EnqueueTagChanged(TagChangedTrigger trigger)
        {
            if (_tagCount < _capacity)
            {
                _tagTriggers[_tagCount++] = trigger;
                return;
            }

            if (_tagOverflowCount >= _capacity)
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: source=TagChanged, capacity={_capacity}, overflowCapacity={_capacity}, tagId={trigger.TagId}.");
            }

            _tagOverflow[_tagOverflowCount++] = trigger;
        }

        public void EnqueueTagCountChanged(TagCountChangedTrigger trigger)
        {
            if (_tagCountTriggerCount < _capacity)
            {
                _tagCountTriggers[_tagCountTriggerCount++] = trigger;
                return;
            }

            if (_tagCountOverflowCount >= _capacity)
            {
                throw new System.InvalidOperationException(
                    $"{CapacityExceededError}: source=TagCountChanged, capacity={_capacity}, overflowCapacity={_capacity}, tagId={trigger.TagId}.");
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
