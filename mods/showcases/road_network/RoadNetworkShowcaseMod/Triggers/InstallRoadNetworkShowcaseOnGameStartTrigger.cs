using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
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
                if (engine.GetService(CoreServiceKeys.OrderTypeRegistry) is OrderTypeRegistry orderTypeRegistry)
                {
                    engine.RegisterSystem(
                        new RoadRouteFollowSystem(engine.World, engine.GlobalContext, orderTypeRegistry, orders),
                        SystemGroup.AbilityActivation);
                }
            }

            engine.RegisterPresentationSystem(new RoadNetworkPresentationSystem(engine, _runtime));
            if (engine.GetService(CoreServiceKeys.SelectionRuntime) is SelectionRuntime selectionRuntime)
            {
                engine.RegisterPresentationSystem(new RoadSelectedRoutePresentationSystem(engine.World, engine.GlobalContext, selectionRuntime));
            }
            _context.Log("[RoadNetworkShowcaseMod] Road input, planner/executor, AI/capture, chunk streaming, and presentation systems registered.");
            return Task.CompletedTask;
        }
    }
}
