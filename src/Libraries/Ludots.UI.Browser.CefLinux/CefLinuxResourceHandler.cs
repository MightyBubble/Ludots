using System;
using System.Runtime.InteropServices;
using global::CefNet;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.CefLinux;

internal sealed class CefLinuxResourceHandler : CefResourceHandler
{
	private readonly byte[] _content;
	private readonly string _mimeType;
	private readonly System.Collections.Generic.IReadOnlyDictionary<string, string> _headers;
	private long _position;

	public CefLinuxResourceHandler(BrowserResource resource)
	{
		ArgumentNullException.ThrowIfNull(resource);
		_content = resource.Content.ToArray();
		_headers = resource.Headers;
		_mimeType = ResolveMimeType(resource.ContentType);
	}

	protected override bool Open(CefRequest request, ref int handleRequest, CefCallback callback)
	{
		handleRequest = 1;
		return true;
	}

	protected override void GetResponseHeaders(CefResponse response, ref long responseLength, ref string redirectUrl)
	{
		response.Status = 200;
		response.MimeType = _mimeType;
		foreach (System.Collections.Generic.KeyValuePair<string, string> header in _headers)
		{
			response.SetHeaderByName(header.Key, header.Value, overwrite: true);
		}

		responseLength = _content.LongLength;
	}

	protected override bool Skip(long bytesToSkip, ref long bytesSkipped, CefResourceSkipCallback callback)
	{
		long remaining = _content.LongLength - _position;
		long skipped = Math.Min(bytesToSkip, remaining);
		_position += skipped;
		bytesSkipped = skipped;
		return true;
	}

	protected override bool Read(IntPtr dataOut, int bytesToRead, ref int bytesRead, CefResourceReadCallback callback)
	{
		int remaining = checked((int)Math.Min(bytesToRead, _content.LongLength - _position));
		if (remaining > 0)
		{
			Marshal.Copy(_content, checked((int)_position), dataOut, remaining);
			_position += remaining;
		}

		bytesRead = remaining;
		return true;
	}

	protected override void Cancel()
	{
	}

	private static string ResolveMimeType(string contentType)
	{
		int separatorIndex = contentType.IndexOf(';');
		string mimeType = separatorIndex < 0
			? contentType
			: contentType[..separatorIndex];
		return string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType.Trim();
	}
}
