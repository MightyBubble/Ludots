namespace Ludots.Core.Components
{
    /// <summary>
    /// Tag component: marks an entity as suspended (excluded from active processing).
    /// Systems use WithNone&lt;SuspendedTag&gt;() to skip suspended entities.
    /// Used during nested Map transitions to pause outer-map entities.
    /// </summary>
    public struct SuspendedTag { }
}
