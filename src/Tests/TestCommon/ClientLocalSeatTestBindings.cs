using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Tests.TestCommon
{
    public static class ClientLocalSeatTestBindings
    {
        public static void BindSoleSeat(IDictionary<string, object> globals, Entity possessedRep, int playerId = 1, string seatId = "seat.0")
        {
            var seats = new ClientLocalSeatRegistry();
            var views = new LogicViewRegistry();
            string viewId = views.EnsureDefaultView(possessedRep);
            var seat = new ClientLocalSeat(seatId)
            {
                PossessedPlayerId = playerId,
                PossessedRep = possessedRep,
                PresentBinding = PresentBinding.FullScreen(viewId, new System.Numerics.Vector2(1280f, 720f)),
            };
            seats.Add(seat);
            globals[CoreServiceKeys.ClientLocalSeatRegistry.Name] = seats;
            globals[CoreServiceKeys.LogicViewRegistry.Name] = views;
        }

        public static void BindSoleSeat(GameEngine engine, Entity possessedRep, int playerId = 1, string seatId = "seat.0")
        {
            ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new System.InvalidOperationException("ClientLocalSeatRegistry missing.");
            LogicViewRegistry views = engine.GetService(CoreServiceKeys.LogicViewRegistry)
                ?? throw new System.InvalidOperationException("LogicViewRegistry missing.");
            seats.Clear();
            views.Clear();
            string viewId = views.EnsureDefaultView(possessedRep, camera: engine.GameSession.Camera);
            var seat = new ClientLocalSeat(seatId)
            {
                PossessedPlayerId = playerId,
                PossessedRep = possessedRep,
                PresentBinding = PresentBinding.FullScreen(viewId, new System.Numerics.Vector2(1280f, 720f)),
            };
            seats.Add(seat);
        }
    }
}
