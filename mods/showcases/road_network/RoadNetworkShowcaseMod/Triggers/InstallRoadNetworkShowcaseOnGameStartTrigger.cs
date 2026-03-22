using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.Runtime;
using RoadNetworkShowcaseMod.Systems;

namespace RoadNetworkShowcaseMod.Triggers
{
    internal sealed class InstallRoadNetworkShowcaseOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly RoadNetworkShowcaseRuntime _runtime;

        public InstallRoadNetworkShowcaseOnGameStartTrigger(IModContext context, RoadNetworkShowcaseRuntime runtime)
        {
            _context = context;
            _runtime = runtime;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
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
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            if (engine.GetService(CoreServiceKeys.OrderQueue) is OrderQueue orders)
            {
                var plans = new RoadNavPlanStore();
                var moveRuntime = new RoadMoveRuntimeService(engine.World, plans);
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
                if (engine.GetService(CoreServiceKeys.OrderTypeRegistry) is OrderTypeRegistry orderTypeRegistry &&
                    TryResolveRoadMoveFollowOrderTypeId(engine, out int roadMoveFollowOrderTypeId))
                {
                    engine.RegisterSystem(
                        new RoadMoveOrderBindingSystem(engine.World, roadMoveFollowOrderTypeId, plans, moveRuntime),
                        SystemGroup.AbilityActivation);
                    engine.RegisterSystem(
                        new RoadMovePlanSelectionSystem(engine.World, roadMoveFollowOrderTypeId, plans, moveRuntime),
                        SystemGroup.AbilityActivation);
                    engine.RegisterSystem(
                        new RoadMoveExecutionSystem(engine.World),
                        SystemGroup.AbilityActivation);
                    engine.RegisterSystem(
                        new RoadMoveLifecycleSystem(engine.World, engine.GlobalContext, orderTypeRegistry, roadMoveFollowOrderTypeId, plans, moveRuntime),
                        SystemGroup.AbilityActivation);
                    engine.GlobalContext[typeof(RoadNavPlanStore).FullName!] = plans;
                }
            }

            engine.RegisterPresentationSystem(new RoadNetworkPresentationSystem(engine, _runtime));
            if (engine.GetService(CoreServiceKeys.SelectionRuntime) is SelectionRuntime selectionRuntime)
            {
                RoadNavPlanStore plans = engine.GlobalContext.TryGetValue(typeof(RoadNavPlanStore).FullName!, out var planObj) && planObj is RoadNavPlanStore resolvedPlans
                    ? resolvedPlans
                    : new RoadNavPlanStore();
                engine.RegisterPresentationSystem(new RoadSelectedRoutePresentationSystem(engine.World, engine.GlobalContext, selectionRuntime, plans));
            }
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
