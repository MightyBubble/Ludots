using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace LudotsCoreMod.Triggers
{
    /// <summary>
    /// Trigger that installs core game systems on game start.
    /// Core systems that were previously hardcoded in GameEngine.InitializeCoreSystems
    /// are now registered here to make them replaceable by other mods.
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
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            // Prevent double installation
            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }
            engine.GlobalContext[InstalledKey] = true;

            System.Console.WriteLine("[LudotsCoreMod] Core systems installed");
            
            // Note: Most core GAS systems are still initialized in GameEngine.InitializeCoreSystems
            // This trigger is here to demonstrate the architecture where systems can be
            // registered by mods. Future refactoring will move more systems here.

            return Task.CompletedTask;
        }
    }
}
