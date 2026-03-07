namespace Ludots.UI.Runtime.Diff;

public sealed class UiSceneSnapshot
{
    public UiSceneSnapshot(long version, UiNodeDiff? root)
    {
        Version = version;
        Root = root;
    }

    public long Version { get; }

    public UiNodeDiff? Root { get; }
}
