namespace Ludots.Core.Components
{
    /// <summary>
    /// Blittable ECS component: identifies which Board an entity belongs to.
    /// Uses an interned int ID from BoardIdRegistry.
    /// </summary>
    public struct BoardRef
    {
        public int BoardIdInterned;
    }
}
