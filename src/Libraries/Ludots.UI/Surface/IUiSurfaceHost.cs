using Ludots.UI.Runtime;

namespace Ludots.UI.Surface;

public interface IUiSurfaceHost
{
	UiScene? Scene { get; }

	UiSurfaceLeaseHandle Acquire(UiSurfaceLeaseRequest request);

	bool Revalidate(UiSurfaceLeaseHandle handle);

	void Publish(UiSurfaceLeaseHandle handle, UiSurfaceContribution contribution);

	void Invalidate(UiSurfaceLeaseHandle handle);

	bool Release(UiSurfaceLeaseHandle handle);
}
