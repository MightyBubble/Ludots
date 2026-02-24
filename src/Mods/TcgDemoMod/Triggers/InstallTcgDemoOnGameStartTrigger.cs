using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace TcgDemoMod.Triggers
{
    public sealed class InstallTcgDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "TcgDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallTcgDemoOnGameStartTrigger(IModContext ctx)
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
            // 2101: Fireball (instant damage spell)
            RegisterEffectAbility(registry, 2101, "Effect.Tcg.Fireball");

            // 2102: Poison Counter (stackable DoT trap)
            RegisterEffectAbility(registry, 2102, "Effect.Tcg.PoisonCounter");

            // 2103: Magic Barrier (permanent enchantment granting Immune.Spell tag)
            RegisterEffectAbility(registry, 2103, "Effect.Tcg.MagicBarrier");

            // 2104: Power Boost (permanent enchantment with stat buff + GrantedTags)
            RegisterEffectAbility(registry, 2104, "Effect.Tcg.PowerBoost");
        }

        private static void RegisterEffectAbility(AbilityDefinitionRegistry registry, int abilityId, string effectName)
        {
            int templateId = EffectTemplateIdRegistry.GetId(effectName);
            if (templateId <= 0)
                throw new InvalidOperationException($"TcgDemoMod missing effect template '{effectName}'.");

            var exec = default(AbilityExecSpec);
            exec.ClockId = GasClockId.FixedFrame;
            int idx = 0;
            exec.SetItem(idx++, ExecItemKind.EffectSignal, tick: 0, templateId: templateId);
            exec.SetItem(idx, ExecItemKind.End, tick: 0);

            registry.Register(abilityId, new AbilityDefinition { ExecSpec = exec });
        }
    }
}

