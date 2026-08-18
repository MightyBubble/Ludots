using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FireballSharedMod;

public sealed class FireballSharedModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[FireballSharedMod] Loaded");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            var input = engine.GlobalContext.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) && inputObj is PlayerInputHandler handler
                ? handler
                : null;
            if (input == null)
            {
                return Task.CompletedTask;
            }

            engine.RegisterPresentationSystem(new FireballCastSystem(engine, input));
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

/// <summary>
/// Press Q → auto-target nearest hostile → fireball (direct attribute damage).
/// Panel variables with realtime:true pick up the change next frame.
/// </summary>
public sealed class FireballCastSystem : Arch.System.ISystem<float>
{
    private const float FireballDamage = 15f;
    private const float ManaCost = 10f;

    private readonly GameEngine _engine;
    private readonly PlayerInputHandler _input;
    private int _healthId = -1;
    private int _manaId = -1;
    public int CastCount { get; private set; }

    public FireballCastSystem(GameEngine engine, PlayerInputHandler input)
    {
        _engine = engine;
        _input = input;
    }

    public void Initialize() { }

    public void Update(in float dt)
    {
        if (_healthId < 0)
        {
            _healthId = AttributeRegistry.GetId("Health");
            _manaId = AttributeRegistry.GetId("Mana");
        }

        if (!_input.PressedThisFrame("CastFireball"))
        {
            return;
        }

        Entity hero = Entity.Null;
        Entity target = Entity.Null;
        float bestDist = float.MaxValue;
        var query = new QueryDescription().WithAll<AttributeBuffer, Team, WorldPositionCm>();
        _engine.World.Query(in query, (Entity e, ref Team team, ref WorldPositionCm pos) =>
        {
            float px = pos.Value.X.ToFloat();
            float py = pos.Value.Y.ToFloat();
            if (team.Id == 1 && hero == Entity.Null)
            {
                hero = e;
            }
            else if (team.Id == 2)
            {
                float dist = px * px + py * py;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    target = e;
                }
            }
        });

        if (hero == Entity.Null || target == Entity.Null)
        {
            return;
        }

        if (_engine.World.TryGet(hero, out AttributeBuffer heroBuffer) && _engine.World.TryGet(target, out AttributeBuffer targetBuffer))
        {
            float mana = heroBuffer.GetCurrent(_manaId);
            if (mana >= ManaCost)
            {
                heroBuffer.SetCurrent(_manaId, mana - ManaCost);
                float hp = targetBuffer.GetCurrent(_healthId);
                targetBuffer.SetCurrent(_healthId, hp - FireballDamage);
                CastCount++;
            }
        }
    }

    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
