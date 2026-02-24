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
    }
}

