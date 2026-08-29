namespace Ludots.Core.Gameplay.Story
{
    /// <summary>
    /// Authoritative surfaceKind vocabulary for story presentation profiles.
    /// Core (projector validation) and frontend adapters both reference these — no second literal set.
    /// </summary>
    public static class StoryPresentationSurfaceKinds
    {
        public const string OverlayDialogue = "OverlayDialogue";
        public const string DialogueBubble = "DialogueBubble";
        public const string StandingPortrait = "StandingPortrait";
        public const string SubtitleBubble = "SubtitleBubble";
        public const string ChoiceList = "ChoiceList";
        public const string TransmissionOverlay = "TransmissionOverlay";
    }
}
