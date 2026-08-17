using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Events;

namespace Ludots.Core.Presentation.Instancing
{
    public enum InstancedBatchSourceKind : byte
    {
        Attribute = 1,
        GasEvent = 2,
        PresentationEvent = 3,
    }

    public enum InstancedBatchOperationKind : byte
    {
        SetVisibility = 1,
        WriteCustomData = 2,
        SetPresentationState = 3,
        Refresh = 4,
        AttachEffect = 5,
        UpdateEffect = 6,
        RemoveEffect = 7,
    }

    public enum InstancedBatchValueMappingKind : byte
    {
        Identity = 0,
        Linear = 1,
        Constant = 2,
    }

    public enum InstancedBatchCoalescingMode : byte
    {
        None = 0,
        LastWriteWins = 1,
    }

    public enum InstancedBatchLifecycleMode : byte
    {
        Persistent = 0,
        UntilOwnerDestroyed = 1,
        Transient = 2,
    }

    public readonly struct InstancedBatchBehaviorBinding
    {
        public InstancedBatchBehaviorBinding(
            string key,
            InstancedBatchSourceKind sourceKind,
            int sourceKeyId,
            PresentationEventKind sourceEventKind,
            InstancedBatchOperationKind operationKind,
            string groupId,
            string bucketId,
            string spanId,
            int customDataSlot,
            InstancedBatchValueMappingKind mappingKind,
            float inputMin,
            float inputMax,
            float outputMin,
            float outputMax,
            float constantValue,
            int order = 0,
            InstancedBatchCoalescingMode coalescing = InstancedBatchCoalescingMode.LastWriteWins,
            InstancedBatchLifecycleMode lifecycle = InstancedBatchLifecycleMode.UntilOwnerDestroyed,
            int targetPayloadId = 0,
            InstancedBatchAddress address = default)
        {
            Key = key ?? string.Empty;
            SourceKind = sourceKind;
            SourceKeyId = sourceKeyId;
            SourceEventKind = sourceEventKind;
            OperationKind = operationKind;
            GroupId = groupId ?? string.Empty;
            BucketId = bucketId ?? string.Empty;
            SpanId = spanId ?? string.Empty;
            CustomDataSlot = customDataSlot;
            MappingKind = mappingKind;
            InputMin = inputMin;
            InputMax = inputMax;
            OutputMin = outputMin;
            OutputMax = outputMax;
            ConstantValue = constantValue;
            Order = order;
            Coalescing = coalescing;
            Lifecycle = lifecycle;
            TargetPayloadId = targetPayloadId;
            Address = address;
        }

        public string Key { get; }
        public InstancedBatchSourceKind SourceKind { get; }
        public int SourceKeyId { get; }
        public PresentationEventKind SourceEventKind { get; }
        public InstancedBatchOperationKind OperationKind { get; }
        public string GroupId { get; }
        public string BucketId { get; }
        public string SpanId { get; }
        public int CustomDataSlot { get; }
        public InstancedBatchValueMappingKind MappingKind { get; }
        public float InputMin { get; }
        public float InputMax { get; }
        public float OutputMin { get; }
        public float OutputMax { get; }
        public float ConstantValue { get; }
        public int Order { get; }
        public InstancedBatchCoalescingMode Coalescing { get; }
        public InstancedBatchLifecycleMode Lifecycle { get; }
        public int TargetPayloadId { get; }
        public InstancedBatchAddress Address { get; }
        public bool HasCompiledAddress => Address.IsValid;

        public InstancedBatchBehaviorBinding WithCompiledAddress(InstancedBatchAddress address)
        {
            return new InstancedBatchBehaviorBinding(
                Key,
                SourceKind,
                SourceKeyId,
                SourceEventKind,
                OperationKind,
                GroupId,
                BucketId,
                SpanId,
                CustomDataSlot,
                MappingKind,
                InputMin,
                InputMax,
                OutputMin,
                OutputMax,
                ConstantValue,
                Order,
                Coalescing,
                Lifecycle,
                TargetPayloadId,
                address);
        }
    }

    public readonly struct InstancedBatchOperation
    {
        public InstancedBatchOperation(
            InstancedBatchOperationKind kind,
            int batchAssetId,
            int presenterStableId,
            Entity owner,
            Entity presenter,
            InstancedBatchAddress address,
            int customDataSlot,
            Vector4 value,
            int payloadId = 0,
            byte state = 0,
            InstancedBatchCoalescingMode coalescing = InstancedBatchCoalescingMode.None,
            InstancedBatchLifecycleMode lifecycle = InstancedBatchLifecycleMode.Persistent)
        {
            Kind = kind;
            BatchAssetId = batchAssetId;
            PresenterStableId = presenterStableId;
            Owner = owner;
            Presenter = presenter;
            Address = address;
            CustomDataSlot = customDataSlot;
            Value = value;
            PayloadId = payloadId;
            State = state;
            Coalescing = coalescing;
            Lifecycle = lifecycle;
        }

        public InstancedBatchOperationKind Kind { get; }
        public int BatchAssetId { get; }
        public int PresenterStableId { get; }
        public Entity Owner { get; }
        public Entity Presenter { get; }
        public InstancedBatchAddress Address { get; }
        public int CustomDataSlot { get; }
        public Vector4 Value { get; }
        public int PayloadId { get; }
        public byte State { get; }
        public InstancedBatchCoalescingMode Coalescing { get; }
        public InstancedBatchLifecycleMode Lifecycle { get; }
    }

    public sealed class InstancedBatchOperationBuffer
    {
        private InstancedBatchOperation[] _buffer;
        private readonly Dictionary<OperationCoalescingKey, int> _coalescedIndices;
        private int _count;

        public InstancedBatchOperationBuffer(int capacity = 4096)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new InstancedBatchOperation[capacity];
            _coalescedIndices = new Dictionary<OperationCoalescingKey, int>(capacity);
        }

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public void Add(in InstancedBatchOperation operation)
        {
            if (operation.Coalescing == InstancedBatchCoalescingMode.LastWriteWins)
            {
                OperationCoalescingKey key = OperationCoalescingKey.From(in operation);
                if (_coalescedIndices.TryGetValue(key, out int index))
                {
                    if ((uint)index < (uint)_count)
                    {
                        _buffer[index] = operation;
                        return;
                    }

                    _coalescedIndices.Remove(key);
                }
            }

            if (_count >= _buffer.Length)
            {
                throw new InvalidOperationException(
                    $"InstancedBatchOperationBuffer overflowed while adding kind={operation.Kind}, batchAssetId={operation.BatchAssetId}.");
            }

            int writeIndex = _count;
            _buffer[writeIndex] = operation;
            _count = writeIndex + 1;
            if (operation.Coalescing == InstancedBatchCoalescingMode.LastWriteWins)
            {
                _coalescedIndices[OperationCoalescingKey.From(in operation)] = writeIndex;
            }
        }

        public ReadOnlySpan<InstancedBatchOperation> GetSpan() => _buffer.AsSpan(0, _count);

        public void Clear()
        {
            _count = 0;
            _coalescedIndices.Clear();
        }

        private readonly struct OperationCoalescingKey : IEquatable<OperationCoalescingKey>
        {
            private readonly InstancedBatchOperationKind _kind;
            private readonly int _batchAssetId;
            private readonly int _presenterStableId;
            private readonly Entity _presenter;
            private readonly InstancedBatchAddress _address;
            private readonly int _customDataSlot;
            private readonly int _payloadIdentity;

            private OperationCoalescingKey(
                InstancedBatchOperationKind kind,
                int batchAssetId,
                int presenterStableId,
                Entity presenter,
                InstancedBatchAddress address,
                int customDataSlot,
                int payloadIdentity)
            {
                _kind = kind;
                _batchAssetId = batchAssetId;
                _presenterStableId = presenterStableId;
                _presenter = presenter;
                _address = address;
                _customDataSlot = customDataSlot;
                _payloadIdentity = payloadIdentity;
            }

            public static OperationCoalescingKey From(in InstancedBatchOperation operation)
            {
                int payloadIdentity = operation.Kind switch
                {
                    InstancedBatchOperationKind.AttachEffect or
                    InstancedBatchOperationKind.UpdateEffect or
                    InstancedBatchOperationKind.RemoveEffect => operation.PayloadId,
                    _ => 0,
                };

                return new OperationCoalescingKey(
                    operation.Kind,
                    operation.BatchAssetId,
                    operation.PresenterStableId,
                    operation.Presenter,
                    operation.Address,
                    operation.CustomDataSlot,
                    payloadIdentity);
            }

            public bool Equals(OperationCoalescingKey other)
            {
                return _kind == other._kind &&
                       _batchAssetId == other._batchAssetId &&
                       _presenterStableId == other._presenterStableId &&
                       _presenter == other._presenter &&
                       _address.Equals(other._address) &&
                       _customDataSlot == other._customDataSlot &&
                       _payloadIdentity == other._payloadIdentity;
            }

            public override bool Equals(object? obj) => obj is OperationCoalescingKey other && Equals(other);

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    _kind,
                    _batchAssetId,
                    _presenterStableId,
                    _presenter,
                    _address,
                    _customDataSlot,
                    _payloadIdentity);
            }
        }
    }
}
