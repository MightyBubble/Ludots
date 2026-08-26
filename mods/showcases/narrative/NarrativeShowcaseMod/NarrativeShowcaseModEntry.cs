using Ludots.Core.Gameplay.Dialogue;
using Ludots.Core.Gameplay.Sequencer;
using Ludots.Core.Gameplay.Tasks;
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
            context.OnEvent(DialogueEventKeys.NodeEntered, runtime.HandleDialogueNodeEnteredAsync);
            context.OnEvent(DialogueEventKeys.ChoiceCommitted, runtime.HandleDialogueChoiceCommittedAsync);
            context.OnEvent(SequencerEventKeys.SectionEntered, runtime.HandleSequencerSectionEnteredAsync);
            context.OnEvent(SequencerEventKeys.Completed, runtime.HandleSequencerCompletedAsync);
            context.OnEvent(SequencerEventKeys.SignalFired, runtime.HandleSequencerSignalFiredAsync);
        }

        public void OnUnload()
        {
        }
    }
}
