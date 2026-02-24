using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public interface IScreenRayProvider
    {
        ScreenRay GetRay(Vector2 screenPosition);
    }
}
