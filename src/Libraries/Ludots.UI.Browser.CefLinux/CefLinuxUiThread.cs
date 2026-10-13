using System;
using global::CefNet;

namespace Ludots.UI.Browser.CefLinux;

internal static class CefLinuxUiThread
{
	public static void Run(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);
		CefApi.PostTask(CefThreadId.UI, new CefLinuxActionTask(action));
	}

	private sealed class CefLinuxActionTask : CefTask
	{
		private readonly Action _action;

		public CefLinuxActionTask(Action action)
		{
			_action = action;
		}

		protected override void Execute()
		{
			_action();
		}
	}
}
