using System;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    public struct BehaviorSlot
    {
        public int SlotIndex;
        public BehaviorKind Kind;
        public int KindId;
        public PerformerBehaviorExecutionLane ExtensionLane;
        public int ExtensionTriggerId;
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
        public GroundingConfig Grounding;
        public MinimapMarkerConfig MinimapMarker;
        public WorldTextConfig WorldText;
        public BehaviorStyleConfig Style;
        public BehaviorMotionConfig Motion;
        public SurfaceAuthoringBlock? SurfaceSource;
        public InstancedBatchConfig InstancedBatch;
    }

    public enum BehaviorKind : byte
    {
        None = 0,
        AssetBinding = 1,
        AttributeBinding = 2,
        TagBinding = 3,
        Animator = 4,
        Attachment = 5,
        Sound = 6,
        Material = 7,
        Spline = 8,
        Grounding = 9,
        MinimapMarker = 10,
        WorldText = 11,
        SurfaceSource = 12,
        InstancedBatch = 13,
        Extension = 255,
    }

    public struct WorldTextConfig
    {
        public WorldTextConfig()
        {
            TextTokenId = 0;
            Mode = WorldHudValueMode.None;
            ValueParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            SecondaryValueParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            FontSize = 16;
        }

        public int TextTokenId;
        public WorldHudValueMode Mode;
        public int ValueParamKey;
        public int SecondaryValueParamKey;
        public int FontSize;
    }

    public enum BehaviorAlphaPolicy : byte
    {
        None = 0,
        FadeOverLifetime = 1,
    }

    public struct BehaviorStyleConfig
    {
        public bool HasColor;
        public Vector4 Color;
        public BehaviorAlphaPolicy AlphaPolicy;
    }

    public struct BehaviorMotionConfig
    {
        public float YDriftPerSecond;
    }

    public struct InstancedBatchConfig
    {
        public int BatchAssetId;
    }

    public struct AssetBindingConfig
    {
        public AssetBindingConfig()
        {
            AssetKind = default;
            AssetId = 0;
            MaterialId = 0;
            RenderPath = VisualRenderPath.None;
            Mobility = default;
            LocalOffset = Vector3.Zero;
            LocalRotation = Quaternion.Identity;
            LocalScale = Vector3.One;
            ScaleParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            ColorParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            MaterialParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            AssetIdParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            AssetSwapParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            AssetSwapTable = Array.Empty<AssetSwapEntry>();
            VisibilityParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            SurfaceLayerKey = string.Empty;
            SortId = 0;
            MaterialCustomData = MaterialCustomDataBinding.Empty;
            HasMaxLod = false;
            MaxLod = LODLevel.Low;
        }

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
        public int AssetIdParamKey;
        public int AssetSwapParamKey;
        public AssetSwapEntry[] AssetSwapTable;
        public int VisibilityParamKey;
        public string SurfaceLayerKey;
        public int SortId;
        public MaterialCustomDataBinding MaterialCustomData;
        public bool HasMaxLod;
        public LODLevel MaxLod;
    }

    public enum MaterialCustomDataLane : byte
    {
        Float = 1,
        Int = 2,
        Vector = 3,
    }

    public struct MaterialCustomDataSlotBinding
    {
        public int Slot;
        public MaterialCustomDataLane Lane;
        public int ParamKey;
        public float DefaultFloatValue;
        public int DefaultIntValue;
        public Vector4 DefaultVectorValue;
    }

    public struct MaterialCustomDataBinding
    {
        public const int MaxSlots = 4;

        public static readonly MaterialCustomDataBinding Empty = new()
        {
            Slots = Array.Empty<MaterialCustomDataSlotBinding>(),
        };

        public MaterialCustomDataSlotBinding[] Slots;
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

    public readonly struct CompiledBinding
    {
        public const int UnboundSourceId = -1;

        public readonly int SlotIndex;
        public readonly int SourceAttributeId;
        public readonly int SourceTagId;
        public readonly int TargetParamKey;
        public readonly ValueSourceKind Mode;
        public readonly bool InvertLogic;
        public readonly ThresholdMapping[] Thresholds;

        public CompiledBinding(
            int slotIndex,
            int sourceAttributeId,
            int sourceTagId,
            int targetParamKey,
            ValueSourceKind mode,
            bool invertLogic,
            ThresholdMapping[] thresholds)
        {
            SlotIndex = slotIndex;
            SourceAttributeId = sourceAttributeId;
            SourceTagId = sourceTagId;
            TargetParamKey = targetParamKey;
            Mode = mode;
            InvertLogic = invertLogic;
            Thresholds = thresholds ?? System.Array.Empty<ThresholdMapping>();
        }

        public bool IsAttributeBound => SourceAttributeId >= 0;
        public bool IsTagBound => SourceTagId >= 0;

        public static CompiledBinding FromAttribute(int slotIndex, in AttributeBindingConfig config)
        {
            return new CompiledBinding(
                slotIndex,
                config.AttributeId,
                UnboundSourceId,
                config.TargetParamKey,
                config.Mode,
                invertLogic: false,
                CompileThresholds(config.Thresholds));
        }

        public static CompiledBinding FromTag(int slotIndex, in TagBindingConfig config)
        {
            return new CompiledBinding(
                slotIndex,
                UnboundSourceId,
                config.TagId,
                config.TargetParamKey,
                ValueSourceKind.Constant,
                config.InvertLogic,
                System.Array.Empty<ThresholdMapping>());
        }

        public bool TrySelectThreshold(float value, out ThresholdMapping selected)
        {
            ThresholdMapping[] thresholds = Thresholds;
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (value <= thresholds[i].Threshold)
                {
                    selected = thresholds[i];
                    return true;
                }
            }

            selected = default;
            return false;
        }

        public int ResolveTagInt(bool tagActive)
        {
            if (InvertLogic)
            {
                tagActive = !tagActive;
            }

            return tagActive ? 1 : 0;
        }

        internal static ThresholdMapping[] CompileThresholds(ThresholdMapping[]? source)
        {
            if (source == null || source.Length == 0)
            {
                return System.Array.Empty<ThresholdMapping>();
            }

            var copy = new ThresholdMapping[source.Length];
            System.Array.Copy(source, copy, source.Length);
            System.Array.Sort(copy, static (left, right) => left.Threshold.CompareTo(right.Threshold));
            return copy;
        }
    }

    public struct TagBindingConfig
    {
        public int TagId;
        public int TargetParamKey;
        public bool InvertLogic;
    }

    public struct AnimatorConfig
    {
        public AnimatorConfig()
        {
            AnimatorControllerId = 0;
            AnimationProfileId = 0;
            SpeedParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            StateParamKey = PresenterParamKeyRegistry.UnsetParamKey;
        }

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
        public SoundConfig()
        {
            SoundAssetId = 0;
            Loop = false;
            Volume = 1f;
            VolumeParamKey = PresenterParamKeyRegistry.UnsetParamKey;
        }

        public int SoundAssetId;
        public bool Loop;
        public float Volume;
        public int VolumeParamKey;
    }

    public struct MaterialConfig
    {
        public MaterialConfig()
        {
            BaseMaterialId = 0;
            MaterialSwapParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            SwapTable = Array.Empty<MaterialSwapEntry>();
        }

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
        public SplineConfig()
        {
            SplineAssetId = 0;
            Usage = SplineUsage.Render;
            WidthParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            ColorParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            SpeedParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            ProgressParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            Loop = false;
            PingPong = false;
            WaypointEventId = 0;
        }

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

    public struct GroundingConfig
    {
        public GroundingMode Mode;
        public float Offset;
        public GroundingUpdatePolicy UpdatePolicy;
    }

    public enum GroundingUpdatePolicy : byte
    {
        Once = 0,
        EveryFrame = 1,
    }

    public enum MinimapMarkerShape : byte
    {
        Circle = 1,
    }

    public enum MinimapMarkerOrientationMode : byte
    {
        None = 0,
        ParamRadians = 1,
        ParamDegrees = 2,
        PresenterForward = 3,
    }

    public struct MinimapMarkerConfig
    {
        public MinimapMarkerConfig()
        {
            Shape = MinimapMarkerShape.Circle;
            Color = Vector4.One;
            SizePx = 6f;
            ColorParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            SizeParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            VisibilityParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            OrientationMode = MinimapMarkerOrientationMode.None;
            OrientationParamKey = PresenterParamKeyRegistry.UnsetParamKey;
            OrientationOffsetRad = 0f;
            OrientationLengthPx = 0f;
        }

        public MinimapMarkerShape Shape;
        public Vector4 Color;
        public float SizePx;
        public int ColorParamKey;
        public int SizeParamKey;
        public int VisibilityParamKey;
        public MinimapMarkerOrientationMode OrientationMode;
        public int OrientationParamKey;
        public float OrientationOffsetRad;
        public float OrientationLengthPx;
    }
}
