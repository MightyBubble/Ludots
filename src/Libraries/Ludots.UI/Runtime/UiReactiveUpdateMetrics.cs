namespace Ludots.UI.Runtime;

public readonly record struct UiReactiveUpdateMetrics(
	UiReactiveUpdateReason Reason,
	long SceneVersion,
	int ReusedNodes,
	int PatchedNodes,
	int InsertedNodes,
	int RemovedNodes,
	int ReplacedNodes,
	bool FullRemount,
	int VirtualizedWindowCount,
	int VirtualizedTotalItems,
	int VirtualizedComposedItems)
{
	public static UiReactiveUpdateMetrics None { get; } = new(
		UiReactiveUpdateReason.None,
		0L,
		0,
		0,
		0,
		0,
		0,
		false,
		0,
		0,
		0);
}
