using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;

namespace Ludots.Tests.TestCommon
{
    /// <summary>Test alias for <see cref="ClientLocalSeatBindings"/>.</summary>
    public static class ClientLocalSeatTestBindings
    {
        public static void BindSoleSeat(IDictionary<string, object> globals, Entity possessedRep, int playerId = 1, string seatId = "seat.0") =>
            ClientLocalSeatBindings.BindSoleSeat(globals, possessedRep, playerId, seatId);

        public static void BindSoleSeat(GameEngine engine, Entity possessedRep, int playerId = 1, string seatId = "seat.0") =>
            ClientLocalSeatBindings.BindSoleSeat(engine, possessedRep, playerId, seatId);
    }
}
