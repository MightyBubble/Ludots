using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace Ludots.UI.Surface;

public sealed class UiSurfaceHost : IUiSurfaceHost
{
	private readonly UIRoot _root;

	private readonly UiScene _scene;

	private readonly Dictionary<string, LeaseEntry> _leases = new(StringComparer.Ordinal);

	private readonly UiSurfaceBuildContext _buildContext;

	private long _nextLeaseId;

	private int _nextGeneration;

	private bool _pendingRebuild;

	private bool _isRebuilding;

	public UiScene? Scene => ReferenceEquals(_root.Scene, _scene) ? _scene : null;

	public UiSurfaceHost(UIRoot root, IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		_root = root ?? throw new ArgumentNullException(nameof(root));
		_scene = new UiScene(
			textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer)),
			imageSizeProvider ?? throw new ArgumentNullException(nameof(imageSizeProvider)));
		_scene.SetReactiveRuntimeRefresh(RefreshRuntimeDependencies);
		_buildContext = new UiSurfaceBuildContext(_scene);
	}

	public UiSurfaceLeaseHandle Acquire(UiSurfaceLeaseRequest request)
	{
		ArgumentNullException.ThrowIfNull(request, nameof(request));
		if (_leases.TryGetValue(request.OwnerId, out LeaseEntry existing))
		{
			existing.Request = request;
			RequestRebuild();
			return existing.Handle;
		}

		UiSurfaceLeaseHandle handle = new UiSurfaceLeaseHandle(
			request.OwnerId,
			++_nextLeaseId,
			++_nextGeneration);
		_leases.Add(request.OwnerId, new LeaseEntry(handle, request));
		RequestRebuild();
		return handle;
	}

	public bool Revalidate(UiSurfaceLeaseHandle handle)
	{
		return TryGetEntry(handle, out _);
	}

	public void Publish(UiSurfaceLeaseHandle handle, UiSurfaceContribution contribution)
	{
		ArgumentNullException.ThrowIfNull(contribution, nameof(contribution));
		LeaseEntry entry = GetRequiredEntry(handle);
		entry.Subscription?.Dispose();
		entry.Contribution = contribution;
		entry.Subscription = contribution.SubscribeInvalidated(RequestRebuild);
		// Mount / replace the contribution tree. Content ticks use Invalidate + SetState;
		// calling Publish every frame forces a full retained-scene rebuild.
		RebuildNow();
	}

	public void Invalidate(UiSurfaceLeaseHandle handle)
	{
		GetRequiredEntry(handle);
		RequestRebuild();
	}

	public bool Release(UiSurfaceLeaseHandle handle)
	{
		if (!TryGetEntry(handle, out LeaseEntry? entry))
		{
			return false;
		}

		entry.Subscription?.Dispose();
		_leases.Remove(handle.OwnerId);
		RebuildNow();
		return true;
	}

	private bool RefreshRuntimeDependencies()
	{
		LeaseEntry[] visibleEntries = GetVisibleEntries().ToArray();
		HashSet<string>? runtimeChangedOwners = null;
		if (!_pendingRebuild)
		{
			foreach (LeaseEntry entry in visibleEntries)
			{
				if (entry.Contribution?.HasRuntimeChanges(_scene) == true)
				{
					runtimeChangedOwners ??= new HashSet<string>(StringComparer.Ordinal);
					runtimeChangedOwners.Add(entry.Handle.OwnerId);
				}
			}

			if (runtimeChangedOwners == null)
			{
				return false;
			}
		}

		RebuildNow(runtimeChangedOwners);
		return true;
	}

	private void RequestRebuild()
	{
		_pendingRebuild = true;
		_root.IsDirty = true;
	}

	private void RebuildNow(ISet<string>? runtimeChangedOwners = null)
	{
		if (_isRebuilding)
		{
			RequestRebuild();
			return;
		}

		_isRebuilding = true;
		try
		{
			_pendingRebuild = false;
			LeaseEntry[] visibleEntries = GetVisibleEntries().ToArray();
			if (visibleEntries.Length == 0)
			{
				_scene.LastReactiveUpdateMetrics = UiReactiveUpdateMetrics.None;
				if (ReferenceEquals(_root.Scene, _scene))
				{
					_root.ClearSceneFromHost();
				}
				return;
			}

			ApplySceneStyleScope(visibleEntries);
			_scene.Dispatcher.Reset();
			int nextId = _scene.GetNextReactiveNodeIdSeed();
			UiNode rootNode = BuildHostRoot(visibleEntries).Build(_scene.Dispatcher, ref nextId);
			UiRetainedPatchStats patchStats = _scene.ApplyReactiveRoot(rootNode);
			ApplyVirtualWindows(visibleEntries);
			ApplySurfaceMetrics(visibleEntries, patchStats, runtimeChangedOwners);
			if (!ReferenceEquals(_root.Scene, _scene))
			{
				_root.MountSceneFromHost(_scene);
			}
			else
			{
				_root.IsDirty = true;
			}
		}
		finally
		{
			_isRebuilding = false;
		}
	}

	private IEnumerable<LeaseEntry> GetVisibleEntries()
	{
		List<LeaseEntry> active = _leases.Values
			.Where(entry => entry.Contribution != null)
			.ToList();
		if (active.Count == 0)
		{
			return Array.Empty<LeaseEntry>();
		}

		List<LeaseEntry> exclusive = active
			.Where(entry => entry.Request.Exclusive)
			.OrderByDescending(entry => entry.Request.Priority)
			.ThenByDescending(entry => entry.Handle.LeaseId)
			.Take(1)
			.ToList();
		if (exclusive.Count != 0)
		{
			return exclusive;
		}

		return active
			.OrderBy(entry => entry.Request.Segment)
			.ThenBy(entry => entry.Request.Priority)
			.ThenBy(entry => entry.Handle.LeaseId)
			.ToArray();
	}

	private UiElementBuilder BuildHostRoot(IReadOnlyList<LeaseEntry> entries)
	{
		UiElementBuilder root = new UiElementBuilder(UiNodeKind.Container, "div")
			.Id("ui-surface-host-root")
			.WidthPercent(100f)
			.HeightPercent(100f)
			.PointerEvents(UiPointerEvents.None);
		for (int i = 0; i < entries.Count; i++)
		{
			LeaseEntry entry = entries[i];
			UiSurfaceContribution contribution = entry.Contribution!;
			int zIndex = (int)entry.Request.Segment + entry.Request.Priority + i;
			root.Child(new UiElementBuilder(UiNodeKind.Container, "section")
				.Id("ui-surface-" + SanitizeElementId(entry.Handle.OwnerId))
				.WidthPercent(100f)
				.HeightPercent(100f)
				.Absolute(0f, 0f)
				.ZIndex(zIndex)
				.PointerEvents(UiPointerEvents.None)
				.Child(contribution.BuildRoot(_buildContext)));
		}

		return root;
	}

	private void ApplySceneStyleScope(IReadOnlyList<LeaseEntry> entries)
	{
		List<UiThemePack> themes = entries
			.Select(entry => entry.Contribution!.Theme)
			.Where(theme => theme != null)
			.Cast<UiThemePack>()
			.GroupBy(theme => theme.Key, StringComparer.Ordinal)
			.Select(group => group.First())
			.ToList();
		List<UiStyleSheet> styleSheets = new();
		foreach (LeaseEntry entry in entries)
		{
			styleSheets.AddRange(entry.Contribution!.StyleSheets);
		}

		if (themes.Count == 1)
		{
			_scene.SetTheme(themes[0]);
		}
		else
		{
			_scene.SetTheme(null);
			foreach (UiThemePack theme in themes)
			{
				styleSheets.AddRange(theme.StyleSheets);
			}
		}

		_scene.SetStyleSheets(styleSheets.ToArray());
	}

	private void ApplyVirtualWindows(IReadOnlyList<LeaseEntry> entries)
	{
		Dictionary<string, UiVirtualWindow> windows = new(StringComparer.Ordinal);
		foreach (LeaseEntry entry in entries)
		{
			entry.Contribution!.CollectVirtualWindows(windows);
		}
		_scene.SetVirtualWindows(windows);
	}

	private void ApplySurfaceMetrics(IReadOnlyList<LeaseEntry> entries, UiRetainedPatchStats patchStats, ISet<string>? runtimeChangedOwners)
	{
		UiReactiveUpdateMetrics? latestMetrics = null;
		foreach (LeaseEntry entry in entries)
		{
			UiSurfaceContribution contribution = entry.Contribution!;
			if (runtimeChangedOwners?.Contains(entry.Handle.OwnerId) == true)
			{
				if (contribution.TryRecordRuntimeUpdate(_scene, patchStats, out UiReactiveUpdateMetrics runtimeMetrics))
				{
					latestMetrics = runtimeMetrics;
					continue;
				}
			}

			if (contribution.TryCreateApplyMetrics(_scene, patchStats, out UiReactiveUpdateMetrics applyMetrics))
			{
				latestMetrics = applyMetrics;
			}
		}

		_scene.LastReactiveUpdateMetrics = latestMetrics ?? UiReactiveUpdateMetrics.None;
	}

	private LeaseEntry GetRequiredEntry(UiSurfaceLeaseHandle handle)
	{
		if (!TryGetEntry(handle, out LeaseEntry? entry))
		{
			throw new InvalidOperationException($"UI surface lease '{handle.OwnerId}' is stale or was not acquired from this host.");
		}

		return entry;
	}

	private bool TryGetEntry(UiSurfaceLeaseHandle handle, out LeaseEntry? entry)
	{
		entry = null;
		return handle.IsValid &&
			_leases.TryGetValue(handle.OwnerId, out entry) &&
			entry.Handle.LeaseId == handle.LeaseId &&
			entry.Handle.Generation == handle.Generation;
	}

	private static string SanitizeElementId(string ownerId)
	{
		StringBuilder builder = new StringBuilder(ownerId.Length);
		foreach (char ch in ownerId)
		{
			builder.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-');
		}
		return builder.Length == 0 ? "unknown" : builder.ToString();
	}

	private sealed class LeaseEntry
	{
		public UiSurfaceLeaseHandle Handle { get; }

		public UiSurfaceLeaseRequest Request { get; set; }

		public UiSurfaceContribution? Contribution { get; set; }

		public IDisposable? Subscription { get; set; }

		public LeaseEntry(UiSurfaceLeaseHandle handle, UiSurfaceLeaseRequest request)
		{
			Handle = handle;
			Request = request;
		}
	}
}
