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
