using System;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NarrativeSlicesMod.Runtime;

namespace NarrativeSlicesMod
{
    public sealed class NarrativeSlicesModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Log("[NarrativeSlicesMod] Loaded");
            var runtime = new NarrativeSlicesRuntime();

            context.OnEvent(GameEvents.GameStart, runtime.HandleGameStartAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapLoadedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapLoadedAsync);
            context.OnEvent(NarrativeEventKeys.DialogueNodeEntered, runtime.HandleDialogueNodeEnteredAsync);
            context.OnEvent(NarrativeEventKeys.DialogueChoiceCommitted, runtime.HandleDialogueChoiceCommittedAsync);
            context.OnEvent(NarrativeEventKeys.CinematicStepEntered, runtime.HandleCinematicStepEnteredAsync);
            context.OnEvent(NarrativeEventKeys.CinematicCompleted, runtime.HandleCinematicCompletedAsync);
            context.OnEvent(TaskEventKeys.Signal, runtime.HandleTaskSignalAsync);
            context.OnEvent(TaskEventKeys.Offered, runtime.HandleTaskOfferedAsync);
            context.OnEvent(TaskEventKeys.Activated, runtime.HandleTaskActivatedAsync);
            context.OnEvent(TaskEventKeys.Completed, runtime.HandleTaskCompletedAsync);
            context.OnEvent(TaskEventKeys.Failed, runtime.HandleTaskFailedAsync);
            context.OnEvent(TaskEventKeys.Abandoned, runtime.HandleTaskAbandonedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
