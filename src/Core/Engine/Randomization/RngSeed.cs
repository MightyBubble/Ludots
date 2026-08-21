namespace Ludots.Core.Engine.Randomization
{
    /// <summary>
    /// FNV-1a seed mixing shared by random-seed derivation sites (named stream
    /// derivation and per-execution graph seed building).
    /// </summary>
    public static class RngSeed
    {
        public const uint OffsetBasis = 2166136261u;

        public static uint Begin(uint seed = 0u) => seed == 0u ? OffsetBasis : seed;

        public static uint Mix(uint hash, int value) => (hash ^ unchecked((uint)value)) * 16777619u;

        public static uint Finalize(uint hash) => hash == 0u ? 1u : hash;
    }
}
