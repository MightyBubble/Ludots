using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace NightRaidShowcaseMod;

internal sealed class NightRaidShowcaseInteractionSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly int _healthAttributeId;
    private int _actCooldownTicks;

    public NightRaidShowcaseInteractionSystem(GameEngine engine)
    {
        _engine = engine;
        _healthAttributeId = AttributeRegistry.GetId("Health");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, NightRaidShowcaseWorld.MapId, StringComparison.OrdinalIgnoreCase) ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        IntegrateWasdMovement(input, dt);

        // Act uses hold semantics with a tick throttle: authoritative-input edge events
        // (PressedThisFrame) are dropped by the fixed-step accumulator on back-to-back
        // injections, so rapid keypresses and test drivers both stay reliable via IsDown.
        _actCooldownTicks = Math.Max(0, _actCooldownTicks - 1);
        if (!input.IsDown(NightRaidShowcaseWorld.ActActionId) || _actCooldownTicks > 0)
        {
            return;
        }

        _actCooldownTicks = NightRaidShowcaseWorld.ActThrottleTicks;

        int wave = _engine.CurrentMapSession?.Variables?.ReadInt("wave") ?? 0;
        if (wave <= 0)
        {
            EnterRaidCircle();
            return;
        }

        StrikeNearest();
    }

    /// <summary>
    /// WASD hero movement (repo-standard Axis2D composite). Walking into the raid circle
    /// lets RegionEntered fire from real position changes; SPACE stays as the act fallback.
    /// </summary>
    private void IntegrateWasdMovement(IInputActionReader input, float dt)
    {
        System.Numerics.Vector2 axis = input.ReadAction<System.Numerics.Vector2>(NightRaidShowcaseWorld.MoveActionId);
        if (axis.LengthSquared() < 0.000001f)
        {
            return;
        }

        Entity hero = NightRaidShowcaseWorld.FindHero(_engine.World);
        if (hero == Entity.Null || !_engine.World.IsAlive(hero) || !_engine.World.TryGet(hero, out WorldPositionCm position))
        {
            return;
        }

        float stepCm = NightRaidShowcaseWorld.MoveSpeedCmPerSecond * MathF.Max(0f, dt);
        int dx = (int)MathF.Round(axis.X * stepCm);
        int dy = (int)MathF.Round(axis.Y * stepCm);
        if (dx == 0 && dy == 0)
        {
            return;
        }

        _engine.World.Set(hero, new WorldPositionCm { Value = position.Value + Fix64Vec2.FromInt(dx, dy) });
    }

    private void EnterRaidCircle()
    {
        Entity hero = NightRaidShowcaseWorld.FindHero(_engine.World);
        if (hero == Entity.Null || !_engine.World.IsAlive(hero))
        {
            throw new InvalidOperationException("Night Raid Act has no live NightRaidHero to send into the raid circle.");
        }

        _engine.World.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(0, 0) });
    }

    private void StrikeNearest()
    {
        Entity hero = NightRaidShowcaseWorld.FindHero(_engine.World);
        Entity target = NightRaidShowcaseWorld.FindNearestHostile(_engine.World, hero);
        if (target == Entity.Null)
        {
            return;
        }

        if (_healthAttributeId < 0 || !_engine.World.Has<AttributeBuffer>(target))
        {
            throw new InvalidOperationException("Night Raid strike target is missing a Health attribute buffer.");
        }

        ref AttributeBuffer attributes = ref _engine.World.Get<AttributeBuffer>(target);
        float nextHealth = MathF.Max(0f, attributes.GetCurrent(_healthAttributeId) - NightRaidShowcaseWorld.StrikeDamage);
        attributes.SetCurrent(_healthAttributeId, nextHealth);
        if (nextHealth > 0f)
        {
            return;
        }

        if (!_engine.World.Has<PresentationStableId>(target))
        {
            throw new InvalidOperationException("Night Raid strike target is missing PresentationStableId.");
        }

        PresentationEntityLifecycle.RequestDestroy(_engine.World, target, "Night Raid showcase strike");
    }
}
