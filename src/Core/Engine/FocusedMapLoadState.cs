using Ludots.Core.Map;

namespace Ludots.Core.Engine
{
    /// <summary>
    /// Current focused-map lifecycle state published by the engine.
    /// Adapter layers may observe it, but they must not own focus bookkeeping.
    /// </summary>
    public readonly record struct FocusedMapLoadState(
        MapSession? Session,
        MapLoadStatus LoadStatus,
        bool HasPendingReturn);

    public interface IFocusedMapLoadStateSink
    {
        void OnFocusedMapChanged(in FocusedMapLoadState state);
    }
}
