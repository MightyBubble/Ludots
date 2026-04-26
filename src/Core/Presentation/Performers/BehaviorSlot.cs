using System.Numerics;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
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

    public struct AssetBindingConfig
    {
        public AssetKind AssetKind;
        public int AssetId;
        public int MaterialId;
        public VisualRenderPath RenderPath;
        public VisualMobility Mobility;
        public Vector3 LocalOffset;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public int ScaleParamKey;
        public int ColorParamKey;
        public int MaterialParamKey;
        public int AssetSwapParamKey;
        public AssetSwapEntry[] AssetSwapTable;
        public int VisibilityParamKey;
        public bool HasMaxLod;
        public LODLevel MaxLod;
        public GroundingMode Grounding;
        public float GroundingOffset;
    }

    public struct AssetSwapEntry
    {
        public float ParamValue;
        public int AssetId;
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
        public AttachmentTarget Target;
        public int BoneId;
        public Vector3 Offset;
        public Quaternion RotationOffset;
        public bool InheritScale;
    }

    public enum AttachmentTarget : byte
    {
        Parent = 0,
        Bone = 1,
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

    public enum SplineUsage : byte
    {
        Render = 1,
        Patrol = 2,
    }

    public enum GroundingMode : byte
    {
        None = 0,
        SnapToGround = 1,
        AlignToSurface = 2,
    }
}
