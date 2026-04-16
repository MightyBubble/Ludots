using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PrefabDecalPart
    {
        public PrefabDecalPart(int materialId, Vector2 size, bool alignToSurface = true)
        {
            MaterialId = materialId;
            Size = size;
            AlignToSurface = alignToSurface;
        }

        public int MaterialId { get; }

        public Vector2 Size { get; }

        public bool AlignToSurface { get; }
    }
}
