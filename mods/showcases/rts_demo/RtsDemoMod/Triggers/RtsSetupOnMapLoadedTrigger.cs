using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Modding;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using RtsDemoMod.Runtime;

namespace RtsDemoMod.Triggers
{
    /// <summary>
    /// Ensures RTS entities have the required GAS components (tags, timed tags)
    /// after the "rts" map is loaded.
    /// </summary>
    public sealed class RtsSetupOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public RtsSetupOnMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, "rts") && !HasTag(mapTags, "rts_showcase")) return Task.CompletedTask;

            var world = engine.World;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                // Ensure all named entities have tag components for GAS interaction
                TagStateInstaller.EnsureInstalled(world, e);
                if (world.Has<CommandSourceSelectableTag>(e) && !world.Has<CommandSourceSelectableState>(e))
                {
                    world.Add(e, CommandSourceSelectableState.EnabledByDefault);
                }
            });

            bool isAuthoritativeServer =
                engine.GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.AuthoritativeServer;
            if (!isAuthoritativeServer)
            {
                RtsPresentationBootstrapper.EnsureReadableActors(engine, world);
                EnsureLocalCommandSourceOwner(engine, world);
            }

            RequirePlayerOwnership(world);
            if (isAuthoritativeServer)
            {
                return Task.CompletedTask;
            }

            RtsShowcaseCommandSourceHelper.EnsureCommandSourceBinding(engine);
            SeedInitialCommandSourceSelection(engine, world);
            return Task.CompletedTask;
        }

        private static void SeedInitialCommandSourceSelection(GameEngine engine, World world)
        {
            // Training and entry maps expect the local player's map-declared representative
            // (producer or worker) selected on load so the first-contact command panel is
            // coherent. The retired name-based seeding chain is replaced by this write through
            // the collection single write point.
            var applier = engine.GetService(CoreServiceKeys.CollectionApplier);
            var store = engine.GetService(CoreServiceKeys.EntityCollectionStore);
            var players = engine.GetService(CoreServiceKeys.PlayerEntityLookup);
            var seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry);
            if (applier == null || store == null || players == null || seats == null ||
                !seats.TryGetSolePossessedRep(out Entity owner))
            {
                return;
            }

            if (!world.TryGet(owner, out PlayerIdentity identity) ||
                identity.PlayerId <= 0 ||
                !players.TryGet(identity.PlayerId, out Entity representative) ||
                !world.IsAlive(representative))
            {
                return;
            }

            int keyId = store.KeyRegistry.GetId("collection.command.source");
            Span<Entity> members = stackalloc Entity[1];
            members[0] = representative;
            applier.Apply(owner, keyId, Ludots.Core.EntityCollections.CollectionWriteOp.Replace, members);
        }

        private static void RequirePlayerOwnership(World world)
        {
            var query = new QueryDescription().WithAll<Team>();
            world.Query(in query, (Entity entity, ref Team team) =>
            {
                if (!world.TryGet(entity, out PlayerOwner owner) ||
                    owner.PlayerId != team.Id)
                {
                    throw new InvalidOperationException(
                        $"RTS showcase entity {entity} has Team {team.Id} but no matching PlayerOwner. Author ownership in the entity template or map data.");
                }
            });
        }

        // TODO(#711-merge): main removed the name-based default command source seeding
        // (EnsureDefaultCommandSource / FindPreferredTarget chain) — the PR's seeding path was not
        // resurrected under the seat model; initial selection comes from the quick-select toolbar.
        private static void EnsureLocalCommandSourceOwner(GameEngine engine, World world)
        {
            Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!world.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    "RTS showcase requires a live sole ClientLocalSeat possession from launchContext.localSeats / startupLocalSeats.");
            }
        }

        private static bool HasTag(List<string> tags, string t)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], t, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

    }
}
