namespace Ludots.UI.Browser.CefLinux.Core;

public interface CefLinuxBrowserProcessScheduler
{
	void ScheduleMessagePumpWork(long delayMs);
}
