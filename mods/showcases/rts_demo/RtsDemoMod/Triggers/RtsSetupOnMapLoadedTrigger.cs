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
            }

            EnsurePlayerOwnership(world);
            if (isAuthoritativeServer)
            {
                return Task.CompletedTask;
            }

            RtsShowcaseCommandSourceHelper.EnsureCommandSourceBinding(engine);
            EnsureDefaultCommandSource(engine, world);
            return Task.CompletedTask;
        }

        private static void EnsurePlayerOwnership(World world, int playerId = 1)
        {
            var query = new QueryDescription().WithAll<Team>();
            world.Query(in query, (Entity entity, ref Team team) =>
            {
                if (team.Id != playerId || world.Has<PlayerOwner>(entity))
                {
                    return;
                }

                world.Add(entity, new PlayerOwner { PlayerId = playerId });
            });
        }

        private static void EnsureDefaultCommandSource(GameEngine engine, World world)
        {
            if (!engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity owner) ||
                !world.IsAlive(owner) ||
                RtsShowcaseCommandSourceHelper.GetCommandSourceCount(engine) > 0)
            {
                return;
            }

            Entity target = FindPreferredTarget(engine, world);
            if (target == Entity.Null || !world.IsAlive(target))
            {
                return;
            }

            RtsShowcaseCommandSourceHelper.TrySetCommandSourceAndFocus(engine, target, snapCamera: false);
        }

        private static bool HasTag(List<string> tags, string t)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], t, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static Entity FindFirstNamedEntity(World world, string nameToFind)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null &&
                    string.Equals(name.Value, nameToFind, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            return result;
        }

        private static Entity FindPreferredTarget(GameEngine engine, World world)
        {
            if (HasMapTag(engine, "rts_training"))
            {
                if (HasMapTag(engine, "war3"))
                {
                    Entity war3Producer = FindFirstNamedEntityContaining(world, "Barracks");
                    if (war3Producer != Entity.Null)
                    {
                        return war3Producer;
                    }
                }

                if (HasMapTag(engine, "cnc"))
                {
                    Entity cncProducer = FindFirstNamedEntityContaining(world, "War Factory");
                    if (cncProducer != Entity.Null)
                    {
                        return cncProducer;
                    }
                }

                if (HasMapTag(engine, "sc2"))
                {
                    Entity sc2Producer = FindFirstNamedEntityContaining(world, "Gateway");
                    if (sc2Producer != Entity.Null)
                    {
                        return sc2Producer;
                    }
                }
            }

            Entity result = FindFirstNamedEntityContaining(world, "Peasant");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstNamedEntityContaining(world, "Barracks");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstNamedEntityContaining(world, "War Factory");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstNamedEntityContaining(world, "Gateway");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstNamedEntityContaining(world, "ConYard");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstNamedEntityContaining(world, "Construction Yard");
            if (result != Entity.Null)
            {
                return result;
            }

            result = FindFirstNamedEntityContaining(world, "Drone");
            if (result != Entity.Null)
            {
                return result;
            }

            return FindFirstAbilityTarget(world);
        }

        private static bool HasMapTag(GameEngine engine, string tag)
        {
            var tags = engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Entity FindFirstNamedEntityContaining(World world, string token)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result != Entity.Null ||
                    string.IsNullOrWhiteSpace(name.Value) ||
                    name.Value.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                result = entity;
            });

            return result;
        }

        private static Entity FindFirstAbilityTarget(World world)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name, AbilityStateBuffer>();
            world.Query(in query, (Entity entity, ref Name _, ref AbilityStateBuffer _) =>
            {
                if (result == Entity.Null)
                {
                    result = entity;
                }
            });

            return result;
        }
    }
}
