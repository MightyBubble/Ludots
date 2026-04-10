namespace MassFlowNavPlaygroundMod.Components
{
    internal struct MassFlowNavPlaygroundEntityTag
    {
    }

    internal struct MassFlowNavSceneRootTag
    {
    }

    internal struct MassFlowNavControllerTag
    {
    }

    internal struct MassFlowNavObstacleTag
    {
    }

    internal struct MassFlowNavTeamFlowAssignment
    {
        public int SurfaceId;
        public int FlowId;
        public int TeamId;
    }

    internal struct MassFlowNavManualGoalTag
    {
    }

    internal struct MassFlowNavFormationMember
    {
        public int GroupId;
        public int SlotIndex;
    }
}
