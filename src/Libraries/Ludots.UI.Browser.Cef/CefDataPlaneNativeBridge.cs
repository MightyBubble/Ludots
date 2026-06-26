using System;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal sealed class CefDataPlaneNativeBridge
{
	private readonly BrowserSharedBufferBridge _sharedBuffers;

	public CefDataPlaneNativeBridge(BrowserSharedBufferBridge sharedBuffers)
	{
		_sharedBuffers = sharedBuffers ?? throw new ArgumentNullException(nameof(sharedBuffers));
	}

	public byte[] ReadSharedBuffer(string bufferId, int byteOffset, int byteLength, long sequence)
	{
		return _sharedBuffers.ReadSharedBuffer(bufferId, byteOffset, byteLength, sequence);
	}
}
