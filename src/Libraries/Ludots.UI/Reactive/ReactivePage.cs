using System;
using System.Collections.Generic;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace Ludots.UI.Reactive;

public sealed class ReactivePage<TState>
{
	private readonly Func<ReactiveContext<TState>, UiElementBuilder> _render;

	private readonly ReactiveContext<TState> _context;

	private readonly Dictionary<string, VirtualWindowRequest> _lastVirtualWindows = new Dictionary<string, VirtualWindowRequest>(StringComparer.Ordinal);

	private readonly Dictionary<string, VirtualWindowRequest> _pendingVirtualWindows = new Dictionary<string, VirtualWindowRequest>(StringComparer.Ordinal);

	public TState State { get; private set; }

	public UiScene Scene { get; }

	public UiThemePack? Theme { get; private set; }

	public ReactiveUpdateStats LastUpdateStats { get; private set; } = ReactiveUpdateStats.None;

	public UiReactiveUpdateMetrics LastUpdateMetrics { get; private set; } = UiReactiveUpdateMetrics.None;

	public long FullRecomposeCount { get; private set; }

	public long IncrementalPatchCount { get; private set; }

	public ReactivePage(TState initialState, Func<ReactiveContext<TState>, UiElementBuilder> render, UiThemePack? theme = null, params UiStyleSheet[] styleSheets)
	{
		State = initialState;
		_render = render ?? throw new ArgumentNullException("render");
		Theme = theme;
		Scene = new UiScene();
		Scene.SetReactiveRuntimeRefresh(RefreshRuntimeDependencies);
		_context = new ReactiveContext<TState>(this);
		if (styleSheets != null && styleSheets.Length != 0)
		{
			Scene.SetStyleSheets(styleSheets);
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
			UiVirtualWindow currentWindow = ComputeVerticalVirtualWindow(request.HostElementId, request.TotalCount, request.ItemExtent, request.ViewportExtent, request.Overscan);
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
		UiVirtualWindow window = ComputeVerticalVirtualWindow(hostElementId, totalCount, itemExtent, viewportExtent, overscan);
		_pendingVirtualWindows[hostElementId] = new VirtualWindowRequest(hostElementId, totalCount, itemExtent, viewportExtent, overscan, window);
		return window;
	}

	private void Recompose(UiReactiveUpdateReason reason)
	{
		_pendingVirtualWindows.Clear();
		Scene.Dispatcher.Reset();
		int nextId = Scene.GetNextReactiveNodeIdSeed();
		UiNode root = _render(_context).Build(Scene.Dispatcher, ref nextId);
		UiRetainedPatchStats patchStats = Scene.ApplyReactiveRoot(root);

		_lastVirtualWindows.Clear();
		foreach (KeyValuePair<string, VirtualWindowRequest> item in _pendingVirtualWindows)
		{
			_lastVirtualWindows[item.Key] = item.Value;
		}

		var currentWindows = new Dictionary<string, UiVirtualWindow>(StringComparer.Ordinal);
		int totalVirtualizedItems = 0;
		int composedVirtualizedItems = 0;
		foreach (VirtualWindowRequest request in _lastVirtualWindows.Values)
		{
			currentWindows[request.HostElementId] = request.Window;
			totalVirtualizedItems += request.TotalCount;
			composedVirtualizedItems += request.Window.VisibleCount;
		}

		LastUpdateMetrics = new UiReactiveUpdateMetrics(
			reason,
			Scene.Version,
			patchStats.ReusedNodes,
			patchStats.PatchedNodes,
			patchStats.InsertedNodes,
			patchStats.RemovedNodes,
			patchStats.ReplacedNodes,
			patchStats.FullRemount,
			currentWindows.Count,
			totalVirtualizedItems,
			composedVirtualizedItems);
		ReactiveApplyMode applyMode = patchStats.FullRemount
			? ReactiveApplyMode.FullRecompose
			: (patchStats.HasChanges ? ReactiveApplyMode.IncrementalPatch : ReactiveApplyMode.None);
		LastUpdateStats = new ReactiveUpdateStats(applyMode, patchStats.PatchedNodes);
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
	}

	private UiVirtualWindow ComputeVerticalVirtualWindow(string hostElementId, int totalCount, float itemExtent, float viewportExtent, int overscan)
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

		UiNode? host = Scene.FindByElementId(hostElementId);
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
