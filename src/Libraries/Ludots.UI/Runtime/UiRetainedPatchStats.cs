namespace Ludots.UI.Runtime;

internal readonly record struct UiRetainedPatchStats(
	int ReusedNodes,
	int PatchedNodes,
	int InsertedNodes,
	int RemovedNodes,
	int ReplacedNodes,
	bool FullRemount)
{
	public bool HasChanges => FullRemount || PatchedNodes > 0 || InsertedNodes > 0 || RemovedNodes > 0 || ReplacedNodes > 0;
}
