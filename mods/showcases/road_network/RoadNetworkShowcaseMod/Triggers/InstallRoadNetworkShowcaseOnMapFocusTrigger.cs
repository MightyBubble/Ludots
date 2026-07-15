using System.Threading.Tasks;
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

            engine.GlobalContext[RoadNetworkShowcaseIds.InstalledKey] = true;

            OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires Core OrderQueue.");
            MassNavigationSimulationRuntime simulation = engine.GetService(MassNavigationKeys.SimulationRuntime)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires MassNavigation simulation runtime.");
            OrderTypeRegistry orderTypeRegistry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new System.InvalidOperationException("RoadNetworkShowcaseMod requires Core OrderTypeRegistry.");
            RoadNetworkOrderTypeIds orderTypeIds = RoadNetworkOrderTypeIds.Require(orderTypeRegistry);
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
                new RoadMoveOrderBindingSystem(engine.World, orderTypeIds.RoadMoveFollow, plans, moveRuntime, simulation),
                SystemGroup.RuntimeEntityBinding);
            engine.RegisterSystem(
                new RoadMovePlanSelectionSystem(engine.World, orderTypeIds.RoadMoveFollow, plans, moveRuntime, simulation),
                SystemGroup.RuntimeEntityBinding);
            engine.RegisterSystem(
                new RoadMoveExecutionSystem(engine.World, simulation),
                SystemGroup.RuntimeEntityBinding);
            engine.RegisterSystem(
                new RoadMoveLifecycleSystem(engine.World, engine.GlobalContext, orderTypeRegistry, orderTypeIds.RoadMoveFollow, plans, moveRuntime, simulation),
                SystemGroup.RuntimeEntityBinding);

            engine.RegisterPresentationSystem(new RoadNetworkPresentationSystem(engine, _runtime));
            engine.RegisterPresentationSystem(new RoadSelectedRoutePresentationSystem(engine.World, engine.GlobalContext, plans));
            _context.Log("[RoadNetworkShowcaseMod] Road input, order binding, nav selection, movement execution, AI/capture, chunk streaming, and presentation systems registered.");
            return Task.CompletedTask;
        }

    }
}
