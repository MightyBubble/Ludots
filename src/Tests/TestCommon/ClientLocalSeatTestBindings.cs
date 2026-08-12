using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;

namespace Ludots.Tests.TestCommon
{
    /// <summary>Test alias for <see cref="ClientLocalSeatBindings"/>.</summary>
    public static class ClientLocalSeatTestBindings
    {
        public static readonly Vector2 DefaultPresentResolutionPx = new(1280f, 720f);

        public static void BindSoleSeat(IDictionary<string, object> globals, Entity possessedRep, int playerId = 1, string seatId = "seat.0") =>
            ClientLocalSeatBindings.BindSoleSeat(globals, possessedRep, playerId, seatId, presentResolutionPx: DefaultPresentResolutionPx);

        public static void BindSoleSeat(
            GameEngine engine,
            Entity possessedRep,
            int playerId = 1,
            string seatId = "seat.0",
            Vector2? presentResolutionPx = null) =>
            ClientLocalSeatBindings.BindSoleSeat(
                engine,
                possessedRep,
                playerId,
                seatId,
                presentResolutionPx ?? DefaultPresentResolutionPx);
    }
}
