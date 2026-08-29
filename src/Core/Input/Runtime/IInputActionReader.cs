namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// Action-state reads over either input rhythm. On the live handler the edge methods
    /// span a single visual frame; on the per-tick frozen snapshot the same methods return
    /// the logic-tick edge (every visual frame since the last freeze is folded in), which
    /// is the authoritative read for fixed-step consumers. Direct tick-named reads live on
    /// <see cref="FrozenInputActionReader"/>.
    /// </summary>
    public interface IInputActionReader
    {
        T ReadAction<T>(string actionId) where T : struct;
        bool IsDown(string actionId);
        bool PressedThisFrame(string actionId);
        bool ReleasedThisFrame(string actionId);
    }
}
