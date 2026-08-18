using System.Linq;
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
                ["instance"] = new JsonObject
                {
                    ["pid"] = Environment.ProcessId,
                    ["port"] = _runtime.BoundPort,
                    ["host"] = _runtime.HostKind,
                    ["label"] = _runtime.Label,
                    ["capabilities"] = new JsonArray(_runtime.Capabilities.Select(c => (JsonNode)c).ToArray()),
                },
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

    /// <summary>
    /// Lists every Ludots bridge instance registered on this machine (alive or
    /// not), so an agent connected to one instance can discover others and
    /// spawn/point an MCP adapter at a chosen one.
    /// </summary>
    public sealed class InstancesListTool : IAgentTool
    {
        private readonly AgentBridgeRuntime _runtime;

        public InstancesListTool(AgentBridgeRuntime runtime) => _runtime = runtime;

        public string Name => "ludots.instances.list";
        public string Description =>
            "List all Ludots agent-bridge instances registered on this machine: pid, port, host kind, label, " +
            "map, capabilities, liveness. Params: {includeDead?=false}. Use label/host/map to pick a target, " +
            "then point an MCP adapter at it with --instance label:<label> or the registry directory.";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            bool includeDead = AgentToolContext.OptionalBool(args, "includeDead", false);
            int selfPid = Environment.ProcessId;

            var instances = new JsonArray();
            int alive = 0;
            foreach ((AgentBridgeInstanceIdentity identity, bool isAlive) in AgentBridgeInstanceRegistry.List(_runtime.ArtifactsRoot))
            {
                if (!isAlive && !includeDead) continue;
                if (isAlive) alive++;

                instances.Add(new JsonObject
                {
                    ["pid"] = identity.Pid,
                    ["port"] = identity.Port,
                    ["host"] = identity.Host,
                    ["label"] = identity.Label,
                    ["mapId"] = identity.MapId,
                    ["capabilities"] = new JsonArray(identity.Capabilities.Select(c => (JsonNode)c).ToArray()),
                    ["alive"] = isAlive,
                    ["self"] = identity.Pid == selfPid,
                    ["startedAtUtc"] = identity.StartedAtUtc == DateTime.MinValue ? null : identity.StartedAtUtc.ToString("O"),
                });
            }

            return new JsonObject
            {
                ["registryDirectory"] = AgentBridgeInstanceRegistry.SessionsDirectory(_runtime.ArtifactsRoot),
                ["aliveCount"] = alive,
                ["instances"] = instances,
            };
        }
    }
}
