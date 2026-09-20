using System;
using System.Runtime.CompilerServices;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    public readonly struct PresenterVisualStableKey : IEquatable<PresenterVisualStableKey>
    {
        public readonly int PresenterStableId;
        public readonly int SlotIndex;
        public readonly AssetKind AssetKind;
        public readonly int Discriminator;

        public PresenterVisualStableKey(
            int presenterStableId,
            int slotIndex,
            AssetKind assetKind,
            int discriminator)
        {
            PresenterStableId = presenterStableId;
            SlotIndex = slotIndex;
            AssetKind = assetKind;
            Discriminator = discriminator;
        }

        public bool Equals(PresenterVisualStableKey other)
        {
            return PresenterStableId == other.PresenterStableId &&
                   SlotIndex == other.SlotIndex &&
                   AssetKind == other.AssetKind &&
                   Discriminator == other.Discriminator;
        }

        public override bool Equals(object? obj)
        {
            return obj is PresenterVisualStableKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((int)Hash());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ulong Hash()
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                hash = Mix(hash, (uint)PresenterStableId);
                hash = Mix(hash, (uint)SlotIndex);
                hash = Mix(hash, (uint)AssetKind);
                hash = Mix(hash, (uint)Discriminator);
                return Finalize(hash);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix(ulong hash, uint value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Finalize(ulong hash)
        {
            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccdUL;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53UL;
            hash ^= hash >> 33;
            return hash;
        }
    }
}
