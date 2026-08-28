using System;
using Ludots.Core.Gameplay.Camera;

namespace Ludots.Core.Client
{
    /// <summary>
    /// One binding's unit of present work handed to the host draw callback: the binding's own
    /// LogicView camera (pose authority), its declared rect with binding-local surface metrics,
    /// and the frame's interpolation alpha. Hosts implement only "draw this one binding".
    /// </summary>
    public readonly struct PresentBindingDrawFrame
    {
        public PresentBindingDrawFrame(
            string seatId,
            in PresentBinding binding,
            CameraManager camera,
            PresentBindingSurface surface,
            float interpolationAlpha)
        {
            if (string.IsNullOrWhiteSpace(seatId))
            {
                throw new ArgumentException("Seat id is required.", nameof(seatId));
            }

            SeatId = seatId;
            Binding = binding;
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            Surface = surface ?? throw new ArgumentNullException(nameof(surface));
            InterpolationAlpha = interpolationAlpha;
        }

        public string SeatId { get; }

        public PresentBinding Binding { get; }

        public CameraManager Camera { get; }

        public PresentBindingSurface Surface { get; }

        public float InterpolationAlpha { get; }
    }

    /// <summary>Host callback: draw one present binding's camera pose + rect + interpolation.</summary>
    public delegate void DrawPresentBinding(in PresentBindingDrawFrame frame);
}
