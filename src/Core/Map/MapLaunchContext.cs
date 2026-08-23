using System;
using System.Collections.Generic;
using Ludots.Core.Client;

namespace Ludots.Core.Map
{
    /// <summary>
    /// Runtime launch/session context carried with map load.
    /// Map identity describes world content; launch context describes how that world is entered.
    /// </summary>
    public sealed class MapLaunchContext
    {
        public IReadOnlyList<LocalSeatLaunchBinding> LocalSeats { get; init; } = Array.Empty<LocalSeatLaunchBinding>();
        public IReadOnlyDictionary<string, object>? Metadata { get; init; }

        public bool HasLocalSeats => LocalSeats != null && LocalSeats.Count > 0;

        public bool IsEmpty =>
            !HasLocalSeats &&
            (Metadata == null || Metadata.Count == 0);

        /// <summary>Build launch context from an explicit seat table (Epic #896).</summary>
        public static MapLaunchContext? Create(
            IReadOnlyList<LocalSeatLaunchBinding> localSeats,
            IReadOnlyDictionary<string, object>? metadata = null)
        {
            var context = new MapLaunchContext
            {
                LocalSeats = NormalizeSeats(localSeats),
                Metadata = metadata,
            };
            return context.IsEmpty ? null : context;
        }

        private static IReadOnlyList<LocalSeatLaunchBinding> NormalizeSeats(IReadOnlyList<LocalSeatLaunchBinding>? seats)
        {
            if (seats == null || seats.Count == 0)
            {
                return Array.Empty<LocalSeatLaunchBinding>();
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var normalized = new LocalSeatLaunchBinding[seats.Count];
            for (int i = 0; i < seats.Count; i++)
            {
                LocalSeatLaunchBinding seat = seats[i];
                if (string.IsNullOrWhiteSpace(seat.SeatId))
                {
                    throw new InvalidOperationException($"MapLaunchContext.LocalSeats[{i}].SeatId must be non-empty.");
                }

                string seatId = seat.SeatId.Trim();
                if (!ids.Add(seatId))
                {
                    throw new InvalidOperationException($"MapLaunchContext.LocalSeats duplicates seat id '{seatId}'.");
                }

                if (seat.PlayerId <= 0)
                {
                    throw new InvalidOperationException($"MapLaunchContext.LocalSeats[{i}].PlayerId must be positive.");
                }

                normalized[i] = new LocalSeatLaunchBinding(seatId, seat.PlayerId, seat.ControlSchemeId);
            }

            return normalized;
        }
    }

    public readonly record struct MapLoadRequest(
        MapId MapId,
        MapLaunchContext? LaunchContext = null,
        string? BoardName = null)
    {
        public static MapLoadRequest FromMapId(string mapId) => new(new MapId(mapId));

        public static MapLoadRequest FromMapId(string mapId, MapLaunchContext? launchContext) =>
            new(new MapId(mapId), launchContext);

        public static MapLoadRequest FromMapId(string mapId, string boardName) =>
            new(new MapId(mapId), null, RequireBoardName(boardName));

        public static MapLoadRequest FromMapId(string mapId, MapLaunchContext? launchContext, string? boardName) =>
            new(new MapId(mapId), launchContext, NormalizeBoardName(boardName));

        public string MapIdValue => MapId.Value;

        private static string? NormalizeBoardName(string? boardName)
            => string.IsNullOrWhiteSpace(boardName) ? null : boardName.Trim();

        private static string RequireBoardName(string boardName)
            => NormalizeBoardName(boardName)
                ?? throw new ArgumentException("Board name is required.", nameof(boardName));
    }
}
