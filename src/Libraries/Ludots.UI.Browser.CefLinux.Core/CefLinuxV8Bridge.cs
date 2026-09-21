using System;
using CefNet;

namespace Ludots.UI.Browser.CefLinux.Core;

internal sealed class CefLinuxV8Bridge : CefV8Handler
{
	private const string PostHostMessageFunctionName = "__ludotsPostHostMessage";

	protected override bool Execute(
		string name,
		CefV8Value obj,
		CefV8Value[] arguments,
		ref CefV8Value retval,
		ref string exception)
	{
		if (!string.Equals(name, PostHostMessageFunctionName, StringComparison.Ordinal))
		{
			exception = $"Unknown Ludots native bridge function '{name}'.";
			return false;
		}

		if (arguments.Length != 1 || arguments[0] is not { IsString: true } payload)
		{
			exception = "Ludots native bridge expects a single JSON string argument.";
			return false;
		}

		CefV8Context? context = CefV8Context.GetCurrentContext();
		CefFrame? frame = context?.Frame;
		if (frame is null || !frame.IsValid)
		{
			exception = "Ludots native bridge has no valid frame for the current V8 context.";
			return false;
		}

		var message = new CefProcessMessage(CefLinuxProcessMessages.HostMessage);
		message.ArgumentList.SetString(0, payload.GetStringValue());
		frame.SendProcessMessage(CefProcessId.Browser, message);
		return true;
	}
}
