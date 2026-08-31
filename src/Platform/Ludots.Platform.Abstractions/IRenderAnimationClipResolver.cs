namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Resolves a presentation animation profile state to the backend clip selector
    /// declared by the presentation asset catalog.
    /// </summary>
    public interface IRenderAnimationClipResolver
    {
        bool TryResolve(int animationProfileId, int packedStateIndex, out ClipAssetLocatorSelector selector);
    }
}
