using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceFixtureHudSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly QueryDescription _query = new QueryDescription().WithAll<CameraAcceptanceFixtureTag, WorldPositionCm>();
        private readonly Dictionary<int, int> _labelIds = new();

        public CameraAcceptanceFixtureHudSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, CameraAcceptanceIds.ProjectionMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is not WorldHudBatchBuffer worldHud ||
                _engine.GetService(CoreServiceKeys.PresentationWorldHudStrings) is not WorldHudStringTable strings)
            {
                return;
            }

            _engine.World.Query(in _query, (Entity entity, ref CameraAcceptanceFixtureTag tag, ref WorldPositionCm position) =>
            {
                int stringId = ResolveLabel(strings, entity.Id);
                if (stringId == 0)
                {
                    return;
                }

                worldHud.TryAdd(new WorldHudItem
                {
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = WorldUnits.WorldCmToVisualMeters(position.ToWorldCmInt2(), yMeters: 1.15f),
                    Color0 = new Vector4(0.97f, 0.98f, 1f, 1f),
                    Id0 = stringId,
                    FontSize = 14,
                });
            });
        }

        private int ResolveLabel(WorldHudStringTable strings, int entityId)
        {
            if (_labelIds.TryGetValue(entityId, out var cached))
            {
                return cached;
            }

            int registered = strings.Register(entityId.ToString(CultureInfo.InvariantCulture));
            if (registered != 0)
            {
                _labelIds[entityId] = registered;
            }

            return registered;
        }
    }
}
