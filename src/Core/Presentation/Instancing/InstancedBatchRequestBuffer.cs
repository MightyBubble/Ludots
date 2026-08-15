using System;
using Arch.Core;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Instancing
{
    public enum InstancedBatchRequestKind : byte
    {
        CreateOrUpdate = 1,
        Remove = 2,
    }

    public readonly struct InstancedBatchRequest
    {
        public InstancedBatchRequest(
            InstancedBatchRequestKind kind,
            int batchAssetId,
            int presenterStableId,
            Entity owner,
            Entity presenter,
            InstancedBatchAddress address,
            VisualRenderPath renderPath,
            int meshAssetId,
            int materialAssetId,
            int instanceStart,
            int instanceCount,
            bool finalChunk)
        {
            Kind = kind;
            BatchAssetId = batchAssetId;
            PresenterStableId = presenterStableId;
            Owner = owner;
            Presenter = presenter;
            Address = address;
            RenderPath = renderPath;
            MeshAssetId = meshAssetId;
            MaterialAssetId = materialAssetId;
            InstanceStart = instanceStart;
            InstanceCount = instanceCount;
            FinalChunk = finalChunk;
        }

        public InstancedBatchRequestKind Kind { get; }
        public int BatchAssetId { get; }
        public int PresenterStableId { get; }
        public Entity Owner { get; }
        public Entity Presenter { get; }
        public InstancedBatchAddress Address { get; }
        public VisualRenderPath RenderPath { get; }
        public int MeshAssetId { get; }
        public int MaterialAssetId { get; }
        public int InstanceStart { get; }
        public int InstanceCount { get; }
        public bool FinalChunk { get; }
    }

    public sealed class InstancedBatchRequestBuffer
    {
        private InstancedBatchRequest[] _buffer;
        private int _count;
        private int _revision;

        public InstancedBatchRequestBuffer(int capacity = 4096)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new InstancedBatchRequest[capacity];
        }

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int Revision => _revision;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public void Add(in InstancedBatchRequest request)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                throw new InvalidOperationException(
                    $"InstancedBatchRequestBuffer overflowed while adding kind={request.Kind}, batchAssetId={request.BatchAssetId}.");
            }

            _buffer[_count++] = request;
            _revision++;
        }

        public ReadOnlySpan<InstancedBatchRequest> GetSpan() => _buffer.AsSpan(0, _count);

        public void Clear()
        {
            _count = 0;
            DroppedSinceClear = 0;
        }
    }
}
