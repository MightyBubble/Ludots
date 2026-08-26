using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace PanelAuthorLayoutKitShowcaseMod;

/// <summary>
/// Seeds stacked + timed effect instances so list/grid/column chips show real remaining time and layers.
/// </summary>
public sealed class PanelAuthorLayoutKitShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelAuthorLayoutKitShowcaseMod] Loaded - author layout classroom");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                throw new InvalidOperationException(
                    "PanelAuthorLayoutKitShowcaseMod requires GameEngine on GameStart.");
            }

            engine.RegisterSystem(
                new SeedAuthorLayoutEffectsSystem(engine.World),
                SystemGroup.SchemaUpdate);

            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

internal sealed class SeedAuthorLayoutEffectsSystem : Arch.System.ISystem<float>
{
    private static readonly (string Name, int Remaining, int Total, int Stacks)[] Seeds =
    {
        ("祝福", 80, 100, 3),
        ("迅捷", 45, 60, 1),
        ("护盾", 20, 40, 2),
        ("洞察", 55, 70, 1),
    };

    private readonly World _world;
    private bool _applied;

    public SeedAuthorLayoutEffectsSystem(World world)
    {
        _world = world;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (_applied)
        {
            return;
        }

        Entity hero = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (string.Equals(name.Value, "试炼者", StringComparison.Ordinal))
            {
                hero = entity;
            }
        });

        if (hero == Entity.Null || !_world.IsAlive(hero))
        {
            return;
        }

        if (!_world.Has<ActiveEffectContainer>(hero))
        {
            _world.Add(hero, new ActiveEffectContainer());
        }

        ref ActiveEffectContainer container = ref _world.Get<ActiveEffectContainer>(hero);
        for (int i = 0; i < Seeds.Length; i++)
        {
            (string templateName, int remaining, int total, int stacks) = Seeds[i];
            int templateId = EffectTemplateIdRegistry.GetId(templateName);
            if (templateId == EffectTemplateIdRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Effect template '{templateName}' was not registered before freeze for PanelAuthorLayoutKitShowcaseMod.");
            }

            Entity effect = GameplayEffectFactory.CreateEffect(
                _world,
                rootId: i + 1,
                source: hero,
                target: hero,
                durationTicks: total,
                lifetimeKind: EffectLifetimeKind.Infinite);
            ref GameplayEffect gameplay = ref _world.Get<GameplayEffect>(effect);
            gameplay.State = EffectState.Committed;
            gameplay.TotalTicks = total;
            gameplay.RemainingTicks = remaining;
            _world.Add(effect, new EffectTemplateRef { TemplateId = templateId });
            _world.Add(effect, new EffectStack
            {
                Count = stacks,
                Limit = Math.Max(stacks, 5),
                Policy = StackPolicy.RefreshDuration,
                OverflowPolicy = StackOverflowPolicy.RejectNew,
            });
            if (!container.Add(effect))
            {
                throw new InvalidOperationException(
                    $"ActiveEffectContainer full while seeding '{templateName}' for PanelAuthorLayoutKitShowcaseMod.");
            }
        }

        _applied = true;
    }
}
