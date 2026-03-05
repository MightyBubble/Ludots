using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

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

            var mapTags = context.Get<List<string>>(ContextKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, "rts")) return Task.CompletedTask;

            var world = engine.World;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                // Ensure all named entities have tag components for GAS interaction
                if (!world.Has<GameplayTagContainer>(e)) world.Add(e, new GameplayTagContainer());
                if (!world.Has<TagCountContainer>(e)) world.Add(e, new TagCountContainer());
                if (!world.Has<TimedTagBuffer>(e)) world.Add(e, new TimedTagBuffer());
            });

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
    }
}
