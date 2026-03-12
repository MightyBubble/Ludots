namespace InteractionShowcaseMod
{
    public static class InteractionShowcaseIds
    {
        public const string HubMapId = "interaction_showcase_hub";
        public const string StressMapId = "interaction_showcase_stress";

        public const string InputContextId = "InteractionShowcase.Controls";
        public const string SelectionIndicatorDefId = "interaction_selection_indicator";
        public const int SelectionScopeId = 22041;

        public const string WowModeId = "Interaction.Mode.WoW";
        public const string LolModeId = "Interaction.Mode.LoL";
        public const string Sc2ModeId = "Interaction.Mode.SC2";
        public const string IndicatorModeId = "Interaction.Mode.Indicator";
        public const string ActionModeId = "Interaction.Mode.Action";

        public const string WowModeActionId = "InteractionModeWoW";
        public const string LolModeActionId = "InteractionModeLoL";
        public const string Sc2ModeActionId = "InteractionModeSC2";
        public const string IndicatorModeActionId = "InteractionModeIndicator";
        public const string ActionModeActionId = "InteractionModeAction";

        public const string ArcweaverName = "Arcweaver";
        public const string VanguardName = "Vanguard";
        public const string CommanderName = "Commander";
        public const string StressRedAnchorName = "StressRedAnchor";
        public const string StressBlueAnchorName = "StressBlueAnchor";

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, HubMapId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mapId, StressMapId, System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsShowcaseMode(string? modeId)
        {
            return string.Equals(modeId, WowModeId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeId, LolModeId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeId, Sc2ModeId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeId, IndicatorModeId, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(modeId, ActionModeId, System.StringComparison.OrdinalIgnoreCase);
        }

        public static string DescribeMap(string? mapId)
        {
            return mapId switch
            {
                HubMapId => "Three controllable heroes on one arena: target-first, smart-cast, aim-cast, indicator release, context-scored action combat, vector casts, ring AoE, chords, double-tap, queue, and toggle flows.",
                StressMapId => "3200 fireball casters deploy in mirrored formations and auto-saturate the battlefield to validate order, GAS, projectile, and ECS throughput.",
                _ => "Interaction showcase for Ludots input, GAS, order, graph, and ECS data pipelines."
            };
        }
    }
}
