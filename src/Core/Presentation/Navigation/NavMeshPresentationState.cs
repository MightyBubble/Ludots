using System;

namespace Ludots.Core.Presentation.Navigation
{
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
            float heightOffsetMeters,
            bool drawFill,
            bool drawEdges)
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
            HeightOffsetMeters = heightOffsetMeters;
            DrawFill = drawFill;
            DrawEdges = drawEdges;
        }

        public NavMeshPresentationColor FillColor { get; }
        public NavMeshPresentationColor EdgeColor { get; }
        public float HeightOffsetMeters { get; }
        public bool DrawFill { get; }
        public bool DrawEdges { get; }

        public bool Equals(NavMeshPresentationStyle other)
            => FillColor.Equals(other.FillColor) &&
               EdgeColor.Equals(other.EdgeColor) &&
               HeightOffsetMeters.Equals(other.HeightOffsetMeters) &&
               DrawFill == other.DrawFill &&
               DrawEdges == other.DrawEdges;

        public override bool Equals(object? obj)
            => obj is NavMeshPresentationStyle other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(FillColor);
            hash.Add(EdgeColor);
            hash.Add(HeightOffsetMeters);
            hash.Add(DrawFill);
            hash.Add(DrawEdges);
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
        // Default style mirrors the proven runtime-showcase values: a debug overlay that is
        // enabled but never Configure()d must still render visibly, not silently draw nothing.
        private NavMeshPresentationStyle _style = new(
            new NavMeshPresentationColor(0.16f, 0.75f, 1.0f, 0.35f),
            new NavMeshPresentationColor(0.08f, 0.35f, 0.63f, 0.92f),
            heightOffsetMeters: 0.05f,
            drawFill: true,
            drawEdges: true);
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

        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            _revision = _revision == uint.MaxValue ? 1u : _revision + 1u;
        }
    }
}
