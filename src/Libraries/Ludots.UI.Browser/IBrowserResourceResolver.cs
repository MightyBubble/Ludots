using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ludots.UI.Browser;

public interface IBrowserResourceResolver
{
	ValueTask<BrowserResource?> ResolveAsync(Uri uri, CancellationToken cancellationToken = default);
}
