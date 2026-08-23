using System.Text.Json.Nodes;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Game-thread execution context handed to every tool. Thin facade over
    /// <see cref="GameEngine"/> service access with explicit-failure helpers.
    /// </summary>
    public sealed class AgentToolContext
    {
        public AgentToolContext(GameEngine engine)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public GameEngine Engine { get; }

        public static int SolePlayerId(GameEngine engine)
        {
            if (!Ludots.Core.Client.ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out _) ||
                !Ludots.Core.Client.ClientLocalSeatAccess.RequireRegistry(engine).TryGetSoleSeat(out var seat) ||
                seat.PossessedPlayerId <= 0)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    "No sole local player is explicitly bound for this session.");
            }

            return seat.PossessedPlayerId;
        }

        /// <summary>
        /// Resolves a runtime entity id to a live Arch entity by scanning.
        /// Throws entity.not_found when no alive entity matches.
        /// </summary>
        public Arch.Core.Entity ResolveEntity(int entityId)
        {
            if (entityId <= 0)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, "entityId must be positive.");
            }

            Arch.Core.Entity found = default;
            bool matched = false;
            var query = new Arch.Core.QueryDescription();
            Engine.World.Query(in query, (Arch.Core.Entity e) =>
            {
                if (!matched && e.Id == entityId)
                {
                    found = e;
                    matched = true;
                }
            });

            if (!matched || !Engine.World.IsAlive(found))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.EntityNotFound,
                    $"Entity {entityId} does not exist or is not alive.");
            }

            return found;
        }

        public bool TryGetService<T>(ServiceKey<T> key, out T value) => Engine.TryGetService(key, out value);

        public T RequireService<T>(ServiceKey<T> key)
        {
            if (!Engine.TryGetService(key, out T value) || value is null)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    $"Required service '{key.Name}' is not available in this runtime.");
            }

            return value;
        }

        public static string? OptionalString(JsonObject? args, string name)
        {
            if (args == null) return null;
            JsonNode? node = args[name];
            return node is JsonValue value && value.TryGetValue(out string? s) ? s : null;
        }

        public static int OptionalInt(JsonObject? args, string name, int defaultValue)
        {
            if (args == null) return defaultValue;
            JsonNode? node = args[name];
            return node is JsonValue value && value.TryGetValue(out int i) ? i : defaultValue;
        }

        public static bool OptionalBool(JsonObject? args, string name, bool defaultValue)
        {
            if (args == null) return defaultValue;
            JsonNode? node = args[name];
            return node is JsonValue value && value.TryGetValue(out bool b) ? b : defaultValue;
        }

        public static int RequireInt(JsonObject? args, string name)
        {
            JsonNode? node = args?[name];
            if (node is JsonValue value && value.TryGetValue(out int i)) return i;
            throw new AgentToolException(
                AgentBridgeErrorCodes.InvalidParams,
                $"Parameter '{name}' (integer) is required.");
        }

        public static string RequireString(JsonObject? args, string name)
        {
            JsonNode? node = args?[name];
            if (node is JsonValue value && value.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)) return s;
            throw new AgentToolException(
                AgentBridgeErrorCodes.InvalidParams,
                $"Parameter '{name}' (string) is required.");
        }
    }
}
