using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.ChunkDebug;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

internal sealed class BrowserMinimapCompositedOverlayNativeMarkerBridgeSystem : ISystem<float>
{
	private readonly GameEngine _engine;
	private readonly BrowserMinimapCompositedOverlayLayoutState _layoutState;
	private readonly HashSet<Entity> _disclosedOwners = new();
	private Entity _viewer = Entity.Null;
	private string _mapId = string.Empty;
	private bool _disposed;

	public BrowserMinimapCompositedOverlayNativeMarkerBridgeSystem(
		GameEngine engine,
		BrowserMinimapCompositedOverlayLayoutState layoutState)
	{
		_engine = engine ?? throw new ArgumentNullException(nameof(engine));
		_layoutState = layoutState ?? throw new ArgumentNullException(nameof(layoutState));
	}

	public void Initialize()
	{
		_viewer = _engine.World.Create();
	}

	public void BeforeUpdate(in float dt)
	{
	}

	public void Update(in float dt)
	{
		if (_disposed)
		{
			return;
		}

		SuppressShowcaseNoise();
		ApplyWebOwnedMinimapViewport();
		EnsureNativeMinimapMarkerKnowledge();
	}

	public void AfterUpdate(in float dt)
	{
	}

	public void Dispose()
	{
		_disposed = true;
		if (_engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime runtime)
		{
			runtime.NativeChromeVisible = true;
			runtime.ClearExternalFieldRect();
			runtime.ClearFieldClipShape();
		}
	}

	private void SuppressShowcaseNoise()
	{
		_engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)?.Clear();
		_engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)?.Clear();
		if (_engine.GetService(CoreServiceKeys.ChunkDebugPanelRuntime) is ChunkDebugPanelRuntime chunkDebug)
		{
			chunkDebug.Visible = false;
		}

		if (_engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
		{
			renderDebug.DrawWorldHudBars = false;
			renderDebug.DrawWorldHudText = false;
			renderDebug.DrawCombatText = false;
			renderDebug.DrawDebugDraw = false;
		}
	}

	private void EnsureNativeMinimapMarkerKnowledge()
	{
		if (_engine.GetService(CoreServiceKeys.MinimapRuntime) is not MinimapRuntime runtime ||
			_engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is not MinimapMarkerBuffer markers ||
			_engine.GetService(CoreServiceKeys.KnowledgeProjectionStore) is not KnowledgeProjectionStore store)
		{
			return;
		}

		ResetIfMapChanged();

		int markerCount = markers.Count;
		if (markerCount <= 0)
		{
			return;
		}

		Entity viewer = ResolveOrCreateViewer();
		if (viewer == Entity.Null)
		{
			return;
		}

		_engine.SetService(CoreServiceKeys.SelectionViewViewerEntity, viewer);
		ReadOnlySpan<Entity> owners = markers.Owners;
		int currentTick = KnowledgeProjectionConsumer.ResolveCurrentTick(_engine.GlobalContext);
		var record = new KnowledgeDisclosureRecord(
			KnowledgePresence.LiveVisible,
			KnowledgePositionAccess.Live,
			KnowledgeIdMask256.Empty,
			KnowledgeIdMask256.Empty,
			KnowledgeIdMask256.Empty,
			viewer,
			currentTick,
			expiryTick: 0,
			confidencePermille: 1000,
			revision: 0);

		for (int i = 0; i < owners.Length; i++)
		{
			Entity owner = owners[i];
			if (owner == Entity.Null ||
				!_engine.World.IsAlive(owner) ||
				!_disclosedOwners.Add(owner))
			{
				continue;
			}

			store.Upsert(viewer, owner, in record);
		}
	}

	private void ApplyWebOwnedMinimapViewport()
	{
		if (_engine.GetService(CoreServiceKeys.MinimapRuntime) is not MinimapRuntime runtime)
		{
			return;
		}

		runtime.NativeChromeVisible = false;
		SyncScreenBounds();
		if (!_layoutState.TryGetRect(out BrowserMinimapCompositedOverlayRect rect))
		{
			runtime.Visible = false;
			return;
		}

		runtime.SetExternalFieldRect(rect.X, rect.Y, rect.Width, rect.Height);
		runtime.SetFieldClipShape(rect.ClipKind);
		runtime.Visible = true;
	}

	private void SyncScreenBounds()
	{
		if (_engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root ||
			root.Width <= 0f ||
			root.Height <= 0f)
		{
			return;
		}

		_layoutState.UpdateScreenBounds(
			Math.Max(1, (int)MathF.Ceiling(root.Width)),
			Math.Max(1, (int)MathF.Ceiling(root.Height)));
	}

	private void ResetIfMapChanged()
	{
		string mapId = _engine.CurrentMapSession?.MapId.Value ?? string.Empty;
		if (string.Equals(_mapId, mapId, StringComparison.Ordinal))
		{
			return;
		}

		_mapId = mapId;
		_disclosedOwners.Clear();
	}

	private Entity ResolveOrCreateViewer()
	{
		if (TryResolveViewer(CoreServiceKeys.SelectionViewViewerEntity.Name, out Entity viewer) ||
			TryResolveViewer(CoreServiceKeys.LocalPlayerEntity.Name, out viewer))
		{
			_viewer = viewer;
			return viewer;
		}

		if (_viewer == Entity.Null || !_engine.World.IsAlive(_viewer))
		{
			_viewer = _engine.World.Create();
		}

		return _viewer;
	}

	private bool TryResolveViewer(string key, out Entity viewer)
	{
		viewer = Entity.Null;
		return _engine.GlobalContext.TryGetValue(key, out object? value) &&
			value is Entity candidate &&
			candidate != Entity.Null &&
			_engine.World.IsAlive(candidate) &&
			(viewer = candidate) != Entity.Null;
	}
}
