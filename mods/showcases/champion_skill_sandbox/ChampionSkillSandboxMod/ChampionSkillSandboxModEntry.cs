using System.Threading.Tasks;
using ChampionSkillSandboxMod.Runtime;
using ChampionSkillSandboxMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod
{
    public sealed class ChampionSkillSandboxModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[ChampionSkillSandboxMod] Loaded");
            ChampionSkillSandboxComponentAuthoring.Register(context.ModId);

            var runtime = new ChampionSkillSandboxRuntime();
            var toolbarProvider = new ChampionSkillCastModeToolbarProvider();

            context.OnEvent(GameEvents.GameStart, new InstallChampionSkillSandboxOnGameStartTrigger(context, runtime, toolbarProvider).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
