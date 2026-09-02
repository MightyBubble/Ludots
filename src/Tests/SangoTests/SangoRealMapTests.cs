using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Ludots.Core.Fields;
using Ludots.Core.Modding;
using NUnit.Framework;
using Sango.Content.MapBin;

namespace Sango.Tests
{
    /// <summary>
    /// M2.a 真图验收:内核 Map.Load 经 VFS 直读真实 DefaultMap.bin(本地内容,不入库),
    /// 256×256 网格逐格装载;抽样断言三层一致(Field2D 层值 == 内核 Map 格值 == bin 直读值);
    /// 城标记摆点与剧本 citySet 对齐;真图路径下存→读→续跑 digest 一致。
    /// bin 缺席(CI)按原版装载器缺席语义走合成图,本套全部 Assert.Ignore。
    /// 测试不引用 SangoSimMod 工程(同 SangoKernelBootTests 惯例),反射调用 SangoRuntime。
    /// </summary>
    [TestFixture]
    public sealed class SangoRealMapTests
    {
        private const int Seed = 20260902;
        private const int RealMapCells = 256;
        private const int CellSizeCm = 2000;
        private const int HalfWorldCm = RealMapCells * CellSizeCm / 2; // 256000,地图中心为原点

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

        private static string RealBinPath => Path.Combine(
            RepoRoot(), "mods", "sango", "SangoContentMod", "assets", "Map", "DefaultMap.bin");

        private static SangoMapBinArchive RequireRealBin()
        {
            if (!File.Exists(RealBinPath))
            {
                Assert.Ignore($"Real DefaultMap.bin not present (local-only content): {RealBinPath}");
            }

            using var stream = File.OpenRead(RealBinPath);
            using var reader = new BinaryReader(stream);
            return SangoMapBinIo.Read(reader);
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

        private static object Boot(Assembly sim)
        {
            return sim.GetType("Sango.Runtime.SangoKernelBoot", throwOnError: true)!
                .GetMethod("Boot", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { NewVfs(), "SangoContentMod", Seed, "Scenario/Scenario.json" })!;
        }

        private static object CurScenario(Assembly sim)
        {
            return sim.GetType("Sango.Core.Scenario", throwOnError: true)!
                .GetProperty("Cur", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        }

        private static object KernelMap(Assembly sim) =>
            CurScenario(sim).GetType().GetProperty("Map")!.GetValue(CurScenario(sim))!;

        private static object GetCell(Assembly sim, int x, int y) =>
            KernelMap(sim).GetType().GetProperty("CellSet")!.GetValue(KernelMap(sim))!
                .GetType().GetMethod("GetCell")!.Invoke(KernelMap(sim).GetType().GetProperty("CellSet")!.GetValue(KernelMap(sim)), new object[] { x, y })!;

        private static int CellTerrainTypeId(object cell) =>
            (int)(cell.GetType().GetProperty("TerrainType")!.GetValue(cell)!
                .GetType().GetProperty("Id")!.GetValue(cell.GetType().GetProperty("TerrainType")!.GetValue(cell))!);

        private static int CellTerrainState(object cell) =>
            (int)cell.GetType().GetField("terrainState")!.GetValue(cell)!;

        private static int CellAreaId(object cell)
        {
            object? belongCity = cell.GetType().GetProperty("BelongCity")!.GetValue(cell);
            return belongCity == null ? 0 : (int)belongCity.GetType().GetProperty("Id")!.GetValue(belongCity)!;
        }

        private static bool TerrainResolves(Assembly sim, int terrainTypeId)
        {
            object commonData = CurScenario(sim).GetType().GetProperty("CommonData")!.GetValue(CurScenario(sim))!;
            object terrainTypes = commonData.GetType().GetField("TerrainTypes")!.GetValue(commonData)!;
            return terrainTypes.GetType().GetMethod("Get", new[] { typeof(int) })!
                .Invoke(terrainTypes, new object[] { terrainTypeId }) != null;
        }

        [Test]
        public void Boot_RealBin_LoadsFullGridNotSynthetic()
        {
            SangoMapBinArchive bin = RequireRealBin();
            Assembly sim = LoadSangoSimMod();
            object result = Boot(sim);

            bool synthetic = (bool)result.GetType().GetProperty("SyntheticMap")!.GetValue(result)!;
            Assert.That(synthetic, Is.False, "bin present in the content mod mount must boot the real map, not the synthetic grid");

            object map = KernelMap(sim);
            int width = (int)map.GetType().GetProperty("Width")!.GetValue(map)!;
            int height = (int)map.GetType().GetProperty("Height")!.GetValue(map)!;
            float gridSize = (float)map.GetType().GetProperty("GridSize")!.GetValue(map)!;
            string name = (string)map.GetType().GetProperty("Name")!.GetValue(map)!;

            TestContext.Progress.WriteLine($"[realmap] kernel {width}x{height} gridSize={gridSize} name={name}; bin {bin.Grid.BoundsX}x{bin.Grid.BoundsY} v{bin.Version}");
            Assert.That(width, Is.EqualTo(RealMapCells), "kernel Map.Width = mapWidth/4 of the real bin");
            Assert.That(height, Is.EqualTo(RealMapCells), "kernel Map.Height = mapHeight/4 of the real bin");
            Assert.That(gridSize, Is.EqualTo(20f), "real bin grid_size (quadSize 5 × gridVertexCount 4)");
            Assert.That(name, Is.EqualTo("DefaultMap"), "map name follows scenario Info.mapType");
            Assert.That(bin.Grid.BoundsX, Is.EqualTo(RealMapCells), "bin grid bounds derive mapWidth*quadSize/gridSize");
            Assert.That(bin.Grid.BoundsY, Is.EqualTo(RealMapCells));
        }

        private static object CitySetOf(Assembly sim) =>
            CurScenario(sim).GetType().GetField("citySet")!.GetValue(CurScenario(sim))!;

        private static bool CitySetResolves(Assembly sim, int areaId) =>
            CitySetOf(sim).GetType().GetMethod("Get", new[] { typeof(int) })!
                .Invoke(CitySetOf(sim), new object[] { areaId }) != null;

        private static int CitySetObjects(Assembly sim)
        {
            var enumerator = (System.Collections.IEnumerator)CitySetOf(sim).GetType()
                .GetMethod("GetEnumerator", Type.EmptyTypes)!.Invoke(CitySetOf(sim), null)!;
            int count = 0;
            while (enumerator.MoveNext())
            {
                if (enumerator.Current != null)
                {
                    count++;
                }
            }
            return count;
        }

        [Test]
        public void SampledCells_KernelMatchesBinDirectRead()
        {
            SangoMapBinArchive bin = RequireRealBin();
            Assembly sim = LoadSangoSimMod();
            Boot(sim);

            // 全图直方(审计输出):两类原版装载语义差异点——
            //   a) bin 地形字节落 TerrainTypes.json 表外(如远海 32)→ 内核回落 id 0;
            //   b) bin areaId 在 citySet 无对应对象(区域无城)→ 内核 BelongCity 为 null → 0。
            int offTable = 0;
            int orphanAreas = 0;
            for (int i = 0; i < bin.Grid.Cells.Length; i++)
            {
                if (!TerrainResolves(sim, bin.Grid.Cells[i].TerrainType))
                {
                    offTable++;
                }
                if (bin.Grid.Cells[i].AreaId != 0 && !CitySetResolves(sim, bin.Grid.Cells[i].AreaId))
                {
                    orphanAreas++;
                }
            }
            TestContext.Progress.WriteLine(
                $"[realmap] off-table terrain cells={offTable}/{bin.Grid.Cells.Length}; orphan areaId cells={orphanAreas}");

            // 固定步长采样,跳过表外字节,凑满 20 格;terrainState 全等,areaId 按
            // "citySet 可解析即相等,孤儿区域断言内核侧为 0"。
            var checkedCells = new List<(int x, int y)>();
            for (int i = 0; i < 4096 && checkedCells.Count < 20; i++)
            {
                int x = (i * 37) % RealMapCells;
                int y = (i * 61) % RealMapCells;
                if (TerrainResolves(sim, bin.Grid.CellAt(x, y).TerrainType))
                {
                    checkedCells.Add((x, y));
                }
            }

            Assert.That(checkedCells.Count, Is.EqualTo(20), "the real map must expose at least 20 on-table sample cells");

            foreach ((int x, int y) in checkedCells)
            {
                MapGridCell binCell = bin.Grid.CellAt(x, y);
                object kernelCell = GetCell(sim, x, y);

                Assert.That(CellTerrainTypeId(kernelCell), Is.EqualTo(binCell.TerrainType),
                    $"terrainType mismatch at ({x},{y})");
                Assert.That(CellTerrainState(kernelCell), Is.EqualTo(binCell.TerrainState),
                    $"terrainState bitmask mismatch at ({x},{y})");
                if (CitySetResolves(sim, binCell.AreaId))
                {
                    Assert.That(CellAreaId(kernelCell), Is.EqualTo(binCell.AreaId),
                        $"areaId mismatch at ({x},{y})");
                }
                else
                {
                    Assert.That(CellAreaId(kernelCell), Is.EqualTo(0),
                        $"orphan bin areaId {binCell.AreaId} at ({x},{y}) must load as no-city (original semantics)");
                }
            }
        }

        [Test]
        public void FieldLayers_MatchKernelAndBin()
        {
            SangoMapBinArchive bin = RequireRealBin();
            Assembly sim = LoadSangoSimMod();
            Boot(sim);

            var registry = new FieldLayerRegistry();
            registry.Register("sango.terrainType", FieldLayerKind.DiscreteId, CellSizeCm, 8,
                FieldLayerDefaultValue.None, persistent: true, "test.writer", maxRegionIds: 255);
            registry.Register("sango.areaId", FieldLayerKind.DiscreteId, CellSizeCm, 8,
                FieldLayerDefaultValue.None, persistent: true, "test.writer", maxRegionIds: 255);
            FieldSessionStore store = FieldSessionStore.Create(
                registry, new[] { "sango.terrainType", "sango.areaId" });

            int written = (int)sim.GetType("Sango.Runtime.SangoFieldLayers", throwOnError: true)!
                .GetMethod("Populate", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new[] { store, CurScenario(sim) })!;
            Assert.That(written, Is.EqualTo(RealMapCells * RealMapCells), "populate writes one value pair per kernel cell");

            var terrainLayer = (DiscreteIdFieldLayerData)LayersByName(store)["sango.terrainType"]!;
            var areaLayer = (DiscreteIdFieldLayerData)LayersByName(store)["sango.areaId"]!;

            // 10 格独立推导:内核格 (x=北, y=东) 的引擎 FieldCell2D = (y-128, x-128)
            // (世界中心为原点,格 2000cm,与 CHTM/VertexMap 同轴)。
            int sampled = 0;
            for (int i = 0; i < 4096 && sampled < 10; i++)
            {
                int x = (i * 53) % RealMapCells;
                int y = (i * 97) % RealMapCells;
                if (!TerrainResolves(sim, bin.Grid.CellAt(x, y).TerrainType))
                {
                    continue;
                }

                var fieldCell = new FieldCell2D(y - HalfWorldCm / CellSizeCm, x - HalfWorldCm / CellSizeCm);
                object kernelCell = GetCell(sim, x, y);
                MapGridCell binCell = bin.Grid.CellAt(x, y);

                Assert.That(terrainLayer.Field.Get(fieldCell), Is.EqualTo(CellTerrainTypeId(kernelCell)),
                    $"terrain layer vs kernel at ({x},{y})");
                Assert.That(terrainLayer.Field.Get(fieldCell), Is.EqualTo((int)binCell.TerrainType),
                    $"terrain layer vs bin at ({x},{y})");
                Assert.That(areaLayer.Field.Get(fieldCell), Is.EqualTo(CellAreaId(kernelCell)),
                    $"area layer vs kernel at ({x},{y})");
                if (CitySetResolves(sim, binCell.AreaId))
                {
                    Assert.That(areaLayer.Field.Get(fieldCell), Is.EqualTo((int)binCell.AreaId),
                        $"area layer vs bin at ({x},{y})");
                }
                else
                {
                    Assert.That(areaLayer.Field.Get(fieldCell), Is.EqualTo(0),
                        $"orphan bin areaId {binCell.AreaId} at ({x},{y}) carries kernel no-city semantics");
                }
                sampled++;
            }

            Assert.That(sampled, Is.EqualTo(10), "field layers must expose at least 10 on-table sample cells");
        }

        [Test]
        public void CityMarkers_PlacementsFollowCitySetAndForceColors()
        {
            RequireRealBin();
            Assembly sim = LoadSangoSimMod();
            Boot(sim);
            int cityObjects = CitySetObjects(sim);

            object placements = sim.GetType("Sango.Runtime.SangoCityMarkers", throwOnError: true)!
                .GetMethod("BuildPlacements", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new[] { CurScenario(sim) })!;
            int count = (int)placements.GetType().GetProperty("Count")!.GetValue(placements)!;

            // citySet.Count 是 MaxCount(id 上界,M1.b 汇报口径的"89"),非空对象数 88;
            // 标记按对象逐城生成,与 ForEach 同口径。
            TestContext.Progress.WriteLine($"[realmap] city objects={cityObjects} marker placements={count}");
            Assert.That(count, Is.EqualTo(cityObjects), "one marker placement per kernel city/gate/port object");
            Assert.That(count, Is.InRange(85, 100), "the default scenario carries the full city roster");

            var forceColors = new Dictionary<int, System.Numerics.Vector4>();
            int owned = 0;
            for (int i = 0; i < count; i++)
            {
                object placement = placements.GetType()
                    .GetMethod("get_Item", new[] { typeof(int) })!.Invoke(placements, new object[] { i })!;
                int cityId = (int)placement.GetType().GetProperty("CityId")!.GetValue(placement)!;
                var position = (System.Numerics.Vector3)placement.GetType().GetProperty("PositionCm")!.GetValue(placement)!;
                var color = (System.Numerics.Vector4)placement.GetType().GetProperty("ForceColor")!.GetValue(placement)!;
                int forceId = (int)placement.GetType().GetProperty("ForceId")!.GetValue(placement)!;

                Assert.That(position.X, Is.InRange(-HalfWorldCm, HalfWorldCm), $"city {cityId} east position inside world bounds");
                Assert.That(position.Z, Is.InRange(-HalfWorldCm, HalfWorldCm), $"city {cityId} north position inside world bounds");

                // 城坐标 → 世界摆点换算(格 x=北、y=东,格边 20 m,中心原点):
                // east = (y*20 + 10)*100 - 256000; north = (x*20 + 10)*100 - 256000。
                object city = CurScenario(sim).GetType().GetField("citySet")!.GetValue(CurScenario(sim))!
                    .GetType().GetMethod("Get", new[] { typeof(int) })!
                    .Invoke(CurScenario(sim).GetType().GetField("citySet")!.GetValue(CurScenario(sim)), new object[] { cityId })!;
                Assert.That(city, Is.Not.Null, $"placement {cityId} must reference a kernel city");
                int cityX = (int)city.GetType().GetField("x")!.GetValue(city)!;
                int cityY = (int)city.GetType().GetField("y")!.GetValue(city)!;
                Assert.That(position.X, Is.EqualTo((cityY * 20 + 10) * 100 - HalfWorldCm).Within(1),
                    $"city {cityId} east placement follows grid mapping");
                Assert.That(position.Z, Is.EqualTo((cityX * 20 + 10) * 100 - HalfWorldCm).Within(1),
                    $"city {cityId} north placement follows grid mapping");

                if (forceId > 0)
                {
                    owned++;
                    if (forceColors.TryGetValue(forceId, out var seen))
                    {
                        Assert.That(color, Is.EqualTo(seen), $"cities of force {forceId} share one banner color");
                    }
                    else
                    {
                        forceColors[forceId] = color;
                    }
                }
            }

            TestContext.Progress.WriteLine($"[realmap] owned cities={owned} distinct force colors={forceColors.Count}");
            Assert.That(owned, Is.GreaterThan(0), "the scenario starts with owned cities");
            Assert.That(forceColors.Count, Is.GreaterThanOrEqualTo(8), "multiple forces must be distinguishable by color");
        }

        [Test]
        public void RealMap_SaveRoundtrip_DigestStaysIdentical()
        {
            RequireRealBin();
            Assembly sim = LoadSangoSimMod();
            IVirtualFileSystem vfs = NewVfs();

            // 链 A:Boot→3 回合→Capture→Restore→2 回合。
            Boot(sim);
            RunTurns(sim, 3);
            object participant = sim.GetType("Sango.Runtime.SangoSaveParticipant", throwOnError: true)!
                .GetConstructor(new[] { typeof(IVirtualFileSystem), typeof(string), typeof(string) })!
                .Invoke(new object[] { vfs, "SangoContentMod", "Scenario/Scenario.json" });
            JsonNode capture = (JsonNode)participant.GetType()
                .GetMethod("CaptureState", BindingFlags.Public | BindingFlags.Instance)!.Invoke(participant, null)!;
            string digestAtCapture = WorldDigest(sim);
            participant.GetType()
                .GetMethod("RestoreState", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(participant, new object[] { capture });
            RunTurns(sim, 2);
            string digestAcrossSave = WorldDigest(sim);

            // 链 B:Boot→5 回合,从不存档。
            Boot(sim);
            RunTurns(sim, 5);
            string digestNeverSaved = WorldDigest(sim);

            TestContext.Progress.WriteLine($"[realmap-save] across={digestAcrossSave} never={digestNeverSaved}");
            Assert.That(digestAcrossSave, Is.EqualTo(digestNeverSaved),
                "on the real map, save→restore→continue must replay the never-saved chain bit for bit");
        }

        private static System.Collections.IDictionary LayersByName(FieldSessionStore store)
        {
            var byName = new System.Collections.Hashtable();
            foreach (FieldLayerData layer in store.Layers)
            {
                byName[layer.LayerKey] = layer;
            }
            return byName;
        }

        private static void RunTurns(Assembly sim, int turns)
        {
            for (int i = 0; i < turns; i++)
            {
                sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                    .GetMethod("AdvanceTurn", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, null);
            }
        }

        private static string WorldDigest(Assembly sim)
        {
            return (string)sim.GetType("Sango.Runtime.SangoTurnDriver", throwOnError: true)!
                .GetMethod("WorldDigest", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null)!;
        }
    }
}
