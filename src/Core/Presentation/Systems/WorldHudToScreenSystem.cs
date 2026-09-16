using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Numerics;
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
        private readonly Func<IContinuousHeightmap?>? _heightmapProvider;
        private readonly TerrainHudOcclusionHorizon _horizon = new();
        private int _frameStamp;
        private int _horizonFrameStamp = -1;
        private int _lastWorldHudRevision = -1;
        private int _lastWorldHudProjectionRevision = -1;
        private int _lastWorldHudPositionRevision = -1;
        private int _lastWorldHudStructuralRevision = -1;
        private int _lastProjectionRevision = -1;
        private int _lastCullVisibilityRevision = -1;

        private static readonly bool HudGateTraceEnabled =
            Environment.GetEnvironmentVariable("LUDOTS_HUD_GATE_TRACE") is "1" or "true" or "yes" or "on";

        private static int _gateTraceFrame;

        public const int ProjectionMarginPixels = 200;
        public const float ProjectionCoarseMarginCm = 600f;
        private readonly HudOwnerFrameSnapshot _ownerSnapshot = new();
        private OwnerProjectionCacheEntry[] _ownerProjectionCache = Array.Empty<OwnerProjectionCacheEntry>();
        private int _frameCacheStamp;
        private bool _retainedProjectedBuild;
        private int _lastHeightmapRevision = -1;
        private IContinuousHeightmap? _lastHeightmap;

        public WorldHudToScreenSystem(
            World world,
            WorldHudBatchBuffer worldHud,
            WorldHudStringTable? strings,
            IScreenProjector projector,
            IViewController view,
            ScreenHudBatchBuffer screenHud,
            PresentationTimingDiagnostics? timingDiagnostics = null,
            CameraCullingDebugState? cullingDebug = null,
            Func<IContinuousHeightmap?>? heightmapProvider = null)
            : base(world)
        {
            _worldHud = worldHud ?? throw new System.ArgumentNullException(nameof(worldHud));
            _strings = strings;
            _projector = projector ?? throw new System.ArgumentNullException(nameof(projector));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
            _screenHud = screenHud ?? throw new System.ArgumentNullException(nameof(screenHud));
            _timingDiagnostics = timingDiagnostics;
            _cullingDebug = cullingDebug;
            _heightmapProvider = heightmapProvider;
        }

        public override void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            _frameStamp++;
            int worldHudRevision = _worldHud.ContentRevision;
            int worldHudProjectionRevision = _worldHud.ProjectionRevision;
            int positionRevision = _worldHud.PositionRevision;
            int structuralRevision = _worldHud.StructuralRevision;
            int projectionRevision = _projector is IProjectionRevisionProvider revisionProvider
                ? revisionProvider.ProjectionRevision
                : -1;
            IContinuousHeightmap? heightmap = _heightmapProvider?.Invoke();
            int heightmapRevision = heightmap is IContinuousHeightmapRenderSource renderSource
                ? renderSource.Revision
                : heightmap == null ? -1 : 0;
            bool terrainUnchanged = ReferenceEquals(heightmap, _lastHeightmap) &&
                heightmapRevision == _lastHeightmapRevision &&
                (heightmap == null || heightmap is IContinuousHeightmapRenderSource);
            int cullVisibilityRevision = _cullingDebug?.VisibilityRevision ?? -1;

            ProjectionSnapshot projectionSnapshot = default;
            bool hasProjectionSnapshot = _projector is IProjectionSnapshotProvider snapshotProvider &&
                                         snapshotProvider.TryGetProjectionSnapshot(out projectionSnapshot);
            System.Numerics.Matrix4x4 inverseProjection = default;
            if (heightmap != null && (!hasProjectionSnapshot ||
                !System.Numerics.Matrix4x4.Invert(projectionSnapshot.ViewProjection, out inverseProjection)))
            {
                throw new InvalidOperationException("Terrain HUD occlusion requires an invertible presentation projection snapshot.");
            }

            bool cameraStill = projectionRevision >= 0 && projectionRevision == _lastProjectionRevision;
            bool positionsChanged = positionRevision != _lastWorldHudPositionRevision;
            bool structuralChanged = structuralRevision != _lastWorldHudStructuralRevision;
            bool projectionChanged = worldHudProjectionRevision != _lastWorldHudProjectionRevision;
            bool contentChanged = worldHudRevision != _lastWorldHudRevision;

            if (HudGateTraceEnabled && (_gateTraceFrame++ % 30) == 0)
            {
                Ludots.Core.Diagnostics.Log.Info(
                    in Ludots.Core.Diagnostics.LogChannels.Presentation,
                    $"[hudgate] f={_gateTraceFrame} rev proj={projectionRevision}/{_lastProjectionRevision} cameraStill={cameraStill} terrainUnchanged={terrainUnchanged} projChanged={projectionChanged} contentChanged={contentChanged} posChanged={positionsChanged} structChanged={structuralChanged} span={_worldHud.Count}");
            }

            // 属主快照按存储序单趟拉取；仅在有值绑定文本要刷新或本帧确有投影工作时付费，
            // 纯静帧早退保持零成本。值刷新先于一切早退判定，静帧也保持数值鲜活。
            bool needsValueRefresh = _screenHud.HasAttributeBoundTexts;
            bool earlyExitEligible = cameraStill && terrainUnchanged &&
                !projectionChanged && !contentChanged;
            if (needsValueRefresh || !earlyExitEligible)
            {
                _ownerSnapshot.Rebuild(World);
            }

            if (needsValueRefresh)
            {
                _screenHud.RefreshAttributeBoundTexts(_ownerSnapshot, World);
            }

            // 相机/地形未变时的三档轻路径。属主剔除/LOD 翻转不再作废轻路径：条目增删由
            // 发射侧保留 lane 处理（Remove/TryAdd 经 removed/dirty 增量流入 tier 2/3），
            // 投影层的 IsOwnerVisible 全量核对只是相机几何变化时的兜底——10K 移动单位每帧
            // 都有 LOD 跨界者，把 cull 版本腿留在门控里会让全量重建（2 万条×4.6ms）永不休眠。
            if (cameraStill && terrainUnchanged)
            {
                // 1) 什么都没变 → 0 成本早退。
                if (!projectionChanged && !contentChanged)
                {
                    _timingDiagnostics?.ObserveWorldHudProjection(
                        (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                    return;
                }

                // 2) 仅内容（值/文本）变化、未涉及任何新的世界位置 → 内容增量透传。
                if (!projectionChanged)
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

                // 3) 保留位图位置/内容增量（无结构变化）→ 只重投脏项，不重做遮挡。
                if (positionsChanged && !structuralChanged)
                {
                    if (ApplyRetainedPropertyDeltas(heightmap, ref start))
                    {
                        return;
                    }
                }
            }

            // 4) 几何变化或结构变化：全量重建（遮挡结果经跨帧缓存，密集人群命中率极高）。
            _retainedProjectedBuild = true;
            _screenHud.BeginProjectedBuild(retained: _retainedProjectedBuild);
            AdvanceFrameCacheStamp();

            var res = _view.Resolution;
            float screenWidth = res.X;
            float screenHeight = res.Y;
            var span = _worldHud.GetSpan();

            System.Numerics.Matrix4x4 viewProjection = hasProjectionSnapshot
                ? projectionSnapshot.ViewProjection
                : default;
            float projectionWidth = hasProjectionSnapshot ? projectionSnapshot.Resolution.X : 0f;
            float projectionHeight = hasProjectionSnapshot ? projectionSnapshot.Resolution.Y : 0f;

            bool useCoarseCull = _cullingDebug != null && _cullingDebug.MaxX > _cullingDebug.MinX && _cullingDebug.MaxY > _cullingDebug.MinY;
            float minX = useCoarseCull ? _cullingDebug!.MinX - ProjectionCoarseMarginCm : 0f;
            float maxX = useCoarseCull ? _cullingDebug!.MaxX + ProjectionCoarseMarginCm : 0f;
            float minZ = useCoarseCull ? _cullingDebug!.MinY - ProjectionCoarseMarginCm : 0f;
            float maxZ = useCoarseCull ? _cullingDebug!.MaxY + ProjectionCoarseMarginCm : 0f;
            int projectedItems = 0;
            int densitySkippedItems = 0;
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
                        if (!ResolveTerrainOcclusion(
                                first.WorldPosition,
                                heightmap,
                                screen,
                                in projectionSnapshot,
                                in inverseProjection))
                        {
                            screen = new System.Numerics.Vector2(float.NaN);
                        }
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
            _lastHeightmapRevision = heightmapRevision;
            _lastHeightmap = heightmap;
            _lastWorldHudPositionRevision = positionRevision;
            _lastWorldHudStructuralRevision = structuralRevision;
            _worldHud.ClearContentDeltas();
            _worldHud.ClearPositionDeltas();
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

            // ProjectWorldToScreenFast 以 NaN 表示屏幕外/相机后；不得放行到保留 upsert——
            // (int)NaN 强转得 0 会被边界剔除误判为屏幕内，NaN 位置随后毒化保留槽。
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                return;
            }

            int ix = (int)x;
            int iy = (int)y;
            int iw = (int)item.Width;
            int ih = (int)item.Height;

            if (item.Kind == WorldHudItemKind.Bar)
            {
                if (iw <= 0 ||
                    ih <= 0 ||
                    ix + iw < -ProjectionMarginPixels ||
                    iy + ih < -ProjectionMarginPixels ||
                    ix > screenWidth + ProjectionMarginPixels ||
                    iy > screenHeight + ProjectionMarginPixels)
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
            if (ix + fontSize < -ProjectionMarginPixels ||
                iy + fontSize < -ProjectionMarginPixels ||
                ix > screenWidth + ProjectionMarginPixels ||
                iy > screenHeight + ProjectionMarginPixels)
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
                ValueBound = item.ValueBound,
                BoundAttributeId = item.BoundAttributeId,
                Owner = item.Owner,
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
            return _ownerSnapshot.TryGetRow(owner, out int row)
                ? _ownerSnapshot.IsVisible(row)
                : World.IsAlive(owner);
        }

        private static bool IsTerrainVisible(
            System.Numerics.Vector3 worldPosition,
            IContinuousHeightmap? heightmap,
            System.Numerics.Vector2 screen,
            in ProjectionSnapshot projectionSnapshot,
            in System.Numerics.Matrix4x4 inverseProjection)
        {
            if (heightmap == null)
            {
                return true;
            }

            var nearClip = new System.Numerics.Vector4(
                screen.X * 2f / projectionSnapshot.Resolution.X - 1f,
                1f - screen.Y * 2f / projectionSnapshot.Resolution.Y, 0f, 1f);
            var nearWorld = System.Numerics.Vector4.Transform(nearClip, inverseProjection);
            var origin = new System.Numerics.Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z) / nearWorld.W;
            System.Numerics.Vector3 delta = worldPosition - origin;
            float targetDistance = delta.Length();
            if (!float.IsFinite(targetDistance))
            {
                throw new InvalidOperationException("Terrain HUD occlusion produced a non-finite projection ray.");
            }
            if (targetDistance <= TerrainOcclusionEpsilonMeters)
            {
                return true;
            }

            ScreenRay ray = new ScreenRay(origin, delta / targetDistance);
            if (!heightmap.TryRaycastGround(in ray, out VisualGroundHit hit))
            {
                return true;
            }
            if (!float.IsFinite(hit.DistanceMeters) || hit.DistanceMeters < 0f)
            {
                throw new InvalidOperationException("Terrain HUD occlusion received an invalid ground hit distance.");
            }
            return hit.DistanceMeters + TerrainOcclusionEpsilonMeters >= targetDistance;
        }

        private const float TerrainOcclusionEpsilonMeters = 0.005f;

        private bool ResolveTerrainOcclusion(
            Vector3 worldPosition,
            IContinuousHeightmap? heightmap,
            Vector2 screen,
            in ProjectionSnapshot projectionSnapshot,
            in Matrix4x4 inverseProjection)
        {
            if (heightmap == null)
            {
                return true;
            }

            // 帧内首查构建地平线包络；构建不可用（相机位缺失/不在图内/高度图不可采样）
            // 时逐项回退精确 raycast。
            if (_horizonFrameStamp != _frameStamp)
            {
                _horizonFrameStamp = _frameStamp;
                _horizon.TryRebuild(heightmap, projectionSnapshot.CameraPosition);
            }

            if (_horizon.IsValid)
            {
                return _horizon.IsVisible(worldPosition);
            }

            return IsTerrainVisible(worldPosition, heightmap, screen, in projectionSnapshot, in inverseProjection);
        }

        private bool ApplyRetainedPropertyDeltas(IContinuousHeightmap? heightmap, ref long start)
        {
            ProjectionSnapshot projectionSnapshot = default;
            bool hasProjectionSnapshot = _projector is IProjectionSnapshotProvider snapshotProvider &&
                                         snapshotProvider.TryGetProjectionSnapshot(out projectionSnapshot);
            Matrix4x4 inverseProjection = default;
            if (heightmap != null && (!hasProjectionSnapshot ||
                !Matrix4x4.Invert(projectionSnapshot.ViewProjection, out inverseProjection)))
            {
                throw new InvalidOperationException("Terrain HUD occlusion requires an invertible presentation projection snapshot.");
            }

            int heightmapRevision = heightmap is IContinuousHeightmapRenderSource renderSource
                ? renderSource.Revision
                : heightmap == null ? -1 : 0;
            Matrix4x4 viewProjection = hasProjectionSnapshot ? projectionSnapshot.ViewProjection : default;
            float projectionWidth = hasProjectionSnapshot ? projectionSnapshot.Resolution.X : 0f;
            float projectionHeight = hasProjectionSnapshot ? projectionSnapshot.Resolution.Y : 0f;
            float screenWidth = _view.Resolution.X;
            float screenHeight = _view.Resolution.Y;

            // 移除（结构未变时为空，防御性处理）
            ReadOnlySpan<int> removedStableIds = _worldHud.GetRemovedStableIdSpan();
            for (int i = 0; i < removedStableIds.Length; i++)
            {
                _screenHud.Remove(removedStableIds[i]);
            }

            // 内容值变化
            ReadOnlySpan<WorldHudItem> dirtyContent = _worldHud.GetDirtyContentSpan();
            for (int i = 0; i < dirtyContent.Length; i++)
            {
                if (!_screenHud.TryApplyWorldContentDelta(in dirtyContent[i]))
                {
                    return false; // 丢失 → 回退全量重建，不静默丢弃
                }
            }

            // 位置变化
            ReadOnlySpan<int> positionDirtyStableIds = _worldHud.GetPositionDirtyStableIdSpan();
            for (int i = 0; i < positionDirtyStableIds.Length; i++)
            {
                if (!_worldHud.TryGetByStableId(positionDirtyStableIds[i], out WorldHudItem item))
                {
                    continue; // 已被移除的 id 由 removed 路径处理
                }

                Vector2 screen = hasProjectionSnapshot
                    ? ProjectWorldToScreenFast(item.WorldPosition, in viewProjection, projectionWidth, projectionHeight)
                    : _projector.WorldToScreen(item.WorldPosition);
                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) ||
                    float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                {
                    _screenHud.Remove(item.StableId);
                    continue;
                }

                if (!ResolveTerrainOcclusion(
                        item.WorldPosition,
                        heightmap,
                        screen,
                        in projectionSnapshot,
                        in inverseProjection))
                {
                    _screenHud.Remove(item.StableId);
                    continue;
                }

                if (item.Width <= 0f || float.IsNaN(item.Width))
                {
                    item.Width = 16f;
                }

                float x = MathF.Round(screen.X - item.Width * 0.5f);
                float y = MathF.Round(screen.Y);
                bool offscreen = item.Kind == WorldHudItemKind.Bar
                    ? (x + item.Width < -ProjectionMarginPixels || x > screenWidth + ProjectionMarginPixels ||
                       y + item.Height < -ProjectionMarginPixels || y > screenHeight + ProjectionMarginPixels)
                    : (x + item.FontSize < -ProjectionMarginPixels || x > screenWidth + ProjectionMarginPixels ||
                       y > screenHeight + ProjectionMarginPixels);
                if (offscreen)
                {
                    _screenHud.Remove(item.StableId);
                    continue;
                }

                bool applied;
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    applied = _screenHud.TryUpsertProjectedBarPosition(-1, item.StableId, item.DirtySerial, x, y);
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    applied = _screenHud.TryUpsertProjectedTextPosition(-1, item.StableId, item.DirtySerial, x, y);
                }
                else
                {
                    continue;
                }

                if (!applied)
                {
                    return false; // 目标未在屏幕缓冲 → 回退全量重建
                }
            }

            _lastWorldHudRevision = _worldHud.ContentRevision;
            _lastWorldHudProjectionRevision = _worldHud.ProjectionRevision;
            _lastProjectionRevision = _projector is IProjectionRevisionProvider provider ? provider.ProjectionRevision : -1;
            _lastCullVisibilityRevision = _cullingDebug?.VisibilityRevision ?? -1;
            _lastHeightmapRevision = heightmapRevision;
            _lastHeightmap = heightmap;
            _lastWorldHudPositionRevision = _worldHud.PositionRevision;
            _lastWorldHudStructuralRevision = _worldHud.StructuralRevision;
            _worldHud.ClearContentDeltas();
            _worldHud.ClearPositionDeltas();
            _timingDiagnostics?.ObserveWorldHudProjection(
                (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                _worldHud.Count,
                _screenHud.Count,
                0);
            return true;
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

            Array.Clear(_ownerProjectionCache, 0, _ownerProjectionCache.Length);
            _frameCacheStamp = 1;
        }

        private readonly record struct OwnerProjectionCacheEntry(
            int Stamp,
            int Version,
            System.Numerics.Vector3 WorldPosition,
            System.Numerics.Vector2 ScreenPosition);

    }
}
