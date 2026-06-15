namespace Ludots.Core.Presentation.Rendering
{
    public readonly struct PresentationPayloadField
    {
        public PresentationPayloadField(string name, in PresentationTypedValue value)
        {
            Name = name ?? string.Empty;
            Value = value;
        }

        public string Name { get; }

        public PresentationTypedValue Value { get; }
    }
}
