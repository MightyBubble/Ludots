using System;

namespace Ludots.Core.Presentation.Hud
{
    public enum PresentationSemanticDomain : byte
    {
        Generic = 0,
        Speaker = 1,
        Attribute = 2,
        Relation = 3,
        Enum = 4,
        Tag = 5,
    }

    public enum PresentationImageKind : byte
    {
        Portrait = 0,
        Badge = 1,
        Card = 2,
        Icon = 3,
        Standing = 4,
    }

    public sealed class PresentationSemanticMapDefinition
    {
        public string Id { get; set; } = string.Empty;
        public PresentationSemanticDomain Domain { get; set; }
        public string Key { get; set; } = string.Empty;
        public string TextToken { get; set; } = string.Empty;
    }

    public sealed class PresentationImageAssetDefinition
    {
        public string Id { get; set; } = string.Empty;
        public PresentationImageKind Kind { get; set; } = PresentationImageKind.Portrait;
        public string Path { get; set; } = string.Empty;
        public string GlyphFallback { get; set; } = string.Empty;
    }
}
