using System.Globalization;
using System.Text.Json.Nodes;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Shared bridge address resolution for CLI / MCP / Inspector backends.
    /// Order: explicit URL → env URL → discovery path/env → default loopback port.
    /// </summary>
    public static class AgentBridgeEndpoint
    {
        public const string UrlEnvVar = "LUDOTS_AGENT_BRIDGE_URL";
        public const string DiscoveryEnvVar = "LUDOTS_AGENT_BRIDGE_DISCOVERY";
        public const string DefaultBaseUrl = "http://127.0.0.1:47921";

        public static string Resolve(string? explicitUrl = null, string? discoveryPath = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitUrl))
            {
                return Normalize(explicitUrl);
            }

            string? envUrl = Environment.GetEnvironmentVariable(UrlEnvVar);
            if (!string.IsNullOrWhiteSpace(envUrl))
            {
                return Normalize(envUrl);
            }

            string? discovery = !string.IsNullOrWhiteSpace(discoveryPath)
                ? discoveryPath
                : Environment.GetEnvironmentVariable(DiscoveryEnvVar);
            if (!string.IsNullOrWhiteSpace(discovery))
            {
                string? fromDiscovery = ResolveFromDiscovery(discovery);
                if (fromDiscovery != null)
                {
                    return fromDiscovery;
                }

                throw new InvalidOperationException(
                    $"Agent bridge discovery path '{discovery}' did not yield a live session. " +
                    "Pass an explicit URL or start a game with AgentBridgeMod.");
            }

            return DefaultBaseUrl;
        }

        public static string? ResolveFromDiscovery(string path)
        {
            if (File.Exists(path))
            {
                return ReadBaseUrl(File.ReadAllText(path));
            }

            if (!Directory.Exists(path))
            {
                return null;
            }

            string sessionsDir = Path.Combine(path, "sessions");
            string[] candidates = Directory.Exists(sessionsDir)
                ? Directory.GetFiles(sessionsDir, "*.json")
                : Directory.GetFiles(path, "*.json");

            string? best = null;
            DateTime bestStart = DateTime.MinValue;
            foreach (string file in candidates)
            {
                JsonObject? session;
                try
                {
                    session = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Unreadable agent-bridge discovery file '{file}': {ex.Message}", ex);
                }

                string? started = session?["startedAtUtc"]?.GetValue<string>();
                if (started == null
                    || !DateTime.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime when)
                    || when <= bestStart)
                {
                    continue;
                }

                string? url = ReadBaseUrl(session);
                if (url == null)
                {
                    throw new InvalidOperationException(
                        $"Discovery file '{file}' is missing a numeric 'port' field.");
                }

                bestStart = when;
                best = url;
            }

            return best;
        }

        private static string? ReadBaseUrl(string json)
        {
            JsonObject? session;
            try
            {
                session = JsonNode.Parse(json) as JsonObject;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid discovery JSON: {ex.Message}", ex);
            }

            return ReadBaseUrl(session);
        }

        private static string? ReadBaseUrl(JsonObject? session)
        {
            if (session?["port"] is JsonValue port && port.TryGetValue(out int p))
            {
                return $"http://127.0.0.1:{p}";
            }

            return null;
        }

        private static string Normalize(string url)
        {
            string trimmed = url.Trim().TrimEnd('/');
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Invalid agent bridge URL '{url}'. Expected absolute http(s) URL.");
            }

            return trimmed;
        }
    }
}
