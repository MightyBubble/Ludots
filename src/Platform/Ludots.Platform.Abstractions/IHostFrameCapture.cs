using System.Threading;
using System.Threading.Tasks;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Host frame capture port. Implemented by host adapters (Raylib, Unity,
    /// Unreal, Godot) that can read back the presented frame. A request is
    /// fulfilled after the next rendered frame completes; the continuation may
    /// run on any thread, and the game loop must never block waiting on it.
    /// </summary>
    public interface IHostFrameCapture
    {
        /// <summary>Capture the next presented frame as PNG bytes.</summary>
        Task<byte[]> CapturePngAsync(CancellationToken cancellationToken = default);
    }
}
