using System;

namespace Ludots.Core.Presentation.Hud
{
    public enum PresentationTextTemplatePartKind : byte
    {
        Literal = 0,
        Argument = 1,
        StyledLiteral = 2,
    }

    public readonly struct PresentationTextTemplatePart
    {
        public PresentationTextTemplatePart(
            PresentationTextTemplatePartKind kind,
            string literal,
            int argIndex,
            PresentationTextStyleOverride style = default)
        {
            Kind = kind;
            Literal = literal ?? string.Empty;
            ArgIndex = argIndex;
            Style = style;
        }

        public PresentationTextTemplatePartKind Kind { get; }

        public string Literal { get; }

        public int ArgIndex { get; }

        public PresentationTextStyleOverride Style { get; }
    }

    public sealed class PresentationTextTemplate
    {
        private readonly PresentationTextTemplatePart[] _parts;

        public PresentationTextTemplate(string source, PresentationTextTemplatePart[] parts)
        {
            Source = source ?? string.Empty;
            _parts = parts ?? Array.Empty<PresentationTextTemplatePart>();
            HasStyledParts = false;
            for (int i = 0; i < _parts.Length; i++)
            {
                PresentationTextTemplatePart part = _parts[i];
                if (part.Kind == PresentationTextTemplatePartKind.StyledLiteral || !part.Style.IsEmpty)
                {
                    HasStyledParts = true;
                    break;
                }
            }
        }

        public string Source { get; }

        public bool HasStyledParts { get; }

        public ReadOnlySpan<PresentationTextTemplatePart> GetParts() => _parts;
    }
}
