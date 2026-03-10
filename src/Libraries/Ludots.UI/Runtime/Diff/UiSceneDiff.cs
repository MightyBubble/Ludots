using System;

namespace Ludots.UI.Runtime.Diff;

public sealed class UiSceneDiff
{
	public UiSceneDiffKind Kind { get; }

	public UiSceneSnapshot Snapshot { get; }

	public UiSceneDiff(UiSceneDiffKind kind, UiSceneSnapshot snapshot)
	{
		Kind = kind;
		Snapshot = snapshot ?? throw new ArgumentNullException("snapshot");
	}
}
