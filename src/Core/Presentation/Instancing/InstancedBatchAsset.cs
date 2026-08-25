using System;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

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
        public InstancedBatchInstanceSource Source;
        public InstancedBatchFactorizedSource? FactorizedSource;

        public int InstanceCount => Source.IsValid
            ? Source.InstanceCount
            : Transforms?.Length ?? 0;
    }

    public struct InstancedBatchTransform
    {
        public Vector3 PositionCm;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    public readonly struct InstancedBatchInstanceSource
    {
        public InstancedBatchInstanceSource(
            string format,
            string assetUri,
            string setId,
            int instanceCount,
            bool groundToVisualHeightmap)
        {
            Format = format ?? string.Empty;
            AssetUri = assetUri ?? string.Empty;
            SetId = setId ?? string.Empty;
            InstanceCount = instanceCount;
            GroundToVisualHeightmap = groundToVisualHeightmap;
        }

        public string Format { get; }
        public string AssetUri { get; }
        public string SetId { get; }
        public int InstanceCount { get; }
        public bool GroundToVisualHeightmap { get; }
        public bool IsValid => InstanceCount > 0 && Format.Length > 0 && AssetUri.Length > 0 && SetId.Length > 0;
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
