using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace LudotsCoreMod.Triggers
{
    /// <summary>
    /// Installs core game systems on game start so that other mods can replace them.
    /// </summary>
    public sealed class InstallCoreSystemsOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "LudotsCoreMod.Installed";

        public InstallCoreSystemsOnGameStartTrigger()
        {
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            if (!context.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }
            engine.GlobalContext[InstalledKey] = true;

            System.Console.WriteLine("[LudotsCoreMod] Core systems installed");
            
            // Most core GAS systems are still initialized in GameEngine.InitializeCoreSystems;
            // this trigger exists to demonstrate the architecture where systems can be registered by mods.

            return Task.CompletedTask;
        }
    }
}
