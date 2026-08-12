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

        /// <summary>
        /// Equal horizontal strip layout (left→right). Multi-split foundation — host still syncs metrics per seat.
        /// </summary>
        public static PresentBinding HorizontalEqualSplit(
            string logicViewId,
            int index,
            int count,
            Vector2 presentResolutionPx)
        {
            ValidateSplitIndex(index, count);
            float width = 1f / count;
            return new PresentBinding(
                logicViewId,
                new Vector4(index * width, 0f, width, 1f),
                presentResolutionPx);
        }

        /// <summary>
        /// Equal vertical strip layout (top→bottom). Multi-split foundation — host still syncs metrics per seat.
        /// </summary>
        public static PresentBinding VerticalEqualSplit(
            string logicViewId,
            int index,
            int count,
            Vector2 presentResolutionPx)
        {
            ValidateSplitIndex(index, count);
            float height = 1f / count;
            return new PresentBinding(
                logicViewId,
                new Vector4(0f, index * height, 1f, height),
                presentResolutionPx);
        }

        private static void ValidateSplitIndex(int index, int count)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Split count must be at least 1.");
            }

            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Split index must be within [0, count).");
            }
        }

        public bool Equals(PresentBinding other) =>
            string.Equals(LogicViewId, other.LogicViewId, StringComparison.Ordinal) &&
            NormalizedScreenRect.Equals(other.NormalizedScreenRect) &&
            PresentResolutionPx.Equals(other.PresentResolutionPx);

        public override bool Equals(object? obj) => obj is PresentBinding other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(LogicViewId, NormalizedScreenRect, PresentResolutionPx);
    }
}
