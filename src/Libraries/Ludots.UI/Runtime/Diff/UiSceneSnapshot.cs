namespace Ludots.UI.Runtime.Diff;

public sealed class UiSceneSnapshot
{
	public long Version { get; }

	public UiNodeDiff? Root { get; }

	public UiSceneSnapshot(long version, UiNodeDiff? root)
	{
		Version = version;
		Root = root;
	}
}
