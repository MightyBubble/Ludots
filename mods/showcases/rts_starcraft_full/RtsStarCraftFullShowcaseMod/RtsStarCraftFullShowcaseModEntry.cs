using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RtsStarCraftFullShowcaseMod.Systems;

namespace RtsStarCraftFullShowcaseMod;

public sealed class RtsStarCraftFullShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[RtsStarCraftFullShowcaseMod] Loaded — 100-unit StarCraft Full RTS showcase.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
    }

    public void OnUnload()
    {
    }

    private static Task OnGameStartAsync(ScriptContext context)
    {
        GameEngine engine = context.GetEngine()
            ?? throw new InvalidOperationException("GameEngine is missing from ScriptContext.");

        engine.RegisterSystem(new RtsScFullItemUpgradeBootstrapSystem(engine), SystemGroup.SchemaUpdate);
        engine.RegisterSystem(new RtsScFullCombatBootstrapSystem(engine), SystemGroup.AbilityActivation);
        engine.RegisterSystem(new RtsScFullScenarioSystem(engine), SystemGroup.EffectProcessing);
        return Task.CompletedTask;
    }
}
