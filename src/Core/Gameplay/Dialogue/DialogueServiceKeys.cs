using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Dialogue
{
    public static class DialogueEventKeys
    {
        public static readonly EventKey NodeEntered = new("Dialogue.NodeEntered");
        public static readonly EventKey ChoiceCommitted = new("Dialogue.ChoiceCommitted");
    }

    public static class DialogueServiceKeys
    {
        public static readonly ServiceKey<string> DialogueId = new("Dialogue.DialogueId");
        public static readonly ServiceKey<string> DialogueNodeId = new("Dialogue.DialogueNodeId");
        public static readonly ServiceKey<string> DialogueChoiceId = new("Dialogue.DialogueChoiceId");
        public static readonly ServiceKey<string> LineId = new("Dialogue.LineId");
        public static readonly ServiceKey<string> SpeakerId = new("Dialogue.SpeakerId");
        public static readonly ServiceKey<string> BodyText = new("Dialogue.BodyText");
        public static readonly ServiceKey<string> PresentationProfile = new("Dialogue.PresentationProfile");
    }

    public static class DialogueInputActionIds
    {
        public const string Interact = "StoryInteract";
        public const string Advance = "StoryAdvance";
        public const string Skip = "StorySkip";
        public const string Choice1 = "StoryChoice1";
        public const string Choice2 = "StoryChoice2";
        public const string Choice3 = "StoryChoice3";
    }
}
