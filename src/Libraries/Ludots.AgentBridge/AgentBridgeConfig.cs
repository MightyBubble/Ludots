using System.Diagnostics;

namespace Ludots.AgentBridge
{
    public sealed class AgentBridgeConfig
    {
        public const string EnableEnvVar = "LUDOTS_AGENT_BRIDGE";
        public const string PortEnvVar = "LUDOTS_AGENT_BRIDGE_PORT";
        public const string LabelEnvVar = "LUDOTS_AGENT_BRIDGE_LABEL";
        public const string HostEnvVar = "LUDOTS_AGENT_BRIDGE_HOST";
        public const int DefaultPort = 47921;
        public const int MaxPortProbes = 16;

        public bool Enabled { get; set; } = true;
        public int RequestedPort { get; set; } = DefaultPort;
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Human/agent-facing instance name from LUDOTS_AGENT_BRIDGE_LABEL (e.g. "editor", "headless-sim-3").</summary>
        public string? Label { get; set; }

        public static AgentBridgeConfig FromEnvironment()
        {
            var config = new AgentBridgeConfig();

            string? enabled = Environment.GetEnvironmentVariable(EnableEnvVar);
            if (!string.IsNullOrWhiteSpace(enabled) &&
                (string.Equals(enabled, "0", StringComparison.Ordinal) ||
                 string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(enabled, "off", StringComparison.OrdinalIgnoreCase)))
            {
                config.Enabled = false;
            }

            string? port = Environment.GetEnvironmentVariable(PortEnvVar);
            if (!string.IsNullOrWhiteSpace(port))
            {
                if (!int.TryParse(port, out int parsed) || parsed <= 0 || parsed > 65535)
                {
                    throw new InvalidOperationException(
                        $"{PortEnvVar} must be an integer in 1..65535, got '{port}'.");
                }

                config.RequestedPort = parsed;
            }

            string? label = Environment.GetEnvironmentVariable(LabelEnvVar);
            if (!string.IsNullOrWhiteSpace(label))
            {
                config.Label = label.Trim();
            }

            return config;
        }

        /// <summary>
        /// Locates the repository root by walking up from the app base directory
        /// looking for global.json; falls back to the base directory itself.
        /// </summary>
        public static string ResolveDiscoveryDirectory(string baseDirectory)
        {
            var dir = new DirectoryInfo(baseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "global.json")))
                {
                    return Path.Combine(dir.FullName, "artifacts", "agent-bridge");
                }

                dir = dir.Parent;
            }

            return Path.Combine(baseDirectory, "artifacts", "agent-bridge");
        }
    }
}
