namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Adapter-facing render lane markers. Core keeps frame-snapshot semantics and adapters own lane-specific runtime state.
    /// </summary>
    public enum VisualRenderPath : byte
    {
        None = 0,
        /// <summary>
        /// Single static mesh lane. Eligible for adapter-side stableId dirty sync, never for animator sync.
        /// </summary>
        StaticMesh = 1,
        /// <summary>
        /// Instanced static mesh lane. Adapter may batch by mesh/material, but the lane remains static and non-skinned.
        /// </summary>
        InstancedStaticMesh = 2,
        /// <summary>
        /// Hierarchical instanced static mesh lane. Adapter owns hierarchy/batch state; animator payload is invalid here.
        /// </summary>
        HierarchicalInstancedStaticMesh = 3,
        /// <summary>
        /// Dedicated skeletal component lane. One stable visual maps to one adapter-owned skinned component/runtime.
        /// </summary>
        SkinnedMesh = 4,
        /// <summary>
        /// GPU-skinned crowd lane. This is not a static instance sync path and requires skinned runtime ownership in the adapter.
        /// </summary>
        GpuSkinnedInstance = 5,
    }

    public static class VisualRenderPathSemantics
    {
        public static bool IsStaticInstanceLane(this VisualRenderPath renderPath)
        {
            return renderPath is VisualRenderPath.StaticMesh
                or VisualRenderPath.InstancedStaticMesh
                or VisualRenderPath.HierarchicalInstancedStaticMesh;
        }

        public static bool IsSkinnedLane(this VisualRenderPath renderPath)
        {
            return renderPath is VisualRenderPath.SkinnedMesh
                or VisualRenderPath.GpuSkinnedInstance;
        }

        public static bool SupportsAnimatorPackedState(this VisualRenderPath renderPath)
        {
            return renderPath.IsSkinnedLane();
        }

        public static bool RequiresExplicitAnimatorController(this VisualRenderPath renderPath)
        {
            return renderPath.IsSkinnedLane();
        }
    }
}
