using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ThreeKingdomsTacticsMod.Runtime;
using ThreeKingdomsTacticsMod.Systems;

namespace ThreeKingdomsTacticsMod;

public sealed class ThreeKingdomsTacticsModEntry : IMod
{
    private ThreeKingdomsTacticsRuntime? _runtime;
    private ThreeKingdomsBrowserHost? _browserHost;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RegisterSharedIds();

        _runtime = new ThreeKingdomsTacticsRuntime();
        _browserHost = new ThreeKingdomsBrowserHost(_runtime);

        context.Log("[ThreeKingdomsTacticsMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            var engine = ctx.GetEngine();
            if (engine == null || _runtime == null)
            {
                return Task.CompletedTask;
            }

            if (!engine.GlobalContext.TryGetValue(ThreeKingdomsTacticsIds.InstalledKey, out object? installed) ||
                installed is not bool trueValue ||
                !trueValue)
            {
                engine.GlobalContext[ThreeKingdomsTacticsIds.InstalledKey] = true;
                engine.GlobalContext[ThreeKingdomsTacticsIds.RuntimeKey] = _runtime;
                engine.RegisterPresentationSystem(new ThreeKingdomsTacticsPresentationSystem(engine, _runtime));
                _browserHost?.TryInstall(ctx);
            }

            return Task.CompletedTask;
        });

        context.OnEvent(GameEvents.MapLoaded, ctx => _runtime?.HandleMapFocusedAsync(ctx) ?? Task.CompletedTask);
        context.OnEvent(GameEvents.MapResumed, ctx => _runtime?.HandleMapFocusedAsync(ctx) ?? Task.CompletedTask);
        context.OnEvent(GameEvents.MapUnloaded, ctx => _runtime?.HandleMapUnloadedAsync(ctx) ?? Task.CompletedTask);
    }

    public void OnUnload()
    {
        _browserHost?.Dispose();
        _browserHost = null;
        _runtime = null;
    }

    private static void RegisterSharedIds()
    {
        string[] attributes =
        [
            "Health",
            "Morale",
            "Supplies",
            "Leadership",
            "WarPower",
            "Strategy",
            "Mobility",
            "Fortification",
            "Gold",
            "Grain"
        ];
        for (int i = 0; i < attributes.Length; i++)
        {
            AttributeRegistry.Register(attributes[i]);
        }

        string[] tags =
        [
            "State.TK.Burning",
            "State.TK.Flooded",
            "State.TK.Routed",
            "State.TK.Fortified",
            "State.TK.InDuel",
            "State.TK.Supplied",
            "State.TK.AmbushReady",
            "State.TK.CommandAura",
            "Command.TK.EndTurn",
            "Effect.TK.Skill"
        ];
        for (int i = 0; i < tags.Length; i++)
        {
            TagRegistry.Register(tags[i]);
        }
    }
}
