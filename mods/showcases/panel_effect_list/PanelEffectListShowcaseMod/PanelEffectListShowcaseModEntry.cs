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

namespace PanelEffectListShowcaseMod;

/// <summary>
/// Seeds durable display buffs on the showcase hero so the effect strip has
/// real ActiveEffectContainer members without going through combat casting.
/// </summary>
public sealed class PanelEffectListShowcaseModEntry : IMod
{
    private const string SeedSystemFactory = "PanelEffectList.SeedEffects";

    public void OnLoad(IModContext context)
    {
        context.Log("[PanelEffectListShowcaseMod] Loaded - active effect list panel showcase");
        context.SystemFactoryRegistry.Register(
            SeedSystemFactory,
            SystemGroup.SchemaUpdate,
            scriptContext =>
            {
                if (!scriptContext.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    throw new InvalidOperationException(
                        "PanelEffectListShowcaseMod requires GameEngine when creating its seed system.");
                }

                return new SeedActiveEffectsSystem(engine.World);
            });

        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (!ctx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
            {
                throw new InvalidOperationException("PanelEffectListShowcaseMod requires GameEngine on GameStart.");
            }

            context.SystemFactoryRegistry.TryActivate(SeedSystemFactory, ctx, engine);
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

internal sealed class SeedActiveEffectsSystem : Arch.System.ISystem<float>
{
    private static readonly (string Name, int Remaining, int Total)[] Seeds =
    {
        ("祝福", 80, 100),
        ("迅捷", 45, 60),
        ("护盾", 20, 40),
    };

    private readonly World _world;
    private bool _applied;

    public SeedActiveEffectsSystem(World world)
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
            (string templateName, int remaining, int total) = Seeds[i];
            int templateId = EffectTemplateIdRegistry.GetId(templateName);
            if (templateId == EffectTemplateIdRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Effect template '{templateName}' was not registered before freeze for PanelEffectListShowcaseMod.");
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
            if (!container.Add(effect))
            {
                throw new InvalidOperationException(
                    $"ActiveEffectContainer full while seeding '{templateName}' for PanelEffectListShowcaseMod.");
            }
        }

        _applied = true;
    }
}
