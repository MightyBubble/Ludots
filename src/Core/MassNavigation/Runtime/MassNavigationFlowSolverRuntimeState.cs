using Schedulers;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed partial class MassNavigationFlowSolverState
{
    private sealed class UnitStepJob : IJob
    {
        public MassNavigationFlowSolverState? Owner { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public float Dt { get; set; }
        public MassNavigationGroupRuntime? NavGroupRuntime { get; set; }
        public float SepRadiusSq { get; set; }
        public float SepRadiusCm { get; set; }
        public float ArrivalRadiusCm { get; set; }
        public float ArrivalRadiusSq { get; set; }
        public float UnitTargetStopThresholdSq { get; set; }
        public int HashWidthMinusOne { get; set; }
        public int HashHeightMinusOne { get; set; }
        public float InvHashCell { get; set; }
        public int FlowObstacleNeighborRadiusCells { get; set; }
        public bool UseCandidateGating { get; set; }

        public void Execute()
        {
            Owner!.StepRange(
                StartIndex,
                EndIndex,
                Dt,
                NavGroupRuntime!,
                SepRadiusSq,
                SepRadiusCm,
                ArrivalRadiusCm,
                ArrivalRadiusSq,
                UnitTargetStopThresholdSq,
                HashWidthMinusOne,
                HashHeightMinusOne,
                InvHashCell,
                FlowObstacleNeighborRadiusCells,
                UseCandidateGating);
        }
    }

    private sealed class TeamRuntimeState
    {
        public TeamRuntimeState(
            int teamId,
            int unitCount,
            float spawnCenterX,
            float spawnCenterY,
            float directionX,
            float directionY,
            float tangentX,
            float tangentY)
        {
            TeamId = teamId;
            UnitCount = unitCount;
            SpawnCenterX = spawnCenterX;
            SpawnCenterY = spawnCenterY;
            DirectionX = directionX;
            DirectionY = directionY;
            TangentX = tangentX;
            TangentY = tangentY;
        }

        public TeamRuntimeState(int teamId)
        {
            TeamId = teamId;
        }

        public int TeamId { get; }
        public int UnitCount { get; set; }
        public float SpawnCenterX { get; }
        public float SpawnCenterY { get; }
        public float DirectionX { get; }
        public float DirectionY { get; }
        public float TangentX { get; }
        public float TangentY { get; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
    }

    private sealed class FlowRuntimeState
    {
        public FlowRuntimeState(int teamStateIndex, uint categoryMask, uint interactionMask, int gridCellCount)
        {
            TeamStateIndex = teamStateIndex;
            CategoryMask = categoryMask;
            InteractionMask = interactionMask;
            Flow = new float[gridCellCount * 2];
        }

        public int TeamStateIndex { get; }
        public uint CategoryMask { get; }
        public uint InteractionMask { get; }
        public float[] Flow { get; }
    }
}
