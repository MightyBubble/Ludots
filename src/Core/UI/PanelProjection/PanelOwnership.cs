using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// Owner axis of the panel triaxis model: the semantic subject of the panel's
    /// variables. This is the single vocabulary shared by template <c>ownerKind</c>
    /// and intent-map <c>playerSource</c> — there is no second enum.
    /// </summary>
    public enum PanelOwnerKind : byte
    {
        Seat = 0,
        Participant = 1,
        Team = 2,
        World = 3,
    }

    public static class PanelOwnerKinds
    {
        public static PanelOwnerKind Parse(string text, string context)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{context} ownerKind is required.");
            }

            return text.Trim() switch
            {
                "seat" => PanelOwnerKind.Seat,
                "participant" => PanelOwnerKind.Participant,
                "team" => PanelOwnerKind.Team,
                "world" => PanelOwnerKind.World,
                _ => throw new InvalidOperationException(
                    $"{context} ownerKind '{text}' is unknown (allowed: seat, participant, team, world)."),
            };
        }

        public static string ToId(PanelOwnerKind kind) => kind switch
        {
            PanelOwnerKind.Seat => "seat",
            PanelOwnerKind.Participant => "participant",
            PanelOwnerKind.Team => "team",
            PanelOwnerKind.World => "world",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown panel owner kind."),
        };
    }

    /// <summary>
    /// Audience axis: which seats may see and operate the panel — every seat
    /// (<c>all-seats</c>) or an explicit seat id list. Shared panels stay one
    /// instance with a multi-seat audience; the audience is never a reason to
    /// duplicate instance state.
    /// </summary>
    public sealed class PanelAudience
    {
        private readonly string[] _seatIds;

        private PanelAudience(string[]? seatIds)
        {
            _seatIds = seatIds ?? Array.Empty<string>();
        }

        public static PanelAudience AllSeats { get; } = new(null);

        /// <summary>Explicit audience; entries must be non-empty, unique, and are trimmed.</summary>
        public static PanelAudience Seats(IReadOnlyList<string> seatIds)
        {
            ArgumentNullException.ThrowIfNull(seatIds);
            if (seatIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Panel audience seat list must not be empty; declare 'all-seats' for every seat.");
            }

            var trimmed = new string[seatIds.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < seatIds.Count; i++)
            {
                string id = seatIds[i]?.Trim() ?? string.Empty;
                if (id.Length == 0)
                {
                    throw new InvalidOperationException("Panel audience seat ids must be non-empty strings.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidOperationException($"Panel audience declares duplicate seat '{id}'.");
                }

                trimmed[i] = id;
            }

            return new PanelAudience(trimmed);
        }

        public bool IsAllSeats => _seatIds.Length == 0;

        public IReadOnlyList<string> SeatIds => _seatIds;

        public bool Contains(string seatId)
        {
            if (string.IsNullOrWhiteSpace(seatId))
            {
                return false;
            }

            if (_seatIds.Length == 0)
            {
                return true;
            }

            string trimmed = seatId.Trim();
            for (int i = 0; i < _seatIds.Length; i++)
            {
                if (string.Equals(_seatIds[i], trimmed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() =>
            _seatIds.Length == 0 ? "all-seats" : string.Join(", ", _seatIds);
    }
}
