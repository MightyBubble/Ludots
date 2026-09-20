using System.Text.Json.Nodes;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Stable machine-readable error codes carried in JSON-RPC error data.code.
    /// </summary>
    public static class AgentBridgeErrorCodes
    {
        public const string InvalidParams = "invalid.params";
        public const string ServiceUnavailable = "service.unavailable";
        public const string EntityNotFound = "entity.not_found";
        public const string CapabilityUnavailable = "capability.unavailable";
        public const string ToolFailed = "tool.failed";
        public const string Timeout = "bridge.timeout";
    }

    public sealed class AgentToolException : Exception
    {
        public AgentToolException(string code, string message, JsonObject? data = null)
            : base(message)
        {
            Code = code;
            Data = data;
        }

        public string Code { get; }

        /// <summary>Structured diagnostics merged into the JSON-RPC error.data object.</summary>
        public JsonObject? Data { get; }
    }
}
