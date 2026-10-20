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
            int playerId,
            string seatId,
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
            int playerId,
            string seatId,
            Vector2? presentResolutionPx = null)
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
            // sole seat 接管呈现眼时收养现有相机实例：地图 DefaultCamera 位姿可能已落在
            // bootstrap 视图上，重建相机会把作者取景连同实例一起丢弃（投影从此解析到构造默认值）。
            CameraManager? adoptedCamera = ResolveAdoptablePresentCamera(views);
            string? preservedScheme = PreserveControlScheme(engine, seats, seatId, playerId);
            seats.Clear();
            views.Clear();
            string viewId = views.EnsureDefaultView(possessedRep, camera: adoptedCamera);
            Vector2 presentResolution = ResolvePresentResolution(engine, presentResolutionPx);
            seats.Add(new ClientLocalSeat(seatId, preservedScheme)
            {
                PossessedPlayerId = playerId,
                PossessedRep = possessedRep,
                PresentBinding = PresentBinding.FullScreen(viewId, presentResolution),
            });

            var replacement = new Ludots.Core.Gameplay.Teams.PlayerEntityLookup();
            replacement.Register(playerId, possessedRep);
            if (engine.CurrentMapSession != null)
            {
                engine.CurrentMapSession.LocalSeats = new[]
                {
                    new ResolvedLocalSeatPossession(seatId, playerId, possessedRep, preservedScheme),
                };
                engine.CurrentMapSession.PlayerEntityLookup?.ReplaceWith(replacement);
            }

            UpsertSolePlayerLookup(engine, playerId, possessedRep, replacement);
        }

        private static string? PreserveControlScheme(
            GameEngine engine,
            ClientLocalSeatRegistry seats,
            string seatId,
            int playerId)
        {
            string? preserved = null;
            System.Collections.Generic.IReadOnlyList<string> existingIds = seats.SeatIds;
            for (int i = 0; i < existingIds.Count; i++)
            {
                ClientLocalSeat existing = seats.Require(existingIds[i]);
                if (!string.IsNullOrWhiteSpace(existing.ControlSchemeId) &&
                    (string.Equals(existing.SeatId, seatId, StringComparison.Ordinal) || existingIds.Count == 1))
                {
                    preserved = existing.ControlSchemeId;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(preserved) && engine.CurrentMapSession?.LaunchContext != null)
            {
                System.Collections.Generic.IReadOnlyList<LocalSeatLaunchBinding> launchSeats =
                    engine.CurrentMapSession.LaunchContext.LocalSeats;
                for (int i = 0; i < launchSeats.Count; i++)
                {
                    LocalSeatLaunchBinding binding = launchSeats[i];
                    if (binding.PlayerId == playerId && !string.IsNullOrWhiteSpace(binding.ControlSchemeId))
                    {
                        preserved = binding.ControlSchemeId;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(preserved) &&
                engine.GlobalContext.TryGetValue(CoreServiceKeys.ControlSchemeRuntime.Name, out object? schemeObj) &&
                schemeObj is Ludots.Core.Input.Interaction.ControlSchemeRuntime schemes &&
                schemes.ActiveSchemeId != 0)
            {
                string active = schemes.SchemeIdRegistry.GetName(schemes.ActiveSchemeId);
                if (!string.IsNullOrWhiteSpace(active))
                {
                    preserved = active;
                }
            }

            return string.IsNullOrWhiteSpace(preserved) ? null : preserved.Trim();
        }

        internal static CameraManager? ResolveAdoptablePresentCamera(LogicViewRegistry views)
        {
            if (views.TryGetClientPresentCamera(out CameraManager clientPresent))
            {
                return clientPresent;
            }

            if (views.Count == 1)
            {
                var soleCamera = new System.Collections.Generic.List<CameraManager>(1);
                views.CopyCameras(soleCamera);
                return soleCamera[0];
            }

            return null;
        }

        private static void UpsertSolePlayerLookup(GameEngine engine, int playerId, Entity possessedRep, Ludots.Core.Gameplay.Teams.PlayerEntityLookup replacement)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.PlayerEntityLookup.Name, out object? lookupObj) &&
                lookupObj is Ludots.Core.Gameplay.Teams.PlayerEntityLookup existing)
            {
                existing.ReplaceWith(replacement);
            }
            else
            {
                engine.GlobalContext[CoreServiceKeys.PlayerEntityLookup.Name] = replacement;
            }
        }

        private static Vector2 ResolvePresentResolution(GameEngine engine, Vector2? presentResolutionPx)
        {
            if (presentResolutionPx is Vector2 explicitResolution)
            {
                if (explicitResolution.X <= 0f || explicitResolution.Y <= 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(presentResolutionPx));
                }

                return explicitResolution;
            }

            if (engine.GetService(CoreServiceKeys.ViewController) is Presentation.Camera.IViewController view &&
                view.Resolution.X > 0f &&
                view.Resolution.Y > 0f)
            {
                return view.Resolution;
            }

            throw new InvalidOperationException(
                "BindSoleSeat requires ViewController.Resolution or an explicit presentResolutionPx.");
        }
    }
}
