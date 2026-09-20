namespace Ludots.Core.Presentation.Hud
{
    public readonly struct PresentationTextRun
    {
        public PresentationTextRun(string text, PresentationTextStyleOverride style)
        {
            Text = text ?? string.Empty;
            Style = style;
        }

        public string Text { get; }

        public PresentationTextStyleOverride Style { get; }
    }
}
