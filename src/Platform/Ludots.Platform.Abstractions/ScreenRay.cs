using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public readonly struct ScreenRay
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;

        public ScreenRay(Vector3 origin, Vector3 direction)
        {
            Origin = origin;
            Direction = direction;
        }
    }
}
