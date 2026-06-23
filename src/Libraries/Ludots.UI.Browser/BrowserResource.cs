using System;
using System.Collections.Generic;

namespace Ludots.UI.Browser;

public sealed class BrowserResource
{
	public BrowserResource(
		string contentType,
		ReadOnlyMemory<byte> content,
		IReadOnlyDictionary<string, string>? headers = null)
	{
		if (string.IsNullOrWhiteSpace(contentType))
		{
			throw new ArgumentException("Content type is required.", nameof(contentType));
		}

		ContentType = contentType;
		Content = content;
		Headers = headers ?? new Dictionary<string, string>();
	}

	public string ContentType { get; }

	public ReadOnlyMemory<byte> Content { get; }

	public IReadOnlyDictionary<string, string> Headers { get; }
}
