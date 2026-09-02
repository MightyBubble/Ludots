// 存档域 sango.sim(M1.d):世界态经 TK fork 序列化为内存 JSON,包进 Ludots 存档容器的
// domains 节点,由 SaveContainerCodec 统一落盘——从根上绕开原版 Scenario.Save 的裸
// System.IO.File.CreateText,不需要给 VFS 网关加写后端。
// 捕获面 = 原版 Scenario.Save 的语义截取:
//   · prepareList 七个对象池先过 OnScenarioSave(活引用 m 字段同步回 Id 字段);
//   · CommonData 不入档(NullValueHandling.Ignore + 置空),回灌侧从数据表整表重载,
//     Id 引用经 Id2ObjConverter 延迟绑定的 OnScenarioPrepare 回放重建;
//   · 相机态不刷新(原版从 MapRender 相机读写;headless 相机是 no-op shim,真实相机
//     归 M2 战争视口,届时再决定相机态入档面)。
// 回灌 = SangoKernelBoot.Restore:与 Boot 同一启动序列,正文换内存 JSON。

using System;
using System.Text.Json.Nodes;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Sango.Core;
using TKNewtonsoft.Json;

namespace Sango.Runtime
{
    public sealed class SangoSaveParticipant : ISaveParticipant
    {
        private readonly IVirtualFileSystem _vfs;
        private readonly string _contentModId;
        private readonly string _scenarioAssetPath;

        public SangoSaveParticipant(IVirtualFileSystem vfs, string contentModId,
            string scenarioAssetPath = "Scenario/Scenario.json")
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            _contentModId = contentModId ?? throw new ArgumentNullException(nameof(contentModId));
            _scenarioAssetPath = scenarioAssetPath ?? throw new ArgumentNullException(nameof(scenarioAssetPath));
        }

        public string DomainKey => "sango.sim";

        public JsonNode CaptureState()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException(
                    "Sango kernel is not booted; sango.sim capture requires Scenario.Cur (SangoKernelBoot.Boot).");

            // 原版 Save 首循环(prepareList 顺序):活引用同步回 Id 字段。
            scenario.forceSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.corpsSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.citySet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.personSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.buildingSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.troopsSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.fireSet.ForEach(o => o.OnScenarioSave(scenario));

            InfoCaptureAdjustments(scenario);

            ScenarioCommonData savedCommonData = scenario.CommonData;
            scenario.CommonData = null;
            string json;
            try
            {
                json = JsonConvert.SerializeObject(scenario, SaveSerializerSettings);
            }
            finally
            {
                scenario.CommonData = savedCommonData;
            }

            int[] randomState = GameRandom.ExportState();
            var randomNode = new JsonArray();
            foreach (int value in randomState)
            {
                randomNode.Add(value);
            }

            return new JsonObject
            {
                ["scenario"] = JsonNode.Parse(json),
                ["random"] = randomNode,
            };
        }

        public void RestoreState(JsonNode state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state is not JsonObject root)
                throw new SaveContextException("Save domain 'sango.sim' must be an object.");

            JsonNode scenarioNode = root["scenario"]
                ?? throw new SaveContextException("Save domain 'sango.sim' is missing 'scenario'.");
            if (root["random"] is not JsonArray randomArray)
                throw new SaveContextException("Save domain 'sango.sim' is missing 'random'.");

            var randomState = new int[randomArray.Count];
            for (int i = 0; i < randomArray.Count; i++)
            {
                if (randomArray[i] is not JsonValue value || !value.TryGetValue<int>(out randomState[i]))
                {
                    throw new SaveContextException($"Save domain 'sango.sim' random[{i}] must be an integer.");
                }
            }

            SangoKernelBoot.Restore(_vfs, _contentModId, scenarioNode.ToJsonString(), randomState, _scenarioAssetPath);
        }

        // 原版 Save 对 Info 的两处置写 + 回合边界校正:
        // isSave 置真让回灌侧跳过 Variables/Map 重建、改由 JSON 填充;dateTime 是存档时间戳
        // 元数据(UI 显示用,不参与 digest)。curForceId 在 headless 捕获点(TurnEnd 之后)
        // 残留上一回合最后行动的势力,而 Start() 的恢复分支会按它重放该势力——那是原版
        // "玩家回合中存档"的语义;边界存档的忠实表示是无当前势力(队列由 MakeForceQuene
        // 全量重建)。
        static void InfoCaptureAdjustments(Scenario scenario)
        {
            scenario.Info.isSave = true;
            scenario.Info.dateTime = DateTime.Now.ToFileTime();
            scenario.Info.curForceId = 0;
        }

        // 与原版 Scenario.Save 同一设置(Formatting 仅影响体积,内存面取紧凑)。
        static readonly JsonSerializerSettings SaveSerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };
    }
}
