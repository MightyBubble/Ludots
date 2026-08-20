namespace Ludots.Core.Engine.Randomization
{
    /// <summary>
    /// FNV-1a style seed mixing shared by all random-seed derivation sites,
    /// keeping named stream seeds and per-execution graph seeds in one hash family.
    /// </summary>
    public static class RngSeed
    {
        public const uint OffsetBasis = 2166136261u;

        public static uint Begin(uint seed = 0u) => seed == 0u ? OffsetBasis : seed;

        public static uint Mix(uint hash, int value) => (hash ^ unchecked((uint)value)) * 16777619u;

        public static uint Finalize(uint hash) => hash == 0u ? 1u : hash;
    }
}
