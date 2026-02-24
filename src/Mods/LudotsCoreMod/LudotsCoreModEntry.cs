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
            
            // Register core system installation trigger
            context.TriggerManager.RegisterTrigger(new InstallCoreSystemsOnGameStartTrigger());
        }

        public void OnUnload()
        {
        }
    }
}
