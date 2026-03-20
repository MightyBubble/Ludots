using System.Numerics;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Rendering
{
    public struct SkinnedVisualBatchItem
    {
        public int StableId;
        public int MeshAssetId;
        public int MaterialId;
        public int TemplateId;
        public VisualRenderPath RenderPath;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public Vector4 Color;
        public AnimatorPackedState Animator;
        public AnimationOverlayRequest AnimationOverlay;
        public VisualVisibility Visibility;
    }
}
