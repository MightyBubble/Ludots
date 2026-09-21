using CefNet;

namespace Ludots.UI.Browser.CefLinux.Core;

internal sealed class CefLinuxBrowserProcessHandler : CefBrowserProcessHandler
{
	private readonly CefLinuxBrowserProcessScheduler? _scheduler;

	public CefLinuxBrowserProcessHandler(CefLinuxBrowserProcessScheduler? scheduler)
	{
		_scheduler = scheduler;
	}

	protected override void OnScheduleMessagePumpWork(long delayMs)
	{
		_scheduler?.ScheduleMessagePumpWork(delayMs);
	}
}
