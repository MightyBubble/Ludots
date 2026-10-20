using TKNewtonsoft.Json;
using Sango.Mod;
using System.Xml;

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


        public void LoadCommonData(ScenarioCommonData scenarioCommonData)
        {
            Sango.Directory.EnumFiles(Path.ContentRootPath + "/Data/Common", file =>
            {
                scenarioCommonData.Load(file);
            });
            ModManager.Instance.EnumFiles("Data/Common", "*.json", System.IO.SearchOption.AllDirectories, file =>
            {
                scenarioCommonData.Load(file);
            });
        }

        public ScenarioCommonData LoadNewCommonData()
        {
            ScenarioCommonData scenarioCommonData = new ScenarioCommonData();
            Sango.Directory.EnumFiles(Path.ContentRootPath + "/Data/Common", file =>
            {
                scenarioCommonData.Load(file);
            });
            ModManager.Instance.EnumFiles("Data/Common", "*.json", System.IO.SearchOption.AllDirectories, file =>
            {
                scenarioCommonData.Load(file);
            });
            return scenarioCommonData;
        }

        internal ScenarioCommonData LoadCommonData()
        {
            if (ScenarioCommonData != null)
                return ScenarioCommonData;

            ScenarioCommonData = new ScenarioCommonData();
            Sango.Directory.EnumFiles(Path.ContentRootPath + "/Data/Common", file =>
            {
                ScenarioCommonData.Load(file);
            });
            ModManager.Instance.EnumFiles("Data/Common", "*.json", System.IO.SearchOption.AllDirectories, file =>
            {
                ScenarioCommonData.Load(file);
            });
            return ScenarioCommonData;
        }
    }
}
