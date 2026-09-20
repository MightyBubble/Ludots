using System;
using System.Collections.Generic;
using Ludots.Core.Client;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>One seat's host-surface pixel rect a panel instance mounts on.</summary>
    public readonly record struct PanelSeatSurface(string SeatId, float X, float Y, float Width, float Height);

    /// <summary>
    /// Panel-level surface placement: which seat surfaces one panel instance mounts on,
    /// and each seat's PresentBinding rect in host pixels. The audience decides placement
    /// only — the panel stays one instance with one state; per-seat mounts are presentation
    /// copies. Seats outside the audience never receive a surface entry. Returns false
    /// when no audience seat holds a PresentBinding; callers then keep the pre-split
    /// full-window mount (sole-seat path stays bit-identical).
    /// </summary>
    public static class PanelSeatSurfacePlacement
    {
        public static bool TryResolveSeatSurfaces(
            PanelAudience audience,
            IReadOnlyList<(string SeatId, PresentBinding Binding)> presentBindings,
            float hostWidth,
            float hostHeight,
            List<PanelSeatSurface> destination)
        {
            ArgumentNullException.ThrowIfNull(audience);
            ArgumentNullException.ThrowIfNull(presentBindings);
            ArgumentNullException.ThrowIfNull(destination);
            if (hostWidth <= 0f || hostHeight <= 0f)
            {
                throw new InvalidOperationException($"Host surface must be positive (got {hostWidth}x{hostHeight}).");
            }

            destination.Clear();
            for (int i = 0; i < presentBindings.Count; i++)
            {
                (string seatId, PresentBinding binding) = presentBindings[i];
                if (!audience.Contains(seatId))
                {
                    continue;
                }

                System.Numerics.Vector4 rect = binding.NormalizedScreenRect;
                destination.Add(new PanelSeatSurface(
                    seatId,
                    rect.X * hostWidth,
                    rect.Y * hostHeight,
                    rect.Z * hostWidth,
                    rect.W * hostHeight));
            }

            return destination.Count > 0;
        }
    }
}
