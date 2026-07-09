namespace EntityCommandPanelShowcaseMod
{
    public static class EntityCommandPanelShowcaseIds
    {
        public const string MapId = "interaction_showcase_hub";

        public const string WebUiTopic = "ludots.showcase.entity_command_panel.state";
        public const string WebUiSessionId = "entity-command-panel-showcase";
        public const string SetProfileCommand = "setProfile";

        public const string AssetIndexPath = "EntityCommandPanelShowcaseMod:assets/entity-command-panel-app/index.html";

        public const string AggregationAlias = "showcase.aggregation";
        public const string AggregationOwnerKey = "EntityCommandPanelShowcase.AggregationOwner";

        public const string ByTemplateProfileId = "aggregation.by_template";
        public const string ByFamilyProfileId = "aggregation.by_family";
        public const string ByAbilityIdProfileId = "aggregation.by_ability_id";

        public const string TemplateLabel = "Template";
        public const string FamilyLabel = "Family";
        public const string AbilityLabel = "Ability";

        public const string TemplateButtonId = "profile.by_template";
        public const string FamilyButtonId = "profile.by_family";
        public const string AbilityButtonId = "profile.by_ability_id";

        public const int ExpectedSourceActorCount = 3;
        public const int ExpectedFamilyTileCount = 8;
        public const int ExpectedTemplateTileCount = 24;
        public const int ExpectedAbilityTileCount = 21;
        public const int ProfileButtonCapacity = 8;

        public const string TemplateProjectionCollectionKey = "collection.ui.entity_command_panel.template";
        public const string AbilityProjectionCollectionKey = "collection.ui.entity_command_panel.ability";
        public const string FamilyProjectionCollectionKey = "collection.ui.entity_command_panel.family";
        public const string TemplateProjectionMarkerDefId = "entity_command_panel.visual.template_projection_marker";
        public const string AbilityProjectionMarkerDefId = "entity_command_panel.visual.ability_projection_marker";
        public const string FamilyProjectionMarkerDefId = "entity_command_panel.visual.family_projection_marker";

        public const string ArcweaverName = "Arcweaver";
        public const string VanguardName = "Vanguard";
        public const string CommanderName = "Commander";
    }
}
