using System;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NarrativeChainShowcaseMod.Runtime;

namespace NarrativeChainShowcaseMod
{
    public sealed class NarrativeChainShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Log("[NarrativeChainShowcaseMod] Loaded");
            var runtime = new NarrativeChainRuntime();

            context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapLoadedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapLoadedAsync);
            context.OnEvent(NarrativeEventKeys.CinematicStepEntered, runtime.HandleCinematicStepEnteredAsync);
            context.OnEvent(NarrativeEventKeys.CinematicCompleted, runtime.HandleCinematicCompletedAsync);
            context.OnEvent(TaskEventKeys.Activated, runtime.HandleTaskActivatedAsync);
            context.OnEvent(TaskEventKeys.Completed, runtime.HandleTaskCompletedAsync);
            context.OnEvent(TaskEventKeys.Signal, runtime.HandleTaskSignalAsync);
        }

        public void OnUnload()
        {
        }
    }
}
