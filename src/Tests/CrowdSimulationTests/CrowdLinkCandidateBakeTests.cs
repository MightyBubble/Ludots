using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Tool;
using CrowdSimulationTests.Parity;
using NUnit.Framework;

namespace CrowdSimulationTests;

[TestFixture]
public sealed class CrowdLinkCandidateBakeTests
{
    [Test]
    public void S1Seeds_StoredSurfaceIsLudotsBake_AndJumpsMatchWebBehaviour()
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
                CrowdSimulationRuntimeConfig runtime = S1SurfaceTruthTests.LoadRuntime(name);
                NavMeshBakeConfigContext ctx = NavMeshBakeConfigLoader.LoadContextFromRepoRoot(repo, modId);
                float shortestCm = float.PositiveInfinity;
                for (int i = 0; i < ctx.AgentProfiles.Count; i++)
                {
                    shortestCm = Math.Min(shortestCm, ctx.AgentProfiles[i].HeightCm);
                }

                string modAssets = Path.Combine(repo, "mods", "showcases", "crowd_simulation", modId, "assets");
                string surfacePath = Path.Combine(modAssets, "terrain", mapId + ".navsurface");
                NavSurfaceAsset stored;
                using (FileStream input = File.OpenRead(surfacePath))
                {
                    stored = NavSurfaceAsset.Read(input);
                }

                string logicTerrain = ctx.Config.Maps[mapId].LogicTerrainAsset;
                LogicTerrainField terrain = ReactGridTerrainBinary.Read(
                    Path.Combine(modAssets, logicTerrain),
                    stored.NavCellSizeCm);
                NavSurfaceAsset baked = CrowdNavSurfaceAuthoring.BakeAuthored(repo, modId, mapId, terrain);
                if (!SameSurface(baked, stored))
                {
                    failures.AppendLine($"seed={seed} 入库的 {mapId}.navsurface 不是当前 Ludots 烘焙结果，重跑 Ludots.Tool nav bake-crowd-navsurface。");
                }

                NavSurfaceJumpCandidate[] web = S1JumpTruth.Read(modAssets);

                string pairs = ComparePairs(name, baked.JumpCandidates, web, out int dropMax, out int dropDiffs);
                TestContext.Out.WriteLine(
                    $"seed={seed} baked={baked.JumpCandidates.Length} web={web.Length} dropMaxCm={dropMax} dropDiffs={dropDiffs} shortestHeightCm={shortestCm.ToString(CultureInfo.InvariantCulture)}");
                if (pairs.Length > 0)
                {
                    failures.AppendLine(pairs);
                }

                if (dropMax > shortestCm)
                {
                    failures.AppendLine($"seed={seed} 落差最大差值 {dropMax} cm，超过最矮机动档案 heightCm {shortestCm.ToString(CultureInfo.InvariantCulture)}。");
                }

                int linkRows = 0;
                foreach (RuntimeNavProfile profile in runtime.NavProfiles)
                {
                    if (profile.Jump == null)
                    {
                        continue;
                    }

                    linkRows++;
                    string links = CompareLinks(name, profile, baked.JumpCandidates, web);
                    TestContext.Out.WriteLine($"seed={seed} profile={profile.Id} {links.Split('\n')[0]}");
                    if (links.Contains("不一致", StringComparison.Ordinal))
                    {
                        failures.AppendLine(links);
                    }
                }

                if (linkRows == 0)
                {
                    failures.AppendLine($"seed={seed} 没有任何移动类型有链接能力，链接对拍没有比到东西。");
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
    public void S1Maps_DeriveEndpointAndHardGapTerrainFromTraversal([ValueSource(typeof(S1SurfaceTruthTests), nameof(S1SurfaceTruthTests.Seeds))] string seed)
    {
        CrowdSimulationRuntimeConfig runtime = S1SurfaceTruthTests.LoadRuntime(seed);
        Assert.That(runtime.LinkCandidateBakes, Has.Count.EqualTo(1));
        RuntimeLinkCandidateBake jump = runtime.LinkCandidateBakes[0];
        string[] Pick(bool[] mask) => runtime.TerrainTypeIds.Where((_, t) => mask[t]).ToArray();
        Assert.That(jump.KindId, Is.EqualTo("jump"));
        Assert.That((jump.MaxGapCm, jump.LinkSpacingCm, jump.EndpointMaxSlopeDeg), Is.EqualTo((37500, 31250, 22d)));
        Assert.That(Pick(jump.EndpointTerrain), Is.EquivalentTo(new[] { "shore", "land", "mountain" }));
        Assert.That(Pick(jump.HardGapTerrain), Is.EquivalentTo(new[] { "cliff", "water" }));
    }

    [Test]
    public void DemoMap_WithoutLinkCandidateBake_HasEmptyKindTable()
    {
        CrowdSimulationRuntimeConfig runtime = TestDefaults.Assemble();
        Assert.That(runtime.LinkCandidateBakes, Is.Empty);
    }

    [Test]
    public void LinkCandidateBake_KindWithoutAnyAbleRow_IsDeadConfig()
    {
        JsonObject traversal = TestDefaults.DefaultTraversalJson();
        foreach (var row in traversal["agentTypes"]!.AsObject())
        {
            row.Value!["links"]!["jump"] = false;
            foreach (var cell in row.Value["cells"]!.AsObject())
            {
                if (cell.Value is JsonObject c)
                {
                    c["depart"] = new JsonArray();
                    c["arrive"] = new JsonArray();
                }
            }
        }

        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(
            navmesh: DemoNavmeshWith("""{ "jump": { "maxGapCm": 37500, "linkSpacingCm": 31250, "endpointMaxSlopeDeg": 22 } }"""),
            traversal: TestDefaults.ParseTraversal(traversal)));
        Assert.That(ex!.Message, Does.Contain("linkCandidateBake.jump"));
        Assert.That(ex.Message, Does.Contain("没有任何移动类型有这种链接能力"));
    }

    [Test]
    public void LinkCandidateBake_UnknownKind_IsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TestDefaults.Assemble(
            navmesh: DemoNavmeshWith("""{ "climb": { "maxGapCm": 37500, "linkSpacingCm": 31250, "endpointMaxSlopeDeg": 22 } }""")));
        Assert.That(ex!.Message, Does.Contain("linkCandidateBake.climb"));
        Assert.That(ex.Message, Does.Contain("linkKinds"));
    }

    [Test]
    public void RetiredJumpLinkBake_NamesTheReplacement()
    {
        JsonObject navmesh = MapWithLinkBake("""
            "jumpLinkBake": { "maxGapCm": 600, "linkSpacingCm": 500, "endpointMaxSlopeDeg": 22 }
            """);
        var ex = Assert.Throws<InvalidOperationException>(() => NavMeshBakeConfigLoader.Parse(navmesh, Bodies()))!;
        Assert.That(ex.Message, Does.Contain("maps.flat.jumpLinkBake 已删除"));
        Assert.That(ex.Message, Does.Contain("linkCandidateBake"));
    }

    [TestCase("""{ "jump": { "maxGapCm": 650, "linkSpacingCm": 500, "endpointMaxSlopeDeg": 22 } }""", "maxGapCm = 650")]
    [TestCase("""{ "jump": { "maxGapCm": 600, "linkSpacingCm": 0, "endpointMaxSlopeDeg": 22 } }""", "linkSpacingCm = 0")]
    [TestCase("""{ "jump": { "maxGapCm": 600, "linkSpacingCm": 500, "endpointMaxSlopeDeg": 90 } }""", "(0, 90)")]
    [TestCase("""{ "jump": { "maxGapCm": 600, "linkSpacingCm": 500, "endpointMaxSlopeDeg": 0 } }""", "(0, 90)")]
    [TestCase("""{ "jump": { "linkSpacingCm": 500, "endpointMaxSlopeDeg": 22 } }""", "maxGapCm")]
    [TestCase("""{ "jump": { "maxGapCm": 600, "linkSpacingCm": 500, "endpointMaxSlopeDeg": 22, "endpointTerrain": ["land"] } }""", "endpointTerrain")]
    [TestCase("""{ "jump": false }""", "必须是对象")]
    [TestCase("""{}""", "非空对象")]
    public void LinkCandidateBake_GeometryRules_AreChecked(string kinds, string expected)
    {
        JsonObject navmesh = MapWithLinkBake($"\"linkCandidateBake\": {kinds}");
        var ex = Assert.Throws<InvalidOperationException>(() => NavMeshBakeConfigLoader.Parse(navmesh, Bodies()))!;
        Assert.That(ex.Message, Does.Contain("maps.flat.linkCandidateBake"));
        Assert.That(ex.Message, Does.Contain(expected));
    }

    [Test]
    public void LinkCandidateBake_GapNotDivisibleByCell_IsRejectedByBaker()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrowdLinkCandidateBake.CellsOf("flat", "linkCandidateBake.jump.maxGapCm", 37501, 100));
        Assert.That(ex!.Message, Does.Contain("flat"));
        Assert.That(ex.Message, Does.Contain("maxGapCm"));
        Assert.That(ex.Message, Does.Contain("37501"));
        Assert.That(ex.Message, Does.Contain("100"));
    }

    private static NavMeshBakeConfig DemoNavmeshWith(string linkCandidateBake)
    {
        JsonObject json = TestDefaults.DefaultNavmeshJson();
        json["maps"]!["crowd_simulation_demo"]!["linkCandidateBake"] = JsonNode.Parse(linkCandidateBake);
        return TestDefaults.ParseNavmesh(json);
    }

    private static NavSurfaceAsset BakeFlat(LogicTerrainField terrain, int cellCm)
    {
        int cells = terrain.WidthCells;
        int span = cells * cellCm;
        var heights = new Fix64[cells * cells];
        Array.Fill(heights, Fix64.FromInt(1000));
        var input = new CrowdLinkCandidateBakeInput
        {
            MapId = "flat",
            Kinds =
            [
                new RuntimeLinkCandidateBake
                {
                    KindId = "jump",
                    MaxGapCm = 6 * cellCm,
                    LinkSpacingCm = 5 * cellCm,
                    EndpointMaxSlopeDeg = 22,
                    EndpointTerrain = [true, false],
                    HardGapTerrain = [false, true],
                },
            ],
            Heights = new NavHeightField
            {
                NavCellsPerSide = cells,
                NavCellSizeCm = cellCm,
                HeightsCm = heights,
                Slope = new Fix64[cells * cells],
            },
            Bridge = new byte[cells * cells],
            BakeInputsDigest = 0,
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
        NavSurfaceJumpCandidate[] web,
        out int dropMax,
        out int dropDiffs)
    {
        dropMax = 0;
        dropDiffs = 0;
        var webAt = new Dictionary<(ushort X, ushort Y, ushort ToX, ushort ToY), int>();
        for (int i = 0; i < web.Length; i++)
        {
            NavSurfaceJumpCandidate jump = web[i];
            webAt[(jump.FromX, jump.FromY, jump.ToX, jump.ToY)] = i;
        }

        var seen = new HashSet<(ushort, ushort, ushort, ushort)>();
        var report = new StringBuilder();
        int orderBreak = -1;
        int limit = Math.Min(baked.Length, web.Length);
        for (int i = 0; i < limit; i++)
        {
            if (orderBreak < 0 && !SameEnds(baked[i], web[i]))
            {
                orderBreak = i;
            }
        }

        if (baked.Length != web.Length || orderBreak >= 0)
        {
            report.AppendLine($"seed={seed} 格对不一致：烘焙 {baked.Length} 条，Web {web.Length} 条，顺序首差下标 {orderBreak}。");
        }

        for (int i = 0; i < baked.Length; i++)
        {
            NavSurfaceJumpCandidate jump = baked[i];
            var key = (jump.FromX, jump.FromY, jump.ToX, jump.ToY);
            if (!webAt.TryGetValue(key, out int at))
            {
                report.AppendLine($"seed={seed} 只在烘焙里：({jump.FromX},{jump.FromY})→({jump.ToX},{jump.ToY}) len={jump.LengthCells} drop={jump.DropCm}");
                continue;
            }

            seen.Add(key);
            NavSurfaceJumpCandidate reference = web[at];
            if (jump.LengthCells != reference.LengthCells)
            {
                report.AppendLine(
                    $"seed={seed} 格距不一致 ({jump.FromX},{jump.FromY})→({jump.ToX},{jump.ToY}) 烘焙 {jump.LengthCells} Web {reference.LengthCells}");
            }

            int diff = Math.Abs(jump.DropCm - reference.DropCm);
            if (diff != 0)
            {
                dropDiffs++;
                if (diff > dropMax)
                {
                    dropMax = diff;
                }
            }
        }

        for (int i = 0; i < web.Length; i++)
        {
            NavSurfaceJumpCandidate jump = web[i];
            var key = (jump.FromX, jump.FromY, jump.ToX, jump.ToY);
            if (!seen.Contains(key) && !ContainsEnd(baked, jump))
            {
                report.AppendLine($"seed={seed} 只在Web里：({jump.FromX},{jump.FromY})→({jump.ToX},{jump.ToY}) len={jump.LengthCells} drop={jump.DropCm}");
            }
        }

        if (orderBreak >= 0 && baked.Length == web.Length && report.ToString().IndexOf("只在", StringComparison.Ordinal) < 0)
        {
            NavSurfaceJumpCandidate b = baked[orderBreak];
            NavSurfaceJumpCandidate s = web[orderBreak];
            report.AppendLine(
                $"seed={seed} 顺序在下标 {orderBreak} 分叉：烘焙 ({b.FromX},{b.FromY})→({b.ToX},{b.ToY})，Web ({s.FromX},{s.FromY})→({s.ToX},{s.ToY})。集合相同则根因在桶的插入序。");
        }

        return report.ToString();
    }

    private static string CompareLinks(
        string seed,
        RuntimeNavProfile profile,
        NavSurfaceJumpCandidate[] baked,
        NavSurfaceJumpCandidate[] web)
    {
        RuntimeJumpCapability jump = profile.Jump!;
        LinkRow[] bakedLinks = Keep(baked, jump);
        LinkRow[] webLinks = Keep(web, jump);
        int bakedKept = bakedLinks.Length;
        int webKept = webLinks.Length;
        int twoWay = 0;
        for (int i = 0; i < bakedLinks.Length; i++)
        {
            if (bakedLinks[i].TwoWay)
            {
                twoWay++;
            }
        }

        var head = $"kept={bakedKept}/{webKept} filteredBaked={baked.Length - bakedKept} filteredWeb={web.Length - webKept} twoWay={twoWay}";
        if (bakedLinks.Length == webLinks.Length)
        {
            bool same = true;
            for (int i = 0; i < bakedLinks.Length && same; i++)
            {
                same = bakedLinks[i].Equals(webLinks[i]);
            }

            if (same)
            {
                return head;
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"seed={seed} profile={profile.Id} 链接不一致 {head}");
        int n = Math.Min(bakedLinks.Length, webLinks.Length);
        for (int i = 0; i < n; i++)
        {
            if (!bakedLinks[i].Equals(webLinks[i]))
            {
                report.AppendLine($"  下标 {i} 烘焙 {bakedLinks[i]} Web {webLinks[i]}");
                report.AppendLine($"  根因：{Cause(profile, baked, web, bakedLinks[i])}");
            }
        }

        if (bakedLinks.Length != webLinks.Length)
        {
            report.AppendLine($"  条数 {bakedLinks.Length} ≠ {webLinks.Length}。rangeNavCells={jump.RangeNavCells} downCm={jump.DownCm} upCm={jump.UpCm}。");
        }

        return report.ToString();
    }

    private static string Cause(
        RuntimeNavProfile profile,
        NavSurfaceJumpCandidate[] baked,
        NavSurfaceJumpCandidate[] web,
        LinkRow row)
    {
        NavSurfaceJumpCandidate? b = Find(baked, row);
        NavSurfaceJumpCandidate? s = Find(web, row);
        if (b == null || s == null)
        {
            return "一侧没有这条格对，链接差是格对差带出来的。";
        }

        RuntimeJumpCapability jump = profile.Jump!;
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

    private static LinkRow[] Keep(NavSurfaceJumpCandidate[] candidates, RuntimeJumpCapability jump)
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

    private static bool SameSurface(NavSurfaceAsset a, NavSurfaceAsset b)
    {
        return a.NavCellsX == b.NavCellsX && a.NavCellsY == b.NavCellsY && a.NavCellSizeCm == b.NavCellSizeCm
            && a.TerrainTypeIds.SequenceEqual(b.TerrainTypeIds) && a.LinkKindIds.SequenceEqual(b.LinkKindIds)
            && a.BakeInputsDigest == b.BakeInputsDigest && a.TerrainCells.SequenceEqual(b.TerrainCells)
            && a.JumpCandidates.SequenceEqual(b.JumpCandidates);
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

    private static JsonObject MapWithLinkBake(string linkBakeJson)
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
                  "navCellSizeCm": 100,
                  "terrainTypes": ["land", "cliff"],
                  {{linkBakeJson}}
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
