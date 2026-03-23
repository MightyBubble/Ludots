using ResponseChainShowcaseMod.Runtime;
using ResponseChainShowcaseMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Physics2D.Config;
using Ludots.Core.Scripting;

namespace ResponseChainShowcaseMod
{
    public sealed class ResponseChainShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[ResponseChainShowcaseMod] Loaded");
            Physics2DComponentAuthoring.Register(context.ModId);

            var runtime = new ResponseChainShowcaseRuntime();
            context.OnEvent(
                GameEvents.GameStart,
                new InstallResponseChainShowcaseOnGameStartTrigger(context, runtime).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
