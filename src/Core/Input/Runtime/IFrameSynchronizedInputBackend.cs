namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// Optional input backend extension for adapters that ingest asynchronous input events
    /// and need to expose a stable per-frame snapshot to the core input runtime.
    /// </summary>
    public interface IFrameSynchronizedInputBackend
    {
        void AdvanceFrameInput();
    }
}
