using System;
using System.Collections.Generic;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace Ludots.UI.Reactive;

public sealed class ReactivePage<TState>
{
	private readonly Func<ReactiveContext<TState>, UiElementBuilder> _render;

	private readonly ReactiveContext<TState> _context;

	private readonly UiStyleSheet[] _styleSheets;

	private readonly Dictionary<string, VirtualWindowRequest> _lastVirtualWindows = new Dictionary<string, VirtualWindowRequest>(StringComparer.Ordinal);

	private readonly Dictionary<string, VirtualWindowRequest> _pendingVirtualWindows = new Dictionary<string, VirtualWindowRequest>(StringComparer.Ordinal);

	private UiScene? _runtimeSceneOverride;

	public TState State { get; private set; }

	public UiScene Scene { get; }

	public UiThemePack? Theme { get; private set; }

	public IReadOnlyList<UiStyleSheet> StyleSheets => _styleSheets;

	public ReactiveUpdateStats LastUpdateStats { get; private set; } = ReactiveUpdateStats.None;

	public UiReactiveUpdateMetrics LastUpdateMetrics { get; private set; } = UiReactiveUpdateMetrics.None;

	public long FullRecomposeCount { get; private set; }

	public long IncrementalPatchCount { get; private set; }

	public event Action? Changed;

	public ReactivePage(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, TState initialState, Func<ReactiveContext<TState>, UiElementBuilder> render, UiThemePack? theme = null, params UiStyleSheet[] styleSheets)
	{
		State = initialState;
		_render = render ?? throw new ArgumentNullException("render");
		_styleSheets = styleSheets ?? Array.Empty<UiStyleSheet>();
		Theme = theme;
		Scene = new UiScene(textMeasurer, imageSizeProvider);
		Scene.SetReactiveRuntimeRefresh(RefreshRuntimeDependencies);
		_context = new ReactiveContext<TState>(this);
		if (_styleSheets.Length != 0)
		{
			Scene.SetStyleSheets(_styleSheets);
		}

		if (Theme != null)
		{
			Scene.SetTheme(Theme);
		}

		Recompose(UiReactiveUpdateReason.Mount);
	}

	public void SetTheme(UiThemePack? theme)
	{
		Theme = theme;
		Scene.SetTheme(theme);
		LastUpdateMetrics = new UiReactiveUpdateMetrics(
			UiReactiveUpdateReason.ThemeChange,
			Scene.Version,
			LastUpdateMetrics.ReusedNodes,
			LastUpdateMetrics.PatchedNodes,
			LastUpdateMetrics.InsertedNodes,
			LastUpdateMetrics.RemovedNodes,
			LastUpdateMetrics.ReplacedNodes,
			LastUpdateMetrics.FullRemount,
			LastUpdateMetrics.VirtualizedWindowCount,
			LastUpdateMetrics.VirtualizedTotalItems,
			LastUpdateMetrics.VirtualizedComposedItems);
		LastUpdateStats = ReactiveUpdateStats.None;
		Scene.LastReactiveUpdateMetrics = LastUpdateMetrics;
		Changed?.Invoke();
	}

	public void SetState(Func<TState, TState> updater)
	{
		ArgumentNullException.ThrowIfNull(updater, "updater");
		State = updater(State);
		Recompose(UiReactiveUpdateReason.StateChange);
	}

	public void Mutate(Action<TState> update)
	{
		ArgumentNullException.ThrowIfNull(update, "update");
		update(State);
		Recompose(UiReactiveUpdateReason.StateChange);
	}

	private bool RefreshRuntimeDependencies()
	{
		if (_lastVirtualWindows.Count == 0)
		{
			return false;
		}

		foreach (VirtualWindowRequest request in _lastVirtualWindows.Values)
		{
			UiVirtualWindow currentWindow = ComputeVerticalVirtualWindow(Scene, request.HostElementId, request.TotalCount, request.ItemExtent, request.ViewportExtent, request.Overscan);
			if (!currentWindow.Equals(request.Window))
			{
				Recompose(UiReactiveUpdateReason.RuntimeWindowChange);
				return true;
			}
		}

		return false;
	}

	public UiVirtualWindow GetVerticalVirtualWindow(string hostElementId, int totalCount, float itemExtent, float viewportExtent, int overscan = 2)
	{
		UiVirtualWindow window = ComputeVerticalVirtualWindow(ResolveRuntimeScene(), hostElementId, totalCount, itemExtent, viewportExtent, overscan);
		_pendingVirtualWindows[hostElementId] = new VirtualWindowRequest(hostElementId, totalCount, itemExtent, viewportExtent, overscan, window);
		return window;
	}

	public UiElementBuilder ComposeCurrentRoot(UiScene runtimeScene)
	{
		ArgumentNullException.ThrowIfNull(runtimeScene, "runtimeScene");
		UiElementBuilder root = ComposeRoot(runtimeScene);
		CommitVirtualWindows(out _, out _, out _);
		return root;
	}

	internal bool HasSurfaceRuntimeWindowChanges(UiScene runtimeScene)
	{
		ArgumentNullException.ThrowIfNull(runtimeScene, "runtimeScene");
		if (_lastVirtualWindows.Count == 0)
		{
			return false;
		}

		foreach (VirtualWindowRequest request in _lastVirtualWindows.Values)
		{
			UiVirtualWindow currentWindow = ComputeVerticalVirtualWindow(runtimeScene, request.HostElementId, request.TotalCount, request.ItemExtent, request.ViewportExtent, request.Overscan);
			if (!currentWindow.Equals(request.Window))
			{
				return true;
			}
		}

		return false;
	}

	internal void AddCurrentVirtualWindowsTo(IDictionary<string, UiVirtualWindow> windows)
	{
		ArgumentNullException.ThrowIfNull(windows, "windows");
		foreach (VirtualWindowRequest request in _lastVirtualWindows.Values)
		{
			windows[request.HostElementId] = request.Window;
		}
	}

	internal UiReactiveUpdateMetrics CreateSurfaceApplyMetrics(UiScene runtimeScene, UiRetainedPatchStats patchStats)
	{
		ArgumentNullException.ThrowIfNull(runtimeScene, "runtimeScene");
		return CreateMetrics(LastUpdateMetrics.Reason, runtimeScene.Version, patchStats);
	}

	internal UiReactiveUpdateMetrics RecordSurfaceRuntimeUpdate(UiScene runtimeScene, UiRetainedPatchStats patchStats)
	{
		ArgumentNullException.ThrowIfNull(runtimeScene, "runtimeScene");
		LastUpdateMetrics = CreateMetrics(UiReactiveUpdateReason.RuntimeWindowChange, runtimeScene.Version, patchStats);
		LastUpdateStats = CreateUpdateStats(patchStats);
		if (patchStats.FullRemount)
		{
			FullRecomposeCount++;
		}
		else if (patchStats.HasChanges)
		{
			IncrementalPatchCount++;
		}

		Scene.LastReactiveUpdateMetrics = LastUpdateMetrics;
		runtimeScene.LastReactiveUpdateMetrics = LastUpdateMetrics;
		return LastUpdateMetrics;
	}

	private void Recompose(UiReactiveUpdateReason reason)
	{
		Scene.Dispatcher.Reset();
		int nextId = Scene.GetNextReactiveNodeIdSeed();
		UiNode root = ComposeRoot(Scene).Build(Scene.Dispatcher, ref nextId);
		UiRetainedPatchStats patchStats = Scene.ApplyReactiveRoot(root);

		CommitVirtualWindows(out Dictionary<string, UiVirtualWindow> currentWindows, out int totalVirtualizedItems, out int composedVirtualizedItems);

		LastUpdateMetrics = CreateMetrics(reason, Scene.Version, patchStats, currentWindows.Count, totalVirtualizedItems, composedVirtualizedItems);
		LastUpdateStats = CreateUpdateStats(patchStats);
		if (patchStats.FullRemount)
		{
			FullRecomposeCount++;
		}
		else if (patchStats.HasChanges)
		{
			IncrementalPatchCount++;
		}
		Scene.LastReactiveUpdateMetrics = LastUpdateMetrics;
		Scene.SetVirtualWindows(currentWindows);
		Changed?.Invoke();
	}

	private UiReactiveUpdateMetrics CreateMetrics(UiReactiveUpdateReason reason, long sceneVersion, UiRetainedPatchStats patchStats)
	{
		GetCurrentVirtualWindowTotals(out int windowCount, out int totalVirtualizedItems, out int composedVirtualizedItems);
		return CreateMetrics(reason, sceneVersion, patchStats, windowCount, totalVirtualizedItems, composedVirtualizedItems);
	}

	private static UiReactiveUpdateMetrics CreateMetrics(
		UiReactiveUpdateReason reason,
		long sceneVersion,
		UiRetainedPatchStats patchStats,
		int windowCount,
		int totalVirtualizedItems,
		int composedVirtualizedItems)
	{
		return new UiReactiveUpdateMetrics(
			reason,
			sceneVersion,
			patchStats.ReusedNodes,
			patchStats.PatchedNodes,
			patchStats.InsertedNodes,
			patchStats.RemovedNodes,
			patchStats.ReplacedNodes,
			patchStats.FullRemount,
			windowCount,
			totalVirtualizedItems,
			composedVirtualizedItems);
	}

	private static ReactiveUpdateStats CreateUpdateStats(UiRetainedPatchStats patchStats)
	{
		ReactiveApplyMode applyMode = patchStats.FullRemount
			? ReactiveApplyMode.FullRecompose
			: (patchStats.HasChanges ? ReactiveApplyMode.IncrementalPatch : ReactiveApplyMode.None);
		return new ReactiveUpdateStats(applyMode, patchStats.PatchedNodes);
	}

	private UiElementBuilder ComposeRoot(UiScene runtimeScene)
	{
		_pendingVirtualWindows.Clear();
		_runtimeSceneOverride = runtimeScene;
		try
		{
			return _render(_context);
		}
		finally
		{
			_runtimeSceneOverride = null;
		}
	}

	private void CommitVirtualWindows(out Dictionary<string, UiVirtualWindow> currentWindows, out int totalVirtualizedItems, out int composedVirtualizedItems)
	{
		_lastVirtualWindows.Clear();
		foreach (KeyValuePair<string, VirtualWindowRequest> item in _pendingVirtualWindows)
		{
			_lastVirtualWindows[item.Key] = item.Value;
		}

		currentWindows = new Dictionary<string, UiVirtualWindow>(StringComparer.Ordinal);
		totalVirtualizedItems = 0;
		composedVirtualizedItems = 0;
		foreach (VirtualWindowRequest request in _lastVirtualWindows.Values)
		{
			currentWindows[request.HostElementId] = request.Window;
			totalVirtualizedItems += request.TotalCount;
			composedVirtualizedItems += request.Window.VisibleCount;
		}
	}

	private void GetCurrentVirtualWindowTotals(out int windowCount, out int totalVirtualizedItems, out int composedVirtualizedItems)
	{
		windowCount = _lastVirtualWindows.Count;
		totalVirtualizedItems = 0;
		composedVirtualizedItems = 0;
		foreach (VirtualWindowRequest request in _lastVirtualWindows.Values)
		{
			totalVirtualizedItems += request.TotalCount;
			composedVirtualizedItems += request.Window.VisibleCount;
		}
	}

	private UiScene ResolveRuntimeScene()
	{
		return _runtimeSceneOverride ?? Scene;
	}

	private UiVirtualWindow ComputeVerticalVirtualWindow(UiScene runtimeScene, string hostElementId, int totalCount, float itemExtent, float viewportExtent, int overscan)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(hostElementId, "hostElementId");
		if (itemExtent <= 0f)
		{
			throw new ArgumentOutOfRangeException("itemExtent");
		}

		if (viewportExtent <= 0f)
		{
			throw new ArgumentOutOfRangeException("viewportExtent");
		}

		if (totalCount <= 0)
		{
			return UiVirtualWindow.Empty(hostElementId, itemExtent, viewportExtent);
		}

		UiNode? host = runtimeScene.FindByElementId(hostElementId);
		float effectiveViewport = host != null && host.LayoutRect.Height > 0.01f ? host.LayoutRect.Height : viewportExtent;
		float scrollOffset = Math.Max(0f, host?.ScrollOffsetY ?? 0f);
		int safeOverscan = Math.Max(0, overscan);
		int baseStart = (int)MathF.Floor(scrollOffset / itemExtent);
		int startIndex = Math.Clamp(baseStart - safeOverscan, 0, totalCount);
		int visibleCapacity = Math.Max(1, (int)MathF.Ceiling(effectiveViewport / itemExtent) + safeOverscan * 2);
		int endIndex = Math.Min(totalCount, startIndex + visibleCapacity);
		float leading = startIndex * itemExtent;
		float trailing = Math.Max(0f, (totalCount - endIndex) * itemExtent);
		return new UiVirtualWindow(hostElementId, totalCount, startIndex, endIndex, itemExtent, effectiveViewport, scrollOffset, leading, trailing);
	}

	private sealed record VirtualWindowRequest(
		string HostElementId,
		int TotalCount,
		float ItemExtent,
		float ViewportExtent,
		int Overscan,
		UiVirtualWindow Window);
}
