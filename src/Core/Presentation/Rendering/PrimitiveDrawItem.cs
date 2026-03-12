using System.Numerics;
using Ludots.Core.Presentation.Components;
namespace Ludots.Core.Presentation.Rendering
{
    public struct PrimitiveDrawItem
    {
        public int MeshAssetId;
        public Vector3 Position;
        public Vector3 Scale;
        public Vector4 Color;
        public int StableId;
        public int MaterialId;
        public int TemplateId;
        public VisualRenderPath RenderPath;
        public VisualMobility Mobility;
        public VisualRuntimeFlags Flags;
        public AnimatorPackedState Animator;
    }
}
