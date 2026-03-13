namespace Ludots.UI.Reactive;

public enum ReactiveApplyMode : byte
{
	None = 0,
	FullRecompose = 1,
	IncrementalPatch = 2
}
