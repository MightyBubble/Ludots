using System;

namespace Ludots.UI.Browser;

public static class BrowserLocalAppUri
{
	public const string Scheme = "ludots-app";
	public const string Host = "app.ludots.local";
	public const string Origin = Scheme + "://" + Host;

	public static Uri Root => new(Origin + "/");

	public static Uri Create(string path, string? query = null)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Browser local app path is required.", nameof(path));
		}

		string normalizedPath = path[0] == '/' ? path : "/" + path;
		string normalizedQuery = string.IsNullOrWhiteSpace(query)
			? string.Empty
			: query[0] == '?' ? query : "?" + query;
		return new Uri(Origin + normalizedPath + normalizedQuery);
	}
}
