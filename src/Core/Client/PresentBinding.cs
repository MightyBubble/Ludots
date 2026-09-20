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

        public const string FullScreenLayoutId = "fullscreen";
        public const string HorizontalEqualSplitLayoutId = "horizontal-equal-split";
        public const string VerticalEqualSplitLayoutId = "vertical-equal-split";

        public static readonly string[] DeclaredLayoutIds =
        {
            FullScreenLayoutId,
            HorizontalEqualSplitLayoutId,
            VerticalEqualSplitLayoutId,
        };

        /// <summary>
        /// Data-declared layout entry point: the layout id from game config selects the rect factory;
        /// switching between split orientations is a data change with no code branch at call sites.
        /// Takes the host surface resolution and derives the binding-local present resolution from the
        /// laid-out rect. Unknown ids fail fast with the id named.
        /// </summary>
        public static PresentBinding FromDeclaredLayout(
            string? layoutId,
            string logicViewId,
            int index,
            int count,
            Vector2 hostResolutionPx)
        {
            string normalized = NormalizeDeclaredLayoutId(layoutId);
            Vector4 rect = normalized switch
            {
                HorizontalEqualSplitLayoutId => EqualSplitRect(index, count, horizontal: true),
                VerticalEqualSplitLayoutId => EqualSplitRect(index, count, horizontal: false),
                _ => new Vector4(0f, 0f, 1f, 1f),
            };
            return new PresentBinding(logicViewId, rect, PresentResolutionForHost(hostResolutionPx, rect));
        }

        public static void ValidateDeclaredLayout(string? layoutId) => _ = NormalizeDeclaredLayoutId(layoutId);

        /// <summary>Binding-local pixel resolution for a normalized rect on a host surface.</summary>
        public static Vector2 PresentResolutionForHost(Vector2 hostResolutionPx, Vector4 normalizedScreenRect) =>
            new(hostResolutionPx.X * normalizedScreenRect.Z, hostResolutionPx.Y * normalizedScreenRect.W);

        private static string NormalizeDeclaredLayoutId(string? layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId))
            {
                return FullScreenLayoutId;
            }

            string trimmed = layoutId.Trim();
            for (int i = 0; i < DeclaredLayoutIds.Length; i++)
            {
                if (string.Equals(DeclaredLayoutIds[i], trimmed, StringComparison.Ordinal))
                {
                    return trimmed;
                }
            }

            throw new InvalidOperationException(
                $"Unknown PresentBinding layout '{trimmed}'; declared layouts are [{string.Join(", ", DeclaredLayoutIds)}].");
        }

        /// <summary>
        /// Equal horizontal strip layout (left→right). Multi-split foundation — host still syncs metrics per seat.
        /// </summary>
        public static PresentBinding HorizontalEqualSplit(
            string logicViewId,
            int index,
            int count,
            Vector2 presentResolutionPx)
        {
            Vector4 rect = EqualSplitRect(index, count, horizontal: true);
            return new PresentBinding(logicViewId, rect, presentResolutionPx);
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
            Vector4 rect = EqualSplitRect(index, count, horizontal: false);
            return new PresentBinding(logicViewId, rect, presentResolutionPx);
        }

        private static Vector4 EqualSplitRect(int index, int count, bool horizontal)
        {
            ValidateSplitIndex(index, count);
            float slice = 1f / count;
            return horizontal
                ? new Vector4(index * slice, 0f, slice, 1f)
                : new Vector4(0f, index * slice, 1f, slice);
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
