using System.IO;
using System.Text.Json.Nodes;
using Ludots.AgentBridge;
using Ludots.AgentBridge.Tools;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Platform.Desktop;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.Gas
{
    /// <summary>
    /// Save bridge tool contracts: every tool drives the formal pipeline (SaveSlotStore over
    /// engine ISaveStorage, clean-boundary capture, WorldRestoreService restore) and the
    /// normalized world digest is stable across engine instances for identical state.
    /// </summary>
    public sealed class SaveToolsContractTests
    {
        private string _root = null!;
        private GameEngine _engine = null!;
        private AgentToolContext _context = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ludots-save-tools-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
            _engine = CreateEngine(_root);
            UseTurnBasedPacemaker(_engine);
            _context = new AgentToolContext(_engine);
        }

        [TearDown]
        public void TearDown()
        {
            _engine.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public void WriteSlotsReadRestore_RoundTripAcrossEngineInstances()
        {
            var write = new SaveWriteTool();
            JsonObject result = write.ExecuteObject(
                new JsonObject { ["name"] = "contract-a" },
                _context)!;
            Assert.That(result["slot"]!.GetValue<string>(), Is.EqualTo("manual/contract-a"));
            string savedDigest = result["worldDigest"]!.GetValue<string>();
            int savedTick = result["tick"]!.GetValue<int>();
            string path = result["path"]!.GetValue<string>();
            Assert.That(savedTick, Is.GreaterThan(0));
            Assert.That(path, Does.Contain(_root));
            Assert.That(File.Exists(path), Is.True, path);

            var slots = new SaveSlotsTool();
            JsonObject listing = slots.ExecuteObject(null, _context);
            Assert.That(listing["count"]!.GetValue<int>(), Is.GreaterThanOrEqualTo(1));
            JsonObject? entry = FindSlot(listing, "manual/contract-a");
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!["tick"]!.GetValue<int>(), Is.EqualTo(savedTick));
            Assert.That(entry["bytes"]!.GetValue<int>(), Is.GreaterThan(0));

            var read = new SaveReadTool();
            JsonObject readResult = read.ExecuteObject(
                new JsonObject { ["name"] = "contract-a" },
                _context)!;
            Assert.That(readResult["worldDigest"]!.GetValue<string>(), Is.EqualTo(savedDigest));

            using GameEngine second = CreateEngine(_root);
            var secondContext = new AgentToolContext(second);
            var restore = new SaveRestoreTool();
            JsonObject restored = restore.ExecuteObject(
                new JsonObject { ["name"] = "contract-a" },
                secondContext)!;
            Assert.That(restored["restoredTick"]!.GetValue<int>(), Is.EqualTo(savedTick));
            Assert.That(
                restored["worldDigest"]!.GetValue<string>(),
                Is.EqualTo(savedDigest),
                "normalized digest must be identical in a different engine instance after restore");
        }

        [Test]
        public void CaptureWithoutWrite_DoesNotCreateSlots()
        {
            var capture = new SaveCaptureTool();
            JsonObject result = capture.ExecuteObject(null, _context);

            Assert.That(result["tick"]!.GetValue<int>(), Is.GreaterThan(0));
            Assert.That(result["worldDigest"]!.GetValue<string>(), Is.Not.Null);
            JsonObject listing = new SaveSlotsTool().ExecuteObject(null, _context);
            Assert.That(listing["count"]!.GetValue<int>(), Is.EqualTo(0));
        }

        [Test]
        public void RestoreOfTamperedSlot_FailsWithReadableError()
        {
            new SaveWriteTool().ExecuteObject(new JsonObject { ["name"] = "tampered" }, _context);
            CorruptSlotBytes("manual/tampered");

            AgentToolException ex = Assert.Throws<AgentToolException>(() =>
                new SaveReadTool().ExecuteObject(new JsonObject { ["name"] = "tampered" }, _context));
            Assert.That(ex!.Message, Does.Contain("tampered"));
        }

        [Test]
        public void MissingStorageService_FailsClosed()
        {
            using var bare = CreateInitializedEngineWithoutStorage();
            var bareContext = new AgentToolContext(bare);

            AgentToolException ex = Assert.Throws<AgentToolException>(() =>
                new SaveSlotsTool().ExecuteObject(null, bareContext));
            Assert.That(ex!.Code, Is.EqualTo("service.unavailable"));
        }

        [Test]
        public void InvalidSlotKind_IsRejected()
        {
            AgentToolException ex = Assert.Throws<AgentToolException>(() =>
                new SaveWriteTool().ExecuteObject(
                    new JsonObject { ["name"] = "x", ["kind"] = "cloud" },
                    _context));
            Assert.That(ex!.Code, Is.EqualTo("invalid.params"));
        }

        private static JsonObject? FindSlot(JsonObject listing, string slot)
        {
            foreach (JsonNode? node in listing["slots"]!.AsArray())
            {
                if (string.Equals(node!["slot"]!.GetValue<string>(), slot, StringComparison.Ordinal))
                {
                    return (JsonObject)node!;
                }
            }

            return null;
        }

        private void CorruptSlotBytes(string slot)
        {
            string path = Path.Combine(_root, "saves", slot.Replace('/', Path.DirectorySeparatorChar) + ".ldsave");
            byte[] bytes = File.ReadAllBytes(path);
            bytes[^3] ^= 0xFF;
            File.WriteAllBytes(path, bytes);
        }

        private static GameEngine CreateEngine(string storageRoot)
        {
            GameEngine engine = CreateInitializedEngineWithoutStorage();
            engine.SetService(
                CoreServiceKeys.SaveStorage,
                (ISaveStorage)new DesktopSaveStorage(storageRoot));
            return engine;
        }

        private static void UseTurnBasedPacemaker(GameEngine engine)
        {
            engine.Pacemaker = new Ludots.Core.Engine.Pacemaker.TurnBasedPacemaker();
            engine.SimulationBudgetMsPerFrame = int.MaxValue;
            engine.SimulationMaxSlicesPerLogicFrame = 1000;
            engine.Start();
            ((Ludots.Core.Engine.Pacemaker.TurnBasedPacemaker)engine.Pacemaker).Step();
            engine.Tick(1f);
        }

        private static GameEngine CreateInitializedEngineWithoutStorage()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string gitPath = Path.Combine(dir, ".git");
                if ((Directory.Exists(gitPath) || File.Exists(gitPath)) &&
                    Directory.Exists(Path.Combine(dir, "src")) &&
                    Directory.Exists(Path.Combine(dir, "mods")))
                {
                    break;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(dir!, new[] { "LudotsCoreMod" }),
                Path.Combine(dir!, "assets"));
            engine.LoadStartupMap();
            return engine;
        }
    }

    internal static class SaveToolTestExtensions
    {
        internal static JsonObject ExecuteObject(this IAgentTool tool, JsonObject? args, AgentToolContext? context = null)
        {
            return (tool.Execute(args, context) as JsonObject)!;
        }
    }
}
