namespace Ludots.Core.Gameplay.AI.Utility
{
    public readonly struct UtilityConsiderationBool256
    {
        public readonly int AtomId;
        public readonly float TrueScore;
        public readonly float FalseScore;

        public UtilityConsiderationBool256(int atomId, float trueScore, float falseScore)
        {
            AtomId = atomId;
            TrueScore = trueScore;
            FalseScore = falseScore;
        }
    }
}

