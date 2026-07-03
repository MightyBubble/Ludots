using System;
using Ludots.UI.Reactive;

namespace Ludots.UI.Surface;

public static class UiSurfaceHostExtensions
{
	public static UiSurfaceLeaseHandle EnsureLease(
		this IUiSurfaceHost host,
		ref UiSurfaceLeaseHandle handle,
		UiSurfaceLeaseRequest request)
	{
		ArgumentNullException.ThrowIfNull(host, nameof(host));
		ArgumentNullException.ThrowIfNull(request, nameof(request));
		if (!handle.IsValid || !host.Revalidate(handle))
		{
			handle = host.Acquire(request);
		}

		return handle;
	}

	public static void PublishReactivePage<TState>(
		this IUiSurfaceHost host,
		ref UiSurfaceLeaseHandle handle,
		UiSurfaceLeaseRequest request,
		ReactivePage<TState> page)
	{
		ArgumentNullException.ThrowIfNull(host, nameof(host));
		ArgumentNullException.ThrowIfNull(page, nameof(page));
		host.Publish(
			host.EnsureLease(ref handle, request),
			UiSurfaceContribution.FromReactivePage(page));
	}

	public static bool ReleaseLease(this IUiSurfaceHost host, ref UiSurfaceLeaseHandle handle)
	{
		ArgumentNullException.ThrowIfNull(host, nameof(host));
		if (!handle.IsValid)
		{
			return false;
		}

		bool released = host.Release(handle);
		handle = default;
		return released;
	}

	public static bool InvalidateLease(this IUiSurfaceHost host, UiSurfaceLeaseHandle handle)
	{
		ArgumentNullException.ThrowIfNull(host, nameof(host));
		if (!handle.IsValid || !host.Revalidate(handle))
		{
			return false;
		}

		host.Invalidate(handle);
		return true;
	}
}
