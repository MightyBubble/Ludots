using Ludots.Core.Modding;

namespace SharedVisionSnapshotShowcaseMod;

public sealed class SharedVisionSnapshotShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[SharedVisionSnapshotShowcaseMod] Loaded");
    }

    public void OnUnload()
    {
    }
}
