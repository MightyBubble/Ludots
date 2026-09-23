using System;
using Ludots.Core.Client;

namespace Ludots.Core.Config
{
    /// <summary>
    /// Cold-start seat recipe in <see cref="GameConfig"/> (Epic #896).
    /// Injected into <see cref="Map.MapLaunchContext.LocalSeats"/> on startup map load — not map identity.
    /// </summary>
    public sealed class StartupLocalSeatConfig
    {
        public string SeatId { get; set; } = string.Empty;
        public int PlayerId { get; set; }
        public string? ControlSchemeId { get; set; }

        public LocalSeatLaunchBinding ToLaunchBinding()
        {
            if (string.IsNullOrWhiteSpace(SeatId))
            {
                throw new InvalidOperationException("GameConfig.startupLocalSeats[].seatId must be non-empty.");
            }

            if (PlayerId <= 0)
            {
                throw new InvalidOperationException("GameConfig.startupLocalSeats[].playerId must be positive.");
            }

            return new LocalSeatLaunchBinding(SeatId.Trim(), PlayerId, ControlSchemeId);
        }
    }
}
