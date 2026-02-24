using System;
using System.Numerics;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Registry;

namespace MobaDemoMod.GAS
{
    public static class MobaDemoAbilityDefinitions
    {
        public static void RegisterBuiltins(AbilityDefinitionRegistry registry, MobaConfig config = null)
        {
            config ??= new MobaConfig();
            var ab = config.Abilities;

            Register(registry, MobaAbilityIds.SkillQ, "Effect.Moba.Damage.Q", "Cooldown.Skill.Q", ab.SkillQ.CooldownTicks, ab.SkillQ.RangeCm, TargetShape.Single, ab);
            Register(registry, MobaAbilityIds.SkillW, "Effect.Moba.Heal.W", "Cooldown.Skill.W", ab.SkillW.CooldownTicks, ab.SkillW.RangeCm, TargetShape.Self, ab);
            Register(registry, MobaAbilityIds.SkillE, "Effect.Moba.Damage.E", "Cooldown.Skill.E", ab.SkillE.CooldownTicks, ab.SkillE.RangeCm, TargetShape.Cone, ab);
            Register(registry, MobaAbilityIds.SkillR, "Effect.Moba.Damage.R", "Cooldown.Skill.R", ab.SkillR.CooldownTicks, ab.SkillR.RangeCm, TargetShape.Circle, ab);
            RegisterMove(registry);
        }

        private static void Register(AbilityDefinitionRegistry registry, int abilityId, string effectTemplateName, string cooldownTag, int cooldownTicks, float castRange, TargetShape shape, MobaConfig.AbilityConfig abilityConfig)
        {
            int templateId = EffectTemplateIdRegistry.GetId(effectTemplateName);
            if (templateId <= 0)
            {
                throw new InvalidOperationException($"Moba demo ability '{abilityId}' references unknown effect template '{effectTemplateName}'.");
            }

            int gcdTagId = TagRegistry.Register("Cooldown.GCD");
            int cdTagId = TagRegistry.Register(cooldownTag);

            var blockTags = default(AbilityActivationBlockTags);
            blockTags.BlockedAny.AddTag(gcdTagId);
            blockTags.BlockedAny.AddTag(cdTagId);

            var exec = default(AbilityExecSpec);
            exec.ClockId = GasClockId.FixedFrame;

            int idx = 0;
            exec.SetItem(idx++, ExecItemKind.TagClip, tick: 0, durationTicks: abilityConfig.GlobalCooldownTicks, tagId: gcdTagId);
            exec.SetItem(idx++, ExecItemKind.TagClip, tick: 0, durationTicks: cooldownTicks, tagId: cdTagId);
            if (abilityId == MobaAbilityIds.SkillR)
            {
                exec.SetItem(idx++, ExecItemKind.SelectionGate, tick: 0, tagId: SelectionRequestTags.CircleEnemy);
            }
            exec.SetItem(idx++, ExecItemKind.EffectSignal, tick: 0, templateId: templateId);
            exec.SetItem(idx, ExecItemKind.End, tick: 0);

            var ic = abilityConfig.Indicator;
            var indicator = new AbilityIndicatorConfig
            {
                Shape = shape,
                Range = castRange,
                ShowRangeCircle = castRange > 0f,
                ValidColor = new Vector4(ic.ValidColor[0], ic.ValidColor[1], ic.ValidColor[2], ic.ValidColor[3]),
                InvalidColor = new Vector4(ic.InvalidColor[0], ic.InvalidColor[1], ic.InvalidColor[2], ic.InvalidColor[3]),
                RangeCircleColor = new Vector4(ic.RangeCircleColor[0], ic.RangeCircleColor[1], ic.RangeCircleColor[2], ic.RangeCircleColor[3]),
            };

            registry.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = exec,
                HasActivationBlockTags = true,
                ActivationBlockTags = blockTags,
                HasIndicator = castRange > 0f,
                Indicator = indicator
            });
        }

        private static void RegisterMove(AbilityDefinitionRegistry registry)
        {
            int moveTagId = TagRegistry.Register("Ability.Nav.Move");
            int arrivedEventTagId = TagRegistry.Register("Event.Nav.Arrived");

            var exec = default(AbilityExecSpec);
            exec.ClockId = GasClockId.Step;
            int idx = 0;
            exec.SetItem(idx++, ExecItemKind.TagSignal, tick: 0, tagId: moveTagId, payloadA: 0);
            exec.SetItem(idx++, ExecItemKind.EventGate, tick: 0, tagId: arrivedEventTagId);
            exec.SetItem(idx++, ExecItemKind.TagSignal, tick: 0, tagId: moveTagId, payloadA: 1);
            exec.SetItem(idx, ExecItemKind.End, tick: 0);

            registry.Register(MobaAbilityIds.Move, new AbilityDefinition
            {
                ExecSpec = exec,
                HasActivationBlockTags = false,
                HasIndicator = false
            });
        }
    }
}
