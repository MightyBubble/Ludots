namespace Ludots.Core.Gameplay.GAS
{
    public struct GasRuntimeState
    {
        public bool EffectLoopInSlice;
        public int EffectLoopStage;
        public int EffectLoopSubstage;
        public int EffectLoopPass;
        public bool HasPendingEffects;

        public byte ProposalWindowPhase;
        public bool ProposalWaitingInput;

        public int EffectRequestCount;
        public int InputRequestCount;
        public int ChainOrderCount;
        public int OrderRequestCount;

        public int EffectLifetimeProcessedLastSlice;
        public int EffectLifetimeDeferredCount;
        public int EffectLifetimeSnapshotCapacity;
    }

    public struct AbilityExecRuntimeState
    {
        public int ProcessedLastSlice;
        public int DeferredEntityCount;
        public int SnapshotEntityCount;
        public int SnapshotCapacity;
    }
}
