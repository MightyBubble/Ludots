using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace PanelCollectionBagsShowcaseMod;

public sealed class PanelCollectionBagsShowcaseModEntry : IMod
{
    private const string SeedSystemFactory = "PanelCollectionBags.Seed";

    public void OnLoad(IModContext context)
    {
        context.Log("[PanelCollectionBagsShowcaseMod] Loaded - typed collection bags showcase");
        context.SystemFactoryRegistry.Register(
            SeedSystemFactory,
            SystemGroup.SchemaUpdate,
            scriptContext =>
            {
                if (!scriptContext.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    throw new InvalidOperationException(
                        "PanelCollectionBagsShowcaseMod requires GameEngine when creating its seed system.");
                }

                return new SeedCollectionBagsSystem(engine.World);
            });

        context.OnEvent(GameEvents.GameStart, scriptContext =>
        {
            if (!scriptContext.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
            {
                throw new InvalidOperationException(
                    "PanelCollectionBagsShowcaseMod requires GameEngine on GameStart.");
            }

            context.SystemFactoryRegistry.TryActivate(SeedSystemFactory, scriptContext, engine);
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

internal sealed class SeedCollectionBagsSystem : Arch.System.ISystem<float>
{
    private static readonly string[] AbilityNames = { "火球术", "闪现", "守护姿态" };
    private static readonly string[] TagNames = { "勇气印记", "洞察印记", "守望印记" };

    private readonly World _world;
    private bool _applied;

    public SeedCollectionBagsSystem(World world)
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
            if (string.Equals(name.Value, "名册守望者", StringComparison.Ordinal))
            {
                hero = entity;
            }
        });

        if (hero == Entity.Null || !_world.IsAlive(hero))
        {
            return;
        }

        if (!_world.Has<AbilityStateBuffer>(hero))
        {
            _world.Add(hero, default(AbilityStateBuffer));
        }

        ref AbilityStateBuffer abilities = ref _world.Get<AbilityStateBuffer>(hero);
        for (int i = 0; i < AbilityNames.Length; i++)
        {
            int abilityId = AbilityIdRegistry.GetId(AbilityNames[i]);
            if (abilityId == AbilityIdRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Ability '{AbilityNames[i]}' is not registered for PanelCollectionBagsShowcaseMod.");
            }

            abilities.AddAbility(abilityId);
        }

        if (!_world.Has<GameplayTagContainer>(hero))
        {
            _world.Add(hero, default(GameplayTagContainer));
        }

        if (!_world.Has<TagCountContainer>(hero))
        {
            _world.Add(hero, default(TagCountContainer));
        }

        ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(hero);
        ref TagCountContainer tagCounts = ref _world.Get<TagCountContainer>(hero);
        for (int i = 0; i < TagNames.Length; i++)
        {
            int tagId = TagRegistry.GetId(TagNames[i]);
            if (tagId == TagRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Tag '{TagNames[i]}' is not registered for PanelCollectionBagsShowcaseMod.");
            }

            tags.AddTag(tagId);
            if (!tagCounts.AddCount(tagId))
            {
                throw new InvalidOperationException(
                    $"TagCountContainer is full while seeding '{TagNames[i]}'.");
            }
        }

        _applied = true;
    }
}
