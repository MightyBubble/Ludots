using System;

namespace Ludots.Core.Gameplay.GAS
{
    public enum GasDiagnosticSystem : byte
    {
        ResponseChain = 1,
        EffectProposal = 2,
        EffectLifetime = 3,
        TagContainer = 4,
        ActiveEffectContainer = 5,
        PhaseListener = 6,
        GameplayEventBus = 7,
        OrderAdmission = 8,
    }

    public enum GasDiagnosticMetric : byte
    {
        ResponseCreatesDropped = 1,
        ResponseDepthDropped = 2,
        ResponseStepBudgetFused = 3,
        ResponseQueueOverflow = 4,
        OnApplyCreatesDropped = 5,
        DurationCallbackCreatesDropped = 6,
        TagCountOverflowDropped = 7,
        ActiveEffectContainerAttachDropped = 8,
        PhaseListenerRegistrationDropped = 9,
        PhaseListenerDispatchDropped = 10,
        GameplayEventBusDropped = 11,
        OrderAdmissionResultOverflow = 12,
        OrderRejectedQueueFull = 13,
        OrderRejectedByRule = 14,
        OrderRejectedValidation = 15,
        OrderRejectedInvalidActor = 16,
        OrderRejectedInvalidOrderType = 17,
        OrderAdmissionResultBacklog = 18,
        OrderAdmissionResultHighWatermark = 19,
    }

    public readonly struct GasDiagnosticEvent
    {
        public readonly int FrameIndex;
        public readonly GasDiagnosticSystem System;
        public readonly GasDiagnosticMetric Metric;
        public readonly int Capacity;
        public readonly long Count;

        public GasDiagnosticEvent(
            int frameIndex,
            GasDiagnosticSystem system,
            GasDiagnosticMetric metric,
            int capacity,
            long count)
        {
            FrameIndex = frameIndex;
            System = system;
            Metric = metric;
            Capacity = capacity;
            Count = count;
        }
    }

    public sealed class GasDiagnosticEventBuffer
    {
        private readonly GasDiagnosticEvent[] _events;
        private int _count;

        public GasDiagnosticEventBuffer(int capacity = 32)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _events = new GasDiagnosticEvent[capacity];
        }

        public int FrameIndex { get; private set; }
        public int Count => _count;
        public int Capacity => _events.Length;
        public GasDiagnosticEvent this[int index] =>
            (uint)index < (uint)_count
                ? _events[index]
                : throw new ArgumentOutOfRangeException(nameof(index));

        public void BeginFrame(int frameIndex)
        {
            FrameIndex = frameIndex;
            _count = 0;
        }

        public void Publish(in GasDiagnosticEvent value)
        {
            if (_count >= _events.Length)
            {
                throw new InvalidOperationException(
                    $"GAS.DIAGNOSTICS.ERR.BufferCapacityExceeded: frame={FrameIndex}, capacity={_events.Length}.");
            }

            _events[_count++] = value;
        }
    }
}
