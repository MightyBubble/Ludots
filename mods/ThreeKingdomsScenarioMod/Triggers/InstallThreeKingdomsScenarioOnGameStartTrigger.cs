using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ThreeKingdomsScenarioMod.Systems;
using ThreeKingdomsScenarioMod.Runtime;

namespace ThreeKingdomsScenarioMod.Triggers;

internal sealed class InstallThreeKingdomsScenarioOnGameStartTrigger : Trigger
{
    private readonly IModContext _context;
    private readonly ThreeKingdomsScenarioRuntime _runtime;

    public InstallThreeKingdomsScenarioOnGameStartTrigger(IModContext context, ThreeKingdomsScenarioRuntime runtime)
    {
        _context = context;
        _runtime = runtime;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (engine.GetService(CoreServiceKeys.OrderQueue) is OrderQueue orders)
        {
            engine.RegisterSystem(new ThreeKingdomsScenarioLocalOrderSourceSystem(engine, orders, _context), SystemGroup.InputCollection);
            engine.RegisterSystem(new ThreeKingdomsScenarioInteractionSystem(engine, orders), SystemGroup.AbilityActivation);
            engine.RegisterPresentationSystem(new ThreeKingdomsScenarioFacingSystem(engine.World, engine));
        }

        engine.RegisterPresentationSystem(new ThreeKingdomsScenarioHudPresentationSystem(engine, _runtime));
        return Task.CompletedTask;
    }
}
