using System.Threading.Tasks;
using ChampionSkillSandboxMod.Runtime;
using ChampionSkillSandboxMod.Triggers;
using CoreInputMod.ViewMode;
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

            Task RegisterViewModesAsync(ScriptContext ctx)
            {
                var engine = ctx.GetEngine();
                if (engine == null)
                {
                    return Task.CompletedTask;
                }

                ViewModeRegistrar.RegisterFromVfs(
                    context,
                    engine.GlobalContext,
                    defaultModeId: null,
                    sourceModId: context.ModId,
                    activateWhenUnset: false);
                return Task.CompletedTask;
            }

            context.OnEvent(GameEvents.GameStart, new InstallChampionSkillSandboxOnGameStartTrigger(context, runtime, toolbarProvider).ExecuteAsync);
            context.OnEvent(GameEvents.GameStart, RegisterViewModesAsync);
            context.OnEvent(GameEvents.MapLoaded, RegisterViewModesAsync);
            context.OnEvent(GameEvents.MapResumed, RegisterViewModesAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
