using System;
using System.Numerics;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Optional presentation binding: which <see cref="LogicView"/> a <see cref="ClientLocalSeat"/> draws,
    /// and where on the host surface. Logic vision does not require a binding.
    /// </summary>
    public readonly struct PresentBinding : IEquatable<PresentBinding>
    {
        public PresentBinding(string logicViewId, Vector4 normalizedScreenRect, Vector2 presentResolutionPx)
        {
            if (string.IsNullOrWhiteSpace(logicViewId))
            {
                throw new ArgumentException("Logic view id is required.", nameof(logicViewId));
            }

            if (normalizedScreenRect.Z <= 0f || normalizedScreenRect.W <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(normalizedScreenRect), "Screen rect width/height must be positive.");
            }

            if (presentResolutionPx.X <= 0f || presentResolutionPx.Y <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(presentResolutionPx), "Present resolution must be positive.");
            }

            LogicViewId = logicViewId.Trim();
            NormalizedScreenRect = normalizedScreenRect;
            PresentResolutionPx = presentResolutionPx;
        }

        /// <summary>Target logical view id.</summary>
        public string LogicViewId { get; }

        /// <summary>Normalized host rect (x, y, width, height) in 0..1 space.</summary>
        public Vector4 NormalizedScreenRect { get; }

        /// <summary>Pixel metrics for picking/projection under this binding only — not LogicView truth.</summary>
        public Vector2 PresentResolutionPx { get; }

        public static PresentBinding FullScreen(string logicViewId, Vector2 presentResolutionPx) =>
            new(logicViewId, new Vector4(0f, 0f, 1f, 1f), presentResolutionPx);

        public bool Equals(PresentBinding other) =>
            string.Equals(LogicViewId, other.LogicViewId, StringComparison.Ordinal) &&
            NormalizedScreenRect.Equals(other.NormalizedScreenRect) &&
            PresentResolutionPx.Equals(other.PresentResolutionPx);

        public override bool Equals(object? obj) => obj is PresentBinding other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(LogicViewId, NormalizedScreenRect, PresentResolutionPx);
    }
}
