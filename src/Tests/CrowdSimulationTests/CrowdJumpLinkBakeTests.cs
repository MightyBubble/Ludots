using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Tool;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public sealed class CrowdJumpLinkBakeTests
{
    [Test]
    public void S1Seeds_MatchStoredJumpCandidates()
    {
        string repo = RepoRoot();
        (string Name, string ModId, int Seed)[] seeds =
        [
            ("s1337", "CrowdSimulationS1Terrain1337Mod", 1337),
            ("s2024", "CrowdSimulationS1Terrain2024Mod", 2024),
            ("s7", "CrowdSimulationS1Terrain7Mod", 7),
        ];

        var failures = new StringBuilder();
        Assert.Multiple(() =>
        {
            foreach ((string name, string modId, int seed) in seeds)
            {
                string mapId = "crowd_simulation_" + name;
                NavMeshBakeConfigContext ctx = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repo, modId);
                float shortestCm = float.PositiveInfinity;
                for (int i = 0; i < ctx.AgentProfiles.Count; i++)
                {
                    shortestCm = Math.Min(shortestCm, ctx.AgentProfiles[i].HeightCm);
                }

                string modAssets = Path.Combine(repo, "mods", "showcases", "crowd_simulation", modId, "assets");
                string surfacePath = Path.Combine(modAssets, "terrain", mapId + ".navsurface");
                NavSurfaceAsset fixture;
                using (FileStream input = File.OpenRead(surfacePath))
                {
                    fixture = NavSurfaceAsset.Read(input);
                }

                string logicTerrain = ctx.Config.Maps[mapId].LogicTerrainAsset;
                LogicTerrainField terrain = ReactGridTerrainBinary.Read(
                    Path.Combine(modAssets, logicTerrain),
                    fixture.NavCellSizeCm);
                NavSurfaceAsset baked = CrowdNavSurfaceAuthoring.BakeAuthored(repo, modId, mapId, terrain);

                string pairs = ComparePairs(name, baked.JumpCandidates, fixture.JumpCandidates, out int dropMax, out int dropDiffs);
                TestContext.Out.WriteLine(
                    $"seed={seed} baked={baked.JumpCandidates.Length} stored={fixture.JumpCandidates.Length} dropMaxCm={dropMax} dropDiffs={dropDiffs} shortestHeightCm={shortestCm.ToString(CultureInfo.InvariantCulture)}");
                if (pairs.Length > 0)
                {
                    failures.AppendLine(pairs);
                }

                if (dropMax > shortestCm)
                {
                    failures.AppendLine($"seed={seed} 落差最大差值 {dropMax} cm，超过最矮机动档案 heightCm {shortestCm.ToString(CultureInfo.InvariantCulture)}。");
                }

                foreach (NavMeshAgentProfileConfig profile in ctx.Config.Profiles)
                {
                    if (profile.Jump == null)
                    {
                        continue;
                    }

                    string links = CompareLinks(name, profile, baked.JumpCandidates, fixture.JumpCandidates);
                    TestContext.Out.WriteLine($"seed={seed} profile={profile.Id} {links.Split('\n')[0]}");
                    if (links.Contains("不一致", StringComparison.Ordinal))
                    {
                        failures.AppendLine(links);
                    }
                }
            }
        });

        if (failures.Length > 0)
        {
            Assert.Fail(failures.ToString());
        }
    }

    [Test]
    public void CliffBand_GainsJumpCandidates_LandRemovesThem()
    {
        const int cells = 16;
        const int cellCm = 100;
        var terrain = new SparseGridLogicTerrainField(cells, cells, cellCm, chunkSizeCells: 64);
        for (int y = 0; y < cells; y++)
        {
            terrain.SetCell(7, y, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None, areaId: 1));
            terrain.SetCell(8, y, new LogicTerrainCell(0, 0, LogicTerrainSurfaceFlags.None, areaId: 1));
        }

        NavSurfaceAsset withCliff = BakeFlat(terrain, cellCm);
        int crossing = 0;
        foreach (NavSurfaceJumpCandidate jump in withCliff.JumpCandidates)
        {
            int left = Math.Min(jump.FromX, jump.ToX);
            int right = Math.Max(jump.FromX, jump.ToX);
            if (left <= 6 && right >= 9)
            {
                crossing++;
            }
        }

        TestContext.Out.WriteLine($"cliffBand candidates={withCliff.JumpCandidates.Length} crossing={crossing}");
        Assert.That(crossing, Is.GreaterThan(0));

        for (int y = 0; y < cells; y++)
        {
            terrain.SetCell(7, y, default);
            terrain.SetCell(8, y, default);
        }

        NavSurfaceAsset land = BakeFlat(terrain, cellCm);
        TestContext.Out.WriteLine($"land candidates={land.JumpCandidates.Length}");
        Assert.That(land.JumpCandidates, Is.Empty);
    }

    [Test]
    public void JumpLinkBake_UnknownTerrain_IsRejected()
    {
        JsonObject navmesh = MapWithJump("""
            "jumpLinkBake": {
              "maxGapCm": 600,
              "linkSpacingCm": 500,
              "endpointMaxSlopeDeg": 22,
              "endpointTerrain": ["land", "marsh"],
              "gapTerrain": ["cliff"]
            }
            """);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.Parse(navmesh, Bodies()))!;
        Assert.That(ex.Message, Does.Contain("maps.flat.jumpLinkBake.endpointTerrain"));
        Assert.That(ex.Message, Does.Contain("marsh"));
        Assert.That(ex.Message, Does.Contain("terrainAreas"));
    }

    [Test]
    public void JumpLinkBake_GapNotDivisibleByCell_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdJumpCandidateBake.CellsOf("flat", "maxGapCm", 37501, 100));
        Assert.That(ex!.Message, Does.Contain("flat"));
        Assert.That(ex.Message, Does.Contain("maxGapCm"));
        Assert.That(ex.Message, Does.Contain("37501"));
        Assert.That(ex.Message, Does.Contain("100"));
    }

    [Test]
    public void JumpLinkBake_MissingField_IsRejected()
    {
        JsonObject navmesh = MapWithJump("""
            "jumpLinkBake": {
              "linkSpacingCm": 500,
              "endpointMaxSlopeDeg": 22,
              "endpointTerrain": ["land"],
              "gapTerrain": ["cliff"]
            }
            """);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            NavMeshBakeConfigLoader.Parse(navmesh, Bodies()))!;
        Assert.That(ex.Message, Does.Contain("maps.flat.jumpLinkBake"));
        Assert.That(ex.Message, Does.Contain("maxGapCm"));
    }

    private static NavSurfaceAsset BakeFlat(LogicTerrainField terrain, int cellCm)
    {
        int cells = terrain.WidthCells;
        int span = cells * cellCm;
        var heights = new Fix64[cells * cells];
        Array.Fill(heights, Fix64.FromInt(1000));
        var input = new CrowdJumpLinkBakeInput
        {
            MapId = "flat",
            Settings = new NavJumpLinkBakeConfig
            {
                MaxGapCm = 6 * cellCm,
                LinkSpacingCm = 5 * cellCm,
                EndpointMaxSlopeDeg = 22,
                EndpointTerrain = ["land"],
                GapTerrain = ["cliff"],
            },
            Heights = new NavHeightField
            {
                NavCellsPerSide = cells,
                NavCellSizeCm = cellCm,
                HeightsCm = heights,
                Slope = new Fix64[cells * cells],
            },
            Bridge = new byte[cells * cells],
        };
        return CrowdNavSurfaceAuthoring.Bake(
            terrain,
            ["land", "cliff"],
            new CrowdNavSurfaceBakeSources(
                "Maps/flat.json",
                span,
                span,
                "terrain/flat.height",
                span,
                span,
                cells,
                cells,
                "Navigation/navmesh.json",
                cellCm),
            input);
    }

    private static string ComparePairs(
        string seed,
        NavSurfaceJumpCandidate[] baked,
        NavSurfaceJumpCandidate[] stored,
        out int dropMax,
        out int dropDiffs)
    {
        dropMax = 0;
        dropDiffs = 0;
        var storedAt = new Dictionary<(ushort X, ushort Y, ushort ToX, ushort ToY), int>();
        for (int i = 0; i < stored.Length; i++)
        {
            NavSurfaceJumpCandidate jump = stored[i];
            storedAt[(jump.FromX, jump.FromY, jump.ToX, jump.ToY)] = i;
        }

        var seen = new HashSet<(ushort, ushort, ushort, ushort)>();
        var report = new StringBuilder();
        int orderBreak = -1;
        int limit = Math.Min(baked.Length, stored.Length);
        for (int i = 0; i < limit; i++)
        {
            if (orderBreak < 0 && !SameEnds(baked[i], stored[i]))
            {
                orderBreak = i;
            }
        }

        if (baked.Length != stored.Length || orderBreak >= 0)
        {
            report.AppendLine($"seed={seed} 格对不一致：烘焙 {baked.Length} 条，入库 {stored.Length} 条，顺序首差下标 {orderBreak}。");
        }

        for (int i = 0; i < baked.Length; i++)
        {
            NavSurfaceJumpCandidate jump = baked[i];
            var key = (jump.FromX, jump.FromY, jump.ToX, jump.ToY);
            if (!storedAt.TryGetValue(key, out int at))
            {
                report.AppendLine($"seed={seed} 只在烘焙里：({jump.FromX},{jump.FromY})→({jump.ToX},{jump.ToY}) len={jump.LengthCells} drop={jump.DropCm}");
                continue;
            }

            seen.Add(key);
            NavSurfaceJumpCandidate fixture = stored[at];
            if (jump.LengthCells != fixture.LengthCells)
            {
                report.AppendLine(
                    $"seed={seed} 格距不一致 ({jump.FromX},{jump.FromY})→({jump.ToX},{jump.ToY}) 烘焙 {jump.LengthCells} 入库 {fixture.LengthCells}");
            }

            int diff = Math.Abs(jump.DropCm - fixture.DropCm);
            if (diff != 0)
            {
                dropDiffs++;
                if (diff > dropMax)
                {
                    dropMax = diff;
                }
            }
        }

        for (int i = 0; i < stored.Length; i++)
        {
            NavSurfaceJumpCandidate jump = stored[i];
            var key = (jump.FromX, jump.FromY, jump.ToX, jump.ToY);
            if (!seen.Contains(key) && !ContainsEnd(baked, jump))
            {
                report.AppendLine($"seed={seed} 只在入库里：({jump.FromX},{jump.FromY})→({jump.ToX},{jump.ToY}) len={jump.LengthCells} drop={jump.DropCm}");
            }
        }

        if (orderBreak >= 0 && baked.Length == stored.Length && report.ToString().IndexOf("只在", StringComparison.Ordinal) < 0)
        {
            NavSurfaceJumpCandidate b = baked[orderBreak];
            NavSurfaceJumpCandidate s = stored[orderBreak];
            report.AppendLine(
                $"seed={seed} 顺序在下标 {orderBreak} 分叉：烘焙 ({b.FromX},{b.FromY})→({b.ToX},{b.ToY})，入库 ({s.FromX},{s.FromY})→({s.ToX},{s.ToY})。集合相同则根因在桶的插入序。");
        }

        return report.ToString();
    }

    private static string CompareLinks(
        string seed,
        NavMeshAgentProfileConfig profile,
        NavSurfaceJumpCandidate[] baked,
        NavSurfaceJumpCandidate[] stored)
    {
        NavMeshJumpConfig jump = profile.Jump;
        LinkRow[] bakedLinks = Keep(baked, jump);
        LinkRow[] storedLinks = Keep(stored, jump);
        int bakedKept = bakedLinks.Length;
        int storedKept = storedLinks.Length;
        int twoWay = 0;
        for (int i = 0; i < bakedLinks.Length; i++)
        {
            if (bakedLinks[i].TwoWay)
            {
                twoWay++;
            }
        }

        var head = $"kept={bakedKept}/{storedKept} filteredBaked={baked.Length - bakedKept} filteredStored={stored.Length - storedKept} twoWay={twoWay}";
        if (bakedLinks.Length == storedLinks.Length)
        {
            bool same = true;
            for (int i = 0; i < bakedLinks.Length && same; i++)
            {
                same = bakedLinks[i].Equals(storedLinks[i]);
            }

            if (same)
            {
                return head;
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"seed={seed} profile={profile.Id} 链接不一致 {head}");
        int n = Math.Min(bakedLinks.Length, storedLinks.Length);
        for (int i = 0; i < n; i++)
        {
            if (!bakedLinks[i].Equals(storedLinks[i]))
            {
                report.AppendLine($"  下标 {i} 烘焙 {bakedLinks[i]} 入库 {storedLinks[i]}");
                report.AppendLine($"  根因：{Cause(profile, baked, stored, bakedLinks[i])}");
            }
        }

        if (bakedLinks.Length != storedLinks.Length)
        {
            report.AppendLine($"  条数 {bakedLinks.Length} ≠ {storedLinks.Length}。rangeNavCells={jump.RangeNavCells} downCm={jump.DownCm} upCm={jump.UpCm}。");
        }

        return report.ToString();
    }

    private static string Cause(
        NavMeshAgentProfileConfig profile,
        NavSurfaceJumpCandidate[] baked,
        NavSurfaceJumpCandidate[] stored,
        LinkRow row)
    {
        NavSurfaceJumpCandidate? b = Find(baked, row);
        NavSurfaceJumpCandidate? s = Find(stored, row);
        if (b == null || s == null)
        {
            return "一侧没有这条格对，链接差是格对差带出来的。";
        }

        NavMeshJumpConfig jump = profile.Jump;
        int bd = b.Value.DropCm;
        int sd = s.Value.DropCm;
        if ((bd <= jump.UpCm) != (sd <= jump.UpCm))
        {
            return $"落差 {bd} cm 与 {sd} cm 卡在 upCm {jump.UpCm} 两侧，单双向翻转。";
        }

        if ((bd > jump.DownCm) != (sd > jump.DownCm))
        {
            return $"落差 {bd} cm 与 {sd} cm 卡在 downCm {jump.DownCm} 两侧，过滤结果翻转。";
        }

        if ((b.Value.LengthCells > jump.RangeNavCells) != (s.Value.LengthCells > jump.RangeNavCells))
        {
            return $"格距 {b.Value.LengthCells} 与 {s.Value.LengthCells} 卡在 rangeNavCells {jump.RangeNavCells} 两侧。";
        }

        return "格对还在，过滤条件没有翻，顺序或端点方向不同。";
    }

    private static LinkRow[] Keep(NavSurfaceJumpCandidate[] candidates, NavMeshJumpConfig jump)
    {
        var kept = new List<LinkRow>();
        for (int i = 0; i < candidates.Length; i++)
        {
            NavSurfaceJumpCandidate cand = candidates[i];
            if (cand.LengthCells > jump.RangeNavCells || cand.DropCm > jump.DownCm)
            {
                continue;
            }

            kept.Add(new LinkRow(cand.FromX, cand.FromY, cand.ToX, cand.ToY, cand.DropCm <= jump.UpCm));
        }

        return kept.ToArray();
    }

    private static NavSurfaceJumpCandidate? Find(NavSurfaceJumpCandidate[] candidates, LinkRow row)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            NavSurfaceJumpCandidate cand = candidates[i];
            if (cand.FromX == row.FromX && cand.FromY == row.FromY && cand.ToX == row.ToX && cand.ToY == row.ToY)
            {
                return cand;
            }
        }

        return null;
    }

    private static bool SameEnds(NavSurfaceJumpCandidate a, NavSurfaceJumpCandidate b)
        => a.FromX == b.FromX && a.FromY == b.FromY && a.ToX == b.ToX && a.ToY == b.ToY;

    private static bool ContainsEnd(NavSurfaceJumpCandidate[] candidates, NavSurfaceJumpCandidate jump)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            if (SameEnds(candidates[i], jump))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonObject MapWithJump(string jumpJson)
        => JsonNode.Parse($$"""
            {
              "mode": "offline",
              "algorithm": "recast",
              "profiles": {
                "Small": { "maxClimbCm": 40, "maxSlopeDeg": 45, "agentProfileId": "Small" }
              },
              "layers": [ { "id": "Ground", "layer": 0 } ],
              "areas": { "Default": { "areaId": 0, "slopeFree": false } },
              "maps": {
                "flat": {
                  "solver": "crowd-simulation",
                  "navAreas": ["Default"],
                  "terrainAreas": [
                    { "terrainId": "land", "areaId": "Default" },
                    { "terrainId": "cliff", "areaId": "Default" }
                  ],
                  {{jumpJson}}
                }
              },
              "runtimeIncremental": {
                "tileBudgetPerFixedTick": 1,
                "includeNeighborTiles": true,
                "heightScaleMeters": 1,
                "minWalkableUpDot": 0.6,
                "cliffHeightThreshold": 1
              }
            }
            """)!.AsObject();

    private static AgentProfileRegistry Bodies()
        => new(new[]
        {
            new AgentProfileConfig
            {
                Id = "Small",
                NavProfileId = "Small",
                RadiusCm = 30,
                HeightCm = 180,
                ClearanceCm = 40,
                DraftCm = 0,
                BeamCm = 0,
                Mass = 1,
                Layer = 0,
            },
        });

    private readonly record struct LinkRow(ushort FromX, ushort FromY, ushort ToX, ushort ToY, bool TwoWay);

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
