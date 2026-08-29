using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Gameplay.Dialogue
{
    /// <summary>
    /// Presentation-facing choice row. Graph wiring stays inside DialogueRuntime.
    /// </summary>
    public sealed record DialogueChoiceView(
        string ChoiceId,
        string LineId,
        string ResolvedText);

    /// <summary>
    /// Session view for projection. Image fields are presentation imageIds (not filesystem paths).
    /// </summary>
    public sealed record DialogueView(
        string DialogueId,
        string DisplayName,
        string NodeId,
        string LineId,
        string SpeakerId,
        string ResolvedSpeakerName,
        string PortraitImageId,
        string StandingImageId,
        string TextToken,
        string ResolvedText,
        string PresentationProfile,
        string CameraId,
        float AutoAdvanceSeconds,
        float ElapsedSeconds,
        IReadOnlyList<DialogueChoiceView> Choices,
        IReadOnlyList<PresentationTextRun>? BodyRuns = null)
    {
        public bool WaitForInput => Choices.Count == 0 && AutoAdvanceSeconds <= 0f;
        public bool AutoAdvance => Choices.Count == 0 && AutoAdvanceSeconds > 0f;
        public float Progress01 => AutoAdvanceSeconds <= 0f ? 0f : Math.Clamp(ElapsedSeconds / AutoAdvanceSeconds, 0f, 1f);
    }
}
