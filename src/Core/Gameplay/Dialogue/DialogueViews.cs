using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Dialogue
{
    public sealed record DialogueChoiceView(
        string ChoiceId,
        string LineId,
        string ResolvedText,
        string NextNode,
        string ConditionGraphId,
        string ActionGraphId);

    public sealed record DialogueView(
        string DialogueId,
        string DisplayName,
        string NodeId,
        string LineId,
        string SpeakerId,
        string ResolvedSpeakerName,
        string PortraitImageSrc,
        string StandingImageSrc,
        string TextToken,
        string ResolvedText,
        string PresentationProfile,
        string CameraId,
        float AutoAdvanceSeconds,
        float ElapsedSeconds,
        IReadOnlyList<DialogueChoiceView> Choices)
    {
        public bool WaitForInput => Choices.Count == 0 && AutoAdvanceSeconds <= 0f;
        public bool AutoAdvance => Choices.Count == 0 && AutoAdvanceSeconds > 0f;
        public float Progress01 => AutoAdvanceSeconds <= 0f ? 0f : Math.Clamp(ElapsedSeconds / AutoAdvanceSeconds, 0f, 1f);
    }
}
