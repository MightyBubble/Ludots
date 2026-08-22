using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace NightRaidShowcaseMod;

/// <summary>
/// Transitional kill-tool residue. Teleport and the hero ring are already pure
/// data (InputActionFired bridge + SetWorldPosition graph op; presenter GroundOverlay
/// slot). What remains here is the right-click execution tool and the raid-circle
/// ground ring, both scheduled for removal by their engine slices:
/// kill via the order→ability→effect damage pipeline, the circle via a
/// region-overlay presenter. No level-flow logic lives in this file.
/// </summary>
internal sealed class NightRaidShowcaseInteractionSystem : ISystem<float>
{
    private const string MapId = "night_raid";
    private const string RightClickAction = "Command";
    private const int KillCooldownTicks = 12;
    private const int KillPickRadiusCm = 400;

    private readonly GameEngine _engine;
    private readonly int _healthId;
    private int _killCooldown;

    public NightRaidShowcaseInteractionSystem(GameEngine engine)
    {
        _engine = engine;
        _healthId = AttributeRegistry.GetId("Health");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, MapId, StringComparison.OrdinalIgnoreCase) ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        _killCooldown = Math.Max(0, _killCooldown - 1);

        if (input.PressedThisFrame(RightClickAction) && _killCooldown == 0 &&
            AuthoritativeGroundPointerHelper.TryRead(input, out WorldCmInt2 ground))
        {
            _killCooldown = KillCooldownTicks;
            ExecuteNearest(new Vector2(ground.X, ground.Y));
        }

        PublishRaidCircle();
    }

    private void ExecuteNearest(Vector2 origin)
    {
        Entity nearest = Entity.Null;
        float best = KillPickRadiusCm * KillPickRadiusCm;
        var query = new QueryDescription().WithAll<WorldPositionCm, AttributeBuffer>();
        _engine.World.Query(in query, (Entity entity, ref WorldPositionCm position) =>
        {
            if (!_engine.World.IsAlive(entity))
            {
                return;
            }

            float dx = position.Value.X.ToFloat() - origin.X;
            float dy = position.Value.Y.ToFloat() - origin.Y;
            float dist = (dx * dx) + (dy * dy);
            if (dist <= best)
            {
                best = dist;
                nearest = entity;
            }
        });

        if (nearest == Entity.Null || !_engine.World.IsAlive(nearest))
        {
            return;
        }

        ref AttributeBuffer attributes = ref _engine.World.Get<AttributeBuffer>(nearest);
        attributes.SetCurrent(_healthId, 0f);
        FireKillToolUsed(nearest);
    }

    private void FireKillToolUsed(Arch.Core.Entity target)
    {
        var registry = _engine.GetService(CoreServiceKeys.CustomEventNameRegistry);
        if (registry == null || _engine.CurrentMapSession == null)
        {
            return;
        }

        var context = _engine.CreateContext();
        context.Set(CoreServiceKeys.MapId, _engine.CurrentMapSession.MapId);
        context.Set(CoreServiceKeys.MapSession, _engine.CurrentMapSession);
        context.Set(MapTriggerEventPayloadKeys.SourceEntity, target);
        _engine.TriggerManager.FireMapCustomEvent(
            _engine.CurrentMapSession.MapId,
            "NightRaid.KillTool.Used",
            context,
            registry);
    }

    /// <summary>
    /// Raid-circle ground ring. Transitional: moves to a region-overlay presenter
    /// slice; the radius mirrors map region raid_circle radiusCm=250 (meters here).
    /// </summary>
    private void PublishRaidCircle()
    {
        if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not GroundOverlayBuffer ground)
        {
            return;
        }

        ground.Upsert(new GroundOverlayItem
        {
            StableId = 910_002,
            Shape = GroundOverlayShape.Ring,
            Center = new Vector3(0f, 0.02f, 0f),
            Radius = 2.5f,
            InnerRadius = 2.28f,
            FillColor = new Vector4(1f, 0.85f, 0.35f, 0.10f),
            BorderColor = new Vector4(1f, 0.85f, 0.35f, 0.95f),
            BorderWidth = 0.10f,
        });
    }
}
