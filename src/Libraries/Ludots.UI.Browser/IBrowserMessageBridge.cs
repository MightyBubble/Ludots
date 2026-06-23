using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ludots.UI.Browser;

public interface IBrowserMessageBridge
{
	event EventHandler<BrowserScriptMessage>? MessageReceived;

	ValueTask PostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken = default);

	ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default);
}
