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

            RtsPresentationBootstrapper.EnsureReadableActors(engine, world);
            EnsureLocalCommandSourceOwner(engine, world);
            RequirePlayerOwnership(world);
            RtsShowcaseCommandSourceHelper.EnsureCommandSourceBinding(engine);
            return Task.CompletedTask;
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
