using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Knowledge;
using Ludots.Core.Scripting;

namespace RtsRedAlertLikeShowcaseMod.Systems;

internal sealed class RtsRedAlertKnowledgeProjectionSystem : ISystem<float>
{
    private static readonly QueryDescription SelectableMapEntityQuery = new QueryDescription()
        .WithAll<MapEntity, SelectionSelectableTag>();

    private readonly GameEngine _engine;
    private readonly KnowledgeProjectionStore _knowledge;

    public RtsRedAlertKnowledgeProjectionSystem(GameEngine engine)
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
        if (!IsRedAlertMapActive() ||
            !_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? viewerObj) ||
            viewerObj is not Entity viewer ||
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

        _engine.World.Query(in SelectableMapEntityQuery, (Entity entity, ref MapEntity mapEntity, ref SelectionSelectableTag _) =>
        {
            if (mapEntity.MapId != currentMapId ||
                !SelectionEligibility.IsSelectableNow(_engine.World, entity))
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

    private bool IsRedAlertMapActive()
    {
        var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
        if (tags == null)
        {
            return false;
        }

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], "red_alert_like", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tags[i], "cnc", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
