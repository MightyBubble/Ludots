using GenreInfoShowcaseMod.Runtime;
using GenreInfoShowcaseMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GenreInfoShowcaseMod
{
    public sealed class GenreInfoShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[GenreInfoShowcaseMod] Loaded");

            var runtime = new GenreInfoShowcaseRuntime();
            context.OnEvent(GameEvents.GameStart, new InstallGenreInfoShowcaseOnGameStartTrigger(context, runtime).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
