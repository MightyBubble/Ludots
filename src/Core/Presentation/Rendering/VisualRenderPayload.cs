using System.Numerics;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Rendering
{
    public struct VisualRenderPayload
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
        public AnimatorPackedState Animator;
        public AnimationOverlayRequest AnimationOverlay;
        public VisualVisibility Visibility;
    }
}
