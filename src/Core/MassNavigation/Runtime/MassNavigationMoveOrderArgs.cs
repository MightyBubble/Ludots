using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.MassNavigation.Runtime;

public readonly struct MassNavigationMoveOrderArgs
{
    private MassNavigationMoveOrderArgs(
        Vector2 destinationCm,
        MassNavigationFormationMode formationMode,
        float rotationRadians,
        Entity collectionOwner,
        EntityCollectionHandle collectionHandle)
    {
        DestinationCm = destinationCm;
        FormationMode = formationMode;
        RotationRadians = rotationRadians;
        CollectionOwner = collectionOwner;
        CollectionHandle = collectionHandle;
    }

    public Vector2 DestinationCm { get; }
    public MassNavigationFormationMode FormationMode { get; }
    public float RotationRadians { get; }
    public Entity CollectionOwner { get; }
    public EntityCollectionHandle CollectionHandle { get; }

    public static OrderArgs Encode(
        Vector2 destinationCm,
        MassNavigationFormationMode formationMode,
        float rotationRadians,
        Entity collectionOwner,
        EntityCollectionHandle collectionHandle)
    {
        ValidateFormationMode(formationMode, nameof(formationMode));
        if (collectionOwner == Entity.Null || !collectionHandle.IsValid)
        {
            throw new InvalidOperationException("MassNavigation move orders require an explicit command-source collection handle.");
        }

        return new OrderArgs
        {
            I0 = (int)formationMode,
            F0 = rotationRadians,
            Spatial = new OrderSpatial
            {
                Kind = OrderSpatialKind.WorldCm,
                Mode = OrderCollectionMode.Single,
                WorldCm = new Vector3(destinationCm.X, 0f, destinationCm.Y),
            },
            Selection = new OrderSelectionReference
            {
                CollectionOwner = collectionOwner,
                CollectionHandle = collectionHandle,
            }
        };
    }

    public static MassNavigationMoveOrderArgs Decode(in Order order)
    {
        if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single)
        {
            throw new InvalidOperationException(
                $"MassNavigation move order {order.OrderId} requires a single WorldCm spatial target.");
        }

        if (!order.Args.Selection.HasCollection)
        {
            throw new InvalidOperationException(
                $"MassNavigation move order {order.OrderId} requires a command-source collection handle.");
        }

        MassNavigationFormationMode formationMode = DecodeFormationMode(order.Args.I0, order.OrderId);
        return new MassNavigationMoveOrderArgs(
            new Vector2(order.Args.Spatial.WorldCm.X, order.Args.Spatial.WorldCm.Z),
            formationMode,
            order.Args.F0,
            order.Args.Selection.CollectionOwner,
            order.Args.Selection.CollectionHandle);
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
