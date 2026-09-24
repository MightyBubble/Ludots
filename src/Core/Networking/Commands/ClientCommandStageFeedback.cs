using System;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.Networking.Commands
{
    public enum ClientCommandStage : byte
    {
        Sending = 0,
        ServerAccepted = 1,
        EntityQueued = 2,
        EntityPending = 3,
        Activated = 4,
        Rejected = 5,
    }

    public readonly struct ClientCommandStageFeedback
    {
        public ClientCommandStageFeedback(
            ulong clientBatchSequence,
            ClientCommandStage stage,
            OrderSubmitResult result,
            OrderAdmissionStage admissionStage)
        {
            ClientBatchSequence = clientBatchSequence;
            Stage = stage;
            Result = result;
            AdmissionStage = admissionStage;
        }

        public ulong ClientBatchSequence { get; }
        public ClientCommandStage Stage { get; }
        public OrderSubmitResult Result { get; }
        public OrderAdmissionStage AdmissionStage { get; }

        public bool IsTerminal =>
            Stage is ClientCommandStage.Activated
                or ClientCommandStage.Rejected
                or ClientCommandStage.EntityQueued;
    }

    public interface IClientCommandStageFeedbackPort
    {
        bool TryPeek(out ClientCommandStageFeedback feedback);

        bool TryRead(out ClientCommandStageFeedback feedback);

        int Count { get; }
    }

    public sealed class ClientCommandStageFeedbackBuffer : IClientCommandStageFeedbackPort
    {
        private readonly ClientCommandStageFeedback[] _items;
        private int _head;
        private int _count;

        public ClientCommandStageFeedbackBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Feedback capacity must be positive.");
            }

            _items = new ClientCommandStageFeedback[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public int AvailableCapacity => _items.Length - _count;

        public bool TryWrite(in ClientCommandStageFeedback feedback)
        {
            if (_count == _items.Length)
            {
                return false;
            }

            int tail = (_head + _count) % _items.Length;
            _items[tail] = feedback;
            _count++;
            return true;
        }

        public bool TryPeek(out ClientCommandStageFeedback feedback)
        {
            if (_count == 0)
            {
                feedback = default;
                return false;
            }

            feedback = _items[_head];
            return true;
        }

        public bool TryRead(out ClientCommandStageFeedback feedback)
        {
            if (_count == 0)
            {
                feedback = default;
                return false;
            }

            feedback = _items[_head];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }
}
