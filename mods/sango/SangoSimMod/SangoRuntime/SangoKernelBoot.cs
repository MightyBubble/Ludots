// sango 模拟内核启动线(M1.b)。
// 依据原版逆向(GameStart.Awake → Game.Init → GameInit 协程 → 剧本选择窗口 →
// Scenario.StartScenario,源:Project/Assets/Sango/Scripts/GameStart.cs、Game/Game.cs、
// Game/Scenario/Scenario.cs),按原语义重建最小 headless 启动:
//   1. IO 网关接管(显式装 VFS 后端,资产流一律走 Ludots VFS,shim 不再读盘)
//   2. Game.Init 的静态注册表序列(ActionBase/Condition/BuffEffect/SkillEffect/Trigger/
//      PersonFunctions/TroopCompareFunction/Skill*Method 群)
//   3. GameLanguage.Init("cn")(原版顺序在 GameSystemManager 之前;Language 目录缺席时空跑)
//   4. GameSystemManager.Init()(进程生命周期一次;二次启动沿用已建单例,同原版 App 语义)
//   5. GameData.Instance.Init()(GameInit 协程体:公共表 + ModelConfig + SkillConfigManager
//      + GameCustomEdit;后两者在资产缺席时按原版空跑)
//   6. 剧本装载(StartScenario 无玩家列表重载的等价展开,playerForceList 为空 → 全势力
//      AI 托管,即原版 headless 语义;Player 视角系统不参与):
//      GameRandom.Init(seed)(D7:种子显式注入,对应原版 StartScenario 首行)
//      → LoadContent(LoadBaseContent:公共表/PersonLibrary/PopulateObject 延迟引用/Map.Load)
//      → CheckPlayer → LoadWorld(MapRender shim)
//   7. shim 的 LoadMap 不产文件流:Map.Load 在 LoadBaseContent 内已按 bin 有无装载真图或
//      no-op(M2.a 起真 DefaultMap.bin 经 SangoVfsIO 直读),装载后仅对 bin 缺席的世界补合成
//      网格并手动 FireOnMapLoaded → OnWorldLoaded(Prepare/Init/Start,含 MakeForceQuene 与首次 Run)
// 不搬的 Unity 侧环节(窗口/菜单/媒体/Debate/协程)见 PLAN D8 与 SangoUnityShim 注释。

using System;
using System.Collections.Generic;
using Ludots.Core.Modding;
using Sango.Core;
using Sango.Core.Action;
using Sango.Mod;
using SangoMod = Sango.Mod.Mod;
using MapRender = Sango.Render.MapRender;

namespace Sango.Runtime
{
    public static class SangoKernelBoot
    {
        // 合成网格的格子尺寸:原版取自 DefaultMap.bin 的 grid_size(仅用于坐标换算,
        // 不入玩法数值);bin 未接入前取编辑器同量级常数。
        private const float SyntheticGridSize = 4f;

        // 合成地图的默认地形 = 草地(TerrainTypes.json id 1,canBuild);见 BuildSyntheticMap。
        private const int GrasslandTerrainId = 1;

        // 合成地图的城市直辖半径:真图来自 bin 的 areaId 分区;合成近似取城市周边 2 圈
        // 六边(覆盖占格半径 1 + 建筑间距),驱动 CityAI 的 areaCellList 选址链。
        private const int SyntheticAreaRadius = 2;

        /// <summary>
        /// 从内容 mod 资产启动世界。同进程内可重复调用(先按 Player.Quit 语义关上一局)。
        /// </summary>
        /// <returns>已就绪(Prepare/Init/Start 完成)的 Scenario,即 Scenario.Cur。</returns>
        public static BootResult Boot(IVirtualFileSystem vfs, string contentModId, int seed,
            string scenarioAssetPath = "Scenario/Scenario.json")
        {
            Scenario scenario = PrepareKernel(vfs, contentModId, scenarioAssetPath);
            scenario.LoadInfo();
            if (scenario.Info == null)
                throw new InvalidOperationException($"Scenario asset has no Info section: {scenario.FilePath}");

            // Scenario.StartScenario(scenario)(无玩家列表)的等价展开。
            // Cur 的赋权发生在 LoadBaseContent 首行(与原版一致,setter 对外不可见)。
            GameRandom.Init(seed);
            return StartScenarioCore(scenario, populateContent: null);
        }

        /// <summary>
        /// 从存档捕获回灌世界(M1.d):与 Boot 同一启动序列,差异仅在两处——
        /// Info/正文取自内存中的存档 JSON(SangoSaveParticipant 捕获面,CommonData 仍从
        /// 数据表整表重载),结束后导入捕获的 GameRandom 流位置,续跑确定性由此成立。
        /// </summary>
        public static BootResult Restore(IVirtualFileSystem vfs, string contentModId,
            string scenarioJson, int[] randomState, string scenarioAssetPath = "Scenario/Scenario.json")
        {
            if (string.IsNullOrEmpty(scenarioJson))
                throw new ArgumentException("Captured scenario JSON is required.", nameof(scenarioJson));

            Scenario scenario = PrepareKernel(vfs, contentModId, scenarioAssetPath);
            scenario.LoadInfoFromText(scenarioJson);
            if (scenario.Info == null)
                throw new InvalidOperationException("Captured sango.sim payload has no Info section.");
            if (!scenario.Info.isSave)
                throw new InvalidOperationException(
                    "sango.sim restore requires a save-state capture (Info.isSave); a raw scenario asset must go through Boot.");

            GameRandom.Init(0);
            // 回灌启动序期间抑制 Force.Init 的回合开始重演(见 Scenario.IsRestoreLoad)。
            scenario.IsRestoreLoad = true;
            BootResult result = StartScenarioCore(scenario, populateContent: scenarioJson);
            scenario.IsRestoreLoad = false;
            // 启动线(Prepare/Init/Start)自身的随机消耗不属于存档时点;流位置整体覆盖。
            GameRandom.ImportState(randomState);
            return result;
        }

        /// <summary>Boot/Restore 共享的进程级序:IO 网关、静态注册表、系统单例、上一局关停。</summary>
        static Scenario PrepareKernel(IVirtualFileSystem vfs, string contentModId, string scenarioAssetPath)
        {
            SangoVfsIO.Install(vfs, contentModId);

            // Game.Init 序(GameStart.cs → Game.cs Init):注册表 → 语言 → 系统 → 数据。
            // sango 私有 mod 层(ModManager.Init/InitMods 的市场/包扫描)由 Ludots VFS 取代:
            // 只置空启用列表,阻断其参与文件枚举(原版空 mod 列表语义)。
            ModManager.Instance.mEnabledModList = new List<SangoMod>();

            ActionBase.Init();
            Condition.Init();
            BuffEffect.Init();
            SkillEffect.Init();
            Trigger.Init();
            PersonFunctions.Init();
            TroopCompareFunction.Init();
            SkillSuccessMethod.Init();
            SkillSpellConditionMethod.Init();
            SkillCriticalMethod.Init();
            SkillRangeFilterMethod.Init();

            GameLanguage.Instance.Init("cn");

            // 原版 App 生命周期内 Init 一次;headless 双启动(确定性比对)沿用已建系统单例。
            if (GameSystemManager.Instance.systemMap.Count == 0)
                GameSystemManager.Instance.Init();

            GameData.Instance.Init();

            // 二次启动:按 Player.Quit() 的 OnGameShutdown 链清上一局(退订事件/清对象池)。
            Scenario.Cur?.OnGameShutdown();

            var scenario = new Scenario();
            scenario.FilePath = SangoVfsIO.FindFile(scenarioAssetPath) ?? throw new FileNotFoundException(
                $"Scenario asset not found via VFS: {contentModId}:assets/{scenarioAssetPath}");
            return scenario;
        }

        /// <summary>StartScenario(scenario) 无玩家列表重载的等价展开,Boot 与 Restore 共用。</summary>
        static BootResult StartScenarioCore(Scenario scenario, string populateContent)
        {
            scenario.IsAlive = false;
            GameEvent.OnScenarioLoadStart?.Invoke(scenario);
            scenario.LoadContent(scenario.FilePath, populateContent);
            // 回灌 JSON 的根 IsAlive=true(捕获自运行中的世界)会让 Start() 内的首次 Run()
            // 越过存活闸、在装载尾声推进半步真实回合;装载完成前重申存活=false,世界的
            // 第一步只属于调用方的回合驱动(原版 StartScenario 在 LoadContent 前的置位
            // 同义,这里补在 populate 之后)。
            scenario.IsAlive = false;
            scenario.CheckPlayer();
            // 本剧本资产未导出 View(相机初始状态);headless 下相机为 no-op,补默认视图
            // 使 Start() 的 SetCamera 调用可走通(相机字段由 ScenarioView 构造默认)。
            if (scenario.View == null)
                scenario.View = new ScenarioView();
            GameEvent.OnScenarioLoadEnd?.Invoke(Scenario.Cur);
            GameEvent.OnWorldLoadStart?.Invoke(Scenario.Cur);
            scenario.LoadWorld();
            // 装载侧地图源语义(M2.a 真 bin 接入后):LoadContent 内的 Map.Load 已经从
            // SangoVfsIO 读真实 DefaultMap.bin(Boot 与 Restore 同序——PopulateObject 之后
            // 总是紧接 Map.Load,回灌 JSON 的空 CellSet 不会短路它);bin 缺席时 Map.Load
            // 是原版装载器的缺席 no-op(Width<6 早退/File 缺席跳过),此时补合成均匀网格。
            // 探测用 GetCell(0,0):bin 装载与合成重建都会填 (0,0),JSON 填充出的空 CellSet
            // (width=0)返回 null。
            bool syntheticMap = false;
            if (scenario.Map.CellSet?.GetCell(0, 0) == null)
            {
                BuildSyntheticMap(scenario);
                syntheticMap = true;
            }
            MapRender.Instance.FireOnMapLoaded();
            // OnWorldLoaded 内完成 Prepare/Init/Start(含 MakeForceQuene 与首次 Run;
            // Run 的 IsAlive 闸在 Start 内先 Run 后置位,启动期不消耗模拟)。

            return new BootResult
            {
                Scenario = scenario,
                Persons = scenario.personSet.Count,
                Cities = scenario.citySet.Count,
                Forces = scenario.forceSet.Count,
                RegisteredSystems = GameSystemManager.Instance.systemMap.Count,
                SyntheticMap = syntheticMap,
            };
        }

        /// <summary>
        /// bin 缺席(CI 等环境)时的最小可玩地图:按剧本城市坐标 + 建筑半径外扩的均匀网格 +
        /// 默认地形,满足 City.OnScenarioPrepare 的 GetSpiral 占格与 Cell.Init 的邻居缝合。
        /// 默认地形取草地(id 1,可建造):真实地图主体即草地,且 CityAI.AIBuilding 的
        /// 选址要求 canBuild 地形;取"无"(id 0,不可建)会让建筑 AI 整体休眠。
        /// </summary>
        static void BuildSyntheticMap(Scenario scenario)
        {
            int maxX = 0;
            int maxY = 0;
            int maxRadius = 1;
            scenario.citySet.ForEach(city =>
            {
                maxX = Math.Max(maxX, city.x);
                maxY = Math.Max(maxY, city.y);
                if (city.BuildingType != null)
                    maxRadius = Math.Max(maxRadius, city.BuildingType.radius);
            });

            scenario.Map.Create(maxX + maxRadius + 1, maxY + maxRadius + 1, SyntheticGridSize);
            scenario.Map.Name = scenario.Info.mapType;

            TerrainType fallback = scenario.CommonData.TerrainTypes.Get(GrasslandTerrainId)
                ?? throw new InvalidOperationException(
                    $"TerrainTypes table lacks the grassland row (id {GrasslandTerrainId}) required by the synthetic map.");
            int width = scenario.Map.Width;
            int height = scenario.Map.Height;
            var state = new System.Collections.Generic.List<Cell>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    scenario.Map.CellSet.SetTerrainTypeAndState(x, y, fallback, 0, null);
                }
            }

            // 城市直辖分区近似:每城认领周边未归属的六边圈(真图由 bin 的 areaId 决定;
            // 先到先得按 citySet 顺序,确定性成立)。关/港的母城传导发生在其
            // OnScenarioPrepare 绑定之后,合成阶段各管各圈。
            scenario.citySet.ForEach(city =>
            {
                state.Clear();
                scenario.Map.GetSpiral(city.x, city.y, SyntheticAreaRadius, state);
                foreach (Cell cell in state)
                {
                    if (cell.BelongCity == null)
                    {
                        scenario.Map.CellSet.SetTerrainTypeAndState(cell.x, cell.y, fallback, 0, city);
                    }
                }
            });
        }
    }

    /// <summary>headless 启动结果的验收快照。</summary>
    public sealed class BootResult
    {
        public Scenario Scenario { get; init; }
        public int Persons { get; init; }
        public int Cities { get; init; }
        public int Forces { get; init; }
        public int RegisteredSystems { get; init; }
        public bool SyntheticMap { get; init; }
    }
}
