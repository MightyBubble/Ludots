using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.MovePlanning;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;
using RoadNetworkShowcaseMod.Systems;

namespace RoadNetworkShowcaseMod.Triggers
{
    internal sealed class InstallRoadNetworkShowcaseOnMapFocusTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly RoadNetworkShowcaseRuntime _runtime;

        public InstallRoadNetworkShowcaseOnMapFocusTrigger(IModContext context, RoadNetworkShowcaseRuntime runtime, EventKey eventKey)
        {
            _context = context;
            _runtime = runtime;
            EventKey = eventKey;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (!RoadNetworkShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(RoadNetworkShowcaseIds.InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires Core OrderQueue.");
            MassNavigationRuntimeBinding binding = engine.GetService(MassNavigationKeys.RuntimeBinding)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires MassNavigation runtime binding.");
            MassNavigationSimulationRuntime simulation = binding.Current
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires an active MassNavigation runtime binding during map focus.");
            if (!string.Equals(binding.CurrentMapId.Value, engine.CurrentMapSession?.MapId.Value, System.StringComparison.Ordinal))
            {
                throw new System.InvalidOperationException(
                    $"RoadNetworkShowcaseMod active MassNavigation runtime map '{binding.CurrentMapId.Value}' does not match focused map '{engine.CurrentMapSession?.MapId.Value ?? "<none>"}'.");
            }

            OrderTypeRegistry orderTypeRegistry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires Core OrderTypeRegistry.");
            int roadMoveFollowOrderTypeId = ResolveRoadMoveFollowOrderTypeId(engine, orderTypeRegistry);
            var plans = new MovePlanStore(engine.World, new RoadRouteFinalTargetMovePlanResolver());
            var moveRuntime = new MovePlanRuntimeService(engine.World, plans);
            engine.GlobalContext[typeof(MovePlanStore).FullName!] = plans;
            engine.RegisterSystem(
                new RoadNetworkLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _context),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new RoadNetworkAiAndCaptureSystem(engine.World, engine.GlobalContext, orders),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new RoadNetworkChunkStreamingSystem(engine, _runtime),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new RoadNetworkCameraResetSystem(engine.GlobalContext, engine, _runtime),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new RoadMoveOrderBindingSystem(engine.World, roadMoveFollowOrderTypeId, plans, moveRuntime, binding),
                SystemGroup.RuntimeEntityBinding);
            engine.RegisterSystem(
                new RoadMovePlanSelectionSystem(engine.World, roadMoveFollowOrderTypeId, plans, moveRuntime, binding),
                SystemGroup.RuntimeEntityBinding);
            engine.RegisterSystem(
                new RoadMoveExecutionSystem(engine.World, binding),
                SystemGroup.RuntimeEntityBinding);
            engine.RegisterSystem(
                new RoadMoveLifecycleSystem(engine.World, engine.GlobalContext, orderTypeRegistry, roadMoveFollowOrderTypeId, plans, moveRuntime, binding),
                SystemGroup.RuntimeEntityBinding);
            engine.GlobalContext[typeof(MovePlanStore).FullName!] = plans;

            engine.RegisterPresentationSystem(new RoadNetworkPresentationSystem(engine, _runtime));
            MovePlanStore presentationPlans = engine.GlobalContext.TryGetValue(typeof(MovePlanStore).FullName!, out var planObj) && planObj is MovePlanStore resolvedPlans
                ? resolvedPlans
                : new MovePlanStore(engine.World, new RoadRouteFinalTargetMovePlanResolver());
            engine.RegisterPresentationSystem(new RoadSelectedRoutePresentationSystem(engine.World, engine.GlobalContext, presentationPlans));
            engine.GlobalContext[RoadNetworkShowcaseIds.InstalledKey] = true;
            _context.Log("[RoadNetworkShowcaseMod] Road input, order binding, nav selection, movement execution, AI/capture, chunk streaming, and presentation systems registered.");
            return Task.CompletedTask;
        }

        private static int ResolveRoadMoveFollowOrderTypeId(GameEngine engine, OrderTypeRegistry orderTypeRegistry)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config)
            {
                throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires GameConfig before resolving roadMoveFollow order type.");
            }

            if (!config.Constants.OrderTypeIds.TryGetValue(RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey, out int orderTypeId) ||
                orderTypeId <= 0)
            {
                throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires configured roadMoveFollow order type id.");
            }

            if (!orderTypeRegistry.TryGetId(RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey, out int registeredOrderTypeId) ||
                registeredOrderTypeId != orderTypeId ||
                !orderTypeRegistry.IsRegistered(orderTypeId))
            {
                throw new System.InvalidOperationException(
                    $"RoadNetworkShowcaseMod requires registered order type '{RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey}' id {orderTypeId}.");
            }

            return orderTypeId;
        }
    }
}
