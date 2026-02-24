using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ArpgDemoMod.Triggers
{
    public sealed class InstallArpgDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "ArpgDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallArpgDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }
            engine.GlobalContext[InstalledKey] = true;

            if (engine.GlobalContext.TryGetValue(ContextKeys.AbilityDefinitionRegistry, out var defsObj) &&
                defsObj is AbilityDefinitionRegistry defs)
            {
                RegisterAbilities(defs);
            }

            return Task.CompletedTask;
        }

        private static void RegisterAbilities(AbilityDefinitionRegistry registry)
        {
            int shootArrow = RequireTemplate("Effect.Arpg.FireArrow");
            int drinkPotion = RequireTemplate("Effect.Arpg.HealPotion");
            int summonWolf = RequireTemplate("Effect.Arpg.SummonWolf");

            RegisterEffect(registry, 3101, shootArrow);
            RegisterEffect(registry, 3102, drinkPotion);
            RegisterEffect(registry, 3103, summonWolf);

            // 3105: Bleed (stackable DoT with GrantedTags)
            int bleed = RequireTemplate("Effect.Arpg.Bleed");
            RegisterEffect(registry, 3105, bleed);

            // 3106: IronSkin (armor buff with GrantedTags)
            int ironSkin = RequireTemplate("Effect.Arpg.IronSkin");
            RegisterEffect(registry, 3106, ironSkin);

            int stunned = TagRegistry.Register("Status.Stunned");
            var exec = default(AbilityExecSpec);
            exec.ClockId = GasClockId.FixedFrame;
            int idx = 0;
            exec.SetItem(idx++, ExecItemKind.TagClip, tick: 0, durationTicks: 60, tagId: stunned);
            exec.SetItem(idx, ExecItemKind.End, tick: 0);
            registry.Register(3104, new AbilityDefinition { ExecSpec = exec });

            static int RequireTemplate(string name)
            {
                int id = EffectTemplateIdRegistry.GetId(name);
                if (id <= 0) throw new InvalidOperationException($"ArpgDemoMod missing effect template '{name}'.");
                return id;
            }

            static void RegisterEffect(AbilityDefinitionRegistry r, int abilityId, int templateId)
            {
                var e = default(AbilityExecSpec);
                e.ClockId = GasClockId.FixedFrame;
                int i = 0;
                e.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: templateId);
                e.SetItem(i, ExecItemKind.End, tick: 0);
                r.Register(abilityId, new AbilityDefinition { ExecSpec = e });
            }
        }
    }
}

