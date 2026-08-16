using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public struct SkinnedVisualBatchItem
    {
        public VisualRenderPayload Payload;
        public LODLevel LOD;

        public int StableId
        {
            readonly get => Payload.StableId;
            set => Payload.StableId = value;
        }

        public int MeshAssetId
        {
            readonly get => Payload.MeshAssetId;
            set => Payload.MeshAssetId = value;
        }

        public int MaterialId
        {
            readonly get => Payload.MaterialId;
            set => Payload.MaterialId = value;
        }

        public int TemplateId
        {
            readonly get => Payload.TemplateId;
            set => Payload.TemplateId = value;
        }

        public int AnimationProfileId
        {
            readonly get => Payload.AnimationProfileId;
            set => Payload.AnimationProfileId = value;
        }

        public VisualRenderPath RenderPath
        {
            readonly get => Payload.RenderPath;
            set => Payload.RenderPath = value;
        }

        public AssetKind AssetKind
        {
            readonly get => Payload.AssetKind;
            set => Payload.AssetKind = value;
        }

        public string SurfaceLayerKey
        {
            readonly get => Payload.SurfaceLayerKey;
            set => Payload.SurfaceLayerKey = value;
        }

        public int SortId
        {
            readonly get => Payload.SortId;
            set => Payload.SortId = value;
        }

        public MaterialCustomDataPayload MaterialCustomData
        {
            readonly get => Payload.MaterialCustomData;
            set => Payload.MaterialCustomData = value;
        }

        public Vector3 Position
        {
            readonly get => Payload.Position;
            set => Payload.Position = value;
        }

        public Quaternion Rotation
        {
            readonly get => Payload.Rotation;
            set => Payload.Rotation = value;
        }

        public Vector3 Scale
        {
            readonly get => Payload.Scale;
            set => Payload.Scale = value;
        }

        public Vector4 Color
        {
            readonly get => Payload.Color;
            set => Payload.Color = value;
        }

        public AnimatorPackedState Animator
        {
            readonly get => Payload.Animator;
            set => Payload.Animator = value;
        }

        public AnimationOverlayRequest AnimationOverlay
        {
            readonly get => Payload.AnimationOverlay;
            set => Payload.AnimationOverlay = value;
        }

        public VisualVisibility Visibility
        {
            readonly get => Payload.Visibility;
            set => Payload.Visibility = value;
        }

    }
}
