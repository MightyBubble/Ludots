using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

internal static class UiRetainedTreeReconciler
{
	public static bool CanReuseNode(UiNode current, UiNode next)
	{
		ArgumentNullException.ThrowIfNull(current, "current");
		ArgumentNullException.ThrowIfNull(next, "next");
		if (current.Kind != next.Kind || !string.Equals(current.TagName, next.TagName, StringComparison.Ordinal))
		{
			return false;
		}

		bool currentHasId = !string.IsNullOrWhiteSpace(current.ElementId);
		bool nextHasId = !string.IsNullOrWhiteSpace(next.ElementId);
		if (currentHasId || nextHasId)
		{
			return string.Equals(current.ElementId, next.ElementId, StringComparison.Ordinal);
		}

		return true;
	}

	public static UiRetainedPatchStats Reconcile(UiNode currentRoot, UiNode nextRoot)
	{
		var builder = new StatsBuilder();
		ReconcileNode(currentRoot, nextRoot, builder);
		return builder.Build();
	}

	public static int CountSubtree(UiNode node)
	{
		int count = 1;
		for (int i = 0; i < node.Children.Count; i++)
		{
			count += CountSubtree(node.Children[i]);
		}

		return count;
	}

	private static bool ReconcileNode(UiNode current, UiNode next, StatsBuilder stats)
	{
		stats.ReusedNodes++;
		bool definitionChanged = current.ApplyDefinitionFrom(next);
		bool childrenChanged = ReconcileChildren(current, next.Children, stats);
		if (definitionChanged || childrenChanged)
		{
			stats.PatchedNodes++;
		}

		return definitionChanged || childrenChanged;
	}

	private static bool ReconcileChildren(UiNode current, IReadOnlyList<UiNode> nextChildren, StatsBuilder stats)
	{
		IReadOnlyList<UiNode> currentChildren = current.Children;
		if (currentChildren.Count == 0 && nextChildren.Count == 0)
		{
			return false;
		}

		UiNode[] reconciled = new UiNode[nextChildren.Count];
		bool[] consumed = currentChildren.Count == 0 ? Array.Empty<bool>() : new bool[currentChildren.Count];
		Dictionary<string, int>? currentById = BuildChildIndex(currentChildren);
		int unnamedSearchIndex = 0;
		bool changed = currentChildren.Count != nextChildren.Count;

		for (int i = 0; i < nextChildren.Count; i++)
		{
			UiNode nextChild = nextChildren[i];
			int matchIndex = -1;
			if (!string.IsNullOrWhiteSpace(nextChild.ElementId) &&
				currentById != null &&
				currentById.TryGetValue(nextChild.ElementId, out int keyedIndex) &&
				!consumed[keyedIndex] &&
				CanReuseNode(currentChildren[keyedIndex], nextChild))
			{
				matchIndex = keyedIndex;
			}
			else if (string.IsNullOrWhiteSpace(nextChild.ElementId))
			{
				for (int j = unnamedSearchIndex; j < currentChildren.Count; j++)
				{
					if (consumed[j] || !string.IsNullOrWhiteSpace(currentChildren[j].ElementId))
					{
						continue;
					}

					if (!CanReuseNode(currentChildren[j], nextChild))
					{
						continue;
					}

					matchIndex = j;
					unnamedSearchIndex = j + 1;
					break;
				}
			}

			if (matchIndex >= 0)
			{
				consumed[matchIndex] = true;
				UiNode matched = currentChildren[matchIndex];
				ReconcileNode(matched, nextChild, stats);
				reconciled[i] = matched;
				if (matchIndex != i)
				{
					changed = true;
				}

				continue;
			}

			reconciled[i] = nextChild;
			stats.InsertedNodes += CountSubtree(nextChild);
			changed = true;
		}

		for (int i = 0; i < currentChildren.Count; i++)
		{
			if (consumed[i])
			{
				continue;
			}

			stats.RemovedNodes += CountSubtree(currentChildren[i]);
			changed = true;
		}

		if (changed)
		{
			current.ReplaceChildren(reconciled);
		}

		return changed;
	}

	private static Dictionary<string, int>? BuildChildIndex(IReadOnlyList<UiNode> children)
	{
		Dictionary<string, int>? index = null;
		for (int i = 0; i < children.Count; i++)
		{
			string? elementId = children[i].ElementId;
			if (string.IsNullOrWhiteSpace(elementId))
			{
				continue;
			}

			index ??= new Dictionary<string, int>(StringComparer.Ordinal);
			if (!index.ContainsKey(elementId))
			{
				index[elementId] = i;
			}
		}

		return index;
	}

	private sealed class StatsBuilder
	{
		public int ReusedNodes { get; set; }

		public int PatchedNodes { get; set; }

		public int InsertedNodes { get; set; }

		public int RemovedNodes { get; set; }

		public int ReplacedNodes { get; set; }

		public bool FullRemount { get; set; }

		public UiRetainedPatchStats Build()
		{
			return new UiRetainedPatchStats(ReusedNodes, PatchedNodes, InsertedNodes, RemovedNodes, ReplacedNodes, FullRemount);
		}
	}
}
