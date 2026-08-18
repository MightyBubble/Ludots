using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Knowledge;
using Ludots.Core.Client;
using Ludots.Core.Scripting;

namespace CityRallyShowcaseMod.Systems
{
    /// <summary>
    /// 引擎选择门控要求实体在 Knowledge 投影中 LiveVisible 才能被点击选中。
    /// 与 RtsRedAlertLikeShowcaseMod 的投影系统同构：把当前地图上可选的己方实体投影给本地观察者。
    /// </summary>
    internal sealed class CityRallyKnowledgeProjectionSystem : ISystem<float>
    {
        private static readonly QueryDescription SelectableMapEntityQuery = new QueryDescription()
            .WithAll<MapEntity, CommandSourceSelectableTag>();

        private readonly GameEngine _engine;
        private readonly KnowledgeProjectionStore _knowledge;

        public CityRallyKnowledgeProjectionSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new InvalidOperationException("KnowledgeProjectionStore service is missing.");
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!IsCityRallyMapActive() ||
                !ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out var viewer) ||
                !_engine.World.IsAlive(viewer))
            {
                return;
            }

            var session = _engine.CurrentMapSession;
            if (session == null)
            {
                return;
            }

            int tick = KnowledgeProjectionConsumer.ResolveCurrentTick(_engine.GlobalContext);
            KnowledgeIdMask256 empty = KnowledgeIdMask256.Empty;
            var currentMapId = session.MapId;

            _engine.World.Query(in SelectableMapEntityQuery, (Entity entity, ref MapEntity mapEntity, ref CommandSourceSelectableTag _) =>
            {
                if (mapEntity.MapId != currentMapId ||
                    !CommandSourceEligibility.IsSelectableNow(_engine.World, entity))
                {
                    return;
                }

                _knowledge.Upsert(
                    viewer,
                    entity,
                    new KnowledgeDisclosureRecord(
                        KnowledgePresence.LiveVisible,
                        KnowledgePositionAccess.Live,
                        empty,
                        empty,
                        empty,
                        viewer,
                        observedTick: tick,
                        expiryTick: 0,
                        confidencePermille: 1000,
                        revision: 0));
            });
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool IsCityRallyMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "city_rally", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tags[i], "rts", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
