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
    private const string MovePlanOrderAdapterInstalledKey =
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

    private static Task InstallMovePlanOrderAdapterAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("MassNavigationMod requires a live GameEngine.");
        if (engine.GlobalContext.ContainsKey(MovePlanOrderAdapterInstalledKey))
        {
            return Task.CompletedTask;
        }

        // MovePlan order adapter anchors on MassNavigationMovePlanExecutionSystem, which is only
        // installed when MassNavigationRuntime focuses a matching mapId. Maps that deliberately
        // disable MassNav (mismatched MassNavigationConfig.mapId) must skip adapter insertion.
        if (!MassNavigationIds.IsCurrentNavigationMap(engine))
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
