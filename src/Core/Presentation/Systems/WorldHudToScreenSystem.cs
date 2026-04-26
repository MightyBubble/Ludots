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
        private int _lastProjectionRevision = -1;

        private const int MaxBarDim = 512;
        private const int Margin = 200;
        private const float WorldHudCoarseMarginCm = 600f;

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
            int projectionRevision = _projector is IProjectionRevisionProvider revisionProvider
                ? revisionProvider.ProjectionRevision
                : -1;
            if (projectionRevision >= 0 &&
                worldHudRevision == _lastWorldHudRevision &&
                projectionRevision == _lastProjectionRevision)
            {
                _timingDiagnostics?.ObserveWorldHudProjection((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                return;
            }

            _screenHud.Clear();

            var res = _view.Resolution;
            float screenWidth = res.X;
            float screenHeight = res.Y;
            ProjectionSnapshot projectionSnapshot = default;
            bool hasProjectionSnapshot = _projector is IProjectionSnapshotProvider snapshotProvider &&
                                         snapshotProvider.TryGetProjectionSnapshot(out projectionSnapshot);

            var span = _worldHud.GetSpan();
            var lastWorldPosition = new System.Numerics.Vector3(float.NaN, float.NaN, float.NaN);
            var lastScreen = new System.Numerics.Vector2(float.NaN, float.NaN);
            bool useCoarseCull = _cullingDebug != null && _cullingDebug.MaxX > _cullingDebug.MinX && _cullingDebug.MaxY > _cullingDebug.MinY;
            float minX = useCoarseCull ? _cullingDebug!.MinX - WorldHudCoarseMarginCm : 0f;
            float maxX = useCoarseCull ? _cullingDebug!.MaxX + WorldHudCoarseMarginCm : 0f;
            float minZ = useCoarseCull ? _cullingDebug!.MinY - WorldHudCoarseMarginCm : 0f;
            float maxZ = useCoarseCull ? _cullingDebug!.MaxY + WorldHudCoarseMarginCm : 0f;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Owner != Entity.Null &&
                    (!World.IsAlive(item.Owner) ||
                     (World.Has<CullState>(item.Owner) && !World.Get<CullState>(item.Owner).IsVisible)))
                {
                    continue;
                }

                float itemXCm = item.WorldPosition.X * 100f;
                float itemZCm = item.WorldPosition.Z * 100f;
                if (useCoarseCull &&
                    (itemXCm < minX ||
                     itemXCm > maxX ||
                     itemZCm < minZ ||
                     itemZCm > maxZ))
                {
                    continue;
                }

                var screen = item.WorldPosition == lastWorldPosition
                    ? lastScreen
                    : hasProjectionSnapshot
                        ? ProjectWorldToScreenFast(item.WorldPosition, in projectionSnapshot)
                        : _projector.WorldToScreen(item.WorldPosition);
                lastWorldPosition = item.WorldPosition;
                lastScreen = screen;
                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) ||
                    float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                    continue;

                float x = MathF.Round(screen.X - item.Width * 0.5f);
                float y = MathF.Round(screen.Y);

                int ix = (int)x;
                int iy = (int)y;
                int iw = (int)item.Width;
                int ih = (int)item.Height;

                if (item.Kind == WorldHudItemKind.Bar)
                {
                    if (iw <= 0 || ih <= 0 || iw > MaxBarDim || ih > MaxBarDim) continue;
                    if (ix + iw < -Margin || iy + ih < -Margin ||
                        ix > screenWidth + Margin || iy > screenHeight + Margin)
                        continue;

                    _screenHud.TryAddBar(new ScreenHudBarItem
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
                    });
                    continue;
                }

                if (item.Kind == WorldHudItemKind.Text)
                {
                    int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                    if (ix + fontSize < -Margin || iy + fontSize < -Margin ||
                        ix > screenWidth + Margin || iy > screenHeight + Margin)
                    {
                        continue;
                    }

                    _screenHud.TryAddText(new ScreenHudTextItem
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
                    });
                }
            }

            _timingDiagnostics?.ObserveWorldHudProjection((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            _lastWorldHudRevision = worldHudRevision;
            _lastProjectionRevision = projectionRevision;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static System.Numerics.Vector2 ProjectWorldToScreenFast(
            in System.Numerics.Vector3 worldPosition,
            in ProjectionSnapshot projection)
        {
            var clip = System.Numerics.Vector4.Transform(
                new System.Numerics.Vector4(worldPosition, 1f),
                projection.ViewProjection);
            if (clip.W <= 0.001f)
            {
                return new System.Numerics.Vector2(float.NaN, float.NaN);
            }

            float invW = 1f / clip.W;
            float ndcX = clip.X * invW;
            float ndcY = clip.Y * invW;
            if (ndcX < -1f || ndcX > 1f || ndcY < -1f || ndcY > 1f)
            {
                return new System.Numerics.Vector2(float.NaN, float.NaN);
            }

            float screenX = (ndcX + 1f) * 0.5f * projection.Resolution.X;
            float screenY = (1f - ndcY) * 0.5f * projection.Resolution.Y;
            return new System.Numerics.Vector2(screenX, screenY);
        }
    }
}
