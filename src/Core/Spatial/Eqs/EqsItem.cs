using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>
    /// EQS candidate item with position, score, and filter state.
    /// </summary>
    public struct EqsItem
    {
        public WorldCmInt2 Position;
        public float Score;
        public bool Filtered;

        public EqsItem(WorldCmInt2 position)
        {
            Position = position;
            Score = 0f;
            Filtered = false;
        }
    }
}
