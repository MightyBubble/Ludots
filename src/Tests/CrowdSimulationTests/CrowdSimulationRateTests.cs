using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Structures;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>频率换算、寿命 tick 与对拍元数据。带 mapId 的对拍 JSON 必须 fixedHz = 30；若有 fnv1a 则必须是 8 位十六进制。不比对摘要是否与某次导出逐字相同。文件数锁 29。</summary>
[TestFixture]
public class CrowdSimulationRateTests
{
    [Test]
    public void PeriodRounding_MatchesReference_AndRejectsRatesAboveTheStep()
    {
        Assert.That(CrowdFog.PeriodTicks(30, 4), Is.EqualTo(8));
        Assert.That(CrowdMovementKernel.AvoidanceStride(30, 15), Is.EqualTo(2));
        Assert.That(CrowdMovementKernel.AvoidanceStride(30, 20.6), Is.EqualTo(1));

        var fog = Assert.Throws<InvalidOperationException>(() => CrowdFog.PeriodTicks(30, 31));
        Assert.That(fog!.Message, Does.Contain("fog.rateHz"));
        Assert.That(fog.Message, Does.Contain("超过步进频率"));

        var avoid = Assert.Throws<InvalidOperationException>(() => CrowdMovementKernel.AvoidanceStride(30, 31));
        Assert.That(avoid!.Message, Does.Contain("avoidance.rateHz"));
        Assert.That(avoid.Message, Does.Contain("超过步进频率"));
    }

    [Test]
    public void LifetimeTicks_UseExactRationalCeiling()
    {
        Assert.That(CrowdStructuresStore.LifetimeTicks(4, 30, 10), Is.EqualTo(12));
        Assert.That(CrowdStructuresStore.LifetimeTicks(900, 30, 10), Is.EqualTo(2700));
        Assert.That(CrowdStructuresStore.LifetimeTicks(900, 39, 10), Is.EqualTo(3510));
    }

    [Test]
    public void ParityTruth_RecordsFixedHz_WithoutTouchingDigest()
    {
        string repoRoot = FindRepoRoot();
        string showcase = Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation");
        int seen = 0;
        foreach (string file in Directory.EnumerateFiles(showcase, "*.json", SearchOption.AllDirectories))
        {
            if (!file.Contains($"{Path.DirectorySeparatorChar}parity{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            if (root?["mapId"] == null)
            {
                continue;
            }

            seen++;
            Assert.That(root["fixedHz"]?.GetValue<int>(), Is.EqualTo(TestDefaults.FixedHz), file);
            if (root["fnv1a"] != null)
            {
                Assert.That(root["fnv1a"]!.GetValue<string>(), Does.Match("^[0-9a-f]{8}$"), file);
            }
        }

        Assert.That(seen, Is.EqualTo(29));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
