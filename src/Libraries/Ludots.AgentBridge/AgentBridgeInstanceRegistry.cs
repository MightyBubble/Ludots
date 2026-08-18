using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Per-instance discovery registry. Each bridge process writes
    /// &lt;artifactsRoot&gt;/sessions/&lt;pid&gt;.json at startup with its routing
    /// identity (port, host kind, label, capabilities) and deletes it on
    /// graceful dispose. Readers enumerate the directory and probe liveness
    /// (dead pids are swept on every bridge start; clients should still probe
    /// /health before connecting). Live gameplay state is NOT stored here —
    /// query the instance's own ludots.session.info after connecting.
    /// </summary>
    public sealed class AgentBridgeInstanceIdentity
    {
        public required int Pid { get; init; }
        public required int Port { get; init; }
        public int Version { get; init; } = 1;
        public required string Host { get; init; }
        public string? Label { get; init; }
        public required string[] Capabilities { get; init; }
        public required string[] Mods { get; init; }
        public string? MapId { get; init; }
        public required DateTime StartedAtUtc { get; init; }
        public string? ProcessPath { get; init; }
    }

    public static class AgentBridgeInstanceRegistry
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

        public static string SessionsDirectory(string artifactsRoot) => Path.Combine(artifactsRoot, "sessions");

        public static string SessionFilePath(string artifactsRoot, int pid) =>
            Path.Combine(SessionsDirectory(artifactsRoot), $"{pid}.json");

        public static void Write(string artifactsRoot, AgentBridgeInstanceIdentity identity)
        {
            Directory.CreateDirectory(SessionsDirectory(artifactsRoot));
            File.WriteAllText(SessionFilePath(artifactsRoot, identity.Pid), ToJson(identity));
        }

        public static void Delete(string artifactsRoot, int pid)
        {
            try
            {
                string path = SessionFilePath(artifactsRoot, pid);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best effort */ }
        }

        /// <summary>Remove session files whose owning process no longer exists.</summary>
        public static int SweepDead(string artifactsRoot)
        {
            int removed = 0;
            string dir = SessionsDirectory(artifactsRoot);
            if (!Directory.Exists(dir)) return 0;

            foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
            {
                AgentBridgeInstanceIdentity? identity = TryRead(file);
                if (identity != null && IsAlive(identity.Pid)) continue;

                try { File.Delete(file); removed++; } catch { /* best effort */ }
            }

            return removed;
        }

        public static List<(AgentBridgeInstanceIdentity Identity, bool Alive)> List(string artifactsRoot)
        {
            var result = new List<(AgentBridgeInstanceIdentity Identity, bool Alive)>();
            string dir = SessionsDirectory(artifactsRoot);
            if (!Directory.Exists(dir)) return result;

            foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
            {
                AgentBridgeInstanceIdentity? identity = TryRead(file);
                if (identity != null)
                {
                    result.Add((identity, IsAlive(identity.Pid)));
                }
            }

            result.Sort((a, b) => a.Identity.StartedAtUtc.CompareTo(b.Identity.StartedAtUtc));
            return result;
        }

        public static bool IsAlive(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static AgentBridgeInstanceIdentity? TryRead(string path)
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject o) return null;
                return new AgentBridgeInstanceIdentity
                {
                    Pid = o["pid"]!.GetValue<int>(),
                    Port = o["port"]!.GetValue<int>(),
                    Version = o["version"]?.GetValue<int>() ?? 1,
                    Host = o["host"]?.GetValue<string>() ?? "unknown",
                    Label = o["label"]?.GetValue<string>(),
                    Capabilities = ReadStringArray(o["capabilities"]),
                    Mods = ReadStringArray(o["mods"]),
                    MapId = o["mapId"]?.GetValue<string>(),
                    StartedAtUtc = o["startedAtUtc"]?.GetValue<DateTime>() ?? DateTime.MinValue,
                    ProcessPath = o["processPath"]?.GetValue<string>(),
                };
            }
            catch
            {
                return null;
            }
        }

        private static string[] ReadStringArray(JsonNode? node) =>
            node is JsonArray array ? array.Select(e => e?.GetValue<string>() ?? string.Empty).ToArray() : Array.Empty<string>();

        private static string ToJson(AgentBridgeInstanceIdentity identity)
        {
            var o = new JsonObject
            {
                ["pid"] = identity.Pid,
                ["port"] = identity.Port,
                ["version"] = identity.Version,
                ["host"] = identity.Host,
                ["label"] = identity.Label,
                ["capabilities"] = new JsonArray(identity.Capabilities.Select(c => (JsonNode)c).ToArray()),
                ["mods"] = new JsonArray(identity.Mods.Select(m => (JsonNode)m).ToArray()),
                ["mapId"] = identity.MapId,
                ["startedAtUtc"] = identity.StartedAtUtc.ToString("O"),
                ["processPath"] = identity.ProcessPath,
            };
            return o.ToJsonString(SerializerOptions);
        }
    }
}
