using System;

namespace Ludots.Core.Presentation.Navigation
{
    public enum NavMeshPresentationTileState : byte
    {
        Pending = 1,
        Rebuilding = 2,
        Committed = 3
    }

    public readonly struct NavMeshPresentationColor : IEquatable<NavMeshPresentationColor>
    {
        public NavMeshPresentationColor(float red, float green, float blue, float alpha)
        {
            ValidateChannel(red, nameof(red));
            ValidateChannel(green, nameof(green));
            ValidateChannel(blue, nameof(blue));
            ValidateChannel(alpha, nameof(alpha));
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }
        public float Alpha { get; }

        public bool Equals(NavMeshPresentationColor other)
            => Red.Equals(other.Red) &&
               Green.Equals(other.Green) &&
               Blue.Equals(other.Blue) &&
               Alpha.Equals(other.Alpha);

        public override bool Equals(object? obj)
            => obj is NavMeshPresentationColor other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Red, Green, Blue, Alpha);

        private static void ValidateChannel(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "NavMesh presentation color channels must be finite values in [0, 1].");
            }
        }
    }

    public readonly struct NavMeshPresentationStyle : IEquatable<NavMeshPresentationStyle>
    {
        public NavMeshPresentationStyle(
            in NavMeshPresentationColor fillColor,
            in NavMeshPresentationColor edgeColor,
            in NavMeshPresentationColor tileBoundsColor,
            in NavMeshPresentationColor pendingColor,
            in NavMeshPresentationColor rebuildingColor,
            in NavMeshPresentationColor committedColor,
            float heightOffsetMeters,
            bool drawFill,
            bool drawEdges,
            bool drawTileBounds,
            bool drawTileStateIndication)
        {
            if (!float.IsFinite(heightOffsetMeters))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(heightOffsetMeters),
                    heightOffsetMeters,
                    "NavMesh presentation height offset must be finite.");
            }

            FillColor = fillColor;
            EdgeColor = edgeColor;
            TileBoundsColor = tileBoundsColor;
            PendingColor = pendingColor;
            RebuildingColor = rebuildingColor;
            CommittedColor = committedColor;
            HeightOffsetMeters = heightOffsetMeters;
            DrawFill = drawFill;
            DrawEdges = drawEdges;
            DrawTileBounds = drawTileBounds;
            DrawTileStateIndication = drawTileStateIndication;
        }

        public NavMeshPresentationColor FillColor { get; }
        public NavMeshPresentationColor EdgeColor { get; }
        public NavMeshPresentationColor TileBoundsColor { get; }
        public NavMeshPresentationColor PendingColor { get; }
        public NavMeshPresentationColor RebuildingColor { get; }
        public NavMeshPresentationColor CommittedColor { get; }
        public float HeightOffsetMeters { get; }
        public bool DrawFill { get; }
        public bool DrawEdges { get; }
        public bool DrawTileBounds { get; }
        public bool DrawTileStateIndication { get; }

        public NavMeshPresentationColor ResolveTileStateColor(NavMeshPresentationTileState state)
            => state switch
            {
                NavMeshPresentationTileState.Pending => PendingColor,
                NavMeshPresentationTileState.Rebuilding => RebuildingColor,
                NavMeshPresentationTileState.Committed => CommittedColor,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown NavMesh presentation tile state.")
            };

        public bool Equals(NavMeshPresentationStyle other)
            => FillColor.Equals(other.FillColor) &&
               EdgeColor.Equals(other.EdgeColor) &&
               TileBoundsColor.Equals(other.TileBoundsColor) &&
               PendingColor.Equals(other.PendingColor) &&
               RebuildingColor.Equals(other.RebuildingColor) &&
               CommittedColor.Equals(other.CommittedColor) &&
               HeightOffsetMeters.Equals(other.HeightOffsetMeters) &&
               DrawFill == other.DrawFill &&
               DrawEdges == other.DrawEdges &&
               DrawTileBounds == other.DrawTileBounds &&
               DrawTileStateIndication == other.DrawTileStateIndication;

        public override bool Equals(object? obj)
            => obj is NavMeshPresentationStyle other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(FillColor);
            hash.Add(EdgeColor);
            hash.Add(TileBoundsColor);
            hash.Add(PendingColor);
            hash.Add(RebuildingColor);
            hash.Add(CommittedColor);
            hash.Add(HeightOffsetMeters);
            hash.Add(DrawFill);
            hash.Add(DrawEdges);
            hash.Add(DrawTileBounds);
            hash.Add(DrawTileStateIndication);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Retained Core-owned intent for baked NavMesh presentation of exactly one layer/profile store.
    /// Mods configure this state; host adapters only consume the resulting frame buffer.
    /// </summary>
    public sealed class NavMeshPresentationState
    {
        private bool _enabled;
        private int _layer;
        private int _profile;
        private NavMeshPresentationStyle _style;
        private uint _revision;

        public bool Enabled => _enabled;
        public int Layer => _layer;
        public int Profile => _profile;
        public NavMeshPresentationStyle Style => _style;
        public uint Revision => _revision;

        public void Configure(
            bool enabled,
            int layer,
            int profile,
            in NavMeshPresentationStyle style)
        {
            if (layer < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(layer), layer, "Layer must be nonnegative.");
            }

            if (profile < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(profile), profile, "Profile must be nonnegative.");
            }

            if (_enabled == enabled &&
                _layer == layer &&
                _profile == profile &&
                _style.Equals(style))
            {
                return;
            }

            _enabled = enabled;
            _layer = layer;
            _profile = profile;
            _style = style;
            _revision = _revision == uint.MaxValue ? 1u : _revision + 1u;
        }

        public void Disable()
        {
            if (!_enabled)
            {
                return;
            }

            _enabled = false;
            _revision = _revision == uint.MaxValue ? 1u : _revision + 1u;
        }
    }
}
