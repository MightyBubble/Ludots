using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.MassNavigation;
using Ludots.Core.Modding;
using Ludots.Core.MovePlanning;
using Ludots.Core.Scripting;

namespace MassNavigationMod;

public sealed class MassNavigationModEntry : IMod
{
    internal const string MovePlanOrderAdapterInstalledKey =
        "MassNavigationMod.MovePlanOrderAdapterInstalled";

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Log("[MassNavigationMod] Loaded MassNavigation assets and MovePlan order adapter.");
        context.OnEvent(GameEvents.MapLoaded, InstallMovePlanOrderAdapterAsync);
        context.OnEvent(GameEvents.MapResumed, InstallMovePlanOrderAdapterAsync);
    }

    public void OnUnload()
    {
    }

    internal static Task InstallMovePlanOrderAdapterAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("MassNavigationMod requires a live GameEngine.");

        // Non-navigation maps may still load MassNavigationMod for shared assets.
        // Adapter install is gated by the MassNavigation map SSOT; applicable maps keep
        // InsertSystemBeforeRequired strict and must not soften the missing-anchor contract.
        if (!MassNavigationIds.IsCurrentNavigationMap(engine))
        {
            return Task.CompletedTask;
        }

        if (engine.GlobalContext.ContainsKey(MovePlanOrderAdapterInstalledKey))
        {
            return Task.CompletedTask;
        }

        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("MassNavigationMod requires OrderTypeRegistry.");
        if (!orderTypes.TryGetId(MassNavigationOrderKeys.Move, out int moveOrderTypeId))
        {
            throw new InvalidOperationException(
                $"MassNavigationMod requires GAS/order_types.json to define '{MassNavigationOrderKeys.Move}'.");
        }

        engine.InsertSystemBeforeRequired<IMovePlanCommandGroupExecutionSystem>(
            new MovePlanOrderProjectionSystem(engine.World, moveOrderTypeId),
            SystemGroup.AbilityActivation);
        engine.RegisterSystem(
            new MovePlanOrderLifecycleSystem(engine.World, orderTypes, moveOrderTypeId),
            SystemGroup.AbilityActivation);
        engine.GlobalContext[MovePlanOrderAdapterInstalledKey] = true;
        return Task.CompletedTask;
    }
}
