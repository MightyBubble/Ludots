using System;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public enum BehaviorKind : byte
    {
        AssetBinding = 0,
        AttributeBinding = 1,
        TagBinding = 2,
        Animator = 3,
        Attachment = 4,
        Sound = 5,
        Material = 6,
        Spline = 7,
    }

    public enum AssetKind : byte
    {
        Mesh = 0,
        SkinnedMesh = 1,
        Decal = 2,
        VFX = 3,
        Sound = 4,
        Spline = 5,
    }

    public enum GroundingMode : byte
    {
        None = 0,
        SnapToGround = 1,
        AlignToSurface = 2,
    }

    public enum ParamLane : byte
    {
        Float = 0,
        Int = 1,
        Vector = 2,
    }

    public enum SplineUsage : byte
    {
        Render = 0,
        Patrol = 1,
    }

    public enum PerformerSurfaceKind : byte
    {
        SplineRibbon = 0,
        AreaMesh = 1,
    }

    public enum PerformerSurfaceChunkOwnership : byte
    {
        PerChunk = 0,
        WholeSurface = 1,
    }

    public enum ProceduralMeshUsageHint : byte
    {
        Static = 0,
        Dynamic = 1,
    }

    public enum PerformerSurfaceValueSourceKind : byte
    {
        Constant = 0,
        Graph = 1,
        Asset = 2,
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
        public ProceduralMeshUsageHint UsageHint;
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
}
