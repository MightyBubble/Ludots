namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public enum AttributeBindingMode : byte
    {
        Add = 0,
        Override = 1
    }

    public enum AttributeBindingResetPolicy : byte
    {
        None = 0,
        ResetToZeroPerLogicFrame = 1
    }

    public readonly struct AttributeBindingEntry
    {
        public readonly int AttributeId;
        public readonly int SinkId;
        public readonly byte Channel;
        public readonly AttributeBindingMode Mode;
        public readonly AttributeBindingResetPolicy ResetPolicy;
        public readonly float Scale;

        public AttributeBindingEntry(int attributeId, int sinkId, byte channel, AttributeBindingMode mode, AttributeBindingResetPolicy resetPolicy, float scale)
        {
            AttributeId = attributeId;
            SinkId = sinkId;
            Channel = channel;
            Mode = mode;
            ResetPolicy = resetPolicy;
            Scale = scale;
        }
    }

    public readonly struct AttributeBindingGroup
    {
        public readonly int SinkId;
        public readonly int Start;
        public readonly int Count;

        public AttributeBindingGroup(int sinkId, int start, int count)
        {
            SinkId = sinkId;
            Start = start;
            Count = count;
        }
    }

    public sealed class AttributeBindingRegistry
    {
        private AttributeBindingEntry[] _entries = System.Array.Empty<AttributeBindingEntry>();
        private AttributeBindingGroup[] _groups = System.Array.Empty<AttributeBindingGroup>();

        public AttributeBindingEntry[] Entries => _entries;
        public AttributeBindingGroup[] Groups => _groups;

        public void Clear()
        {
            _entries = System.Array.Empty<AttributeBindingEntry>();
            _groups = System.Array.Empty<AttributeBindingGroup>();
        }

        public void Set(AttributeBindingEntry[] entries, AttributeBindingGroup[] groups)
        {
            _entries = entries ?? System.Array.Empty<AttributeBindingEntry>();
            _groups = groups ?? System.Array.Empty<AttributeBindingGroup>();
        }
    }
}
