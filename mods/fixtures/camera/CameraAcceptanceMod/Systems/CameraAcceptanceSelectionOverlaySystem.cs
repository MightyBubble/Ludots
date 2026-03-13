using System;
using System.Numerics;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using CameraAcceptanceMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceSelectionOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 LabelColor = new(0.98f, 0.96f, 0.68f, 1f);
        private static readonly QueryDescription HotpathCrowdQuery = new QueryDescription()
            .WithAll<CameraAcceptanceHotpathCrowdTag, MapEntity, VisualTransform, CullState>();

        private readonly GameEngine _engine;

        public CameraAcceptanceSelectionOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase))
            {
                PublishHotpathSelectionCount(0);
                Observe(start);
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
            {
                Observe(start);
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
            {
                Observe(start);
                return;
            }

            if (_engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is not CameraAcceptanceDiagnosticsState diagnostics ||
                !diagnostics.TextEnabled)
            {
                PublishHotpathSelectionCount(0);
                Observe(start);
                return;
            }

            if (string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase))
            {
                MapId currentMapId = _engine.CurrentMapSession?.MapId ?? default;
                int labelCount = 0;
                _engine.World.Query(in HotpathCrowdQuery, (Entity entity, ref MapEntity mapEntity, ref VisualTransform transform, ref CullState cull) =>
                {
                    if (!MatchesMap(mapEntity, currentMapId) ||
                        !cull.IsVisible ||
                        labelCount >= CameraAcceptanceIds.HotpathSelectionLabelLimit)
                    {
                        return;
                    }

                    if (TryDrawLabel(overlay, projector, entity, transform.Position + new Vector3(0f, 1.35f, 0f)))
                    {
                        labelCount++;
                    }
                });

                PublishHotpathSelectionCount(labelCount);
            }
            else
            {
                Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
                int count = CameraAcceptanceSelectionView.CopySelectedEntities(_engine.World, _engine.GlobalContext, selected);
                for (int i = 0; i < count; i++)
                {
                    Entity entity = selected[i];
                    if (!_engine.World.TryGet(entity, out VisualTransform transform))
                    {
                        continue;
                    }

                    TryDrawLabel(overlay, projector, entity, transform.Position + new Vector3(0f, 1.35f, 0f));
                }

                PublishHotpathSelectionCount(0);
            }

            Observe(start);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        private void Observe(long startTicks)
        {
            if (_engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is CameraAcceptanceDiagnosticsState diagnostics)
            {
                diagnostics.ObserveTextBuild((Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency);
            }
        }

        private void PublishHotpathSelectionCount(int labelCount)
        {
            if (_engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is CameraAcceptanceDiagnosticsState diagnostics)
            {
                diagnostics.PublishHotpathSelectionLabelCount(labelCount);
            }
        }

        private static bool TryDrawLabel(ScreenOverlayBuffer overlay, IScreenProjector projector, Entity entity, Vector3 worldPosition)
        {
            Vector2 screen = projector.WorldToScreen(worldPosition);
            if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
            {
                return false;
            }

            string label = CameraAcceptanceSelectionView.FormatEntityId(entity);
            int x = (int)MathF.Round(screen.X) - label.Length * 4;
            int y = (int)MathF.Round(screen.Y) - 10;
            overlay.AddText(x, y, label, 14, LabelColor);
            return true;
        }

        private static bool MatchesMap(in MapEntity mapEntity, in MapId currentMapId)
        {
            return string.Equals(mapEntity.MapId.Value, currentMapId.Value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
