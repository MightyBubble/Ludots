using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    public sealed class SessionInfoTool : IAgentTool
    {
        private readonly AgentBridgeRuntime _runtime;

        public SessionInfoTool(AgentBridgeRuntime runtime)
        {
            _runtime = runtime;
        }

        public string Name => "ludots.session.info";
        public string Description =>
            "Engine session snapshot: tick, players, loaded mods, camera, resolution, pacemaker mode, bridge stats, " +
            "and the client local seat table (id/playerId/schemeId/possessed/present rect per seat; split-screen seats " +
            "all appear, the top-level camera is the first binding in seat order). No parameters.";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            var engine = context.Engine;
            var session = engine.GameSession;
            var (camera, cameraSeatId) = SeatRouting.ResolveDefaultCamera(context);
            var cameraState = camera.State;

            var mods = new JsonArray();
            foreach (string id in engine.ModLoader.LoadedModIds)
            {
                mods.Add(id);
            }

            var result = new JsonObject
            {
                ["tick"] = session.CurrentTick,
                ["pacemaker"] = engine.Pacemaker?.GetType().Name ?? "null",
                ["mods"] = mods,
                ["camera"] = new JsonObject
                {
                    ["seatId"] = cameraSeatId,
                    ["targetCm"] = new JsonObject { ["x"] = cameraState.TargetCm.X, ["y"] = cameraState.TargetCm.Y },
                    ["pitch"] = cameraState.Pitch,
                    ["distanceCm"] = cameraState.DistanceCm,
                },
                ["bridge"] = new JsonObject
                {
                    ["pendingRequests"] = _runtime.PendingCount,
                    ["toolCount"] = _runtime.Tools.Tools.Count,
                },
            };

            if (context.TryGetService(CoreServiceKeys.ViewController, out var view))
            {
                result["resolution"] = new JsonObject { ["width"] = view.Resolution.X, ["height"] = view.Resolution.Y };
            }

            ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(engine);
            JsonArray seatRows = BuildSeatRows(seats);
            if (seatRows.Count > 0)
            {
                result["seats"] = seatRows;
            }

            if (seats.TryGetSoleSeat(out var seat) &&
                seat.HasPossession)
            {
                result["localPlayerId"] = seat.PossessedPlayerId;
                result["localSeatId"] = seat.SeatId;
            }

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.MapId.Name, out object? mapId) && mapId != null)
            {
                result["mapId"] = mapId.ToString();
            }

            return result;
        }

        private static JsonArray BuildSeatRows(ClientLocalSeatRegistry seats)
        {
            var rows = new JsonArray();
            IReadOnlyList<string> ids = seats.SeatIds;
            for (int i = 0; i < ids.Count; i++)
            {
                ClientLocalSeat seat = seats.Require(ids[i]);
                var row = new JsonObject
                {
                    ["seatId"] = seat.SeatId,
                    ["playerId"] = seat.PossessedPlayerId > 0 ? seat.PossessedPlayerId : null,
                    ["schemeId"] = seat.ControlSchemeId,
                    ["possessed"] = seat.HasPossession,
                    ["possessedEntityId"] = seat.HasPossession ? seat.PossessedRep.Id : null,
                };

                if (seat.PresentBinding is PresentBinding binding)
                {
                    Vector4 rect = binding.NormalizedScreenRect;
                    row["logicViewId"] = binding.LogicViewId;
                    row["presentRect"] = new JsonObject
                    {
                        ["x"] = rect.X,
                        ["y"] = rect.Y,
                        ["w"] = rect.Z,
                        ["h"] = rect.W,
                    };
                    row["presentResolution"] = new JsonObject
                    {
                        ["width"] = binding.PresentResolutionPx.X,
                        ["height"] = binding.PresentResolutionPx.Y,
                    };
                }
                else
                {
                    row["presentRect"] = null;
                }

                rows.Add(row);
            }

            return rows;
        }
    }
}
