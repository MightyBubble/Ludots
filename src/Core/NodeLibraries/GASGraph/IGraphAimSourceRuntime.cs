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
    /// and surface metrics — until per-seat projector routing is installed, the
    /// production runtime answers every seat under the sole present binding.
    /// </summary>
    public interface IGraphAimSourceRuntime
    {
        bool TryScreenPointToGround(float screenX, float screenY, out IntVector2 groundCm);

        Entity PickScreenPointEntity(
            ReadOnlySpan<Entity> candidates,
            int count,
            Entity owner,
            string? seatId,
            float screenX,
            float screenY,
            float radiusPixels);

        int FilterScreenRegionEntities(Span<Entity> entities, int count, in ScreenRect rect);
    }
}
