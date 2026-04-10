namespace MassFlowNavPlaygroundMod
{
    internal static class MassFlowNavPlaygroundIds
    {
        public const string MapId = "mass_flow_nav_playground";
        public const string InputContextId = "MassFlowNavPlayground.Controls";
        public const string RotateFormationLeftActionId = "MassFlowNav.RotateFormationLeft";
        public const string RotateFormationRightActionId = "MassFlowNav.RotateFormationRight";
        public const string UnitVisualTemplateId = "mass_flow_nav.unit";
        public const string ObstacleVisualTemplateId = "mass_flow_nav.obstacle";
        public const string ControllerName = "MassFlowNavController";
        public const int LocalPlayerId = 1;
        public const int FriendlyTeamId = 1;
        public const int EnemyTeamId = 2;
        public const float UnitPrimitiveScale = 0.55f;
        public const float ObstaclePrimitiveScaleMin = 0.8f;

        public static int ResolveFlowIdForTeam(int teamId)
        {
            return teamId == EnemyTeamId ? 1 : 0;
        }
    }
}
