using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Rendering
{
    public readonly struct SurfaceDrawItem
    {
        public int StableId { get; init; }
        public int MeshAssetId { get; init; }
        public int MaterialId { get; init; }
        public string SurfaceLayerKey { get; init; }
        public int SortId { get; init; }
        public Vector3 Position { get; init; }
        public Quaternion Rotation { get; init; }
        public Vector3 Scale { get; init; }
        public VisualVisibility Visibility { get; init; }
        public MaterialCustomData MaterialCustomData { get; init; }
    }
}
