using System.Text.Json.Nodes;
using Ludots.Core.Engine.Pacemaker;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// Owns the pacemaker swap used by ludots.time.control. Follows the
    /// DiagnosticsOverlayMod turn-based pattern: pause installs a
    /// TurnBasedPacemaker, resume restores the previous pacemaker.
    /// </summary>
    public sealed class AgentTimeController
    {
        private IPacemaker? _savedPacemaker;
        private TurnBasedPacemaker? _turnPacemaker;

        public bool IsPaused => _turnPacemaker != null;

        public JsonObject Control(string action, int steps, Core.Engine.GameEngine engine)
        {
            switch (action)
            {
                case "pause":
                    if (_turnPacemaker == null)
                    {
                        _savedPacemaker = engine.Pacemaker;
                        _turnPacemaker = new TurnBasedPacemaker();
                        engine.Pacemaker = _turnPacemaker;
                    }
                    break;

                case "resume":
                    if (_turnPacemaker != null)
                    {
                        engine.Pacemaker = _savedPacemaker ?? new RealtimePacemaker();
                        _turnPacemaker = null;
                        _savedPacemaker = null;
                    }
                    break;

                case "step":
                    if (_turnPacemaker == null)
                    {
                        throw new AgentToolException(
                            AgentBridgeErrorCodes.InvalidParams,
                            "step requires paused state; call pause first.");
                    }

                    if (steps <= 0 || steps > 10000)
                    {
                        throw new AgentToolException(
                            AgentBridgeErrorCodes.InvalidParams,
                            "steps must be in 1..10000.");
                    }

                    for (int i = 0; i < steps; i++)
                    {
                        _turnPacemaker.Step();
                    }

                    // Queued steps execute on the following frames, so the tick
                    // below is still pre-step; targetTick is the committed value.
                    JsonObject stepped = Status(engine);
                    stepped["targetTick"] = engine.GameSession.CurrentTick + steps;
                    return stepped;

                default:
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Unknown action '{action}'. Expected pause | resume | step.");
            }

            return Status(engine);
        }

        public JsonObject Status(Core.Engine.GameEngine engine)
        {
            return new JsonObject
            {
                ["paused"] = IsPaused,
                ["pacemaker"] = engine.Pacemaker?.GetType().Name ?? "null",
                ["tick"] = engine.GameSession.CurrentTick,
            };
        }
    }

    public sealed class TimeGetTool : IAgentTool
    {
        private readonly AgentTimeController _time;

        public TimeGetTool(AgentTimeController time) => _time = time;

        public string Name => "ludots.time.get";
        public string Description => "Current time-flow state: paused flag, pacemaker type, simulation tick. No parameters.";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context) => _time.Status(context.Engine);
    }

    public sealed class TimeControlTool : IAgentTool
    {
        private readonly AgentTimeController _time;

        public TimeControlTool(AgentTimeController time) => _time = time;

        public string Name => "ludots.time.control";
        public string Description => "Control simulation time: {action: 'pause'|'resume'|'step', steps?: int}. Pause installs a turn-based pacemaker; step queues N fixed steps (executed on following frames — response carries targetTick, verify with ludots.time.get); resume restores realtime flow.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("pause", "resume", "step") },
                ["steps"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 10000, ["description"] = "only for action=step" },
            },
            ["required"] = new JsonArray("action"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string action = AgentToolContext.RequireString(args, "action");
            int steps = AgentToolContext.OptionalInt(args, "steps", 1);
            return _time.Control(action, steps, context.Engine);
        }
    }
}
