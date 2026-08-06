using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace CombatStanceBehaviorMod.Components;

public static class CombatStanceOrderPayload
{
    public static void ConfigureAttackMove(ref Order order, int leashRadiusCm, Vector3 destinationWorldCm)
    {
        if (leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance attack-move payload requires a positive leash radius.");
        }

        OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I0, leashRadiusCm);
        order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
        order.Args.Spatial.Mode = OrderCollectionMode.Single;
        order.Args.Spatial.WorldCm = destinationWorldCm;
    }

    public static int RequireAttackMoveLeashRadiusCm(in Order order, string orderName)
    {
        int leashRadiusCm = order.Args.I0;
        if (leashRadiusCm <= 0)
        {
            throw new InvalidOperationException($"Combat stance {orderName} payload requires a positive leash radius.");
        }

        return leashRadiusCm;
    }

    public static void ConfigureGuard(ref Order order, Entity target, int radiusCm, int leashRadiusCm)
    {
        if (target == default)
        {
            throw new InvalidOperationException("Combat stance guard payload requires a target entity.");
        }

        if (radiusCm <= 0 || leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance guard payload requires positive guard and leash radii.");
        }

        order.Target = target;
        OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I0, radiusCm);
        OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I1, leashRadiusCm);
    }

    public static void RequireGuardRadii(in Order order, out int radiusCm, out int leashRadiusCm)
    {
        radiusCm = order.Args.I0;
        leashRadiusCm = order.Args.I1;
        if (radiusCm <= 0 || leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance guard payload requires positive guard and leash radii.");
        }
    }

    public static void ConfigureSetCombatStance(
        ref Order order,
        int stance,
        int leashRadiusCm,
        int retaliationTtlSteps)
    {
        if (!CombatStances.IsDefined(stance))
        {
            throw new InvalidOperationException($"Combat stance payload references unknown stance {stance}.");
        }

        if (stance != CombatStances.HoldFire && leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance payload requires a positive leash radius for non-HoldFire stances.");
        }

        OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I0, stance);
        OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I1, leashRadiusCm);
        OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I2, retaliationTtlSteps);
    }

    public static void ReadSetCombatStance(
        in Order order,
        int defaultRetaliationTtlSteps,
        out int stance,
        out int leashRadiusCm,
        out int retaliationTtlSteps)
    {
        stance = order.Args.I0;
        if (!CombatStances.IsDefined(stance))
        {
            throw new InvalidOperationException($"Combat stance setCombatStance payload references unknown stance {stance}.");
        }

        leashRadiusCm = order.Args.I1;
        retaliationTtlSteps = order.Args.I2 > 0 ? order.Args.I2 : defaultRetaliationTtlSteps;
        if (stance != CombatStances.HoldFire && leashRadiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance setCombatStance payload requires a positive leash radius for non-HoldFire stances.");
        }
    }

    public static void ConfigureScatter(ref Order order, int radiusCm)
    {
        if (radiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance scatter payload requires a positive radius.");
        }

        OrderBuilder.SetIntArg(ref order, OrderIntArgSlot.I0, radiusCm);
    }

    public static int RequireScatterRadiusCm(in Order order)
    {
        int radiusCm = order.Args.I0;
        if (radiusCm <= 0)
        {
            throw new InvalidOperationException("Combat stance scatter payload requires a positive radius.");
        }

        return radiusCm;
    }
}
