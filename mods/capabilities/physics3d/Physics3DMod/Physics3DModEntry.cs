using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace Physics3DMod;

public sealed class Physics3DModEntry : IMod
{
    private Physics3DRuntime? _runtime;

    public void OnLoad(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_runtime != null)
        {
            throw new InvalidOperationException("Physics3DMod is already loaded.");
        }

        _runtime = new Physics3DRuntime();
        context.OnEvent(GameEvents.MapLoaded, _runtime.EnsureInstalledAsync);
        context.OnEvent(GameEvents.MapResumed, _runtime.EnsureInstalledAsync);
        context.Log("[Physics3DMod] Registered the authoritative server Physics3D runtime.");
    }

    public void OnUnload()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
