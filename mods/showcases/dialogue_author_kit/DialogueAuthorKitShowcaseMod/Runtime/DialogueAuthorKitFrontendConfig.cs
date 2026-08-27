using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativeFrontendMod.Runtime;

namespace DialogueAuthorKitShowcaseMod.Runtime
{
    internal sealed class DialogueAuthorKitFrontendConfig
    {
        public string OwnerId { get; set; } = DialogueAuthorKitIds.OwnerId;
        public string BackdropHex { get; set; } = string.Empty;
        public DialogueAuthorKitSurfaceConfig PromptRibbon { get; set; } = new();
        public DialogueAuthorKitSurfaceConfig VariablesPanel { get; set; } = new();
        public DialogueAuthorKitSurfaceConfig OverlayDialogue { get; set; } = new();
        public DialogueAuthorKitSurfaceConfig ChoiceList { get; set; } = new();
        public DialogueAuthorKitHintConfig Hints { get; set; } = new();
        public DialogueAuthorKitVariableHudConfig[] Variables { get; set; } = Array.Empty<DialogueAuthorKitVariableHudConfig>();

        public static DialogueAuthorKitFrontendConfig Load(Stream stream)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<DialogueAuthorKitFrontendConfig>(stream, options)
                   ?? throw new InvalidOperationException("Failed to deserialize dialogue author kit frontend config.");
        }
    }

    internal sealed class DialogueAuthorKitSurfaceConfig
    {
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

        public NarrativeFrontendAnchor ResolveAnchor() =>
            Enum.TryParse(Anchor, ignoreCase: true, out NarrativeFrontendAnchor anchor)
                ? anchor
                : NarrativeFrontendAnchor.TopLeft;
    }

    internal sealed class DialogueAuthorKitHintConfig
    {
        public string PromptTitle { get; set; } = string.Empty;
        public string ExplorePrompt { get; set; } = string.Empty;
        public string ChoicePrompt { get; set; } = string.Empty;
        public string SkinHint { get; set; } = string.Empty;
    }

    internal sealed class DialogueAuthorKitVariableHudConfig
    {
        public string VariableId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string AccentHex { get; set; } = string.Empty;
    }
}
