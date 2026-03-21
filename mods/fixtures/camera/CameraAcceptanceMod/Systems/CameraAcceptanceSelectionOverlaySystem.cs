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
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceSelectionOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 LabelColor = new(0.98f, 0.96f, 0.68f, 1f);
        private static readonly QueryDescription HotpathCrowdQuery = new QueryDescription()
            .WithAll<CameraAcceptanceHotpathCrowdTag, MapEntity, VisualTransform, CullState>();

        private readonly GameEngine _engine;
        private int _entityIdTokenId;

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

            if (_engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is not WorldHudBatchBuffer worldHud)
            {
                Observe(start);
                return;
            }

            if (!TryResolveEntityIdToken(out int entityIdTokenId))
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

                    if (TryQueueLabel(worldHud, entityIdTokenId, entity, transform.Position + new Vector3(0f, 1.35f, 0f)))
                    {
                        labelCount++;
                    }
                });

                PublishHotpathSelectionCount(labelCount);
            }
            else
            {
                Entity[] selected = CameraAcceptanceSelectionView.SnapshotSelectedEntities(_engine.World, _engine.GlobalContext);
                int count = selected.Length;
                for (int i = 0; i < count; i++)
                {
                    Entity entity = selected[i];
                    if (!_engine.World.TryGet(entity, out VisualTransform transform))
                    {
                        continue;
                    }

                    TryQueueLabel(worldHud, entityIdTokenId, entity, transform.Position + new Vector3(0f, 1.35f, 0f));
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

        private bool TryResolveEntityIdToken(out int tokenId)
        {
            if (_entityIdTokenId > 0)
            {
                tokenId = _entityIdTokenId;
                return true;
            }

            if (_engine.GetService(CoreServiceKeys.PresentationTextCatalog) is not PresentationTextCatalog catalog)
            {
                throw new InvalidOperationException("PresentationTextCatalog is required for camera acceptance selection labels.");
            }

            tokenId = catalog.GetTokenId(WellKnownHudTextKeys.EntityId);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException($"Missing HUD text token '{WellKnownHudTextKeys.EntityId}' required by camera acceptance selection labels.");
            }

            _entityIdTokenId = tokenId;
            return true;
        }

        private static bool TryQueueLabel(WorldHudBatchBuffer worldHud, int tokenId, Entity entity, Vector3 worldPosition)
        {
            string label = CameraAcceptanceSelectionView.FormatEntityId(entity);
            var packet = PresentationTextPacket.FromToken(tokenId);
            packet.SetArg(0, PresentationTextArg.FromInt32(entity.Id));

            return worldHud.TryAdd(new WorldHudItem
            {
                StableId = HudItemIdentity.ComposeStableId(entity.Id, WorldHudItemKind.Text, discriminator: 3),
                DirtySerial = HudItemIdentity.ComposeTextDirtySerial(
                    fontSize: 14,
                    legacyStringId: 0,
                    legacyModeId: 0,
                    value0: 0f,
                    value1: 0f,
                    color: LabelColor,
                    packet: packet),
                Kind = WorldHudItemKind.Text,
                WorldPosition = worldPosition,
                Width = label.Length * 8f,
                FontSize = 14,
                Color0 = LabelColor,
                Text = packet,
            });
        }

        private static bool MatchesMap(in MapEntity mapEntity, in MapId currentMapId)
        {
            return string.Equals(mapEntity.MapId.Value, currentMapId.Value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
