using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation2D.Services;

public sealed class NullNavigation2DService : INavigation2DService
{
    public static readonly NullNavigation2DService Instance = new();

    private NullNavigation2DService()
    {
    }

    public bool TryGetDesiredVelocityCm(in Fix64Vec2 positionCm, in Fix64Vec2 currentVelocityCm, out Fix64Vec2 desiredVelocityCm)
    {
        desiredVelocityCm = default;
        return false;
    }
}

