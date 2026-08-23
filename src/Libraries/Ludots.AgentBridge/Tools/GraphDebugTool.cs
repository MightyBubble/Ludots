using System;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    public sealed class GraphDebugTool : IAgentTool
    {
        public string Name => "ludots.graph.debug";

        public string Description =>
            "Inspect mounted TriggerGraph entries and drain their opt-in fixed-capacity execution trace. " +
            "Actions: list, configure, drain. Drain is incremental by sequence and reports gaps/dropped records.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("action"),
            ["properties"] = new JsonObject
            {
                ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("list", "configure", "drain") },
                ["graphId"] = new JsonObject { ["type"] = "string" },
                ["entryLabel"] = new JsonObject { ["type"] = "string" },
                ["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("off", "node", "nodeAndPins") },
                ["since"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
                ["max"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 512 },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string action = AgentToolContext.RequireString(args, "action");
            IReadOnlyList<TriggerGraphMountTrigger> mounts = FindMounts(context);
            return action switch
            {
                "list" => ListMounts(mounts),
                "configure" => Configure(args, mounts),
                "drain" => Drain(args, context, mounts),
                _ => throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Unknown graph debug action '{action}'. Expected list | configure | drain."),
            };
        }

        private static IReadOnlyList<TriggerGraphMountTrigger> FindMounts(AgentToolContext context)
        {
            if (context.Engine.CurrentMapSession == null)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "No current map session is mounted; graph debug requires a loaded map.");
            }

            var list = new List<TriggerGraphMountTrigger>();
            IReadOnlyList<Ludots.Core.Scripting.Trigger> triggers = context.Engine.CurrentMapSession.Triggers;
            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i] is TriggerGraphMountTrigger mount)
                {
                    list.Add(mount);
                }
            }

            return list;
        }

        private static JsonObject ListMounts(IReadOnlyList<TriggerGraphMountTrigger> mounts)
        {
            var entries = new JsonArray();
            for (int i = 0; i < mounts.Count; i++)
            {
                entries.Add(MountSnapshot(mounts[i]));
            }

            return new JsonObject { ["mounts"] = entries, ["count"] = mounts.Count };
        }

        private static JsonObject Configure(JsonObject? args, IReadOnlyList<TriggerGraphMountTrigger> mounts)
        {
            TriggerGraphMountTrigger mount = RequireMount(args, mounts);
            string modeText = AgentToolContext.RequireString(args, "mode");
            GraphDebugTraceMode mode = modeText switch
            {
                "off" => GraphDebugTraceMode.Disabled,
                "node" => GraphDebugTraceMode.Node,
                "nodeAndPins" => GraphDebugTraceMode.NodeAndPins,
                _ => throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Unknown graph debug mode '{modeText}'. Expected off | node | nodeAndPins."),
            };

            mount.DebugTrace.Configure(mode);
            mount.DebugTrace.Clear();
            return new JsonObject { ["ok"] = true, ["mount"] = MountSnapshot(mount) };
        }

        private static JsonObject Drain(
            JsonObject? args,
            AgentToolContext context,
            IReadOnlyList<TriggerGraphMountTrigger> mounts)
        {
            TriggerGraphMountTrigger mount = RequireMount(args, mounts);
            long since = AgentToolContext.OptionalInt(args, "since", 0);
            int max = Math.Clamp(AgentToolContext.OptionalInt(args, "max", 256), 1, 512);
            var buffer = new GraphDebugTraceRecord[max];
            int count = mount.DebugTrace.ReadSince(since, buffer, out long oldestSequence);
            var events = new JsonArray();

            if (!context.Engine.TryGetService(Ludots.Core.Scripting.CoreServiceKeys.GraphProgramRegistry, out GraphProgramRegistry? programs) ||
                programs == null || !programs.TryGetSourceMap(mount.GraphId, out GraphInstructionSourceMap sourceMap) ||
                !sourceMap.HasSources || !string.Equals(sourceMap.GraphId, mount.GraphName, StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentToolException(AgentBridgeErrorCodes.ServiceUnavailable,
                    $"Graph debug source map is unavailable for mounted graph '{mount.GraphName}'.");
            }

            for (int i = 0; i < count; i++)
            {
                GraphDebugTraceRecord record = buffer[i];
                int sourcePc = record.SourcePc >= 0 ? record.SourcePc : record.CursorPc;
                sourceMap.TryGetSource(sourcePc, out GraphInstructionSource source);
                events.Add(ToJson(record, source));
            }

            long latest = mount.DebugTrace.LatestSequence;
            bool gap = oldestSequence <= latest && oldestSequence > since + 1;
            return new JsonObject
            {
                ["mount"] = MountSnapshot(mount),
                ["events"] = events,
                ["oldestSequence"] = oldestSequence,
                ["latestSequence"] = latest,
                ["droppedCount"] = mount.DebugTrace.DroppedCount,
                ["gap"] = gap,
            };
        }

        private static JsonObject MountSnapshot(TriggerGraphMountTrigger mount)
        {
            GraphExecutionCursor cursor = mount.Cursor;
            return new JsonObject
            {
                ["graphId"] = mount.GraphId,
                ["graphName"] = mount.GraphName,
                ["entryLabel"] = mount.EntryLabel,
                ["event"] = mount.EventKey.Value,
                ["domain"] = mount.Domain.ToString(),
                ["scopeEntityId"] = mount.Scope.Id,
                ["mode"] = mount.DebugTrace.Mode.ToString(),
                ["capacity"] = mount.DebugTrace.Capacity,
                ["latestSequence"] = mount.DebugTrace.LatestSequence,
                ["droppedCount"] = mount.DebugTrace.DroppedCount,
                ["cursor"] = new JsonObject
                {
                    ["pc"] = cursor.Pc,
                    ["steps"] = cursor.Steps,
                    ["callStackCount"] = cursor.CallStackCount,
                    ["status"] = cursor.Status.ToString(),
                    ["suspended"] = cursor.IsSuspended,
                },
            };
        }

        private static JsonObject ToJson(GraphDebugTraceRecord record, GraphInstructionSource source)
        {
            var result = new JsonObject
            {
                ["sequence"] = record.Sequence,
                ["event"] = record.EventKind.ToString(),
                ["sourcePc"] = record.SourcePc,
                ["cursorPc"] = record.CursorPc,
                ["steps"] = record.Steps,
                ["nodeId"] = string.IsNullOrWhiteSpace(source.NodeId) ? null : source.NodeId,
                ["op"] = string.IsNullOrWhiteSpace(source.Op) ? null : source.Op,
            };

            if (record.EventKind == GraphDebugTraceEvent.PinInt || record.EventKind == GraphDebugTraceEvent.PinBool)
            {
                result["pinIndex"] = record.RegisterIndex;
                result["value"] = record.EventKind == GraphDebugTraceEvent.PinBool ? record.IntValue != 0 : record.IntValue;
            }
            else if (record.EventKind == GraphDebugTraceEvent.PinFloat)
            {
                result["pinIndex"] = record.RegisterIndex;
                result["value"] = record.FloatValue;
            }
            else if (record.EventKind == GraphDebugTraceEvent.PinEntity || record.EventKind == GraphDebugTraceEvent.BlackboardEntity)
            {
                result["pinIndex"] = record.RegisterIndex;
                result["value"] = record.EntityValue.Id;
            }
            else if (record.EventKind is GraphDebugTraceEvent.BlackboardInt or GraphDebugTraceEvent.BlackboardFloat)
            {
                result["keyId"] = record.RegisterIndex;
                result["value"] = record.EventKind == GraphDebugTraceEvent.BlackboardInt ? record.IntValue : record.FloatValue;
            }

            return result;
        }

        private static TriggerGraphMountTrigger RequireMount(
            JsonObject? args,
            IReadOnlyList<TriggerGraphMountTrigger> mounts)
        {
            string graphId = AgentToolContext.RequireString(args, "graphId");
            string entryLabel = AgentToolContext.RequireString(args, "entryLabel");
            TriggerGraphMountTrigger? match = null;
            for (int i = 0; i < mounts.Count; i++)
            {
                TriggerGraphMountTrigger mount = mounts[i];
                if (!string.Equals(mount.GraphName, graphId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(mount.GraphId.ToString(), graphId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(mount.EntryLabel, entryLabel, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Graph debug selection '{graphId}/{entryLabel}' is ambiguous; select by a unique mounted graph name.");
                }

                match = mount;
            }

            return match ?? throw new AgentToolException(
                AgentBridgeErrorCodes.InvalidParams,
                $"Mounted TriggerGraph '{graphId}' entry '{entryLabel}' was not found. Call ludots.graph.debug action=list first.");
        }
    }
}
