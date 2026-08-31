using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativeFrontendMod.Runtime;

namespace NarrativeShowcaseMod.Runtime
{
    internal sealed class NarrativeShowcaseFrontendConfig
    {
        public string OwnerId { get; set; } = "NarrativeShowcase";
        public string PlayerLocale { get; set; } = string.Empty;
        public string BackdropHex { get; set; } = string.Empty;
        public NarrativeShowcaseSurfaceConfig PromptRibbon { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig ObjectiveTracker { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig HistoryJournal { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig VariablesPanel { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig NotificationStack { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig OverlayDialogue { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig DialogueBubble { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig StandingPortrait { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig SubtitleBubble { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig TransmissionOverlay { get; set; } = new();
        public NarrativeShowcaseSurfaceConfig Nameplate { get; set; } = new();
        public NarrativeShowcaseHintConfig Hints { get; set; } = new();
        public NarrativeShowcaseTemplateConfig Templates { get; set; } = new();
        public NarrativeShowcaseRoutingConfig Routing { get; set; } = new();
        public NarrativeShowcaseStageHudConfig StageHud { get; set; } = new();
        public NarrativeShowcaseBootstrapConfig Bootstrap { get; set; } = new();
        public NarrativeShowcaseInteractConfig Interact { get; set; } = new();
        public NarrativeShowcaseCastMemberConfig[] Cast { get; set; } = Array.Empty<NarrativeShowcaseCastMemberConfig>();
        public NarrativeShowcaseVariableConfig[] Variables { get; set; } = Array.Empty<NarrativeShowcaseVariableConfig>();
        public Dictionary<string, string> EndingLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static NarrativeShowcaseFrontendConfig Load(Stream stream)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            NarrativeShowcaseFrontendConfig? config = JsonSerializer.Deserialize<NarrativeShowcaseFrontendConfig>(stream, options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize narrative showcase frontend config.");
            }

            return config;
        }

        public string ResolveEndingLabel(int ending)
        {
            return EndingLabels.TryGetValue(ending.ToString(), out string? label) &&
                   !string.IsNullOrWhiteSpace(label)
                ? label
                : throw new InvalidOperationException(
                    $"Narrative frontend config has no endingLabels entry for ending '{ending}'.");
        }

        public IReadOnlyList<string> ResolveChoiceSignals(string choiceId)
        {
            if (string.IsNullOrWhiteSpace(choiceId) || Routing.ChoiceSignals == null)
            {
                return Array.Empty<string>();
            }

            for (int i = 0; i < Routing.ChoiceSignals.Length; i++)
            {
                NarrativeShowcaseChoiceSignalRoute route = Routing.ChoiceSignals[i];
                if (string.Equals(route.ChoiceId, choiceId, StringComparison.OrdinalIgnoreCase))
                {
                    return route.Signals ?? Array.Empty<string>();
                }
            }

            return Array.Empty<string>();
        }
    }

    internal sealed class NarrativeShowcaseSurfaceConfig
    {
        public string LayoutId { get; set; } = string.Empty;
        public string StyleClass { get; set; } = string.Empty;
        public string Anchor { get; set; } = "TopLeft";
        public float Width { get; set; } = 360f;
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public int ZIndex { get; set; } = 40;
        public string Eyebrow { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Footer { get; set; } = string.Empty;
        public string AccentHex { get; set; } = string.Empty;
        public string BackgroundHex { get; set; } = string.Empty;
        public string BorderHex { get; set; } = string.Empty;
        public string ForegroundHex { get; set; } = string.Empty;
        public string MutedHex { get; set; } = string.Empty;

        public NarrativeFrontendAnchor ResolveAnchor()
        {
            return Enum.TryParse(Anchor, ignoreCase: true, out NarrativeFrontendAnchor anchor)
                ? anchor
                : throw new InvalidOperationException($"Narrative frontend surface anchor '{Anchor}' is not a known anchor name.");
        }
    }

    internal sealed class NarrativeShowcaseHintConfig
    {
        public string PromptTitle { get; set; } = string.Empty;
        public string IntroPrompt { get; set; } = string.Empty;
        public string ExplorePrompt { get; set; } = string.Empty;
        public string ExploreWardenPrompt { get; set; } = string.Empty;
        public string ExploreWardenNearPrompt { get; set; } = string.Empty;
        public string ExploreShrinePrompt { get; set; } = string.Empty;
        public string ExploreShrineNearPrompt { get; set; } = string.Empty;
        public string ChoicePrompt { get; set; } = string.Empty;
        public string ContinuePrompt { get; set; } = string.Empty;
        public string CombatPrompt { get; set; } = string.Empty;
        public string ReturnPrompt { get; set; } = string.Empty;
        public string ReturnNearPrompt { get; set; } = string.Empty;
        public string SkipPrompt { get; set; } = string.Empty;
        public string AutoAdvancePrompt { get; set; } = string.Empty;
    }

    internal sealed class NarrativeShowcaseInteractConfig
    {
        public float WardenRangeCm { get; set; } = 420f;
        public float ShrineRangeCm { get; set; } = 360f;
    }

    internal sealed class NarrativeShowcaseCastMemberConfig
    {
        public string EntityName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string AccentHex { get; set; } = string.Empty;
        public float HeadOffsetYCm { get; set; } = 160f;
    }

    /// <summary>Template tokens: words live in the text catalog; composition shape (order/punctuation) stays in code.</summary>
    internal static class NarrativeShowcaseCastDefaults
    {
        public const float HeadOffsetYCm = 140f;
    }

    internal sealed class NarrativeShowcaseTemplateConfig
    {
        public string TaskActivatedPrefix { get; set; } = string.Empty;
        public string DialogueChoiceCommittedPrefix { get; set; } = string.Empty;
        public string TaskCompleted { get; set; } = string.Empty;
        public string BeastSpawned { get; set; } = string.Empty;
        public string RewardApplied { get; set; } = string.Empty;
    }

    internal sealed class NarrativeShowcaseRoutingConfig
    {
        public string[] SubtitleSequenceIds { get; set; } = Array.Empty<string>();
        public NarrativeShowcaseChoiceSignalRoute[] ChoiceSignals { get; set; } = Array.Empty<NarrativeShowcaseChoiceSignalRoute>();
    }

    /// <summary>
    /// Stage-focused HUD: one beat, one job. Debug panels are opt-in, never always-on kitchen sink.
    /// </summary>
    internal sealed class NarrativeShowcaseStageHudConfig
    {
        public bool ShowHistoryAlways { get; set; }
        public bool ShowVariablesAlways { get; set; }
        public bool ShowVariablesWhenNonZero { get; set; }
        public bool ShowObjectiveWithDialogue { get; set; }
        public bool ShowObjectiveWithSequence { get; set; }
        public bool ShowPromptWithDialogue { get; set; }
        public bool ShowPromptWithSequence { get; set; }
        public bool HideCastDuringStandingPortrait { get; set; } = true;
        public bool HidePanelsDuringStandingPortrait { get; set; } = true;
    }

    internal sealed class NarrativeShowcaseChoiceSignalRoute
    {
        public string ChoiceId { get; set; } = string.Empty;
        public string[] Signals { get; set; } = Array.Empty<string>();
    }

    internal sealed class NarrativeShowcaseVariableConfig
    {
        public string VariableId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string AccentHex { get; set; } = string.Empty;
    }

    internal sealed class NarrativeShowcaseBootstrapConfig
    {
        /// <summary>
        /// 纯 Story 车道：VirtualCamera 过场 + 完整对话，不挂 Task/Trigger 副作用图。
        /// 也可由非 story-ember 的 panelTheme 自动启用（主题壳演示换皮）。
        /// </summary>
        public bool PureStoryLane { get; set; }

        public string PureIntroSequenceId { get; set; } = "Sequence.Demo.Overture";
        public int BeastSpawnXcm { get; set; } = 1960;
        public int BeastSpawnYcm { get; set; } = 940;
        public float BeastSpawnFacingRad { get; set; } = 3.14159f;
        public int HistoryCapacity { get; set; } = 14;
        public string PureBriefingDialogueId { get; set; } = "Dialogue.Demo.Audience";
    }
}
