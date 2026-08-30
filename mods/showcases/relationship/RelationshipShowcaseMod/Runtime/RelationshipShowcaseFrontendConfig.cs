using System;
using System.IO;
using System.Text.Json;
using NarrativeFrontendMod.Runtime;

namespace RelationshipShowcaseMod.Runtime
{
    public sealed class RelationshipShowcaseFrontendConfig
    {
        public string OwnerId { get; set; } = "RelationshipShowcase";
        public string BackdropHex { get; set; } = string.Empty;
        public RelationshipShowcaseSurfaceConfig PromptRibbon { get; set; } = new();
        public RelationshipShowcaseSurfaceConfig StatusPanel { get; set; } = new();
        public RelationshipShowcaseSurfaceConfig RelationshipNotebook { get; set; } = new();
        public RelationshipShowcaseSurfaceConfig NotificationStack { get; set; } = new();
        public RelationshipShowcaseSurfaceConfig ThreatBanner { get; set; } = new();
        public RelationshipShowcaseSurfaceConfig FlowReview { get; set; } = new();

        public static RelationshipShowcaseFrontendConfig Load(Stream stream)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            RelationshipShowcaseFrontendConfig? config = JsonSerializer.Deserialize<RelationshipShowcaseFrontendConfig>(stream, options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize relationship showcase frontend config.");
            }

            return config;
        }
    }

    public sealed class RelationshipShowcaseSurfaceConfig
    {
        public string LayoutId { get; set; } = string.Empty;
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
                : throw new InvalidOperationException(
                    $"Relationship frontend surface anchor '{Anchor}' is not a known anchor name.");
        }
    }
}
