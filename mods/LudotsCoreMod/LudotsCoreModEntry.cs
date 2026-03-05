using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using LudotsCoreMod.Triggers;

namespace LudotsCoreMod
{
    /// <summary>
    /// Core game framework mod - provides base systems, controllers, and configuration.
    /// This mod should be loaded first (priority: -1000) and provides the foundation
    /// for all game mods.
    /// </summary>
    public sealed class LudotsCoreModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[LudotsCoreMod] Loaded - Core game framework initialized");

            // Register core system installation via OnEvent (Phase 2 formal pipeline)
            var installTrigger = new InstallCoreSystemsOnGameStartTrigger();
            context.OnEvent(GameEvents.GameStart, installTrigger.ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
