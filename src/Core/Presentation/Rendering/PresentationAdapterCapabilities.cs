using System;

namespace Ludots.Core.Presentation.Rendering
{
    [Flags]
    public enum PresentationVisualCapabilities
    {
        None = 0,
        Decal = 1 << 0,
        Vfx = 1 << 1,
        Surface = 1 << 2,
        MaterialOverride = 1 << 3,
        InstanceCustomData = 1 << 4,
        ExternalTargetLifecycle = 1 << 5,
        InstancedStaticMeshBatch = 1 << 6,
        HierarchicalInstancedStaticMeshBatch = 1 << 7,
        InstancedBatchVisibility = 1 << 8,
        InstancedBatchRefresh = 1 << 9,
        InstancedBatchPresentationState = 1 << 10,
        InstancedBatchEffect = 1 << 11,
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
                PresentationVisualRequestKind.Vfx => Visuals.HasFlag(PresentationVisualCapabilities.Vfx),
                PresentationVisualRequestKind.Surface => Visuals.HasFlag(PresentationVisualCapabilities.Surface),
                PresentationVisualRequestKind.MaterialOverride => Visuals.HasFlag(PresentationVisualCapabilities.MaterialOverride),
                PresentationVisualRequestKind.InstanceCustomData => Visuals.HasFlag(PresentationVisualCapabilities.InstanceCustomData),
                PresentationVisualRequestKind.None => true,
                _ => false,
            };
        }
    }
}
