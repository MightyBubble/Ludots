using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// Seat-aware routing shared by the bridge tools: camera resolution per PresentBinding,
    /// window-point routing to the owning binding (the same half-open containment rule as
    /// PresentBindingScreenRayProvider), and per-seat semantic input channel resolution.
    /// Omitted seatId keeps the sole-seat behavior; multi-binding defaults resolve the first
    /// binding in seat order.
    /// </summary>
    internal static class SeatRouting
    {
        /// <summary>
        /// Explicit seatId → that seat's PresentBinding camera. Throws invalid.params for an
        /// unknown seat and capability.unavailable for a seat without a PresentBinding.
        /// </summary>
        public static (ClientLocalSeat Seat, PresentBinding Binding, CameraManager Camera) RequireSeatPresentCamera(
            AgentToolContext context,
            string seatId)
        {
            ClientLocalSeat seat = RequireSeat(context, seatId);
            if (!ClientLocalSeatAccess.TryResolvePresentCamera(context.Engine, seat.SeatId, out CameraManager? camera, out PresentBinding binding))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.CapabilityUnavailable,
                    $"Seat '{seat.SeatId}' has no PresentBinding; camera addressing requires a presenting seat.");
            }

            return (seat, binding, camera);
        }

        public static ClientLocalSeat RequireSeat(AgentToolContext context, string seatId)
        {
            ClientLocalSeatRegistry registry = ClientLocalSeatAccess.RequireRegistry(context.Engine);
            if (!registry.TryGet(seatId, out ClientLocalSeat seat))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Seat '{seatId}' does not exist. Known seats: {DescribeSeatIds(registry)}.");
            }

            return seat;
        }

        /// <summary>
        /// Default camera for seatless calls: sole binding keeps the authority camera; multiple
        /// bindings resolve the first in seat order (single-viewport consumer contract).
        /// </summary>
        public static (CameraManager Camera, string? SeatId) ResolveDefaultCamera(AgentToolContext context)
        {
            ClientLocalSeatRegistry registry = ClientLocalSeatAccess.RequireRegistry(context.Engine);
            var bindings = new List<(string SeatId, PresentBinding Binding)>(registry.Count);
            registry.CopyPresentBindings(bindings);
            if (bindings.Count == 0)
            {
                return (ClientLocalSeatAccess.ResolveAuthorityCamera(context.Engine), null);
            }

            PresentBinding first = bindings[0].Binding;
            return (ClientLocalSeatAccess.RequireLogicViews(context.Engine).RequireCamera(first.LogicViewId), bindings[0].SeatId);
        }

        /// <summary>
        /// Routes a host-window point to the binding whose normalized rect contains it. Rect
        /// membership is half-open with the shared edge belonging to the later binding in seat
        /// order; a point outside every rect falls back to the first binding — the routing rule
        /// of PresentBindingScreenRayProvider. Returns false with no bindings or no host view.
        /// </summary>
        public static bool TryRouteWindowPoint(
            AgentToolContext context,
            Vector2 windowPoint,
            List<(string SeatId, PresentBinding Binding)> bindings,
            out int routedIndex)
        {
            routedIndex = 0;
            if (bindings.Count == 0)
            {
                return false;
            }

            if (!context.TryGetService(CoreServiceKeys.ViewController, out IViewController? view) ||
                view == null ||
                view.Resolution.X <= 0f ||
                view.Resolution.Y <= 0f)
            {
                return false;
            }

            float nx = windowPoint.X / view.Resolution.X;
            float ny = windowPoint.Y / view.Resolution.Y;
            for (int i = 0; i < bindings.Count; i++)
            {
                Vector4 rect = bindings[i].Binding.NormalizedScreenRect;
                if (nx >= rect.X && nx < rect.X + rect.Z && ny >= rect.Y && ny < rect.Y + rect.W)
                {
                    routedIndex = i;
                    return true;
                }
            }

            routedIndex = 0;
            return true;
        }

        /// <summary>
        /// Per-seat semantic input handler: a multi-seat table answers through the seat's own
        /// ClientLocalSeatInputRuntime channel; the sole seat keeps the engine-global handler
        /// (its interpretation stack is the global chain, no channel shadows it).
        /// </summary>
        public static PlayerInputHandler ResolveSeatInputHandler(AgentToolContext context, string seatId)
        {
            ClientLocalSeat seat = RequireSeat(context, seatId);
            if (!context.TryGetService(CoreServiceKeys.ClientLocalSeatInputRuntime, out ClientLocalSeatInputRuntime? seatInput) ||
                seatInput == null)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "ClientLocalSeatInputRuntime is not available in this runtime; per-seat input addressing requires it.");
            }

            if (seatInput.TryGetChannel(seat.SeatId, out ClientLocalSeatInputChannel channel))
            {
                return channel.Handler;
            }

            if (ClientLocalSeatAccess.RequireRegistry(context.Engine).Count == 1)
            {
                return context.RequireService(CoreServiceKeys.InputHandler);
            }

            throw new AgentToolException(
                AgentBridgeErrorCodes.ServiceUnavailable,
                $"Seat '{seat.SeatId}' has no per-seat input channel; re-enter the map to republish seat channels.");
        }

        public static string DescribeSeatIds(ClientLocalSeatRegistry registry)
        {
            return registry.SeatIds.Count == 0 ? "(none)" : string.Join(",", registry.SeatIds);
        }
    }
}
