using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Services;

public interface INavigation2DService
{
    bool TryGetDesiredVelocityCm(in Fix64Vec2 positionCm, in Fix64Vec2 currentVelocityCm, out Fix64Vec2 desiredVelocityCm);
}

