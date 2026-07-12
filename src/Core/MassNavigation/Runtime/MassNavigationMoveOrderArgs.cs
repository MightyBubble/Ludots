using System;
using System.Numerics;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.MassNavigation.Runtime;

public readonly struct MassNavigationMoveOrderArgs
{
    private MassNavigationMoveOrderArgs(Vector2 destinationCm)
    {
        DestinationCm = destinationCm;
    }

    public Vector2 DestinationCm { get; }

    public static OrderArgs Encode(Vector2 destinationCm)
    {
        ValidateFiniteDestination(destinationCm, nameof(destinationCm));
        return OrderArgs.CreateSingleWorldCm(new Vector3(destinationCm.X, 0f, destinationCm.Y));
    }

    public static MassNavigationMoveOrderArgs Decode(in Order order)
    {
        if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single)
        {
            throw new InvalidOperationException(
                $"MassNavigation move order {order.OrderId} requires a single WorldCm spatial target.");
        }

        Vector2 destinationCm = new(order.Args.Spatial.WorldCm.X, order.Args.Spatial.WorldCm.Z);
        if (!float.IsFinite(order.Args.Spatial.WorldCm.X) ||
            !float.IsFinite(order.Args.Spatial.WorldCm.Y) ||
            !float.IsFinite(order.Args.Spatial.WorldCm.Z))
        {
            throw new InvalidOperationException(
                $"MassNavigation move order {order.OrderId} requires a finite WorldCm spatial target.");
        }

        if (order.Args.I0 != 0 ||
            order.Args.I1 != 0 ||
            order.Args.I2 != 0 ||
            order.Args.I3 != 0 ||
            order.Args.F0 != 0f ||
            order.Args.F1 != 0f ||
            order.Args.F2 != 0f ||
            order.Args.F3 != 0f ||
            order.Args.Spatial.A0 != 0 ||
            order.Args.Spatial.A1 != 0 ||
            order.Args.Spatial.A2 != 0 ||
            order.Args.Spatial.PointCount != 0)
        {
            throw new InvalidOperationException(
                $"MassNavigation move order {order.OrderId} contains retired non-spatial payload fields.");
        }

        return new MassNavigationMoveOrderArgs(destinationCm);
    }

    private static void ValidateFiniteDestination(Vector2 destinationCm, string parameterName)
    {
        if (!float.IsFinite(destinationCm.X) || !float.IsFinite(destinationCm.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                destinationCm,
                "MassNavigation move order destination must contain finite coordinates.");
        }
    }
}
