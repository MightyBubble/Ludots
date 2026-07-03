using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace Ludots.UI.Surface;

public sealed class UiSurfaceContribution
{
	private static readonly IReadOnlyList<UiStyleSheet> EmptyStyleSheets = Array.Empty<UiStyleSheet>();

	private readonly Func<UiSurfaceBuildContext, UiElementBuilder> _buildRoot;

	private readonly Func<UiScene, bool>? _hasRuntimeChanges;

	private readonly Action<IDictionary<string, UiVirtualWindow>>? _collectVirtualWindows;

	private readonly Func<Action, IDisposable>? _subscribeInvalidated;

	private readonly Func<UiScene, UiRetainedPatchStats, UiReactiveUpdateMetrics>? _createApplyMetrics;

	private readonly Func<UiScene, UiRetainedPatchStats, UiReactiveUpdateMetrics>? _recordRuntimeUpdate;

	public UiThemePack? Theme { get; }

	public IReadOnlyList<UiStyleSheet> StyleSheets { get; }

	private UiSurfaceContribution(
		Func<UiSurfaceBuildContext, UiElementBuilder> buildRoot,
		UiThemePack? theme,
		IReadOnlyList<UiStyleSheet> styleSheets,
		Func<UiScene, bool>? hasRuntimeChanges = null,
		Action<IDictionary<string, UiVirtualWindow>>? collectVirtualWindows = null,
		Func<Action, IDisposable>? subscribeInvalidated = null,
		Func<UiScene, UiRetainedPatchStats, UiReactiveUpdateMetrics>? createApplyMetrics = null,
		Func<UiScene, UiRetainedPatchStats, UiReactiveUpdateMetrics>? recordRuntimeUpdate = null)
	{
		_buildRoot = buildRoot ?? throw new ArgumentNullException(nameof(buildRoot));
		Theme = theme;
		StyleSheets = styleSheets;
		_hasRuntimeChanges = hasRuntimeChanges;
		_collectVirtualWindows = collectVirtualWindows;
		_subscribeInvalidated = subscribeInvalidated;
		_createApplyMetrics = createApplyMetrics;
		_recordRuntimeUpdate = recordRuntimeUpdate;
	}

	public static UiSurfaceContribution FromBuilder(
		Func<UiElementBuilder> buildRoot,
		UiThemePack? theme = null,
		IEnumerable<UiStyleSheet>? styleSheets = null)
	{
		ArgumentNullException.ThrowIfNull(buildRoot, nameof(buildRoot));
		return FromBuilder(_ => buildRoot(), theme, styleSheets);
	}

	public static UiSurfaceContribution FromBuilder(
		Func<UiSurfaceBuildContext, UiElementBuilder> buildRoot,
		UiThemePack? theme = null,
		IEnumerable<UiStyleSheet>? styleSheets = null)
	{
		return new UiSurfaceContribution(
			buildRoot,
			theme,
			FreezeStyleSheets(styleSheets));
	}

	public static UiSurfaceContribution FromReactivePage<TState>(ReactivePage<TState> page)
	{
		ArgumentNullException.ThrowIfNull(page, nameof(page));
		return new UiSurfaceContribution(
			context => page.ComposeCurrentRoot(context.Scene),
			page.Theme,
			FreezeStyleSheets(page.StyleSheets),
			page.HasSurfaceRuntimeWindowChanges,
			page.AddCurrentVirtualWindowsTo,
			invalidated =>
			{
				page.Changed += invalidated;
				return new DelegateDisposable(() => page.Changed -= invalidated);
			},
			page.CreateSurfaceApplyMetrics,
			page.RecordSurfaceRuntimeUpdate);
	}

	internal UiElementBuilder BuildRoot(UiSurfaceBuildContext context)
	{
		return _buildRoot(context) ?? throw new InvalidOperationException("UI surface contribution returned a null root.");
	}

	internal bool HasRuntimeChanges(UiScene runtimeScene)
	{
		return _hasRuntimeChanges != null && _hasRuntimeChanges(runtimeScene);
	}

	internal void CollectVirtualWindows(IDictionary<string, UiVirtualWindow> windows)
	{
		_collectVirtualWindows?.Invoke(windows);
	}

	internal IDisposable? SubscribeInvalidated(Action invalidated)
	{
		return _subscribeInvalidated?.Invoke(invalidated);
	}

	internal bool TryCreateApplyMetrics(UiScene runtimeScene, UiRetainedPatchStats patchStats, out UiReactiveUpdateMetrics metrics)
	{
		if (_createApplyMetrics == null)
		{
			metrics = UiReactiveUpdateMetrics.None;
			return false;
		}

		metrics = _createApplyMetrics(runtimeScene, patchStats);
		return true;
	}

	internal bool TryRecordRuntimeUpdate(UiScene runtimeScene, UiRetainedPatchStats patchStats, out UiReactiveUpdateMetrics metrics)
	{
		if (_recordRuntimeUpdate == null)
		{
			metrics = UiReactiveUpdateMetrics.None;
			return false;
		}

		metrics = _recordRuntimeUpdate(runtimeScene, patchStats);
		return true;
	}

	private static IReadOnlyList<UiStyleSheet> FreezeStyleSheets(IEnumerable<UiStyleSheet>? styleSheets)
	{
		if (styleSheets == null)
		{
			return EmptyStyleSheets;
		}

		UiStyleSheet[] array = styleSheets.Where(sheet => sheet != null).ToArray();
		return array.Length == 0 ? EmptyStyleSheets : array;
	}

	private sealed class DelegateDisposable : IDisposable
	{
		private Action? _dispose;

		public DelegateDisposable(Action dispose)
		{
			_dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
		}

		public void Dispose()
		{
			Action? dispose = _dispose;
			_dispose = null;
			dispose?.Invoke();
		}
	}
}
