using System.Numerics;

namespace Ludots.Core.Presentation.Components
{
    public struct PresentationLocalBounds
    {
        public Vector3 Center;
        public Vector3 Extents;

        public static PresentationLocalBounds Create(in Vector3 center, in Vector3 extents)
        {
            return new PresentationLocalBounds
            {
                Center = center,
                Extents = extents,
            };
        }
    }
}
