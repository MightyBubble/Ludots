using System;
using Arch.Core;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Aimsource kernel bridge for the graph VM: the three screen/pointer-dependent
    /// helpers behind ScreenPointToGround / ScreenPointToEntity / ScreenRegionToEntities.
    /// Bound by the host (production binds <c>GraphAimSourceRuntime</c> over the engine
    /// globals; galleries bind deterministic fakes). The contract is seat-addressed and
    /// binding-local: a named seat answers under that seat's present binding's camera
    /// and surface metrics; a null seat answers under the sole present binding (window
    /// points route by rect membership when multiple bindings exist).
    /// </summary>
    public interface IGraphAimSourceRuntime
    {
        bool TryScreenPointToGround(float screenX, float screenY, string? seatId, out IntVector2 groundCm);

        Entity PickScreenPointEntity(
            ReadOnlySpan<Entity> candidates,
            int count,
            Entity owner,
            string? seatId,
            float screenX,
            float screenY,
            float radiusPixels);

        int FilterScreenRegionEntities(Span<Entity> entities, int count, in ScreenRect rect, string? seatId);

        /// <summary>
        /// Live window-pixel pointer from the authoritative input snapshot (PointerPos).
        /// False when the snapshot is unavailable; callers fail closed.
        /// </summary>
        bool TryReadLivePointerScreen(out float screenX, out float screenY);
    }
}
