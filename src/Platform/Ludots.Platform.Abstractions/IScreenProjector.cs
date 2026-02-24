using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public interface IScreenProjector
    {
        Vector2 WorldToScreen(Vector3 worldPosition);
    }
}
