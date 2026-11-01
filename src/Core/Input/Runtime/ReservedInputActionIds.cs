namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// Engine-reserved input action ids that author configs may bind in an IMC context
    /// but never declare in the actions directory — the engine resolves them by name
    /// (mirrors <see cref="Ludots.Core.Input.CommandSources.CommandSourceModifierActionIds"/>
    /// and the PointerPos camera/aim contract).
    /// </summary>
    public static class ReservedInputActionIds
    {
        /// <summary>Live pointer position (Vector2), the authoritative pointer snapshot used by
        /// LoadPointerScreenX/Y and the presenter pointerScreen param sources.</summary>
        public const string PointerPos = "PointerPos";

        /// <summary>
        /// Reserved pointer-motion action. Dispatch watches <see cref="PointerPos"/> on the
        /// same value-change path as an Axis2D <c>firesOn: change</c> action: one sample
        /// when that position is present in the authoritative snapshot, then only when the
        /// vector changes. An input context that does not bind <see cref="PointerPos"/>
        /// never fires it.
        /// </summary>
        public const string PointerMoved = "PointerMoved";
    }
}
