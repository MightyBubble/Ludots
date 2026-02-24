using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Triggers
{
    /// <summary>
    /// Registers RTS abilities and sets up 3-faction team relationships on game start.
    /// Covers: Ability Cost (multi-signal), PeriodicSearch aura, Search AOE, CreateUnit,
    ///         Buff with GrantedTags, StimPack (RequiredAll activation), 3-team asymmetric relations.
    /// </summary>
    public sealed class InstallRtsDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "RtsDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallRtsDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var obj) && obj is bool b && b)
                return Task.CompletedTask;
            engine.GlobalContext[InstalledKey] = true;

            // ── Register abilities ──
            if (engine.GlobalContext.TryGetValue(ContextKeys.AbilityDefinitionRegistry, out var defsObj) &&
                defsObj is AbilityDefinitionRegistry defs)
            {
                RegisterAbilities(defs);
            }

            // ── Setup 3-faction team relationships ──
            // Team 1: Terran, Team 2: Zerg, Team 3: Protoss
            // All pairs are mutually hostile (asymmetric capable, but symmetric here).
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Hostile);

            return Task.CompletedTask;
        }

        private static void RegisterAbilities(AbilityDefinitionRegistry registry)
        {
            // ── Effect template IDs ──
            int buildCostBarracks = RequireTemplate("Effect.Rts.BuildCost.Barracks");
            int buildBarracks = RequireTemplate("Effect.Rts.BuildBarracks");
            int trainCostMarine = RequireTemplate("Effect.Rts.TrainCost.Marine");
            int trainMarine = RequireTemplate("Effect.Rts.TrainMarine");
            int researchComplete = RequireTemplate("Effect.Rts.ResearchComplete");
            int shieldAura = RequireTemplate("Effect.Rts.ShieldAura");
            int siegeTankAoe = RequireTemplate("Effect.Rts.SiegeTankAoe");
            int stimBuff = RequireTemplate("Effect.Rts.StimBuff");
            int irradiate = RequireTemplate("Effect.Rts.Irradiate");

            // ── Tag IDs ──
            int techStim = TagRegistry.Register("Tech.Stim");

            // 5001: BuildBarracks (SCV) - Cost + CreateUnit, two signals at tick 0
            {
                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int i = 0;
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: buildCostBarracks);
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: buildBarracks);
                exec.SetItem(i, ExecItemKind.End, tick: 0);
                registry.Register(5001, new AbilityDefinition { ExecSpec = exec });
            }

            // 5002: TrainMarine (Barracks) - Cost + CreateUnit
            {
                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int i = 0;
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: trainCostMarine);
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: trainMarine);
                exec.SetItem(i, ExecItemKind.End, tick: 0);
                registry.Register(5002, new AbilityDefinition { ExecSpec = exec });
            }

            // 5003: ResearchStim (Barracks) - TagClip for research duration + GrantedTags effect
            {
                int researchingTag = TagRegistry.Register("Status.Researching");
                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int i = 0;
                exec.SetItem(i++, ExecItemKind.TagClip, tick: 0, durationTicks: 120, tagId: researchingTag);
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 120, templateId: researchComplete);
                exec.SetItem(i, ExecItemKind.End, tick: 120);
                registry.Register(5003, new AbilityDefinition { ExecSpec = exec });
            }

            // 5004: StimPack (Marine) - Requires Tech.Stim tag on caster
            {
                var blockTags = default(AbilityActivationBlockTags);
                blockTags.RequiredAll.AddTag(techStim);

                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int i = 0;
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: stimBuff);
                exec.SetItem(i, ExecItemKind.End, tick: 0);
                registry.Register(5004, new AbilityDefinition
                {
                    ExecSpec = exec,
                    HasActivationBlockTags = true,
                    ActivationBlockTags = blockTags,
                });
            }

            // 5005: SiegeMode AOE (Siege Tank) - Search area + dispatch damage
            {
                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int i = 0;
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: siegeTankAoe);
                exec.SetItem(i, ExecItemKind.End, tick: 0);
                registry.Register(5005, new AbilityDefinition { ExecSpec = exec });
            }

            // 5006: Shield Aura (Science Vessel) - PeriodicSearch passive
            {
                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int i = 0;
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: shieldAura);
                exec.SetItem(i, ExecItemKind.End, tick: 0);
                registry.Register(5006, new AbilityDefinition { ExecSpec = exec });
            }

            // 5007: Irradiate (Science Vessel) - DoT on target
            {
                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int i = 0;
                exec.SetItem(i++, ExecItemKind.EffectSignal, tick: 0, templateId: irradiate);
                exec.SetItem(i, ExecItemKind.End, tick: 0);
                registry.Register(5007, new AbilityDefinition { ExecSpec = exec });
            }
        }

        private static int RequireTemplate(string name)
        {
            int id = EffectTemplateIdRegistry.GetId(name);
            if (id <= 0)
                throw new InvalidOperationException($"RtsDemoMod missing effect template '{name}'.");
            return id;
        }
    }
}
