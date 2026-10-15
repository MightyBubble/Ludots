using System;
using Ludots.Core.CrowdSimulation;
using CrowdSimulationTests.Parity;
using NUnit.Framework;

namespace CrowdSimulationTests.Uat;

/// <summary>跳跃候选的端点不落在桥面上：桥跨本身和两端各 portalReachNavCells − 1 格的岸上延伸段，按桥面全宽算。</summary>
[TestFixture]
public sealed class BankJumpUatTests
{
    private const byte Span = 1;
    private const byte Bank = 2;

    [Test]
    public void NoJumpEndpoint_OnBridgeSpanOrBankExtension([ValueSource(typeof(S4DeployTruthTests), nameof(S4DeployTruthTests.Seeds))] string seed)
    {
        var runtime = S1SurfaceTruthTests.LoadRuntime(seed);
        var bridges = S1SurfaceTruthTests.LoadMapSurface(seed).LayeredPaths;
        var jumps = S1SurfaceTruthTests.ReadSurface(seed).JumpCandidates;
        int n = runtime.NavCellsPerSide, cs = runtime.NavCellSizeCm;
        Assert.That(bridges.Count, Is.GreaterThan(0), "场景无效：世界无桥");
        Assert.That(jumps.Length, Is.GreaterThan(0), "场景无效：世界无 jump 候选");

        var zone = new byte[n * n];
        int ext = 0;
        foreach (var bridge in bridges)
        {
            var template = runtime.Structures.Templates[runtime.Structures.TemplateIndexOf(bridge.TemplateId)];
            ext = template.PortalReachNavCells - 1;
            Assert.That(ext, Is.GreaterThanOrEqualTo(1), $"场景无效：{template.Id} 的 portalReachNavCells − 1 < 1，无岸上延伸段");
            var s = SpanCells(bridge.Span, cs, n, ext);
            for (int t = -ext; t <= s.Len + ext; t++)
            {
                for (int o = 0; o < s.W; o++)
                {
                    int x = s.X + s.Dx * t + s.Dy * (s.Low + o);
                    int y = s.Y + s.Dy * t + s.Dx * (s.Low + o);
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    byte kind = t >= 0 && t <= s.Len ? Span : Bank;
                    if (kind > zone[y * n + x]) zone[y * n + x] = kind;
                }
            }
        }

        int span = 0, bank = 0;
        foreach (var jump in jumps)
        {
            foreach (int c in new[] { jump.FromY * n + jump.FromX, jump.ToY * n + jump.ToX })
            {
                if (zone[c] == Span) span++;
                else if (zone[c] == Bank) bank++;
            }
        }

        TestContext.Out.WriteLine($"[uat-bankjump] {seed} bridges={bridges.Count} jumps={jumps.Length} onSpan={span} onBank={bank} ext={ext}");
        Assert.That(span, Is.EqualTo(0), $"{span} 个 jump 端点落在桥跨上");
        Assert.That(bank, Is.EqualTo(0), $"{bank} 个 jump 端点落在桥端岸上延伸段（portalReachNavCells−1={ext} 格）");
    }

    /// <summary>
    /// 地图桥实体的端点是桥跨两端的格心各往岸上延伸 ext 格；偶数宽的中线落在格边上。
    /// 这里反推桥跨：起锚格 (X, Y)、轴向 (Dx, Dy)、跨长 Len、横向起始偏移 Low 与宽 W，均为导航格。
    /// </summary>
    private static (int X, int Y, int Dx, int Dy, int Len, int Low, int W) SpanCells(
        CrowdSimulationPathSpan span, int cs, int n, int ext)
    {
        int dx = Math.Sign(span.ToXCm - span.FromXCm), dy = Math.Sign(span.ToYCm - span.FromYCm);
        if (dx < 0 || dy < 0 || dx + dy != 1)
        {
            throw new InvalidOperationException($"场景无效：桥 ({span.FromXCm},{span.FromYCm})→({span.ToXCm},{span.ToYCm}) 不是沿 +x 或 +y 的轴向桥。");
        }

        int along0 = dx == 1 ? span.FromXCm : span.FromYCm, along1 = dx == 1 ? span.ToXCm : span.ToYCm;
        int across = dx == 1 ? span.FromYCm : span.FromXCm;
        int w = Exact(span.WidthCm, cs, "宽");
        int start = Exact(along0 - cs / 2, cs, "起端") + ext;
        int len = Exact(along1 - along0, cs, "长") - 2 * ext;
        int low = Exact(2 * across - w * cs, 2 * cs, "横向");
        if (start - ext < 0 || start + len + ext >= n || len < 1)
        {
            throw new InvalidOperationException($"场景无效：桥起锚 {start} 跨长 {len} 超出 {n} 格。");
        }

        return dx == 1 ? (start, 0, 1, 0, len, low, w) : (0, start, 0, 1, len, low, w);
    }

    private static int Exact(int cm, int unit, string what)
    {
        if (cm % unit != 0) throw new InvalidOperationException($"场景无效：桥的{what} {cm} 厘米不是 {unit} 的整数倍。");
        return cm / unit;
    }
}
