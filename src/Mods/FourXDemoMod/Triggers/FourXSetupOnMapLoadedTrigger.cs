using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;

namespace FourXDemoMod.Triggers
{
    public sealed class FourXSetupOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public FourXSetupOnMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapTags = context.Get<List<string>>(ContextKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, "fourx")) return Task.CompletedTask;

            if (!engine.GlobalContext.TryGetValue(ContextKeys.TagOps, out var tagOpsObj) || tagOpsObj is not TagOps tagOps)
            {
                return Task.CompletedTask;
            }

            var world = engine.World;
            var (hero, _) = FindHero(world);
            if (!world.IsAlive(hero)) return Task.CompletedTask;

            EnsureTagComponents(world, hero);

            int canColonize = TagRegistry.Register("Status.CanColonize");
            ref var tags = ref world.Get<GameplayTagContainer>(hero);
            ref var counts = ref world.Get<TagCountContainer>(hero);
            tagOps.AddTag(ref tags, ref counts, canColonize);

            return Task.CompletedTask;
        }

        private static bool HasTag(List<string> tags, string t)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], t, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void EnsureTagComponents(World world, Entity e)
        {
            if (!world.Has<GameplayTagContainer>(e)) world.Add(e, new GameplayTagContainer());
            if (!world.Has<TagCountContainer>(e)) world.Add(e, new TagCountContainer());
            if (!world.Has<TimedTagBuffer>(e)) world.Add(e, new TimedTagBuffer());
        }

        private static (Entity Hero, Entity Site) FindHero(World world)
        {
            Entity hero = Entity.Null;
            Entity site = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (hero == Entity.Null && string.Equals(name.Value, "Governor", StringComparison.OrdinalIgnoreCase)) hero = e;
                if (site == Entity.Null && string.Equals(name.Value, "OutpostSite", StringComparison.OrdinalIgnoreCase)) site = e;
            });
            return (hero, site);
        }
    }
}
