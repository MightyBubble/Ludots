using System;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    /// <summary>
    /// Runtime owner of the published <see cref="NavTriangleSurfaceTileIndex"/> bake SSOT.
    /// Cold compile publishes the first generation. After bootstrap, only
    /// <see cref="RuntimeNavTriangleSurfaceEditTransaction"/> may publish replacements so the
    /// surface service, <c>CoreServiceKeys.NavTriangleSurface</c>, and rebuild queue stay atomic.
    /// </summary>
    public sealed class RuntimeNavTriangleSurfaceService
    {
        private NavTriangleSurfaceTileIndex _published;
        private ulong _contentGeneration;

        public RuntimeNavTriangleSurfaceService(NavTriangleSurfaceTileIndex initial)
        {
            Publish(initial);
        }

        public NavTriangleSurfaceTileIndex Published => _published
            ?? throw new InvalidOperationException("RuntimeNavTriangleSurfaceService has no published surface.");

        /// <summary>
        /// Monotonic non-zero generation of the currently published surface. Starts at 1.
        /// </summary>
        public ulong ContentGeneration => _contentGeneration;

        public void Publish(NavTriangleSurfaceTileIndex surface)
        {
            if (surface == null)
            {
                throw new ArgumentNullException(nameof(surface));
            }

            if (_contentGeneration == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "RuntimeNavTriangleSurfaceService content generation overflowed; owner RuntimeNavTriangleSurfaceService.ContentGeneration.");
            }

            _published = surface;
            _contentGeneration = checked(_contentGeneration + 1UL);
        }
    }
}
