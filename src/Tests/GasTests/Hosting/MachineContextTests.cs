using Ludots.Platform.Abstractions.Hosting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Hosting
{
    [TestFixture]
    public sealed class MachineContextTests
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ludots-machinetests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static void WriteDiscoveryFile(string directory, string fileName, int pid, int port)
        {
            // 与 AgentBridgeHttpServer.WriteDiscoveryFile 相同的字段形状。
            File.WriteAllText(
                Path.Combine(directory, fileName),
                $"{{\"pid\":{pid},\"port\":{port},\"version\":1,\"startedAtUtc\":\"2026-08-24T00:00:00.0000000Z\",\"processPath\":\"test\"}}");
        }

        [Test]
        public void CreateIsolated_CreatesIndependentDirectoryPerMachine()
        {
            MachineContext machineA = MachineContext.CreateIsolated("machine-a", _root);
            MachineContext machineB = MachineContext.CreateIsolated("machine-b", _root);

            Assert.Multiple(() =>
            {
                Assert.That(machineA.MachineId, Is.EqualTo("machine-a"));
                Assert.That(machineB.MachineId, Is.EqualTo("machine-b"));
                Assert.That(machineA.DiscoveryDirectory, Is.Not.EqualTo(machineB.DiscoveryDirectory));
                Assert.That(Directory.Exists(machineA.DiscoveryDirectory), Is.True);
                Assert.That(Directory.Exists(machineB.DiscoveryDirectory), Is.True);
                Assert.That(machineA.DiscoveryDirectory, Does.StartWith(Path.GetFullPath(_root)));
                Assert.That(machineB.DiscoveryDirectory, Does.StartWith(Path.GetFullPath(_root)));
            });
        }

        [Test]
        public void CreateIsolated_SanitizesMachineIdIntoDirectorySegment()
        {
            MachineContext machine = MachineContext.CreateIsolated("ci/shard:1", _root);

            Assert.Multiple(() =>
            {
                Assert.That(machine.MachineId, Is.EqualTo("ci/shard:1"));
                Assert.That(Directory.Exists(machine.DiscoveryDirectory), Is.True);
                Assert.That(Path.GetDirectoryName(machine.DiscoveryDirectory), Is.EqualTo(Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar)));
            });
        }

        [Test]
        public void GetDiscoveredProcesses_ParsesAgentBridgeDiscoveryFile()
        {
            MachineContext machine = MachineContext.CreateIsolated("parser", _root);
            WriteDiscoveryFile(machine.DiscoveryDirectory, "session.json", pid: 4242, port: 47921);
            string discoveryFile = Path.Combine(machine.DiscoveryDirectory, "session.json");
            var expectedWriteTime = new DateTimeOffset(File.GetLastWriteTimeUtc(discoveryFile));

            IReadOnlyList<DiscoveredProcess> processes = machine.GetDiscoveredProcesses();

            Assert.That(processes, Has.Count.EqualTo(1));
            DiscoveredProcess process = processes[0];
            Assert.Multiple(() =>
            {
                Assert.That(process.ProcessId, Is.EqualTo(4242));
                Assert.That(process.BridgePort, Is.EqualTo(47921));
                Assert.That(process.DiscoveryFilePath, Is.EqualTo(discoveryFile));
                Assert.That(process.LastWriteTimeUtc, Is.EqualTo(expectedWriteTime));
            });
        }

        [Test]
        public void GetDiscoveredProcesses_SkipsMalformedAndUnrelatedFiles()
        {
            MachineContext machine = MachineContext.CreateIsolated("skips", _root);
            File.WriteAllText(Path.Combine(machine.DiscoveryDirectory, "broken.json"), "{ not json");
            File.WriteAllText(Path.Combine(machine.DiscoveryDirectory, "no-port.json"), "{\"pid\":1}");
            WriteDiscoveryFile(machine.DiscoveryDirectory, "keep.json", pid: 77, port: 5000);

            IReadOnlyList<DiscoveredProcess> processes = machine.GetDiscoveredProcesses();

            Assert.That(processes, Has.Count.EqualTo(1));
            Assert.That(processes[0].ProcessId, Is.EqualTo(77));
            Assert.That(processes[0].BridgePort, Is.EqualTo(5000));
        }

        [Test]
        public void GetDiscoveredProcesses_MissingDiscoveryDirectory_ReturnsEmpty()
        {
            var machine = new MachineContext("ghost", Path.Combine(_root, "never-created"));

            Assert.That(machine.GetDiscoveredProcesses(), Is.Empty);
        }

        [Test]
        public void IsolatedMachines_DoNotSeeEachOthersProcesses()
        {
            MachineContext machineA = MachineContext.CreateIsolated("iso-a", _root);
            MachineContext machineB = MachineContext.CreateIsolated("iso-b", _root);
            WriteDiscoveryFile(machineA.DiscoveryDirectory, "session.json", pid: 111, port: 48001);
            WriteDiscoveryFile(machineA.DiscoveryDirectory, "second.json", pid: 222, port: 48002);

            Assert.Multiple(() =>
            {
                Assert.That(machineA.GetDiscoveredProcesses(), Has.Count.EqualTo(2));
                Assert.That(machineB.GetDiscoveredProcesses(), Is.Empty);
            });
        }
    }
}
