using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Gameplay.Tasks;
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

                return new SeedCollectionBagsSystem(engine);
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
    private static readonly string[] HeroAbilityNames = { "火球术", "闪现", "守护姿态" };
    private static readonly string[] ApprenticeAbilityNames = { "火球术" };
    private static readonly string[] TagNames = { "勇气印记", "洞察印记", "守望印记" };

    private readonly GameEngine _engine;
    private readonly World _world;
    private bool _applied;

    public SeedCollectionBagsSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
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

        Entity hero = FindByName("名册守望者");
        Entity apprentice = FindByName("名册学徒");
        if (hero == Entity.Null || apprentice == Entity.Null)
        {
            return;
        }

        SeedAbilities(hero, HeroAbilityNames);
        SeedAbilities(apprentice, ApprenticeAbilityNames);
        SeedTags(hero);
        SeedInventory(hero);
        SeedTasksAndActivities(hero);
        SeedProgression(hero);
        _applied = true;
    }

    private Entity FindByName(string expected)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (string.Equals(name.Value, expected, StringComparison.Ordinal))
            {
                found = entity;
            }
        });
        return found;
    }

    private void SeedAbilities(Entity owner, string[] abilityNames)
    {
        if (!_world.Has<AbilityStateBuffer>(owner))
        {
            _world.Add(owner, default(AbilityStateBuffer));
        }

        ref AbilityStateBuffer abilities = ref _world.Get<AbilityStateBuffer>(owner);
        for (int i = 0; i < abilityNames.Length; i++)
        {
            int abilityId = AbilityIdRegistry.GetId(abilityNames[i]);
            if (abilityId == AbilityIdRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Ability '{abilityNames[i]}' is not registered for PanelCollectionBagsShowcaseMod.");
            }

            abilities.AddAbility(abilityId);
        }
    }

    private void SeedTags(Entity hero)
    {
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
    }

    private void SeedInventory(Entity hero)
    {
        ItemDefinitionRegistry definitions = _engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("PanelCollectionBagsShowcaseMod requires ItemDefinitionRegistry.");
        OwnershipResolver ownership = _engine.GetService(CoreServiceKeys.OwnershipResolver)
            ?? throw new InvalidOperationException("PanelCollectionBagsShowcaseMod requires OwnershipResolver.");
        InventoryRuntimeService inventory = _engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("PanelCollectionBagsShowcaseMod requires InventoryRuntimeService.");

        const string potionKey = "Item.CollectionBags.Potion";
        int definitionId = definitions.GetId(potionKey);
        if (definitionId <= 0)
        {
            definitionId = definitions.Register(potionKey, new ItemDefinition
            {
                Id = potionKey,
                DisplayName = "试炼药剂",
                MaxStack = 20
            });
        }

        const string rationKey = "Item.CollectionBags.Ration";
        if (definitions.GetId(rationKey) <= 0)
        {
            definitions.Register(rationKey, new ItemDefinition
            {
                Id = rationKey,
                DisplayName = "干粮",
                MaxStack = 10
            });
        }

        Entity container = _world.Create(new ItemContainerCm
        {
            LayoutId = 0,
            Purpose = ItemContainerPurpose.Backpack
        });
        ownership.EnsureOwnership(hero, container);
        for (int i = 0; i < 3; i++)
        {
            Entity item = _world.Create(
                new ItemInstanceCm { DefinitionId = definitionId, StackCount = 1 },
                new ItemLocationCm { Container = container });
            ownership.EnsureOwnership(container, item);
        }

        Span<Entity> seeded = stackalloc Entity[4];
        if (inventory.CollectOwnedItemInstances(hero, seeded) < 3)
        {
            throw new InvalidOperationException(
                "PanelCollectionBagsShowcaseMod could not seed owned inventory item instances.");
        }
    }

    private void SeedTasksAndActivities(Entity hero)
    {
        _world.Create(
            new Name { Value = "巡夜差事" },
            new TaskInstanceCm
            {
                DefinitionId = 1,
                InstanceId = 1,
                State = TaskInstanceState.Active,
                ScopeHost = hero,
                Revision = 1
            });
        _world.Create(
            new Name { Value = "名册集会" },
            new ActivityInstanceCm
            {
                DefinitionId = 1,
                InstanceId = 1,
                State = ActivityInstanceState.Active,
                ScopeHost = hero,
                Revision = 1
            });
    }

    private void SeedProgression(Entity hero)
    {
        int progressionId = ProgressionIdRegistry.GetId("名册修行");
        if (progressionId <= 0)
        {
            throw new InvalidOperationException(
                "Progression '名册修行' is not registered for PanelCollectionBagsShowcaseMod.");
        }

        if (!_world.Has<ProgressionStateBuffer>(hero))
        {
            _world.Add(hero, new ProgressionStateBuffer());
        }

        ref ProgressionStateBuffer state = ref _world.Get<ProgressionStateBuffer>(hero);
        if (state.Count == 0 && !state.TrySetLevel(progressionId, 1))
        {
            throw new InvalidOperationException(
                "PanelCollectionBagsShowcaseMod could not seed ProgressionStateBuffer.");
        }
    }
}
