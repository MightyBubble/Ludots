using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Projects WorldHudBatchBuffer to screen space and culls off-screen items.
    /// Outputs to ScreenHudBatchBuffer. Adapter draws without projection or culling.
    /// </summary>
    public sealed class WorldHudToScreenSystem : BaseSystem<World, float>
    {
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly WorldHudStringTable? _strings;
        private readonly IScreenProjector _projector;
        private readonly IViewController _view;
        private readonly ScreenHudBatchBuffer _screenHud;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;
        private readonly CameraCullingDebugState? _cullingDebug;
        private int _lastWorldHudRevision = -1;
        private int _lastWorldHudProjectionRevision = -1;
        private int _lastProjectionRevision = -1;
        private int _lastCullVisibilityRevision = -1;
        private int _lastSelectedCount;

        private const int Margin = 200;
        private const float WorldHudCoarseMarginCm = 600f;
        private SelectedHudProjection[] _selectedHudProjections = Array.Empty<SelectedHudProjection>();
        private OwnerVisibilityCacheEntry[] _ownerVisibilityCache = Array.Empty<OwnerVisibilityCacheEntry>();
        private OwnerProjectionCacheEntry[] _ownerProjectionCache = Array.Empty<OwnerProjectionCacheEntry>();
        private int _frameCacheStamp;
        private bool _retainedProjectedBuild;

        public WorldHudToScreenSystem(
            World world,
            WorldHudBatchBuffer worldHud,
            WorldHudStringTable? strings,
            IScreenProjector projector,
            IViewController view,
            ScreenHudBatchBuffer screenHud,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            CameraCullingDebugState? cullingDebug = null)
            : base(world)
        {
            _worldHud = worldHud ?? throw new System.ArgumentNullException(nameof(worldHud));
            _strings = strings;
            _projector = projector ?? throw new System.ArgumentNullException(nameof(projector));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
            _screenHud = screenHud ?? throw new System.ArgumentNullException(nameof(screenHud));
            _timingDiagnostics = timingDiagnostics;
            _cullingDebug = cullingDebug;
        }

        public override void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            int worldHudRevision = _worldHud.ContentRevision;
            int worldHudProjectionRevision = _worldHud.ProjectionRevision;
            int projectionRevision = _projector is IProjectionRevisionProvider revisionProvider
                ? revisionProvider.ProjectionRevision
                : -1;
            int cullVisibilityRevision = _cullingDebug?.VisibilityRevision ?? -1;
            bool projectionUnchanged = projectionRevision >= 0 &&
                                       worldHudProjectionRevision == _lastWorldHudProjectionRevision &&
                                       projectionRevision == _lastProjectionRevision &&
                                       cullVisibilityRevision == _lastCullVisibilityRevision;
            if (projectionUnchanged && worldHudRevision != _lastWorldHudRevision)
            {
                ReadOnlySpan<WorldHudItem> dirtyContent = _worldHud.GetDirtyContentSpan();
                ReadOnlySpan<int> removedStableIds = _worldHud.GetRemovedStableIdSpan();
                if (dirtyContent.Length > 0 || removedStableIds.Length > 0)
                {
                    if (ApplyContentOnlyDeltas(dirtyContent, removedStableIds, ref start))
                    {
                        return;
                    }
                }
            }

            if (projectionRevision >= 0 &&
                worldHudRevision == _lastWorldHudRevision &&
                projectionRevision == _lastProjectionRevision &&
                cullVisibilityRevision == _lastCullVisibilityRevision)
            {
                _timingDiagnostics?.ObserveWorldHudProjection(
                    (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                return;
            }

            _retainedProjectedBuild = true;
            _screenHud.BeginProjectedBuild(retained: _retainedProjectedBuild);
            AdvanceFrameCacheStamp();

            var res = _view.Resolution;
            float screenWidth = res.X;
            float screenHeight = res.Y;
            var span = _worldHud.GetSpan();

            ProjectionSnapshot projectionSnapshot = default;
            bool hasProjectionSnapshot = _projector is IProjectionSnapshotProvider snapshotProvider &&
                                         snapshotProvider.TryGetProjectionSnapshot(out projectionSnapshot);
            System.Numerics.Matrix4x4 viewProjection = hasProjectionSnapshot
                ? projectionSnapshot.ViewProjection
                : default;
            float projectionWidth = hasProjectionSnapshot ? projectionSnapshot.Resolution.X : 0f;
            float projectionHeight = hasProjectionSnapshot ? projectionSnapshot.Resolution.Y : 0f;

            bool useCoarseCull = _cullingDebug != null && _cullingDebug.MaxX > _cullingDebug.MinX && _cullingDebug.MaxY > _cullingDebug.MinY;
            float minX = useCoarseCull ? _cullingDebug!.MinX - WorldHudCoarseMarginCm : 0f;
            float maxX = useCoarseCull ? _cullingDebug!.MaxX + WorldHudCoarseMarginCm : 0f;
            float minZ = useCoarseCull ? _cullingDebug!.MinY - WorldHudCoarseMarginCm : 0f;
            float maxZ = useCoarseCull ? _cullingDebug!.MaxY + WorldHudCoarseMarginCm : 0f;
            int projectedItems = 0;
            int densitySkippedItems = 0;
            bool canReplaySelection = projectionRevision >= 0 &&
                                      worldHudProjectionRevision == _lastWorldHudProjectionRevision &&
                                      projectionRevision == _lastProjectionRevision &&
                                      cullVisibilityRevision == _lastCullVisibilityRevision &&
                                      _lastSelectedCount > 0;
            if (canReplaySelection)
            {
                ReplaySelectedHudItems(ref projectedItems);
                _timingDiagnostics?.ObserveWorldHudProjection(
                    (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
                    span.Length,
                    projectedItems,
                    0);
                _screenHud.EndProjectedBuild(removeUnseenProjectedItems: false);
                _lastWorldHudRevision = worldHudRevision;
                _lastCullVisibilityRevision = cullVisibilityRevision;
                return;
            }

            _lastSelectedCount = 0;
            int projectedBarIndex = 0;
            int projectedTextIndex = 0;

            for (int itemIndex = 0; itemIndex < span.Length;)
            {
                ref readonly WorldHudItem first = ref span[itemIndex];
                if (IsAssignedOwner(first.Owner) && !IsOwnerVisible(first.Owner))
                {
                    itemIndex = SkipAdjacentOwnerItems(span, itemIndex, in first);
                    continue;
                }

                float itemXCm = first.WorldPosition.X * 100f;
                float itemZCm = first.WorldPosition.Z * 100f;
                if (useCoarseCull &&
                    (itemXCm < minX ||
                     itemXCm > maxX ||
                     itemZCm < minZ ||
                     itemZCm > maxZ))
                {
                    itemIndex = SkipAdjacentOwnerItems(span, itemIndex, in first);
                    continue;
                }

                System.Numerics.Vector2 screen;
                if (!TryGetOwnerFrameProjection(first.Owner, first.WorldPosition, out screen))
                {
                    screen = hasProjectionSnapshot
                        ? ProjectWorldToScreenFast(first.WorldPosition, in viewProjection, projectionWidth, projectionHeight)
                        : _projector.WorldToScreen(first.WorldPosition);
                    if (!float.IsNaN(screen.X) &&
                        !float.IsNaN(screen.Y) &&
                        !float.IsInfinity(screen.X) &&
                        !float.IsInfinity(screen.Y))
                    {
                        CacheOwnerFrameProjection(first.Owner, first.WorldPosition, screen);
                    }
                }

                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) ||
                    float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                {
                    itemIndex = SkipAdjacentOwnerItems(span, itemIndex, in first);
                    continue;
                }

                itemIndex = ProjectAdjacentOwnerItems(
                    span,
                    itemIndex,
                    in first,
                    screen,
                    screenWidth,
                    screenHeight,
                    ref projectedItems,
                    ref projectedBarIndex,
                    ref projectedTextIndex);
            }

            _timingDiagnostics?.ObserveWorldHudProjection(
                (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
                span.Length,
                projectedItems,
                densitySkippedItems);
            _screenHud.EndProjectedBuild(removeUnseenProjectedItems: true, projectedBarIndex, projectedTextIndex);
            _lastWorldHudRevision = worldHudRevision;
            _lastWorldHudProjectionRevision = worldHudProjectionRevision;
            _lastProjectionRevision = projectionRevision;
            _lastCullVisibilityRevision = cullVisibilityRevision;
            _worldHud.ClearContentDeltas();
        }

        private bool ApplyContentOnlyDeltas(
            ReadOnlySpan<WorldHudItem> dirtyContent,
            ReadOnlySpan<int> removedStableIds,
            ref long start)
        {
            int cullVisibilityRevision = _cullingDebug?.VisibilityRevision ?? -1;
            int projectedItems = 0;
            for (int i = 0; i < removedStableIds.Length; i++)
            {
                _screenHud.Remove(removedStableIds[i]);
            }

            for (int i = 0; i < dirtyContent.Length; i++)
            {
                ref readonly WorldHudItem item = ref dirtyContent[i];
                if (_screenHud.TryApplyWorldContentDelta(in item))
                {
                    projectedItems++;
                }
            }

            _lastWorldHudRevision = _worldHud.ContentRevision;
            _lastWorldHudProjectionRevision = _worldHud.ProjectionRevision;
            _lastCullVisibilityRevision = cullVisibilityRevision;
            _worldHud.ClearContentDeltas();
            _timingDiagnostics?.ObserveWorldHudProjection(
                (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
                _worldHud.Count,
                _screenHud.Count,
                0);
            return true;
        }

        private void ReplaySelectedHudItems(ref int projectedItems)
        {
            for (int i = 0; i < _lastSelectedCount; i++)
            {
                ref readonly SelectedHudProjection selected = ref _selectedHudProjections[i];
                if (!_worldHud.TryGetByStableId(selected.StableId, out WorldHudItem item))
                {
                    continue;
                }

                if (IsAssignedOwner(item.Owner) && !IsOwnerVisible(item.Owner))
                {
                    continue;
                }

                var screen = selected.ScreenPosition;
                float x = MathF.Round(screen.X - item.Width * 0.5f);
                float y = MathF.Round(screen.Y);
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    if (_screenHud.TryAddProjectedBar(new ScreenHudBarItem
                    {
                        StableId = item.StableId,
                        DirtySerial = item.DirtySerial,
                        ScreenX = x,
                        ScreenY = y,
                        Color0 = item.Color0,
                        Color1 = item.Color1,
                        Width = item.Width,
                        Height = item.Height,
                        Value0 = item.Value0,
                    }))
                    {
                        projectedItems++;
                    }
                    continue;
                }

                if (item.Kind == WorldHudItemKind.Text &&
                    _screenHud.TryAddProjectedText(new ScreenHudTextItem
                    {
                        StableId = item.StableId,
                        DirtySerial = item.DirtySerial,
                        ScreenX = x,
                        ScreenY = y,
                        Color0 = item.Color0,
                        Value0 = item.Value0,
                        Value1 = item.Value1,
                        Id0 = item.Id0,
                        Id1 = item.Id1,
                        FontSize = item.FontSize,
                        Text = item.Text,
                    }))
                {
                    projectedItems++;
                }
            }
        }

        private void RememberSelectedHudProjection(int stableId, System.Numerics.Vector2 screen)
        {
            if (stableId <= 0)
            {
                return;
            }

            if (_lastSelectedCount >= _selectedHudProjections.Length)
            {
                int next = _selectedHudProjections.Length == 0 ? 256 : _selectedHudProjections.Length * 2;
                Array.Resize(ref _selectedHudProjections, next);
            }

            _selectedHudProjections[_lastSelectedCount++] = new SelectedHudProjection(stableId, screen);
        }

        private void ProjectSingleItem(
            in WorldHudItem item,
            System.Numerics.Vector2 screen,
            float screenWidth,
            float screenHeight,
            ref int projectedItems,
            ref int projectedBarIndex,
            ref int projectedTextIndex)
        {
            float x = MathF.Round(screen.X - item.Width * 0.5f);
            float y = MathF.Round(screen.Y);

            int ix = (int)x;
            int iy = (int)y;
            int iw = (int)item.Width;
            int ih = (int)item.Height;

            if (item.Kind == WorldHudItemKind.Bar)
            {
                if (iw <= 0 ||
                    ih <= 0 ||
                    ix + iw < -Margin ||
                    iy + ih < -Margin ||
                    ix > screenWidth + Margin ||
                    iy > screenHeight + Margin)
                {
                    return;
                }

                if (_retainedProjectedBuild &&
                    _screenHud.TryUpsertProjectedBarPosition(
                        projectedBarIndex,
                        item.StableId,
                        item.DirtySerial,
                        x,
                        y))
                {
                    projectedBarIndex++;
                    projectedItems++;
                    return;
                }

                ScreenHudBarItem bar = new()
                {
                    StableId = item.StableId,
                    DirtySerial = item.DirtySerial,
                    ScreenX = x,
                    ScreenY = y,
                    Color0 = item.Color0,
                    Color1 = item.Color1,
                    Width = item.Width,
                    Height = item.Height,
                    Value0 = item.Value0,
                };
                bool accepted = _retainedProjectedBuild
                    ? _screenHud.TryUpsertProjectedBar(in bar, projectedBarIndex)
                    : _screenHud.TryAddProjectedBar(in bar);
                projectedBarIndex++;
                if (accepted)
                {
                    projectedItems++;
                }

                return;
            }

            if (item.Kind != WorldHudItemKind.Text)
            {
                return;
            }

            int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
            if (ix + fontSize < -Margin ||
                iy + fontSize < -Margin ||
                ix > screenWidth + Margin ||
                iy > screenHeight + Margin)
            {
                return;
            }

            if (_retainedProjectedBuild &&
                _screenHud.TryUpsertProjectedTextPosition(
                    projectedTextIndex,
                    item.StableId,
                    item.DirtySerial,
                    x,
                    y))
            {
                projectedTextIndex++;
                projectedItems++;
                return;
            }

            ScreenHudTextItem text = new()
            {
                StableId = item.StableId,
                DirtySerial = item.DirtySerial,
                ScreenX = x,
                ScreenY = y,
                Color0 = item.Color0,
                Value0 = item.Value0,
                Value1 = item.Value1,
                Id0 = item.Id0,
                Id1 = item.Id1,
                FontSize = item.FontSize,
                Text = item.Text,
            };
            bool textAccepted = _retainedProjectedBuild
                ? _screenHud.TryUpsertProjectedText(in text, projectedTextIndex)
                : _screenHud.TryAddProjectedText(in text);
            projectedTextIndex++;
            if (textAccepted)
            {
                projectedItems++;
            }
        }

        private int ProjectAdjacentOwnerItems(
            ReadOnlySpan<WorldHudItem> span,
            int startIndex,
            in WorldHudItem first,
            System.Numerics.Vector2 screen,
            float screenWidth,
            float screenHeight,
            ref int projectedItems,
            ref int projectedBarIndex,
            ref int projectedTextIndex)
        {
            int index = startIndex;
            do
            {
                ref readonly WorldHudItem item = ref span[index];
                ProjectSingleItem(
                    in item,
                    screen,
                    screenWidth,
                    screenHeight,
                    ref projectedItems,
                    ref projectedBarIndex,
                    ref projectedTextIndex);
                index++;
            }
            while (index < span.Length &&
                   span[index].Owner == first.Owner &&
                   span[index].WorldPosition == first.WorldPosition);

            return index;
        }

        private static int SkipAdjacentOwnerItems(
            ReadOnlySpan<WorldHudItem> span,
            int startIndex,
            in WorldHudItem first)
        {
            int index = startIndex + 1;
            while (index < span.Length &&
                   span[index].Owner == first.Owner &&
                   span[index].WorldPosition == first.WorldPosition)
            {
                index++;
            }

            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsOwnerVisible(Entity owner)
        {
            int ownerKey = ResolveOwnerCacheKey(owner);
            if ((uint)ownerKey < (uint)_ownerVisibilityCache.Length)
            {
                ref OwnerVisibilityCacheEntry cached = ref _ownerVisibilityCache[ownerKey];
                if (cached.Stamp == _frameCacheStamp && cached.Version == owner.Version)
                {
                    return cached.IsVisible;
                }
            }

            bool visible = World.IsAlive(owner) &&
                (!World.Has<CullState>(owner) || World.Get<CullState>(owner).IsVisible);
            SetOwnerVisible(ownerKey, owner.Version, visible);
            return visible;
        }

        private void SetOwnerVisible(int ownerKey, int ownerVersion, bool visible)
        {
            if (ownerKey >= _ownerVisibilityCache.Length)
            {
                int next = _ownerVisibilityCache.Length == 0 ? 1024 : _ownerVisibilityCache.Length;
                while (next <= ownerKey)
                {
                    next *= 2;
                }

                Array.Resize(ref _ownerVisibilityCache, next);
            }

            _ownerVisibilityCache[ownerKey] = new OwnerVisibilityCacheEntry(_frameCacheStamp, ownerVersion, visible);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetOwnerFrameProjection(
            Entity owner,
            System.Numerics.Vector3 worldPosition,
            out System.Numerics.Vector2 screen)
        {
            if (!IsAssignedOwner(owner))
            {
                screen = default;
                return false;
            }

            int ownerKey = ResolveOwnerCacheKey(owner);
            if ((uint)ownerKey < (uint)_ownerProjectionCache.Length)
            {
                ref OwnerProjectionCacheEntry cached = ref _ownerProjectionCache[ownerKey];
                if (cached.Stamp == _frameCacheStamp &&
                    cached.Version == owner.Version &&
                    cached.WorldPosition == worldPosition)
                {
                    screen = cached.ScreenPosition;
                    return true;
                }
            }

            screen = default;
            return false;
        }

        private void CacheOwnerFrameProjection(
            Entity owner,
            System.Numerics.Vector3 worldPosition,
            System.Numerics.Vector2 screen)
        {
            if (!IsAssignedOwner(owner))
            {
                return;
            }

            int ownerKey = ResolveOwnerCacheKey(owner);
            if (ownerKey >= _ownerProjectionCache.Length)
            {
                int next = _ownerProjectionCache.Length == 0 ? 1024 : _ownerProjectionCache.Length;
                while (next <= ownerKey)
                {
                    next *= 2;
                }

                Array.Resize(ref _ownerProjectionCache, next);
            }

            _ownerProjectionCache[ownerKey] = new OwnerProjectionCacheEntry(
                _frameCacheStamp,
                owner.Version,
                worldPosition,
                screen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ResolveOwnerCacheKey(Entity owner)
        {
            return owner.Id + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAssignedOwner(Entity owner)
        {
            return owner.Id >= 0 && owner.Version > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static System.Numerics.Vector2 ProjectWorldToScreenFast(
            in System.Numerics.Vector3 worldPosition,
            in System.Numerics.Matrix4x4 matrix,
            float resolutionX,
            float resolutionY)
        {
            float clipX = (worldPosition.X * matrix.M11) + (worldPosition.Y * matrix.M21) + (worldPosition.Z * matrix.M31) + matrix.M41;
            float clipY = (worldPosition.X * matrix.M12) + (worldPosition.Y * matrix.M22) + (worldPosition.Z * matrix.M32) + matrix.M42;
            float clipW = (worldPosition.X * matrix.M14) + (worldPosition.Y * matrix.M24) + (worldPosition.Z * matrix.M34) + matrix.M44;
            if (clipW <= 0.001f)
            {
                return new System.Numerics.Vector2(float.NaN, float.NaN);
            }

            float invW = 1f / clipW;
            float ndcX = clipX * invW;
            float ndcY = clipY * invW;
            if (ndcX < -1f || ndcX > 1f || ndcY < -1f || ndcY > 1f)
            {
                return new System.Numerics.Vector2(float.NaN, float.NaN);
            }

            float screenX = (ndcX + 1f) * 0.5f * resolutionX;
            float screenY = (1f - ndcY) * 0.5f * resolutionY;
            return new System.Numerics.Vector2(screenX, screenY);
        }

        private void AdvanceFrameCacheStamp()
        {
            _frameCacheStamp++;
            if (_frameCacheStamp != int.MaxValue)
            {
                return;
            }

            Array.Clear(_ownerVisibilityCache, 0, _ownerVisibilityCache.Length);
            Array.Clear(_ownerProjectionCache, 0, _ownerProjectionCache.Length);
            _frameCacheStamp = 1;
        }

        private readonly record struct OwnerVisibilityCacheEntry(int Stamp, int Version, bool IsVisible);

        private readonly record struct OwnerProjectionCacheEntry(
            int Stamp,
            int Version,
            System.Numerics.Vector3 WorldPosition,
            System.Numerics.Vector2 ScreenPosition);

        private readonly record struct SelectedHudProjection(
            int StableId,
            System.Numerics.Vector2 ScreenPosition);
    }
}
