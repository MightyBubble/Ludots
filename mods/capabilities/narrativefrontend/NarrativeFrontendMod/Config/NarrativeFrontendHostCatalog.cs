using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.Config
{
    /// <summary>
    /// Content-mod host row for story UI. Declared in Frontend/narrative_hosts.json (ArrayById ownerId).
    /// </summary>
    public sealed class NarrativeFrontendHostDefinition
    {
        public string OwnerId { get; set; } = string.Empty;
        public string ActiveMapId { get; set; } = string.Empty;
        public string BackdropHex { get; set; } = string.Empty;
        public NarrativeFrontendBootstrapConfig Bootstrap { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig PromptRibbon { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig VariablesPanel { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig OverlayDialogue { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig DialogueBubble { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig StandingPortrait { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig SubtitleBubble { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig ChoiceList { get; set; } = new();
        public NarrativeFrontendSurfaceChromeConfig TransmissionOverlay { get; set; } = new();
        public NarrativeFrontendHintConfig Hints { get; set; } = new();
        public NarrativeFrontendVariableHudConfig[] Variables { get; set; } = Array.Empty<NarrativeFrontendVariableHudConfig>();
    }

    public sealed class NarrativeFrontendBootstrapConfig
    {
        public string StartDialogueId { get; set; } = string.Empty;
        public string InputContextId { get; set; } = string.Empty;
    }

    public sealed class NarrativeFrontendSurfaceChromeConfig
    {
        public string Anchor { get; set; } = "BottomCenter";
        public float Width { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public int ZIndex { get; set; }
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
                : NarrativeFrontendAnchor.BottomCenter;
    }

    public sealed class NarrativeFrontendHintConfig
    {
        public string PromptTitle { get; set; } = string.Empty;
        public string ExplorePrompt { get; set; } = string.Empty;
        public string ChoicePrompt { get; set; } = string.Empty;
        public string SkinHint { get; set; } = string.Empty;
    }

    public sealed class NarrativeFrontendVariableHudConfig
    {
        public string VariableId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string AccentHex { get; set; } = string.Empty;
    }

    public sealed class NarrativeFrontendHostCatalog
    {
        private readonly List<NarrativeFrontendHostDefinition> _hosts = new();

        public IReadOnlyList<NarrativeFrontendHostDefinition> Hosts => _hosts;

        public static NarrativeFrontendHostCatalog LoadOptional(
            ConfigPipeline pipeline,
            ConfigCatalog? catalog,
            ConfigConflictReport? report = null)
        {
            var result = new NarrativeFrontendHostCatalog();
            if (catalog == null ||
                !catalog.TryGet("Frontend/narrative_hosts.json", out ConfigCatalogEntry entry))
            {
                return result;
            }

            if (entry.MergePolicy != ConfigMergePolicy.ArrayById ||
                !string.Equals(entry.IdField, "ownerId", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Frontend/narrative_hosts.json must be ArrayById with IdField 'ownerId'.");
            }

            IReadOnlyList<MergedConfigEntry> merged = pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            for (int i = 0; i < merged.Count; i++)
            {
                NarrativeFrontendHostDefinition? host =
                    merged[i].Node.Deserialize<NarrativeFrontendHostDefinition>(options);
                if (host == null || string.IsNullOrWhiteSpace(host.OwnerId))
                {
                    throw new InvalidOperationException(
                        $"Frontend/narrative_hosts.json entry at index {i} requires ownerId.");
                }

                result._hosts.Add(host);
            }

            return result;
        }

        public bool TryGetForMap(string mapId, out NarrativeFrontendHostDefinition host)
        {
            for (int i = 0; i < _hosts.Count; i++)
            {
                NarrativeFrontendHostDefinition candidate = _hosts[i];
                if (!string.IsNullOrWhiteSpace(candidate.ActiveMapId) &&
                    string.Equals(candidate.ActiveMapId, mapId, StringComparison.OrdinalIgnoreCase))
                {
                    host = candidate;
                    return true;
                }
            }

            host = null!;
            return false;
        }
    }
}
