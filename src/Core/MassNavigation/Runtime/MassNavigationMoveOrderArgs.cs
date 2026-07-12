using System;
using System.Numerics;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.MassNavigation.Runtime;

public readonly struct MassNavigationMoveOrderArgs
{
    private MassNavigationMoveOrderArgs(
        Vector2 destinationCm,
        MassNavigationFormationMode formationMode,
        float rotationRadians,
        bool hasExplicitRotation)
    {
        DestinationCm = destinationCm;
        FormationMode = formationMode;
        RotationRadians = rotationRadians;
        HasExplicitRotation = hasExplicitRotation;
    }

    public Vector2 DestinationCm { get; }
    public MassNavigationFormationMode FormationMode { get; }
    public float RotationRadians { get; }
    public bool HasExplicitRotation { get; }

    public static OrderArgs Encode(
        Vector2 destinationCm,
        MassNavigationFormationMode formationMode,
        float rotationRadians)
    {
        ValidateFormationMode(formationMode, nameof(formationMode));

        OrderArgs args = OrderArgs.CreateSingleWorldCm(new Vector3(destinationCm.X, 0f, destinationCm.Y));
        args.I0 = (int)formationMode;
        args.I1 = 1;
        args.F0 = rotationRadians;
        return args;
    }

    public static MassNavigationMoveOrderArgs Decode(in Order order)
    {
        if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single)
        {
            throw new InvalidOperationException(
                $"MassNavigation move order {order.OrderId} requires a single WorldCm spatial target.");
        }

        MassNavigationFormationMode formationMode = DecodeFormationMode(order.Args.I0, order.OrderId);
        return new MassNavigationMoveOrderArgs(
            new Vector2(order.Args.Spatial.WorldCm.X, order.Args.Spatial.WorldCm.Z),
            formationMode,
            order.Args.F0,
            hasExplicitRotation: (order.Args.I1 & 1) != 0);
    }

    private static MassNavigationFormationMode DecodeFormationMode(int rawValue, int orderToken)
    {
        if (!Enum.IsDefined(typeof(MassNavigationFormationMode), rawValue))
        {
            throw new InvalidOperationException(
                $"MassNavigation move order {orderToken} references unsupported formation mode value {rawValue}.");
        }

        return (MassNavigationFormationMode)rawValue;
    }

    private static void ValidateFormationMode(MassNavigationFormationMode value, string parameterName)
    {
        if (!Enum.IsDefined(typeof(MassNavigationFormationMode), value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "MassNavigation move order formation mode is not defined.");
        }
    }
}
