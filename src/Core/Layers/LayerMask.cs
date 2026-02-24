using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Layers
{
    /// <summary>
    /// Bitmask-based layer classification for spatial/physics/effect filtering.
    /// Analogous to Unity LayerMask / Unreal Collision Channels.
    ///
    /// Category = which layers this object belongs to (bitmask, up to 32).
    /// Mask     = which layers this object can interact with (bitmask).
    ///
    /// This struct is a generic service type — usable by:
    ///   - ECS entities (via EntityLayer component)
    ///   - Physics static bodies
    ///   - Terrain cells / trigger volumes
    ///   - Any system that needs layer-based filtering
    /// </summary>
    public struct LayerMask : IEquatable<LayerMask>
    {
        /// <summary>Which layers this object belongs to.</summary>
        public uint Category;

        /// <summary>Which layers this object can interact with.</summary>
        public uint Mask;

        public static LayerMask All => new LayerMask(uint.MaxValue, uint.MaxValue);
        public static LayerMask None => default;

        public LayerMask(uint category, uint mask)
        {
            Category = category;
            Mask = mask;
        }

        /// <summary>
        /// One-way test: does target's Category overlap with source's Mask?
        /// Use when only the source side decides who it can "see".
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Test(in LayerMask source, in LayerMask target)
            => (target.Category & source.Mask) != 0;

        /// <summary>
        /// One-way test using raw uint values (avoids struct load when only one side is a struct).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Test(uint sourceMask, uint targetCategory)
            => (targetCategory & sourceMask) != 0;

        /// <summary>
        /// Bidirectional test: both sides must allow the interaction.
        /// Use for physics collision pairs.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TestBidirectional(in LayerMask a, in LayerMask b)
            => (a.Category & b.Mask) != 0 && (b.Category & a.Mask) != 0;

        public bool Equals(LayerMask other) => Category == other.Category && Mask == other.Mask;
        public override bool Equals(object obj) => obj is LayerMask other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Category, Mask);
        public static bool operator ==(LayerMask left, LayerMask right) => left.Equals(right);
        public static bool operator !=(LayerMask left, LayerMask right) => !left.Equals(right);

        public override string ToString() => $"LayerMask(Cat=0x{Category:X8}, Mask=0x{Mask:X8})";
    }
}
