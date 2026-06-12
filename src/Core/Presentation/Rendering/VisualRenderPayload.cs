using System;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Rendering
{
    public struct MaterialCustomDataPayload : IEquatable<MaterialCustomDataPayload>
    {
        public byte Count;
        public Vector4 Slot0;
        public Vector4 Slot1;
        public Vector4 Slot2;
        public Vector4 Slot3;

        public readonly Vector4 GetSlot(int index)
        {
            return index switch
            {
                0 => Slot0,
                1 => Slot1,
                2 => Slot2,
                3 => Slot3,
                _ => throw new System.ArgumentOutOfRangeException(nameof(index)),
            };
        }

        public void SetSlot(int index, Vector4 value)
        {
            switch (index)
            {
                case 0:
                    Slot0 = value;
                    break;
                case 1:
                    Slot1 = value;
                    break;
                case 2:
                    Slot2 = value;
                    break;
                case 3:
                    Slot3 = value;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(index));
            }
        }

        public readonly bool Equals(MaterialCustomDataPayload other)
        {
            return Count == other.Count &&
                   Slot0.Equals(other.Slot0) &&
                   Slot1.Equals(other.Slot1) &&
                   Slot2.Equals(other.Slot2) &&
                   Slot3.Equals(other.Slot3);
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is MaterialCustomDataPayload other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(Count, Slot0, Slot1, Slot2, Slot3);
        }
    }

    public struct VisualRenderPayload : IEquatable<VisualRenderPayload>
    {
        public int MeshAssetId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector4 Color;
        public int StableId;
        public int MaterialId;
        public int TemplateId;
        public int AnimationProfileId;
        public VisualRenderPath RenderPath;
        public AssetKind AssetKind;
        public string SurfaceLayerKey;
        public int SortId;
        public MaterialCustomDataPayload MaterialCustomData;
        public AnimatorPackedState Animator;
        public AnimationOverlayRequest AnimationOverlay;
        public VisualVisibility Visibility;

        public readonly bool Equals(VisualRenderPayload other)
        {
            return MeshAssetId == other.MeshAssetId &&
                   Position.Equals(other.Position) &&
                   Rotation.Equals(other.Rotation) &&
                   Scale.Equals(other.Scale) &&
                   Color.Equals(other.Color) &&
                   StableId == other.StableId &&
                   MaterialId == other.MaterialId &&
                   TemplateId == other.TemplateId &&
                   AnimationProfileId == other.AnimationProfileId &&
                   RenderPath == other.RenderPath &&
                   AssetKind == other.AssetKind &&
                   string.Equals(SurfaceLayerKey, other.SurfaceLayerKey, StringComparison.Ordinal) &&
                   SortId == other.SortId &&
                   MaterialCustomData.Equals(other.MaterialCustomData) &&
                   Animator.Equals(other.Animator) &&
                   AnimationOverlay.Equals(other.AnimationOverlay) &&
                   Visibility == other.Visibility;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is VisualRenderPayload other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(MeshAssetId);
            hash.Add(Position);
            hash.Add(Rotation);
            hash.Add(Scale);
            hash.Add(Color);
            hash.Add(StableId);
            hash.Add(MaterialId);
            hash.Add(TemplateId);
            hash.Add(AnimationProfileId);
            hash.Add(RenderPath);
            hash.Add(AssetKind);
            hash.Add(SurfaceLayerKey, StringComparer.Ordinal);
            hash.Add(SortId);
            hash.Add(MaterialCustomData);
            hash.Add(Animator);
            hash.Add(AnimationOverlay);
            hash.Add(Visibility);
            return hash.ToHashCode();
        }
    }
}
