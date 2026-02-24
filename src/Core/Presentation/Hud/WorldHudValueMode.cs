namespace Ludots.Core.Presentation.Hud
{
    /// <summary>
    /// Specifies how a WorldHudItem text value should be interpreted by platform renderers.
    /// Originally part of WorldHudConfig; migrated here as it belongs to the HUD rendering protocol.
    /// </summary>
    public enum WorldHudValueMode : byte
    {
        None = 0,
        /// <summary>Display as "current/max" format using Value0 and Value1.</summary>
        AttributeCurrentOverBase = 1,
        /// <summary>Display as single integer using Value0.</summary>
        AttributeCurrent = 2,
        /// <summary>Display a constant value from Value0.</summary>
        Constant = 3,
    }
}
