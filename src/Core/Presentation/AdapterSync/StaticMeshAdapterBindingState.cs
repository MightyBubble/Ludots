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
            in PrimitiveDrawItem item,
            int projectionGeneration = 0)
        {
            StableId = stableId;
            Lane = lane;
            Slot = slot;
            Generation = generation;
            Item = item;
            ProjectionGeneration = projectionGeneration;
        }

        public int StableId { get; }

        public StaticMeshLaneKey Lane { get; }

        public int Slot { get; }

        public int Generation { get; }

        public int ProjectionGeneration { get; }

        public PrimitiveDrawItem Item { get; }

        public bool IsVisible => Item.Visibility == VisualVisibility.Visible;

        public StaticMeshAdapterBindingState WithItem(in PrimitiveDrawItem item)
        {
            return new StaticMeshAdapterBindingState(StableId, Lane, Slot, Generation, item, ProjectionGeneration);
        }

        public StaticMeshAdapterBindingState WithItem(in PrimitiveDrawItem item, int projectionGeneration)
        {
            return new StaticMeshAdapterBindingState(StableId, Lane, Slot, Generation, item, projectionGeneration);
        }

        public StaticMeshAdapterBindingState WithProjectionGeneration(int projectionGeneration)
        {
            return new StaticMeshAdapterBindingState(StableId, Lane, Slot, Generation, Item, projectionGeneration);
        }
    }
}
