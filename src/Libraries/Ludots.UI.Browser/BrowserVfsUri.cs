using System;

namespace Ludots.UI.Browser;

public static class BrowserVfsUri
{
	public const string Scheme = "ludots-vfs";
	public const string AssetsDirectoryName = "assets";

	// Uri.Host and AbsoluteUri lowercase the authority. The mod id is case-sensitive,
	// so callers must keep the address text that was written and parse that.
	public static bool Matches(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return uri.OriginalString.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase);
	}

	public static string FromVfsPath(string vfsPath)
	{
		if (!TrySplitMountedPath(vfsPath, out string modId, out string relativePath))
		{
			throw new ArgumentException(
				$"VFS path '{vfsPath}' cannot be addressed as {Scheme}://<ModId>/{AssetsDirectoryName}/...",
				nameof(vfsPath));
		}

		return Scheme + "://" + modId + "/" + relativePath;
	}

	public static bool TryParse(Uri uri, out string modId, out string relativePath)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return TryParse(uri.OriginalString, out modId, out relativePath);
	}

	public static bool TryParse(string? rawUrl, out string modId, out string relativePath)
	{
		modId = string.Empty;
		relativePath = string.Empty;
		if (string.IsNullOrWhiteSpace(rawUrl))
		{
			return false;
		}

		int schemeSeparator = rawUrl.IndexOf("://", StringComparison.Ordinal);
		if (schemeSeparator <= 0)
		{
			return false;
		}

		if (!rawUrl.AsSpan(0, schemeSeparator).Equals(Scheme, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string remainder = rawUrl[(schemeSeparator + 3)..];
		int cut = remainder.IndexOfAny(['?', '#']);
		if (cut >= 0)
		{
			remainder = remainder[..cut];
		}

		int slash = remainder.IndexOf('/');
		if (slash <= 0 || slash >= remainder.Length - 1)
		{
			return false;
		}

		if (!TryNormalizeModId(remainder[..slash], out modId))
		{
			return false;
		}

		return TryNormalizeAssetRelativePath(remainder[(slash + 1)..], out relativePath);
	}

	public static string ToMountedPath(string modId, string relativePath)
	{
		return modId + ":" + relativePath;
	}

	private static bool TrySplitMountedPath(string? vfsPath, out string modId, out string relativePath)
	{
		modId = string.Empty;
		relativePath = string.Empty;
		if (string.IsNullOrWhiteSpace(vfsPath))
		{
			return false;
		}

		int colon = vfsPath.IndexOf(':');
		if (colon <= 0 || colon >= vfsPath.Length - 1 || vfsPath.IndexOf(':', colon + 1) >= 0)
		{
			return false;
		}

		if (!TryNormalizeModId(vfsPath[..colon], out modId))
		{
			return false;
		}

		return TryNormalizeAssetRelativePath(vfsPath[(colon + 1)..], out relativePath);
	}

	private static bool TryNormalizeModId(string rawModId, out string modId)
	{
		modId = string.Empty;
		string decoded;
		try
		{
			decoded = Uri.UnescapeDataString(rawModId);
		}
		catch (UriFormatException)
		{
			return false;
		}

		if (decoded.Length == 0 || decoded.Contains("..", StringComparison.Ordinal))
		{
			return false;
		}

		for (int i = 0; i < decoded.Length; i++)
		{
			char c = decoded[i];
			bool allowed = char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.';
			if (!allowed)
			{
				return false;
			}
		}

		modId = decoded;
		return true;
	}

	private static bool TryNormalizeAssetRelativePath(string rawRelativePath, out string relativePath)
	{
		relativePath = string.Empty;
		string decoded;
		try
		{
			decoded = Uri.UnescapeDataString(rawRelativePath.Replace('\\', '/'));
		}
		catch (UriFormatException)
		{
			return false;
		}

		if (decoded.Contains('\0', StringComparison.Ordinal) || decoded.Contains(':', StringComparison.Ordinal))
		{
			return false;
		}

		string[] segments = decoded.Split('/');
		if (segments.Length < 2 ||
		    !string.Equals(segments[0], AssetsDirectoryName, StringComparison.Ordinal))
		{
			return false;
		}

		for (int i = 0; i < segments.Length; i++)
		{
			string segment = segments[i];
			if (segment.Length == 0 || segment is "." or "..")
			{
				return false;
			}
		}

		relativePath = string.Join('/', segments);
		return true;
	}
}
