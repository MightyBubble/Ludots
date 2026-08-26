using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace PanelEntityListShowcaseMod;

/// <summary>
/// Seeds the stunned guard with Status.Stunned after map load so the roster
/// badge path is exercised without a combat ability (spawn templates keep
/// GameplayTagContainer empty by contract).
/// </summary>
public sealed class PanelEntityListShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelEntityListShowcaseMod] Loaded - entity roster panel showcase");
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine)
            {
                throw new InvalidOperationException("PanelEntityListShowcaseMod requires GameEngine on GameStart.");
            }

            engine.RegisterSystem(
                new ApplyRosterSeedTagsSystem(engine.World),
                SystemGroup.Continuation);

            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

internal sealed class ApplyRosterSeedTagsSystem : Arch.System.ISystem<float>
{
    private readonly World _world;
    private bool _applied;

    public ApplyRosterSeedTagsSystem(World world)
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

        int tagId = TagRegistry.GetId("Status.Stunned");
        if (tagId == TagRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                "Status.Stunned is not registered; PanelEntityListShowcaseMod requires GAS/tag_rules.json.");
        }

        var query = new QueryDescription().WithAll<Name, GameplayTagContainer>();
        bool found = false;
        _world.Query(in query, (Entity entity, ref Name name, ref GameplayTagContainer tags) =>
        {
            if (!string.Equals(name.Value, "晕眩卫士", StringComparison.Ordinal))
            {
                return;
            }

            tags.AddTag(tagId);
            found = true;
        });

        if (!found)
        {
            return;
        }

        _applied = true;
    }
}
