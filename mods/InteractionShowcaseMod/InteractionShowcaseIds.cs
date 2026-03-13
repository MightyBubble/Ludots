using System;

namespace InteractionShowcaseMod
{
    public static class InteractionShowcaseIds
    {
        public const string ModId = "InteractionShowcaseMod";
        public const string B1SelfBuffMapId = "interaction_b1_self_buff";
        public const string B1SelfBuffScenarioId = "instant_press.b1_self_buff";
        public const string B1SelfBuffAbilityId = "Ability.Interaction.B1SelfBuff";
        public const string B1SelfBuffBuffEffectId = "Effect.Interaction.B1SelfBuffBuff";
        public const string B1SelfBuffHeroTemplateId = "interaction_b1_hero";
        public const string B1SelfBuffEnemyTemplateId = "interaction_b1_enemy";
        public const string C1HostileUnitDamageMapId = "interaction_c1_hostile_unit_damage";
        public const string C1HostileUnitDamageScenarioId = "unit_target.c1_hostile_unit_damage";
        public const string C1HostileUnitDamageAbilityId = "Ability.Interaction.C1HostileUnitDamage";
        public const string C1HostileUnitDamageEffectId = "Effect.Interaction.C1HostileUnitDamage";
        public const string C1HostileUnitDamageHeroTemplateId = "interaction_c1_hero";
        public const string C1HostileUnitDamagePrimaryTargetTemplateId = "interaction_c1_target_primary";
        public const string C1HostileUnitDamageInvalidTargetTemplateId = "interaction_c1_target_invalid";
        public const string C1HostileUnitDamageFarTargetTemplateId = "interaction_c1_target_far";
        public const string C2FriendlyUnitHealMapId = "interaction_c2_friendly_unit_heal";
        public const string C2FriendlyUnitHealScenarioId = "unit_target.c2_friendly_unit_heal";
        public const string C2FriendlyUnitHealAbilityId = "Ability.Interaction.C2FriendlyUnitHeal";
        public const string C2FriendlyUnitHealEffectId = "Effect.Interaction.C2FriendlyUnitHeal";
        public const string C2FriendlyUnitHealHeroTemplateId = "interaction_c2_hero";
        public const string C2FriendlyUnitHealAllyTargetTemplateId = "interaction_c2_target_ally";
        public const string C2FriendlyUnitHealHostileTargetTemplateId = "interaction_c2_target_hostile";
        public const string C2FriendlyUnitHealDeadAllyTargetTemplateId = "interaction_c2_target_dead_ally";
        public const string C3AnyUnitConditionalMapId = "interaction_c3_any_unit_conditional";
        public const string C3AnyUnitConditionalScenarioId = "unit_target.c3_any_unit_conditional";
        public const string C3AnyUnitConditionalAbilityId = "Ability.Interaction.C3AnyUnitConditional";
        public const string C3HostileConditionalSearchEffectId = "Effect.Interaction.C3HostileConditionalSearch";
        public const string C3FriendlyConditionalSearchEffectId = "Effect.Interaction.C3FriendlyConditionalSearch";
        public const string C3HostilePolymorphEffectId = "Effect.Interaction.C3HostilePolymorph";
        public const string C3FriendlyHasteEffectId = "Effect.Interaction.C3FriendlyHaste";
        public const string C3AnyUnitConditionalHeroTemplateId = "interaction_c3_hero";
        public const string C3AnyUnitConditionalHostileTargetTemplateId = "interaction_c3_target_hostile";
        public const string C3AnyUnitConditionalFriendlyTargetTemplateId = "interaction_c3_target_friendly";
        public const string HeroName = "ArpgHero";
        public const string DummyName = "ArpgEnemy";
        public const string C1PrimaryTargetName = "C1EnemyPrimary";
        public const string C1InvalidTargetName = "C1EnemyInvalid";
        public const string C1FarTargetName = "C1EnemyFar";
        public const string C2AllyTargetName = "C2AllyPrimary";
        public const string C2HostileTargetName = "C2EnemyInvalid";
        public const string C2DeadAllyTargetName = "C2AllyDead";
        public const string C3HostileTargetName = "C3EnemyPrimary";
        public const string C3FriendlyTargetName = "C3AllyPrimary";
        public const int B1SelfBuffSlot = 0;
        public const int C1HostileUnitDamageSlot = 0;
        public const int C2FriendlyUnitHealSlot = 0;
        public const int C3AnyUnitConditionalSlot = 0;

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, B1SelfBuffMapId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapId, C1HostileUnitDamageMapId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapId, C2FriendlyUnitHealMapId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapId, C3AnyUnitConditionalMapId, StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveScenarioId(string? mapId)
        {
            if (string.Equals(mapId, B1SelfBuffMapId, StringComparison.OrdinalIgnoreCase))
            {
                return B1SelfBuffScenarioId;
            }

            if (string.Equals(mapId, C1HostileUnitDamageMapId, StringComparison.OrdinalIgnoreCase))
            {
                return C1HostileUnitDamageScenarioId;
            }

            if (string.Equals(mapId, C2FriendlyUnitHealMapId, StringComparison.OrdinalIgnoreCase))
            {
                return C2FriendlyUnitHealScenarioId;
            }

            if (string.Equals(mapId, C3AnyUnitConditionalMapId, StringComparison.OrdinalIgnoreCase))
            {
                return C3AnyUnitConditionalScenarioId;
            }

            return string.Empty;
        }
    }
}
