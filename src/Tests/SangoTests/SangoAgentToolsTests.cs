using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Ludots.AgentBridge;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Sango.Tests
{
    /// <summary>
    /// T0.8(#1841/#1847):原 AgentBridge events.fire 取证事件键改 ludots.sango.*
    /// 四个 IAgentTool。验收面:真实 AgentToolRegistry 注册后 DescribeAll(即
    /// ludots.tools.list 的数据面)可见四工具与 InputSchema;可在无引擎上下文执行
    /// 的两个工具(播种/journal 报告)给出结构化返回。step_turns 依赖引擎 Manual
    /// 时钟,其行为由 SangoKernelBootTests 的时钟链用例间接覆盖,这里只验 schema。
    /// </summary>
    [TestFixture]
    public sealed class SangoAgentToolsTests
    {
        private static string RepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate Ludots repo root (showcase.registry.json).");
        }

        private static Assembly LoadSangoSimMod()
        {
            string dll = Path.Combine(
                RepoRoot(), "mods", "sango", "SangoSimMod", "bin", "net9.0", "SangoSimMod.dll");
            Assert.That(File.Exists(dll), Is.True, $"SangoSimMod build output missing: {dll} (run dotnet build first)");
            return Assembly.LoadFrom(dll);
        }

        private static IVirtualFileSystem NewVfs()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("SangoContentMod", Path.Combine(RepoRoot(), "mods", "sango", "SangoContentMod"));
            return vfs;
        }

        private static IAgentTool NewTool(Assembly sim, string typeName, params object?[] args)
        {
            Type type = sim.GetType($"Sango.Runtime.{typeName}", throwOnError: true)!;
            return (IAgentTool)Activator.CreateInstance(type, args)!;
        }

        [Test]
        public void AgentTools_RegisterIntoBridgeRegistry_DescribeAllListsFourToolsWithSchema()
        {
            Assembly sim = LoadSangoSimMod();
            IVirtualFileSystem vfs = NewVfs();
            var registry = new AgentToolRegistry();
            IAgentTool[] tools =
            {
                NewTool(sim, "SangoSeedTroopsTool", vfs),
                NewTool(sim, "SangoSeedBattleTool", vfs),
                NewTool(sim, "SangoStepTurnsTool"),
                NewTool(sim, "SangoJournalReportTool"),
            };
            foreach (IAgentTool tool in tools)
            {
                registry.Register(tool);
            }

            string[] listed = registry.DescribeAll().Select(node => (string)node!["name"]!).ToArray();
            Assert.That(listed, Is.EquivalentTo(new[]
            {
                "ludots.sango.seed_troops",
                "ludots.sango.seed_battle",
                "ludots.sango.step_turns",
                "ludots.sango.journal_report",
            }));

            IAgentTool stepTurns = tools.Single(t => t.Name == "ludots.sango.step_turns");
            Assert.That(stepTurns.InputSchema, Is.Not.Null, "step_turns must carry an InputSchema (count no longer encoded in the key name)");
            Assert.That((int)stepTurns.InputSchema!["properties"]!["count"]!["maximum"]!, Is.EqualTo(99));
            Assert.That((int)stepTurns.InputSchema["properties"]["count"]!["minimum"]!, Is.EqualTo(1));
        }

        [Test]
        public void SeedTroops_And_JournalReport_ExecuteWithStructuredReturns()
        {
            Assembly sim = LoadSangoSimMod();
            IVirtualFileSystem vfs = NewVfs();
            IAgentTool seedTroops = NewTool(sim, "SangoSeedTroopsTool", vfs);
            IAgentTool journal = NewTool(sim, "SangoJournalReportTool");

            // 播种工具自举内核(合成图),推进一回合后编成;返回结构化计数而非只落日志。
            System.Text.Json.Nodes.JsonNode? seeded = seedTroops.Execute(null, null!);
            Assert.That(seeded, Is.Not.Null);
            Assert.That((int)seeded!["budget"]!, Is.EqualTo(8));
            Assert.That(seeded["created"]!, Is.Not.Null);
            Assert.That(seeded["rejected"]!, Is.Not.Null);

            // 播种链经过 SangoTurnDriver/journal,报告工具给出非空形状。
            System.Text.Json.Nodes.JsonNode? report = journal.Execute(null, null!);
            Assert.That(report, Is.Not.Null);
            Assert.That((int)report!["total"]!, Is.GreaterThan(0), "seeded turns must have entered the command journal");
            Assert.That(report["byKind"]!, Is.Not.Null);
            Assert.That(report["head"]!, Is.Not.Null);
        }
    }
}
