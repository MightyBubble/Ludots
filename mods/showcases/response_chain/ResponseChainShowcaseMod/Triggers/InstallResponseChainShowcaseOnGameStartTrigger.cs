using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ResponseChainShowcaseMod.Runtime;
using ResponseChainShowcaseMod.Systems;

namespace ResponseChainShowcaseMod.Triggers
{
    internal sealed class InstallResponseChainShowcaseOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly ResponseChainShowcaseRuntime _runtime;

        public InstallResponseChainShowcaseOnGameStartTrigger(IModContext context, ResponseChainShowcaseRuntime runtime)
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

            if (engine.GlobalContext.TryGetValue(ResponseChainShowcaseIds.RuntimeInstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[ResponseChainShowcaseIds.RuntimeInstalledKey] = true;
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out object? ordersObj) &&
                ordersObj is OrderQueue orders)
            {
                engine.RegisterSystem(
                    new ResponseChainShowcaseLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _context),
                    SystemGroup.InputCollection);
            }

            engine.RegisterPresentationSystem(new ResponseChainShowcasePresentationSystem(engine, _runtime));
            _context.Log("[ResponseChainShowcaseMod] Registered local order source and showcase HUD runtime.");
            return Task.CompletedTask;
        }
    }
}
