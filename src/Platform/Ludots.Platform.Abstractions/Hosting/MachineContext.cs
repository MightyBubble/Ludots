using System.Text.Json;

namespace Ludots.Platform.Abstractions.Hosting
{
    /// <summary>
    /// 一台机器（实机或 CI 隔离实例）的运行上下文，对应一个 AgentBridge discovery
    /// 目录：目录内每个 discovery JSON 文件描述一个已发现的 App 进程（pid + port）。
    /// 部署层概念，自包含，不依赖引擎初始化。
    /// </summary>
    public sealed class MachineContext
    {
        public string MachineId { get; }

        public string DiscoveryDirectory { get; }

        public MachineContext(string machineId, string discoveryDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
            ArgumentException.ThrowIfNullOrWhiteSpace(discoveryDirectory);
            MachineId = machineId;
            DiscoveryDirectory = Path.GetFullPath(discoveryDirectory);
        }

        public IReadOnlyList<DiscoveredProcess> GetDiscoveredProcesses()
        {
            if (!Directory.Exists(DiscoveryDirectory))
            {
                return Array.Empty<DiscoveredProcess>();
            }

            var processes = new List<DiscoveredProcess>();
            foreach (string filePath in Directory.EnumerateFiles(DiscoveryDirectory, "*.json"))
            {
                DiscoveredProcess process;
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("pid", out JsonElement pidNode) ||
                        !root.TryGetProperty("port", out JsonElement portNode) ||
                        pidNode.ValueKind != JsonValueKind.Number ||
                        portNode.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    process = new DiscoveredProcess(
                        pidNode.GetInt32(),
                        portNode.GetInt32(),
                        filePath,
                        new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)));
                }
                catch (IOException)
                {
                    // Discovery 文件可能正被写入或在枚举后被删除；跳过本次快照。
                    continue;
                }
                catch (JsonException)
                {
                    continue;
                }

                processes.Add(process);
            }

            processes.Sort(static (left, right) => string.Compare(left.DiscoveryFilePath, right.DiscoveryFilePath, StringComparison.Ordinal));
            return processes;
        }

        /// <summary>
        /// 创建一台隔离机器：在 rootDirectory 下按 machineId 建独立 discovery 目录，
        /// CI 并行验收的各组进程互不见对方的 discovery 文件。
        /// </summary>
        public static MachineContext CreateIsolated(string machineId, string rootDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(machineId);
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

            string directory = Path.Combine(Path.GetFullPath(rootDirectory), ToDirectorySegment(machineId));
            Directory.CreateDirectory(directory);
            return new MachineContext(machineId, directory);
        }

        private static string ToDirectorySegment(string machineId)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string segment = new string(machineId.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            segment = segment.TrimEnd(' ', '.');
            return segment.Length == 0 || segment == "." || segment == ".." ? "machine" : segment;
        }
    }

    /// <summary>一个从 discovery 文件解析出的 App 进程。</summary>
    public readonly record struct DiscoveredProcess(
        int ProcessId,
        int BridgePort,
        string DiscoveryFilePath,
        DateTimeOffset LastWriteTimeUtc);
}
