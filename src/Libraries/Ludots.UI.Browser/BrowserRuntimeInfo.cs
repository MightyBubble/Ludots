using System;

namespace Ludots.UI.Browser;

public sealed class BrowserRuntimeInfo
{
	public BrowserRuntimeInfo(
		BrowserEngineKind engineKind,
		string engineName,
		string engineVersion,
		BrowserEngineCapabilities capabilities)
	{
		if (string.IsNullOrWhiteSpace(engineName))
		{
			throw new ArgumentException("Browser engine name is required.", nameof(engineName));
		}
		if (string.IsNullOrWhiteSpace(engineVersion))
		{
			throw new ArgumentException("Browser engine version is required.", nameof(engineVersion));
		}

		EngineKind = engineKind;
		EngineName = engineName;
		EngineVersion = engineVersion;
		Capabilities = capabilities;
	}

	public BrowserEngineKind EngineKind { get; }

	public string EngineName { get; }

	public string EngineVersion { get; }

	public BrowserEngineCapabilities Capabilities { get; }
}
