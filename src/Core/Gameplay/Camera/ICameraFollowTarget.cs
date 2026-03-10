using System.Numerics;

namespace Ludots.Core.Gameplay.Camera
{
    public interface ICameraFollowTarget
    {
        bool TryGetPosition(out Vector2 positionCm);
    }
}
