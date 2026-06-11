using System;
using System.Numerics;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public enum BehaviorKind : byte
    {
        AssetBinding = 1,
        AttributeBinding = 2,
        TagBinding = 3,
        Animator = 4,
        Attachment = 5,
        Sound = 6,
        Material = 7,
        Spline = 8,
    }

    public enum AssetKind : byte
    {
        Mesh = 1,
        SkinnedMesh = 2,
        Decal = 3,
        VFX = 4,
        Spline = 5,
        Sound = 6,
        WorldHud = 7,
        WorldText = 8,
        GroundOverlay = 9,
        Surface = 10,
    }

    public enum ParamLane : byte
    {
        Float = 0,
        Int = 1,
        Vector = 2,
    }

    public enum GroundingMode : byte
    {
        None = 0,
        SnapToSurface = 1,
        AlignToSurface = 2,
    }

    public enum SplineUsage : byte
    {
        Render = 1,
        Patrol = 2,
    }

    public struct ChildPerformerRef
    {
        public int DefinitionId;
        public int ScopeTag;
        public ParamDefault[] ParamOverrides;
    }

    public struct ParamDefault
    {
        public int ParamKey;
        public ParamLane Lane;
        public float FloatValue;
        public int IntValue;
        public Vector4 VectorValue;
    }

    public struct BehaviorSlot
    {
        public int SlotIndex;
        public BehaviorKind Kind;
        public bool ActiveByDefault;
        public ConditionRef ActivationCondition;
        public AssetBindingConfig AssetBinding;
        public AttributeBindingConfig AttributeBinding;
        public TagBindingConfig TagBinding;
        public AnimatorConfig Animator;
        public AttachmentConfig Attachment;
        public SoundConfig Sound;
        public MaterialConfig Material;
        public SplineConfig Spline;
    }

    public struct AssetBindingConfig
    {
        public AssetKind AssetKind;
        public int AssetId;
        public int MaterialId;
        public int AnimatorSlot;
        public VisualRenderPath RenderPath;
        public VisualMobility Mobility;
        public Vector3 LocalOffset;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public int ScaleParamKey;
        public int ColorParamKey;
        public int MaterialParamKey;
        public int AssetSwapParamKey;
        public int VisibilityParamKey;
        public GroundingMode Grounding;
        public float GroundingOffset;
        public string SurfaceLayerKey;
        public int SortId;
        public MaterialCustomDataBinding[] MaterialCustomData;
    }

    public struct AttributeBindingConfig
    {
        public int AttributeId;
        public int TargetParamKey;
        public ValueSourceKind Mode;
        public ThresholdMapping[] Thresholds;
    }

    public struct ThresholdMapping
    {
        public float Threshold;
        public int OutputParamKey;
        public float OutputValue;
    }

    public struct TagBindingConfig
    {
        public int TagId;
        public int TargetParamKey;
        public bool InvertLogic;
    }

    public struct AnimatorConfig
    {
        public int AnimatorControllerId;
        public int AnimationProfileId;
        public int SpeedParamKey;
        public int StateParamKey;
    }

    public struct AttachmentConfig
    {
        public int BoneId;
        public Vector3 Offset;
        public Quaternion RotationOffset;
        public bool InheritScale;
    }

    public struct SoundConfig
    {
        public int SoundAssetId;
        public bool Loop;
        public float Volume;
        public int VolumeParamKey;
    }

    public struct MaterialConfig
    {
        public int BaseMaterialId;
        public int MaterialSwapParamKey;
        public MaterialSwapEntry[] SwapTable;
    }

    public struct MaterialSwapEntry
    {
        public float ParamValue;
        public int MaterialId;
    }

    public struct SplineConfig
    {
        public int SplineAssetId;
        public SplineUsage Usage;
        public int WidthParamKey;
        public int ColorParamKey;
        public int SpeedParamKey;
        public int ProgressParamKey;
        public bool Loop;
        public bool PingPong;
        public int WaypointEventId;
    }

    public readonly struct MaterialCustomData : IEquatable<MaterialCustomData>
    {
        public const int MaxSlots = 4;

        public readonly uint SlotMask;
        public readonly Vector4 Slot0;
        public readonly Vector4 Slot1;
        public readonly Vector4 Slot2;
        public readonly Vector4 Slot3;

        public static MaterialCustomData Empty => default;

        public bool HasAny => SlotMask != 0;

        private MaterialCustomData(uint slotMask, in Vector4 slot0, in Vector4 slot1, in Vector4 slot2, in Vector4 slot3)
        {
            SlotMask = slotMask;
            Slot0 = slot0;
            Slot1 = slot1;
            Slot2 = slot2;
            Slot3 = slot3;
        }

        public Vector4 GetSlot(int slot)
        {
            return slot switch
            {
                0 => Slot0,
                1 => Slot1,
                2 => Slot2,
                3 => Slot3,
                _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Material custom data slot is outside the fixed payload range."),
            };
        }

        public MaterialCustomData WithSlot(int slot, in Vector4 value)
        {
            if ((uint)slot >= MaxSlots)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Material custom data slot is outside the fixed payload range.");
            }

            uint mask = SlotMask | (1u << slot);
            return slot switch
            {
                0 => new MaterialCustomData(mask, value, Slot1, Slot2, Slot3),
                1 => new MaterialCustomData(mask, Slot0, value, Slot2, Slot3),
                2 => new MaterialCustomData(mask, Slot0, Slot1, value, Slot3),
                _ => new MaterialCustomData(mask, Slot0, Slot1, Slot2, value),
            };
        }

        public bool Equals(MaterialCustomData other)
        {
            return SlotMask == other.SlotMask
                && Slot0.Equals(other.Slot0)
                && Slot1.Equals(other.Slot1)
                && Slot2.Equals(other.Slot2)
                && Slot3.Equals(other.Slot3);
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialCustomData other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SlotMask, Slot0, Slot1, Slot2, Slot3);
        }

        public static bool operator ==(MaterialCustomData left, MaterialCustomData right) => left.Equals(right);
        public static bool operator !=(MaterialCustomData left, MaterialCustomData right) => !left.Equals(right);
    }

    public struct MaterialCustomDataBinding
    {
        public int Slot;
        public int XParamKey;
        public int YParamKey;
        public int ZParamKey;
        public int WParamKey;
        public Vector4 DefaultValue;
    }

    public struct SurfaceAuthoringBlock
    {
        public PerformerSurfaceKind Kind;
        public string ProfileId;
        public PerformerSurfaceGeometrySource GeometrySource;
        public PerformerSurfaceChunkBakePolicy ChunkBake;
        public PerformerSurfaceMaterialSet MaterialSet;
        public string LodProfileId;
        public PerformerSurfaceGroundingPolicy Grounding;
        public string BoundsPolicy;
    }

    public enum PerformerSurfaceKind : byte
    {
        SplineRibbon = 1,
        ClosedArea = 2,
        RawMeshPayload = 3,
    }

    public struct PerformerSurfaceGeometrySource
    {
        public PerformerSurfaceValueSource? ControlPointSource;
        public PerformerSurfaceValueSource? WidthSource;
        public PerformerSurfaceValueSource? FlowDirectionSource;
        public string SegmentationPolicy;
        public PerformerSurfaceValueSource? BoundaryPointSource;
        public string TriangulationPolicy;
        public PerformerSurfaceValueSource? MeshPayloadSource;
    }

    public struct PerformerSurfaceChunkBakePolicy
    {
        public bool Enabled;
        public PerformerSurfaceChunkOwnership Ownership;
        public string ChunkInfluencePolicy;
        public string RebakePolicy;
        public PerformerSurfaceUsageHint UsageHint;
    }

    public enum PerformerSurfaceChunkOwnership : byte
    {
        PerChunk = 1,
        PerSource = 2,
    }

    public enum PerformerSurfaceUsageHint : byte
    {
        Static = 1,
        Dynamic = 2,
    }

    public struct PerformerSurfaceMaterialSet
    {
        public string PrimaryMaterialId;
        public string SecondaryMaterialId;
        public bool AllowInstanceOverride;
    }

    public struct PerformerSurfaceGroundingPolicy
    {
        public string Mode;
    }

    public struct PerformerSurfaceValueSource
    {
        public PerformerSurfaceValueSourceKind Kind;
        public string Id;
        public int GraphProgramId;
    }

    public enum PerformerSurfaceValueSourceKind : byte
    {
        Constant = 1,
        Param = 2,
        Graph = 3,
    }

    public static class AssetKindSemantics
    {
        public static bool SupportsMaterialCustomData(AssetKind assetKind, VisualRenderPath renderPath)
        {
            return assetKind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Surface
                && (renderPath.IsStaticInstanceLane() || renderPath.IsSkinnedLane() || renderPath.IsSurfaceLane());
        }
    }
}
