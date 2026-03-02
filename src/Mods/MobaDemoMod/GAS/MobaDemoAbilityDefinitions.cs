namespace MobaDemoMod.GAS
{
    /// <summary>
    /// Abilities are now loaded from GAS/abilities.json via AbilityExecLoader (production config pipeline).
    /// This class is retained only for the stable ability tag string constants used by other systems.
    /// </summary>
    public static class MobaDemoAbilityDefinitions
    {
        public const string SkillQ = "Ability.Moba.SkillQ";
        public const string SkillW = "Ability.Moba.SkillW";
        public const string SkillE = "Ability.Moba.SkillE";
        public const string SkillR = "Ability.Moba.SkillR";
        public const string Move   = "Ability.Moba.Move";
    }
}
