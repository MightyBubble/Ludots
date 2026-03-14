using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.AdapterSync
{
    /// <summary>
    /// Adapter-local ownership state for one stable visual.
    /// </summary>
    public readonly struct StaticMeshAdapterBindingState
    {
        public StaticMeshAdapterBindingState(
            int stableId,
            StaticMeshLaneKey lane,
            int slot,
            int generation,
            in PrimitiveDrawItem item)
        {
            StableId = stableId;
            Lane = lane;
            Slot = slot;
            Generation = generation;
            Item = item;
        }

        public int StableId { get; }

        public StaticMeshLaneKey Lane { get; }

        public int Slot { get; }

        public int Generation { get; }

        public PrimitiveDrawItem Item { get; }

        public bool IsVisible => Item.Visibility == VisualVisibility.Visible;

        public StaticMeshAdapterBindingState WithItem(in PrimitiveDrawItem item)
        {
            return new StaticMeshAdapterBindingState(StableId, Lane, Slot, Generation, item);
        }
    }
}
