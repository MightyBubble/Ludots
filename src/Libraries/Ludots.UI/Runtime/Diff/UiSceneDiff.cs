namespace Ludots.UI.Runtime.Diff;

public sealed class UiSceneDiff
{
    public UiSceneDiff(UiSceneDiffKind kind, UiSceneSnapshot snapshot)
    {
        Kind = kind;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public UiSceneDiffKind Kind { get; }

    public UiSceneSnapshot Snapshot { get; }
}
