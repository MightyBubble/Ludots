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
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace NightRaidShowcaseMod;

/// <summary>
/// Hard-coded interaction layer, deliberately dumb: left click teleports the hero to the
/// clicked ground point, right click zeroes Health on the nearest killable entity.
/// Every rule that fires afterwards (stage/tick/yield/panel) lives in the map's
/// TriggerGraph data — this system never touches map variables.
/// </summary>
internal sealed class NightRaidShowcaseInteractionSystem : ISystem<float>
{
    private const string MapId = "night_raid";
    private const string LeftClickAction = "CommandSourceAcquire";
    private const string RightClickAction = "Command";
    private const string GroundPointerAction = "__runtime.PointerGroundWorldCm";
    private const int ActionCooldownTicks = 12;

    private readonly GameEngine _engine;
    private readonly int _healthId;
    private int _leftCooldown;
    private int _rightCooldown;

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

        _leftCooldown = Math.Max(0, _leftCooldown - 1);
        _rightCooldown = Math.Max(0, _rightCooldown - 1);

        Vector3 ground = input.ReadAction<Vector3>(GroundPointerAction);
        bool hasGround = ground != Vector3.Zero;

        if (hasGround && input.IsDown(LeftClickAction) && _leftCooldown == 0)
        {
            _leftCooldown = ActionCooldownTicks;
            Entity hero = FindHero();
            if (hero != Entity.Null && _engine.World.IsAlive(hero))
            {
                _engine.World.Set(hero, new WorldPositionCm
                {
                    Value = Fix64Vec2.FromInt((int)ground.X, (int)ground.Z),
                });
            }
        }

        if (hasGround && input.IsDown(RightClickAction) && _rightCooldown == 0)
        {
            _rightCooldown = ActionCooldownTicks;
            Entity target = FindNearest(new Vector2(ground.X, ground.Z), maxDistanceCm: 400f);
            if (target != Entity.Null && _engine.World.IsAlive(target) && _engine.World.Has<AttributeBuffer>(target))
            {
                ref AttributeBuffer attributes = ref _engine.World.Get<AttributeBuffer>(target);
                attributes.SetCurrent(_healthId, 0f);
                FireKillToolUsed(target);
            }
        }

        PublishReadabilityOverlays();
    }

    /// <summary>
    /// Observation-only anchors: the gold raid circle and the cyan hero ring. Same
    /// contract as the old presentation layer — pure reads, no game state writes.
    /// </summary>
    private void PublishReadabilityOverlays()
    {
        if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not Ludots.Platform.Abstractions.GroundOverlayBuffer ground)
        {
            return;
        }

        ground.Upsert(new Ludots.Platform.Abstractions.GroundOverlayItem
        {
            StableId = 910_002,
            Shape = Ludots.Platform.Abstractions.GroundOverlayShape.Ring,
            Center = new Vector3(0f, 0.02f, 0f),
            Radius = 2.5f,
            InnerRadius = 2.28f,
            FillColor = new Vector4(1f, 0.85f, 0.35f, 0.10f),
            BorderColor = new Vector4(1f, 0.85f, 0.35f, 0.95f),
            BorderWidth = 0.10f,
        });

        Entity hero = FindHero();
        if (hero != Entity.Null && _engine.World.IsAlive(hero) &&
            _engine.World.TryGet(hero, out WorldPositionCm heroPos))
        {
            ground.Upsert(new Ludots.Platform.Abstractions.GroundOverlayItem
            {
                StableId = 910_001,
                Shape = Ludots.Platform.Abstractions.GroundOverlayShape.Ring,
                Center = new Vector3(heroPos.Value.X.ToFloat() * 0.01f, 0.03f, heroPos.Value.Y.ToFloat() * 0.01f),
                Radius = 1.5f,
                InnerRadius = 1.25f,
                FillColor = new Vector4(0.2f, 0.95f, 0.95f, 0.12f),
                BorderColor = new Vector4(0.2f, 0.95f, 0.95f, 0.95f),
                BorderWidth = 0.09f,
            });
        }
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

    private Entity FindHero()
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity entity, ref Name name) =>
        {
            if (found == Entity.Null && string.Equals(name.Value, "NightRaidHero", StringComparison.Ordinal))
            {
                found = entity;
            }
        });
        return found;
    }

    private Entity FindNearest(Vector2 origin, float maxDistanceCm)
    {
        Entity nearest = Entity.Null;
        float best = maxDistanceCm * maxDistanceCm;
        var query = new QueryDescription().WithAll<WorldPositionCm, Name, AttributeBuffer>();
        _engine.World.Query(in query, (Entity entity, ref WorldPositionCm position) =>
        {
            if (!_engine.World.IsAlive(entity))
            {
                return;
            }

            if (_engine.World.TryGet(entity, out Name name) &&
                string.Equals(name.Value, "NightRaidHero", StringComparison.Ordinal))
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
        return nearest;
    }
}
