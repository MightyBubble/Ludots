using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace CncTriNationFullGameMod.Triggers;

internal sealed class CncTriNationMapLoadedTrigger
{
    private readonly IModContext _ctx;

    public CncTriNationMapLoadedTrigger(IModContext ctx) => _ctx = ctx;

    public Task ExecuteAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine?.CurrentMapSession?.MapConfig?.Id != "cnc_tri_nation_war")
        {
            return Task.CompletedTask;
        }

        World world = engine.World;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name _) =>
        {
            if (!world.Has<GameplayTagContainer>(entity))
            {
                world.Add(entity, new GameplayTagContainer());
            }

            if (!world.Has<TagCountContainer>(entity))
            {
                world.Add(entity, new TagCountContainer());
            }

            if (!world.Has<TimedTagBuffer>(entity))
            {
                world.Add(entity, new TimedTagBuffer());
            }

        });

        EnsureDefaultSelection(engine, world);
        _ctx.Log("[CncTriNationFullGameMod] Map loaded and RTS entities prepared.");
        return Task.CompletedTask;
    }

    private static void EnsureDefaultSelection(GameEngine engine, World world)
    {
        Entity conyard = Entity.Null;
        var query = new QueryDescription().WithAll<Name, PlayerOwner>();
        world.Query(in query, (Entity entity, ref Name name, ref PlayerOwner owner) =>
        {
            if (conyard != Entity.Null || owner.PlayerId != 1)
            {
                return;
            }

            if (name.Value.Contains("Allied Construction Yard", StringComparison.Ordinal))
            {
                conyard = entity;
            }
        });

        if (conyard == Entity.Null)
        {
            return;
        }

        EntityCollectionStore? collections = engine.GetService(CoreServiceKeys.EntityCollectionStore);
        Entity ownerEntity = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (collections == null || !world.IsAlive(ownerEntity))
        {
            return;
        }

        Span<Entity> next = stackalloc Entity[1];
        next[0] = conyard;
        var descriptor = EntityCollectionDescriptor.Create(
            EntityCollectionKeys.CommandSource,
            EntityCollectionSourceKind.Explicit,
            EntityCollectionRoleKind.CommandSource,
            ownerEntity,
            conyard,
            "C&C command source",
            "Default construction yard selection.");
        collections.Replace(ownerEntity, descriptor, next, ownerEntity);
    }
}
