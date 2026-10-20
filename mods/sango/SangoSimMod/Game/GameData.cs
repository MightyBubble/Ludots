using System;
using TKNewtonsoft.Json;
using Sango.Runtime;

namespace Sango.Core
{
    public class GameData : Singleton<GameData>
    {
        /// <summary>
        /// 通用配置
        /// </summary>
        public ScenarioCommonData ScenarioCommonData { get; private set; }

        public void Init()
        {
            Sango.Log.Info("GameData.Init()");
            LoadCommonData();
            GameMedia.Instance.Load();
            SkillConfigManager.Instance.Init();
            GameCustomEdit.Instance.Init();
        }

        // T1.1(#1842/#1847):内容表装载走官方 config_catalog(SangoContentMod
        // assets/config_catalog.json 逐表 DeepObject 声明)——目录枚举装载退役。
        // 合并后的逐表节点喂给 TK PopulateObject(行级反序列化链不动);单源下
        // 合并结果与整文件内容一致,未声明/缺文件的表由 catalog 装载器 fail-fast。
        static void LoadCommonTablesInto(ScenarioCommonData scenarioCommonData)
        {
            Ludots.Core.Config.ConfigPipeline pipeline = SangoConfigRuntime.Pipeline();
            Ludots.Core.Config.ConfigCatalog catalog = SangoConfigRuntime.Catalog();
            foreach (Ludots.Core.Config.ConfigCatalogEntry entry in catalog.Entries)
            {
                if (!entry.RelativePath.StartsWith("Data/Common/", StringComparison.Ordinal))
                    continue;

                Ludots.Core.Config.ConfigCatalogEntry declared =
                    Ludots.Core.Config.ConfigPipeline.RequireEntry(catalog, entry.RelativePath, Ludots.Core.Config.ConfigMergePolicy.DeepObject);
                var merged = pipeline.MergeDeepObjectFromCatalog(in declared)
                    ?? throw new InvalidOperationException($"Config table '{declared.RelativePath}' merged to null.");
                TKNewtonsoft.Json.JsonConvert.PopulateObject(merged.ToJsonString(), scenarioCommonData);
            }
        }

        public void LoadCommonData(ScenarioCommonData scenarioCommonData)
        {
            LoadCommonTablesInto(scenarioCommonData);
        }

        public ScenarioCommonData LoadNewCommonData()
        {
            ScenarioCommonData scenarioCommonData = new ScenarioCommonData();
            LoadCommonTablesInto(scenarioCommonData);
            return scenarioCommonData;
        }

        internal ScenarioCommonData LoadCommonData()
        {
            if (ScenarioCommonData != null)
                return ScenarioCommonData;

            ScenarioCommonData = new ScenarioCommonData();
            LoadCommonTablesInto(ScenarioCommonData);
            return ScenarioCommonData;
        }
    }
}
