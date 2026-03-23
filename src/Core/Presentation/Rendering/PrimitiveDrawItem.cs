using System.Numerics;
using Ludots.Core.Presentation.Components;
namespace Ludots.Core.Presentation.Rendering
{
    public struct PrimitiveDrawItem
    {
        public PrimitiveDrawKind PrimitiveKind;
        public int MeshAssetId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector4 Color;
        public int StableId;
        public int MaterialId;
        public int TemplateId;
        public VisualRenderPath RenderPath;
        public VisualMobility Mobility;
        public VisualRuntimeFlags Flags;
        public AnimatorPackedState Animator;
        public AnimationOverlayRequest AnimationOverlay;
        public VisualVisibility Visibility;
        public float PrimitiveLength;
        public float PrimitiveWidth;
        public float PrimitiveEndWidth;
        public float PrimitiveInnerRadius;
        public float PrimitiveOuterRadius;
        public float PrimitiveSweepAngleDeg;
        public int PrimitiveSegmentCount;
        public float PrimitiveArcHeight;
        public Vector2 PrimitiveControlPoint0;
        public Vector2 PrimitiveControlPoint1;
        public float PrimitivePulseSpeed;
        public float PrimitivePulseAmplitude;
    }
}
