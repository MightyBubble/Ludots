using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Config;
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

            engine.GlobalContext[RoadNetworkShowcaseIds.InstalledKey] = true;

            OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires Core OrderQueue.");
            MassNavigationRuntimeBinding runtimeBinding = engine.GetService(MassNavigationKeys.RuntimeBinding)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires MassNavigation runtime binding.");
            var plans = new MovePlanStore(new RoadRouteFinalTargetMovePlanResolver());
            var moveRuntime = new MovePlanRuntimeService(engine.World, plans);
            engine.RegisterSystem(
                new RoadNetworkLocalOrderSourceSystem(engine, _runtime, engine.World, engine.GlobalContext, orders, _context),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new RoadNetworkAiAndCaptureSystem(engine, _runtime, engine.World, engine.GlobalContext, orders),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new RoadNetworkChunkStreamingSystem(engine, _runtime),
                SystemGroup.InputCollection);
            engine.RegisterSystem(
                new RoadNetworkCameraResetSystem(engine.GlobalContext, engine, _runtime),
                SystemGroup.InputCollection);
            if (engine.GetService(CoreServiceKeys.OrderTypeRegistry) is OrderTypeRegistry orderTypeRegistry &&
                TryResolveRoadMoveFollowOrderTypeId(engine, out int roadMoveFollowOrderTypeId))
            {
                engine.RegisterSystem(
                    new RoadMoveOrderBindingSystem(engine.World, roadMoveFollowOrderTypeId, plans, moveRuntime, runtimeBinding),
                    SystemGroup.RuntimeEntityBinding);
                engine.RegisterSystem(
                    new RoadMovePlanSelectionSystem(engine.World, roadMoveFollowOrderTypeId, plans, moveRuntime, runtimeBinding),
                    SystemGroup.RuntimeEntityBinding);
                engine.RegisterSystem(
                    new RoadMoveExecutionSystem(engine.World, runtimeBinding),
                    SystemGroup.RuntimeEntityBinding);
                engine.RegisterSystem(
                    new RoadMoveLifecycleSystem(engine.World, engine.GlobalContext, orderTypeRegistry, roadMoveFollowOrderTypeId, plans, moveRuntime, runtimeBinding),
                    SystemGroup.RuntimeEntityBinding);
                engine.GlobalContext[typeof(MovePlanStore).FullName!] = plans;
            }

            engine.RegisterPresentationSystem(new RoadNetworkPresentationSystem(engine, _runtime));
            MovePlanStore presentationPlans = engine.GlobalContext.TryGetValue(typeof(MovePlanStore).FullName!, out var planObj) && planObj is MovePlanStore resolvedPlans
                ? resolvedPlans
                : new MovePlanStore(new RoadRouteFinalTargetMovePlanResolver());
            engine.RegisterPresentationSystem(new RoadSelectedRoutePresentationSystem(engine, _runtime, engine.World, engine.GlobalContext, presentationPlans));
            _context.Log("[RoadNetworkShowcaseMod] Road input, order binding, nav selection, movement execution, AI/capture, chunk streaming, and presentation systems registered.");
            return Task.CompletedTask;
        }

        private static bool TryResolveRoadMoveFollowOrderTypeId(GameEngine engine, out int orderTypeId)
        {
            orderTypeId = 0;
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.GameConfig.Name, out object? configObj) ||
                configObj is not GameConfig config)
            {
                return false;
            }

            return config.Constants.OrderTypeIds.TryGetValue(RoadNetworkShowcaseIds.RoadMoveFollowOrderTypeKey, out orderTypeId) &&
                   orderTypeId > 0;
        }
    }
}
