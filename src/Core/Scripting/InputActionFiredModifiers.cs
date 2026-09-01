namespace Ludots.Core.Scripting
{
    /// <summary>
    /// Bit assignments of the <see cref="MapTriggerEventPayloadKeys.Modifiers"/> payload:
    /// which semantic modifier actions were held when an InputActionFired fired, read
    /// from the authoritative input snapshot. Bits map to the established semantic
    /// action ids (QueueModifier / PrecisionModifier); further modifiers take the next
    /// power of two without an engine enum change.
    /// </summary>
    public static class InputActionFiredModifiers
    {
        public const int None = 0;
        public const int Queue = 1 << 0;
        public const int Precision = 1 << 1;
    }
}
