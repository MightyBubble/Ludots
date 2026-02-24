using Arch.Core;
 
namespace Ludots.Core.Gameplay.GAS
{
    public enum ResponseChainTelemetryKind : byte
    {
        None = 0,
        WindowOpened = 1,
        PromptRequested = 2,
        OrderConsumed = 3,
        ProposalAdded = 4,
        ProposalResolved = 5,
        WindowClosed = 6
    }
 
    public enum ResponseChainResolveOutcome : byte
    {
        None = 0,
        AppliedInstant = 1,
        CreatedEffect = 2,
        Cancelled = 3,
        Negated = 4,
        TargetDead = 5,
        TemplateMissing = 6
    }
 
    public struct ResponseChainTelemetryEvent
    {
        public ResponseChainTelemetryKind Kind;
        public int RootId;
        public int TemplateId;
        public int TagId;
        public int ProposalIndex;
        public int PromptTagId;
        public int OrderTagId;
        public ResponseChainResolveOutcome Outcome;
        public Entity Source;
        public Entity Target;
        public Entity Context;
    }
 
    public sealed class ResponseChainTelemetryBuffer
    {
        private readonly ResponseChainTelemetryEvent[] _buffer;
        private int _count;
 
        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }
 
        public ResponseChainTelemetryBuffer(int capacity = 4096)
        {
            if (capacity <= 0) capacity = 256;
            _buffer = new ResponseChainTelemetryEvent[capacity];
        }
 
        public bool TryAdd(in ResponseChainTelemetryEvent evt)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }
 
            _buffer[_count++] = evt;
            return true;
        }
 
        public ResponseChainTelemetryEvent this[int index] => _buffer[index];
 
        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
