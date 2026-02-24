using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;

namespace TcgDemoMod.Triggers
{
    public sealed class TcgSetupOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public TcgSetupOnMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override unsafe Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapTags = context.Get<List<string>>(ContextKeys.MapTags) ?? new List<string>();
            bool modify = HasTag(mapTags, "tcg_modify");
            bool hook = HasTag(mapTags, "tcg_hook");
            bool chain = HasTag(mapTags, "tcg_chain");
            bool stack = HasTag(mapTags, "tcg_stack");
            bool grant = HasTag(mapTags, "tcg_grant");

            if (!modify && !hook && !chain && !stack && !grant) return Task.CompletedTask;

            var world = engine.World;
            EnsureTagComponents(world);

            int spellTagId = TagRegistry.Register("Effect.Tcg.Spell");

            if (hook || modify)
            {
                var listener = default(ResponseChainListener);
                listener.Add(spellTagId, hook ? ResponseType.Hook : ResponseType.Modify,
                    priority: 100, modifyValue: 10, modifyOp: ModifierOp.Add);

                world.Create(
                    new Name { Value = hook ? "TcgHookListener" : "TcgModifyListener" },
                    listener
                );
            }

            if (chain)
            {
                // Chain response: when a spell resolves, append CounterBlast as a chained effect
                int counterBlastId = EffectTemplateIdRegistry.GetId("Effect.Tcg.CounterBlast");
                var chainListener = default(ResponseChainListener);
                chainListener.Add(spellTagId, ResponseType.Chain, priority: 50,
                    effectTemplateId: counterBlastId);

                world.Create(
                    new Name { Value = "TcgChainListener" },
                    chainListener
                );
            }

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

        private static void EnsureTagComponents(World world)
        {
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (!string.Equals(name.Value, "TcgHero", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name.Value, "TcgEnemy", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!world.Has<GameplayTagContainer>(e)) world.Add(e, new GameplayTagContainer());
                if (!world.Has<TagCountContainer>(e)) world.Add(e, new TagCountContainer());
                if (!world.Has<TimedTagBuffer>(e)) world.Add(e, new TimedTagBuffer());
            });
        }
    }
}
