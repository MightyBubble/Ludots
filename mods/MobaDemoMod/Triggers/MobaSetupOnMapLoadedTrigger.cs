using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace MobaDemoMod.Triggers
{
    /// <summary>
    /// Wires MOBA runtime state after the demo map is loaded.
    /// Keeps demo-scene setup separate from camera concerns.
    /// </summary>
    public sealed class MobaSetupOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public MobaSetupOnMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, "moba_demo")) return Task.CompletedTask;

            var world = engine.World;
            var q = new QueryDescription().WithAll<Name>();
            Entity localPlayer = default;
            world.Query(in q, (Entity e, ref Name _) =>
            {
                if (!world.Has<GameplayTagContainer>(e)) world.Add(e, new GameplayTagContainer());
                if (!world.Has<TagCountContainer>(e)) world.Add(e, new TagCountContainer());
                if (!world.Has<TimedTagBuffer>(e)) world.Add(e, new TimedTagBuffer());

                if (localPlayer == default &&
                    world.Has<PlayerOwner>(e) &&
                    world.Get<PlayerOwner>(e).PlayerId == 1)
                {
                    localPlayer = e;
                }
            });

            if (localPlayer != default)
            {
                engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = localPlayer;
                engine.GlobalContext[CoreServiceKeys.SelectedEntity.Name] = localPlayer;
                _ctx.Log("[MobaDemoMod] LocalPlayerEntity wired to player-owned hero.");
            }

            return Task.CompletedTask;
        }

        private static bool HasTag(List<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
