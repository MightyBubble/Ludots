namespace Ludots.UI.Reactive;

public readonly record struct ReactiveUpdateStats(ReactiveApplyMode Mode, int PatchedNodes)
{
	public static ReactiveUpdateStats None { get; } = new(ReactiveApplyMode.None, 0);

	public bool UsedIncrementalPatch => Mode == ReactiveApplyMode.IncrementalPatch;

	public bool UsedFullRecompose => Mode == ReactiveApplyMode.FullRecompose;
}
