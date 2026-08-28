using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Core.Systems
{
    /// <summary>
    /// One present binding's culling pass: the binding's LogicView camera plus its present surface.
    /// <see cref="CameraCullingSystem"/> runs one pass per entry; the shared <see cref="Ludots.Core.Presentation.Components.CullState"/>
    /// receives the union of the passes, never a merged global visible set.
    /// </summary>
    public readonly struct PresentBindingCullPass
    {
        public PresentBindingCullPass(string seatId, CameraManager camera, IViewController surface)
        {
            SeatId = seatId;
            Camera = camera ?? throw new System.ArgumentNullException(nameof(camera));
            Surface = surface ?? throw new System.ArgumentNullException(nameof(surface));
        }

        /// <summary>Owning seat id; null for the sole-binding legacy rebind.</summary>
        public string? SeatId { get; }

        public CameraManager Camera { get; }

        public IViewController Surface { get; }
    }
}
