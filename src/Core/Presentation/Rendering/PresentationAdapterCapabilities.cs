using System;

namespace Ludots.Core.Presentation.Rendering
{
    [Flags]
    public enum PresentationVisualCapabilities
    {
        None = 0,
        Decal = 1 << 0,
        Surface = 1 << 1,
        MaterialOverride = 1 << 2,
        InstanceCustomData = 1 << 3,
        ExternalTargetLifecycle = 1 << 4,
        InstancedStaticMeshBatch = 1 << 5,
        HierarchicalInstancedStaticMeshBatch = 1 << 6,
        InstancedBatchVisibility = 1 << 7,
        InstancedBatchRefresh = 1 << 8,
        InstancedBatchPresentationState = 1 << 9,
        InstancedBatchEffect = 1 << 10,
    }

    public sealed class PresentationAdapterCapabilities
    {
        public PresentationAdapterCapabilities(PresentationVisualCapabilities visuals)
        {
            Visuals = visuals;
        }

        public PresentationVisualCapabilities Visuals { get; }

        public bool Supports(PresentationVisualRequestKind kind)
        {
            return kind switch
            {
                PresentationVisualRequestKind.Decal => Visuals.HasFlag(PresentationVisualCapabilities.Decal),
                PresentationVisualRequestKind.Surface => Visuals.HasFlag(PresentationVisualCapabilities.Surface),
                PresentationVisualRequestKind.MaterialOverride => Visuals.HasFlag(PresentationVisualCapabilities.MaterialOverride),
                PresentationVisualRequestKind.InstanceCustomData => Visuals.HasFlag(PresentationVisualCapabilities.InstanceCustomData),
                PresentationVisualRequestKind.None => true,
                _ => false,
            };
        }
    }
}
