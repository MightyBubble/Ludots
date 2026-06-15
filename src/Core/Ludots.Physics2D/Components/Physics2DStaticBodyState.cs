namespace Ludots.Core.Physics2D.Components
{
    /// <summary>
    /// Marks a static physics body whose broadphase descriptors are owned by the Physics2D static cache.
    /// </summary>
    public struct Physics2DStaticBodyState
    {
    }

    /// <summary>
    /// Requests a static physics cache rebuild after a static body is added, moved, reshaped, or removed.
    /// </summary>
    public struct Physics2DStaticBodyDirty
    {
    }
}
