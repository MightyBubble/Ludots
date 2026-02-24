using Ludots.Core.Layers;

namespace Ludots.Core.Gameplay.Components
{
    /// <summary>
    /// ECS component wrapping <see cref="LayerMask"/> for entity-level layer classification.
    /// Attach to gameplay entities that participate in layer-based filtering
    /// (spatial queries, effect targeting, physics interaction).
    /// </summary>
    public struct EntityLayer
    {
        public LayerMask Value;

        public EntityLayer(LayerMask value) { Value = value; }
        public EntityLayer(uint category, uint mask) { Value = new LayerMask(category, mask); }
    }
}
