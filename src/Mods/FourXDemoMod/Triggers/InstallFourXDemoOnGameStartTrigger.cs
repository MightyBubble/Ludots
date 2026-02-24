using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FourXDemoMod.Triggers
{
    public sealed class InstallFourXDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "FourXDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallFourXDemoOnGameStartTrigger(IModContext ctx)
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

            var config = (GameConfig)engine.GlobalContext[ContextKeys.GameConfig];

            if (engine.GlobalContext.TryGetValue(ContextKeys.AbilityDefinitionRegistry, out var defsObj) &&
                defsObj is AbilityDefinitionRegistry defs)
            {
                RegisterAbilities(defs, config);
            }

            // ── Setup 4-faction asymmetric team relationships ──
            // Team 1: Empire (player), Team 2: Federation, Team 3: Horde, Team 4: Nomads
            // Empire↔Federation: Friendly (alliance)
            // Empire↔Horde: Hostile (war)
            // Empire↔Nomads: Neutral
            // Federation↔Horde: Hostile
            // Federation↔Nomads: Neutral
            // Horde↔Nomads: Hostile
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Friendly);
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(1, 4, TeamRelationship.Neutral);
            TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(2, 4, TeamRelationship.Neutral);
            TeamManager.SetRelationshipSymmetric(3, 4, TeamRelationship.Hostile);

            return Task.CompletedTask;
        }

        private static void RegisterAbilities(AbilityDefinitionRegistry registry, GameConfig config)
        {
            int gcdTagId = TagRegistry.Register("Cooldown.GCD");
            int canColonize = TagRegistry.Register("Status.CanColonize");
            int blocked = TagRegistry.Register("Status.Blocked");

            RegisterEffectAbility(registry, abilityId: 4101, "Effect.4X.BuildOutpost", blockedAny: default, requiredAll: default);
            RegisterTagClipAbility(registry, abilityId: 4102, tagId: TagRegistry.Register("Status.Colonizing"), durationTicks: 120, blockedAny: blocked, requiredAll: canColonize);
            RegisterTagClipAbility(registry, abilityId: 4103, tagId: blocked, durationTicks: 120, blockedAny: default, requiredAll: default);

            // 4104: Tech Research (applies TechResearch buff with GrantedTags + duration)
            RegisterEffectAbility(registry, abilityId: 4104, "Effect.4X.TechResearch", blockedAny: default, requiredAll: default);
            // 4105: Diplomacy Pact (grants permanent allied tag)
            RegisterEffectAbility(registry, abilityId: 4105, "Effect.4X.DiplomacyPact", blockedAny: default, requiredAll: default);
            // 4106: Trade Route (stackable gold income buff, limit 5)
            RegisterEffectAbility(registry, abilityId: 4106, "Effect.4X.TradeRoute", blockedAny: default, requiredAll: default);

            void RegisterEffectAbility(AbilityDefinitionRegistry r, int abilityId, string effectTemplateName, int blockedAny, int requiredAll)
            {
                int templateId = EffectTemplateIdRegistry.GetId(effectTemplateName);
                if (templateId <= 0) throw new InvalidOperationException($"FourX ability '{abilityId}' references unknown effect '{effectTemplateName}'.");

                var blockTags = default(AbilityActivationBlockTags);
                if (blockedAny > 0) blockTags.BlockedAny.AddTag(blockedAny);
                if (requiredAll > 0) blockTags.RequiredAll.AddTag(requiredAll);

                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int idx = 0;
                exec.SetItem(idx++, ExecItemKind.TagClip, tick: 0, durationTicks: 10, tagId: gcdTagId);
                exec.SetItem(idx++, ExecItemKind.EffectSignal, tick: 0, templateId: templateId);
                exec.SetItem(idx, ExecItemKind.End, tick: 0);

                r.Register(abilityId, new AbilityDefinition
                {
                    ExecSpec = exec,
                    HasActivationBlockTags = blockedAny > 0 || requiredAll > 0,
                    ActivationBlockTags = blockTags,
                });
            }

            void RegisterTagClipAbility(AbilityDefinitionRegistry r, int abilityId, int tagId, int durationTicks, int blockedAny, int requiredAll)
            {
                var blockTags = default(AbilityActivationBlockTags);
                if (blockedAny > 0) blockTags.BlockedAny.AddTag(blockedAny);
                if (requiredAll > 0) blockTags.RequiredAll.AddTag(requiredAll);

                var exec = default(AbilityExecSpec);
                exec.ClockId = GasClockId.FixedFrame;
                int idx = 0;
                exec.SetItem(idx++, ExecItemKind.TagClip, tick: 0, durationTicks: durationTicks, tagId: tagId);
                exec.SetItem(idx, ExecItemKind.End, tick: 0);

                r.Register(abilityId, new AbilityDefinition
                {
                    ExecSpec = exec,
                    HasActivationBlockTags = blockedAny > 0 || requiredAll > 0,
                    ActivationBlockTags = blockTags,
                });
            }
        }
    }
}

