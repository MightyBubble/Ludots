using System;
using System.Collections.Generic;
using System.IO;
using CefSharp;

namespace Ludots.UI.Browser.Cef;

internal sealed class CefBrowserSchemeHandlerFactory : ISchemeHandlerFactory
{
	private readonly CefBrowserSurfaceRegistry _registry;

	public CefBrowserSchemeHandlerFactory(CefBrowserSurfaceRegistry registry)
	{
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
	}

	public IResourceHandler? Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
	{
		ArgumentNullException.ThrowIfNull(browser);
		ArgumentNullException.ThrowIfNull(request);

		if (!_registry.TryGet(browser.Identifier, out CefBrowserSurface? surface))
		{
			return null;
		}

		return surface.ResolveResource(request.Url);
	}

	public static IResourceHandler CreateResourceHandler(BrowserResource resource)
	{
		ArgumentNullException.ThrowIfNull(resource);
		IResourceHandler handler = ResourceHandler.FromStream(
			new MemoryStream(resource.Content.ToArray(), writable: false),
			ResolveMimeType(resource.ContentType),
			autoDisposeStream: true,
			charSet: TryExtractCharset(resource.ContentType));

		if (resource.Headers.Count == 0 || handler is not ResourceHandler typedHandler)
		{
			return handler;
		}

		foreach (KeyValuePair<string, string> header in resource.Headers)
		{
			typedHandler.Headers[header.Key] = header.Value;
		}

		return typedHandler;
	}

	private static string ResolveMimeType(string contentType)
	{
		int separatorIndex = contentType.IndexOf(';');
		string mimeType = separatorIndex < 0
			? contentType
			: contentType[..separatorIndex];

		mimeType = mimeType.Trim();
		return string.IsNullOrWhiteSpace(mimeType)
			? ResourceHandler.DefaultMimeType
			: mimeType;
	}

	private static string? TryExtractCharset(string contentType)
	{
		int separatorIndex = contentType.IndexOf(';');
		if (separatorIndex < 0 || separatorIndex == contentType.Length - 1)
		{
			return null;
		}

		ReadOnlySpan<char> suffix = contentType.AsSpan(separatorIndex + 1).Trim();
		const string Prefix = "charset=";
		return suffix.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
			? suffix[Prefix.Length..].Trim().ToString()
			: null;
	}
}
