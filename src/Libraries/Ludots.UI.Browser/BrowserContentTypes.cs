using System;
using System.Collections.Generic;
using System.IO;

namespace Ludots.UI.Browser;

public static class BrowserContentTypes
{
	public static readonly IReadOnlyDictionary<string, string> Default = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		[".html"] = "text/html; charset=utf-8",
		[".htm"] = "text/html; charset=utf-8",
		[".js"] = "application/javascript; charset=utf-8",
		[".mjs"] = "application/javascript; charset=utf-8",
		[".css"] = "text/css; charset=utf-8",
		[".json"] = "application/json; charset=utf-8",
		[".wasm"] = "application/wasm",
		[".png"] = "image/png",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".gif"] = "image/gif",
		[".webp"] = "image/webp",
		[".svg"] = "image/svg+xml",
		[".ico"] = "image/x-icon",
		[".woff"] = "font/woff",
		[".woff2"] = "font/woff2",
		[".ttf"] = "font/ttf",
		[".otf"] = "font/otf"
	};

	public static string ForFile(string path)
	{
		string extension = Path.GetExtension(path);
		return Default.TryGetValue(extension, out string? contentType)
			? contentType
			: "application/octet-stream";
	}
}
