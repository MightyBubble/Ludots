using System.Runtime.InteropServices;
using CefSharp;
using CefSharp.BrowserSubprocess;
using CefSharp.RenderProcess;

LudotsRenderProcessLog.Write("subprocess starting: " + string.Join(" ", args));
try
{
	int exitCode = new BrowserSubprocessExecutable().Main(args, new LudotsRenderProcessHandler());
	LudotsRenderProcessLog.Write($"subprocess exiting: {exitCode}");
	return exitCode;
}
catch (Exception ex)
{
	LudotsRenderProcessLog.Write("subprocess failed: " + ex);
	throw;
}

internal sealed partial class LudotsRenderProcessHandler : IRenderProcessHandler
{
	public void OnWebKitInitialized()
	{
		LudotsRenderProcessLog.Write("OnWebKitInitialized");
		int result = LudotsCefV8Install();
		LudotsRenderProcessLog.Write($"LudotsCefV8Install returned {result}");
		if (result != 0)
		{
			throw new InvalidOperationException($"Ludots native CEF V8 buffer bridge install failed with error {result}.");
		}
	}

	public void OnContextCreated(IBrowser browser, IFrame frame, IV8Context context)
	{
	}

	public void OnContextReleased(IBrowser browser, IFrame frame, IV8Context context)
	{
	}

	[LibraryImport("Ludots.UI.Browser.Cef.Native")]
	private static partial int LudotsCefV8Install();
}

internal static class LudotsRenderProcessLog
{
	private static readonly object Sync = new();
	private static readonly string PathValue = System.IO.Path.Combine(
		System.IO.Path.GetTempPath(),
		"Ludots",
		"CefV8Bridge",
		"render-process.log");

	public static void Write(string message)
	{
		try
		{
			lock (Sync)
			{
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathValue)!);
				File.AppendAllText(
					PathValue,
					$"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}
}
