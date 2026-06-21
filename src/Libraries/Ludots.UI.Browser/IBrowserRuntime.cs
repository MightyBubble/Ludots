using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ludots.UI.Browser;

public interface IBrowserRuntime : IAsyncDisposable
{
	BrowserRuntimeInfo Info { get; }

	ValueTask<IBrowserSurface> CreateSurfaceAsync(
		BrowserViewport viewport,
		IBrowserResourceResolver? resourceResolver = null,
		CancellationToken cancellationToken = default);
}
