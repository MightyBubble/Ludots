using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Scripting;

namespace Ludots.Core.Client
{
    /// <summary>
    /// Programmatic sole-seat possession binding for harnesses and showcases (Epic #896).
    /// Not a LocalPlayer compatibility shim — writes only seat/LogicView registries (+ MapSession.LocalSeats when present).
    /// </summary>
    public static class ClientLocalSeatBindings
    {
        public static void BindSoleSeat(
            System.Collections.Generic.IDictionary<string, object> globals,
            Entity possessedRep,
            int playerId = 1,
            string seatId = "seat.0",
            CameraManager? primaryCamera = null,
            Vector2? presentResolutionPx = null)
        {
            ArgumentNullException.ThrowIfNull(globals);
            if (possessedRep == Entity.Null)
            {
                throw new ArgumentException("Possessed rep is required.", nameof(possessedRep));
            }

            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }

            var seats = new ClientLocalSeatRegistry();
            var views = new LogicViewRegistry();
            string viewId = views.EnsureDefaultView(possessedRep, camera: primaryCamera);
            Vector2 resolution = presentResolutionPx ?? new Vector2(1280f, 720f);
            seats.Add(new ClientLocalSeat(seatId)
            {
                PossessedPlayerId = playerId,
                PossessedRep = possessedRep,
                PresentBinding = PresentBinding.FullScreen(viewId, resolution),
            });
            globals[CoreServiceKeys.ClientLocalSeatRegistry.Name] = seats;
            globals[CoreServiceKeys.LogicViewRegistry.Name] = views;
        }

        public static void BindSoleSeat(
            GameEngine engine,
            Entity possessedRep,
            int playerId = 1,
            string seatId = "seat.0")
        {
            ArgumentNullException.ThrowIfNull(engine);
            if (possessedRep == Entity.Null)
            {
                throw new ArgumentException("Possessed rep is required.", nameof(possessedRep));
            }

            if (playerId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerId));
            }

            ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException("ClientLocalSeatRegistry missing.");
            LogicViewRegistry views = engine.GetService(CoreServiceKeys.LogicViewRegistry)
                ?? throw new InvalidOperationException("LogicViewRegistry missing.");
            seats.Clear();
            views.Clear();
            string viewId = views.EnsureDefaultView(possessedRep, camera: engine.GameSession.Camera);
            seats.Add(new ClientLocalSeat(seatId)
            {
                PossessedPlayerId = playerId,
                PossessedRep = possessedRep,
                PresentBinding = PresentBinding.FullScreen(viewId, new Vector2(1280f, 720f)),
            });

            if (engine.CurrentMapSession != null)
            {
                engine.CurrentMapSession.LocalSeats = new[]
                {
                    new ResolvedLocalSeatPossession(seatId, playerId, possessedRep, ControlSchemeId: null),
                };
            }
        }
    }
}
