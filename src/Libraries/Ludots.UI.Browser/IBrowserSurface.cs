using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ludots.UI.Browser;

public interface IBrowserSurface : IAsyncDisposable
{
	event EventHandler<BrowserFrameReadyEventArgs>? FrameReady;

	BrowserSurfaceId Id { get; }

	BrowserViewport Viewport { get; }

	IBrowserMessageBridge Messages { get; }

	ValueTask NavigateAsync(BrowserNavigationRequest request, CancellationToken cancellationToken = default);

	ValueTask ResizeAsync(BrowserViewport viewport, CancellationToken cancellationToken = default);

	ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default);

	BrowserFrame? TryGetLatestFrame();

	bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame);
}
