using System;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Instancing
{
    public sealed class InstancedBatchAsset
    {
        public InstancedBatchAsset()
        {
            Key = string.Empty;
            OwnerStableId = string.Empty;
            Groups = Array.Empty<InstancedBatchGroup>();
            CustomDataChannels = Array.Empty<InstancedBatchCustomDataChannel>();
            Behaviors = Array.Empty<InstancedBatchBehaviorBinding>();
        }

        public int Id { get; set; }
        public string Key { get; set; }
        public VisualRenderPath RenderPath { get; set; }
        public string OwnerStableId { get; set; }
        public InstancedBatchGroup[] Groups { get; set; }
        public InstancedBatchCustomDataChannel[] CustomDataChannels { get; set; }
        public InstancedBatchBehaviorBinding[] Behaviors { get; set; }
        public InstancedBatchProgressiveSubmissionPolicy ProgressiveSubmission { get; set; }
        public InstancedBatchAddressTable AddressTable { get; set; } = null!;
    }

    public struct InstancedBatchGroup
    {
        public string Id;
        public int MeshAssetId;
        public int MaterialId;
        public string BucketId;
        public string InstanceSpanId;
        public InstancedBatchAddress Address;
        public InstancedBatchTransform[] Transforms;
    }

    public struct InstancedBatchTransform
    {
        public Vector3 PositionCm;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    public struct InstancedBatchCustomDataChannel
    {
        public string Key;
        public int Slot;
        public MaterialCustomDataLane Lane;
    }

    public readonly struct InstancedBatchProgressiveSubmissionPolicy
    {
        public static readonly InstancedBatchProgressiveSubmissionPolicy None = new(0);

        public InstancedBatchProgressiveSubmissionPolicy(int maxInstancesPerFlush)
        {
            MaxInstancesPerFlush = maxInstancesPerFlush;
        }

        public int MaxInstancesPerFlush { get; }
        public bool IsEnabled => MaxInstancesPerFlush > 0;
    }
}
