using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Narrative
{
    public static class NarrativeEventKeys
    {
        public static readonly EventKey Signal = new("Narrative.Signal");
        public static readonly EventKey QuestStageChanged = new("Narrative.QuestStageChanged");
        public static readonly EventKey QuestCompleted = new("Narrative.QuestCompleted");
        public static readonly EventKey DialogueNodeEntered = new("Narrative.DialogueNodeEntered");
        public static readonly EventKey DialogueChoiceCommitted = new("Narrative.DialogueChoiceCommitted");
        public static readonly EventKey CinematicStepEntered = new("Narrative.CinematicStepEntered");
        public static readonly EventKey CinematicCompleted = new("Narrative.CinematicCompleted");
    }

    public static class NarrativeServiceKeys
    {
        public static readonly ServiceKey<string> SignalId = new("Narrative.SignalId");
        public static readonly ServiceKey<int> SignalIntValue = new("Narrative.SignalIntValue");
        public static readonly ServiceKey<string> SignalStringValue = new("Narrative.SignalStringValue");
        public static readonly ServiceKey<string> QuestId = new("Narrative.QuestId");
        public static readonly ServiceKey<string> QuestStageId = new("Narrative.QuestStageId");
        public static readonly ServiceKey<string> DialogueId = new("Narrative.DialogueId");
        public static readonly ServiceKey<string> DialogueNodeId = new("Narrative.DialogueNodeId");
        public static readonly ServiceKey<string> DialogueChoiceId = new("Narrative.DialogueChoiceId");
        public static readonly ServiceKey<string> CinematicId = new("Narrative.CinematicId");
        public static readonly ServiceKey<string> CinematicStepId = new("Narrative.CinematicStepId");
        public static readonly ServiceKey<string> SpeakerName = new("Narrative.SpeakerName");
        public static readonly ServiceKey<string> BodyText = new("Narrative.BodyText");
    }
}
