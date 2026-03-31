using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NarrativeShowcaseMod.Runtime;

namespace NarrativeShowcaseMod
{
    public sealed class NarrativeShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[NarrativeShowcaseMod] Loaded");
            var runtime = new NarrativeShowcaseRuntime(context);

            context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
            context.OnEvent(NarrativeEventKeys.Signal, runtime.HandleNarrativeSignalAsync);
            context.OnEvent(NarrativeEventKeys.CinematicCompleted, runtime.HandleCinematicCompletedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
