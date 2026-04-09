using System;

namespace Ludots.Core.Presentation.Assets
{
    public enum PrefabPartGroundingMode : byte
    {
        None = 0,
        VisualHeightmap = 1,
    }

    public readonly struct PrefabPartGrounding : IEquatable<PrefabPartGrounding>
    {
        public static PrefabPartGrounding None { get; } = new(PrefabPartGroundingMode.None, 0f, false, 0);

        public PrefabPartGrounding(
            PrefabPartGroundingMode mode,
            float verticalOffsetMeters = 0f,
            bool alignToGroundNormal = false,
            int layerIndex = 0)
        {
            if (layerIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIndex), "Grounding layer index cannot be negative.");
            }

            Mode = mode;
            VerticalOffsetMeters = verticalOffsetMeters;
            AlignToGroundNormal = alignToGroundNormal;
            LayerIndex = layerIndex;
        }

        public PrefabPartGroundingMode Mode { get; }

        public float VerticalOffsetMeters { get; }

        public bool AlignToGroundNormal { get; }

        public int LayerIndex { get; }

        public bool RequiresVisualHeightmap => Mode == PrefabPartGroundingMode.VisualHeightmap;

        public bool Equals(PrefabPartGrounding other)
        {
            return Mode == other.Mode &&
                   VerticalOffsetMeters.Equals(other.VerticalOffsetMeters) &&
                   AlignToGroundNormal == other.AlignToGroundNormal &&
                   LayerIndex == other.LayerIndex;
        }

        public override bool Equals(object? obj)
        {
            return obj is PrefabPartGrounding other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)Mode, VerticalOffsetMeters, AlignToGroundNormal, LayerIndex);
        }

        public static bool operator ==(PrefabPartGrounding left, PrefabPartGrounding right) => left.Equals(right);

        public static bool operator !=(PrefabPartGrounding left, PrefabPartGrounding right) => !left.Equals(right);
    }
}
