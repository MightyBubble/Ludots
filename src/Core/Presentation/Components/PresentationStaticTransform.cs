namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Authoring tag for entities whose world transform is immutable after spawn.
    /// Static entities opt into one-shot transform/height sync and camera-change-only culling.
    /// </summary>
    public struct PresentationStaticTransform
    {
        public int CullEpoch;
    }

    /// <summary>
    /// Internal marker: static entity still needs its initial WorldPosition -> VisualTransform sync.
    /// </summary>
    public struct PresentationStaticVisualPending
    {
    }

    /// <summary>
    /// Internal marker: static entity still needs its initial terrain height projection.
    /// </summary>
    public struct PresentationStaticHeightPending
    {
    }

    /// <summary>
    /// Internal marker: static entity still needs culling/LOD evaluation for the current camera state.
    /// </summary>
    public struct PresentationStaticCullPending
    {
    }
}
