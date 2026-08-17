using System.Text.Json.Nodes;
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
        public string Description => "Engine session snapshot: tick, players, loaded mods, camera, resolution, pacemaker mode, bridge stats. No parameters.";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            var engine = context.Engine;
            var session = engine.GameSession;
            var camera = Ludots.Core.Client.ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State;

            var mods = new JsonArray();
            foreach (string id in engine.ModLoader.LoadedModIds)
            {
                mods.Add(id);
            }

            var result = new JsonObject
            {
                ["tick"] = session.CurrentTick,
                ["localPlayerId"] = Ludots.Core.Client.ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out var soleRep)
                    && Ludots.Core.Client.ClientLocalSeatAccess.RequireRegistry(engine).Require("seat.0").PossessedPlayerId > 0
                    ? Ludots.Core.Client.ClientLocalSeatAccess.RequireRegistry(engine).Require("seat.0").PossessedPlayerId
                    : 1,
                ["pacemaker"] = engine.Pacemaker?.GetType().Name ?? "null",
                ["mods"] = mods,
                ["camera"] = new JsonObject
                {
                    ["targetCm"] = new JsonObject { ["x"] = camera.TargetCm.X, ["y"] = camera.TargetCm.Y },
                    ["pitch"] = camera.Pitch,
                    ["distanceCm"] = camera.DistanceCm,
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

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.MapId.Name, out object? mapId) && mapId != null)
            {
                result["mapId"] = mapId.ToString();
            }

            return result;
        }
    }
}
