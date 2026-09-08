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
        /// Per-seat pointer-motion edge (Vector2): fired once per tick when the bound seat's
        /// live pointer position changed since the last dispatched sample, while the binding
        /// context is active. Continuous gestures (drag preview) ride this input-domain edge
        /// instead of a simulation-clock event: no movement means no dispatch, no work.
        /// </summary>
        public const string PointerMoved = "PointerMoved";
    }
}
