namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationSemanticAttributeDefinition
    {
        public required string SemanticKey { get; init; }
        public string AttributeKey { get; init; } = string.Empty;
        public int AttributeId { get; init; }
        public required int LabelTokenId { get; init; }
        public required int CurrentFormatTokenId { get; init; }
        public required int CurrentOverBaseFormatTokenId { get; init; }
        public required int ConstantFormatTokenId { get; init; }
        public int UnitTokenId { get; init; }
    }
}
