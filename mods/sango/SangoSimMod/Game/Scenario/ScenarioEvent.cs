/*
 * 文件名：Scenario.cs
 * 描述：剧本剧情事件类
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

using Sango.Render;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TKNewtonsoft.Json;

namespace Sango.Core
{
    /// <summary>
    /// 剧情数据获取接口类
    /// </summary>
    public interface IScenarioEventData
    {
        Person ActionGovernor { get; }
        Person ActionCounsellor { get; }
        Person TargetGovernor { get; }
        Person TargetCounsellor { get; }
        SkillInstance ActionSkill { get; }
        SkillInstance TargetSkill { get; }
        Person ActionPerson { get; }
        Person TargetPerson { get; }
        Troop ActionTroop { get; }
        Troop TargetTroop { get; }
        Cell ActionCell { get; }
        Cell TargetCell { get; }
        City ActionCity { get; }
        City TargetCity { get; }
        Corps ActionCorps { get; }
        Corps TargetCorps { get; }
        Force ActionForce { get; }
        Force TargetForce { get; }
        object ActionObject { get; }
        object TargetObject { get; }
    }

    public enum ScenarioEventType
    {
        Text,
        PersonTalk,
        PersonTalkChoice,
    }

    /// <summary>
    /// 剧情逻辑基类
    /// </summary>
    public class ScenarioEventBase : RenderEventBase
    {
        public IScenarioEventData scenarioEventData;

    }

    /// <summary>
    /// 剧本类，管理游戏剧本的所有数据
    /// 包含势力、武将、城市、部队、建筑等游戏对象
    /// 负责剧本的加载、保存、运行等核心功能
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ScenarioEvent : SangoObject
    {
        public bool IsDone { get; set; }
        public ScenarioEventType eventType;
        public Condition condition;
        public string formatContent;
        public List<string> variables;
        public List<int> nextEvent;

        /// <summary>
        /// 格式化剧情内容，将formatContent中的{:变量名}占位符替换为IScenarioEventData对应属性的Name值
        /// 占位符格式：{:ActionGovernor}，其中ActionGovernor为IScenarioEventData中定义的属性名
        /// </summary>
        /// <param name="data">剧情事件数据，包含武将、部队、城市等对象引用</param>
        /// <returns>格式化后的剧情文本</returns>
        public string FormatContent(IScenarioEventData data)
        {
            if (string.IsNullOrEmpty(formatContent))
            {
                return formatContent;
            }

            if (data == null)
            {
                return formatContent;
            }

            // 使用StringBuilder拼接最终字符串，手动解析占位符
            StringBuilder sb = new StringBuilder();
            int index = 0;
            int length = formatContent.Length;

            while (index < length)
            {
                // 查找占位符起始标记 "{:"
                int placeholderStart = formatContent.IndexOf("{:", index);
                if (placeholderStart < 0)
                {
                    // 没有更多占位符，直接追加剩余部分
                    sb.Append(formatContent, index, length - index);
                    break;
                }

                // 追加占位符之前的普通文本
                if (placeholderStart > index)
                {
                    sb.Append(formatContent, index, placeholderStart - index);
                }

                // 查找占位符结束标记 "}"
                int placeholderEnd = formatContent.IndexOf('}', placeholderStart + 2);
                if (placeholderEnd < 0)
                {
                    // 没有找到闭合的}，将"{:"及之后内容当作普通文本处理
                    sb.Append(formatContent, index, length - index);
                    break;
                }

                // 提取占位符中的变量名
                int varNameStart = placeholderStart + 2;
                string varName = formatContent.Substring(varNameStart, placeholderEnd - varNameStart);

                // 根据变量名获取对应的属性值并解析其Name
                string nameValue = GetNameByVariableName(data, varName);

                // 追加替换后的值
                sb.Append(nameValue);

                // 移动到占位符之后
                index = placeholderEnd + 1;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 根据变量名从IScenarioEventData中获取对应属性的Name值
        /// 不使用反射，直接通过接口属性访问
        /// </summary>
        /// <param name="data">剧情事件数据</param>
        /// <param name="variableName">变量名</param>
        /// <returns>对应对象的Name值，找不到返回空字符串</returns>
        public static string GetNameByVariableName(IScenarioEventData data, string variableName)
        {
            // 根据变量名直接获取接口属性，不使用反射
            object obj = GetVariableObject(data, variableName);
            if (obj == null)
            {
                return string.Empty;
            }

            // 通过类型检查直接获取Name，不使用反射
            return GetObjectName(obj);
        }

        public static object GetVariableObject(IScenarioEventData data, string variableName)
        {
            if (string.IsNullOrEmpty(variableName))
            {
                return null;
            }

            // 根据变量名直接获取接口属性，不使用反射
            object obj = null;
            switch (variableName)
            {
                case "ActionGovernor":
                    obj = data.ActionGovernor;
                    break;
                case "ActionCounsellor":
                    obj = data.ActionCounsellor;
                    break;
                case "TargetGovernor":
                    obj = data.TargetGovernor;
                    break;
                case "TargetCounsellor":
                    obj = data.TargetCounsellor;
                    break;
                case "ActionSkill":
                    obj = data.ActionSkill;
                    break;
                case "TargetSkill":
                    obj = data.TargetSkill;
                    break;
                case "ActionPerson":
                    obj = data.ActionPerson;
                    break;
                case "TargetPerson":
                    obj = data.TargetPerson;
                    break;
                case "ActionTroop":
                    obj = data.ActionTroop;
                    break;
                case "TargetTroop":
                    obj = data.TargetTroop;
                    break;
                case "ActionCell":
                    obj = data.ActionCell;
                    break;
                case "TargetCell":
                    obj = data.TargetCell;
                    break;
                case "ActionCity":
                    obj = data.ActionCity;
                    break;
                case "TargetCity":
                    obj = data.TargetCity;
                    break;
                case "ActionCorps":
                    obj = data.ActionCorps;
                    break;
                case "TargetCorps":
                    obj = data.TargetCorps;
                    break;
                case "ActionForce":
                    obj = data.ActionForce;
                    break;
                case "TargetForce":
                    obj = data.TargetForce;
                    break;
                case "ActionObject":
                    obj = data.ActionObject;
                    break;
                case "TargetObject":
                    obj = data.TargetObject;
                    break;
                default:
                    // 未知变量名，返回空字符串
                    return null;
            }

            return obj;
        }

        /// <summary>
        /// 获取游戏对象的Name属性值，通过类型判断直接获取，不使用反射
        /// </summary>
        /// <param name="obj">游戏对象</param>
        /// <returns>对象的Name值，获取失败返回空字符串</returns>
        private static string GetObjectName(object obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            // 通过类型检查直接获取Name属性
            if (obj is Person person)
            {
                return person.Name ?? string.Empty;
            }
            if (obj is SkillInstance skill)
            {
                return skill.Name ?? string.Empty;
            }
            if (obj is Troop troop)
            {
                return troop.Name ?? string.Empty;
            }
            if (obj is City city)
            {
                return city.Name ?? string.Empty;
            }
            if (obj is Corps corps)
            {
                return corps.Name ?? string.Empty;
            }
            if (obj is Force force)
            {
                return force.Name ?? string.Empty;
            }

            // 未知类型，返回空字符串
            return string.Empty;
        }

    }




    /// <summary>
    /// IScenarioEventData 的通用实现(M3.e):触发点现地装配变量面(执行武将/目标/
    /// 城市等),FormatContent 按 variables 消费。
    /// </summary>
    public sealed class ScenarioEventData : IScenarioEventData
    {
        public Person ActionGovernor { get; set; }
        public Person ActionCounsellor { get; set; }
        public Person TargetGovernor { get; set; }
        public Person TargetCounsellor { get; set; }
        public SkillInstance ActionSkill { get; set; }
        public SkillInstance TargetSkill { get; set; }
        public Person ActionPerson { get; set; }
        public Person TargetPerson { get; set; }
        public Troop ActionTroop { get; set; }
        public Troop TargetTroop { get; set; }
        public Cell ActionCell { get; set; }
        public Cell TargetCell { get; set; }
        public City ActionCity { get; set; }
        public City TargetCity { get; set; }
        public Corps ActionCorps { get; set; }
        public Corps TargetCorps { get; set; }
        public Force ActionForce { get; set; }
        public Force TargetForce { get; set; }
        public object ActionObject { get; set; }
        public object TargetObject { get; set; }
    }

    /// <summary>
    /// Data/ScenarioEvent/*.json 表行的反序列化面。eventType 在表里是字符串编码
    /// ("PersonTalk" = 人物对话;"Dialog:&lt;GameDialog.DialogStyle&gt;" = 对话框样式;
    /// "GameSystem:&lt;系统名&gt;" = 推入游戏系统)——ScenarioEvent.eventType 的枚举
    /// 面存不下这些前缀编码,表驱动触发面消费本类型。
    /// </summary>
    public sealed class ScenarioEventTableEntry
    {
        public int Id { get; set; }
        public string eventType { get; set; }
        public List<string> variables { get; set; }
        public string formatContent { get; set; }

        /// <summary>复用 ScenarioEvent.FormatContent 的占位符解析({:变量名} → Name)。</summary>
        public string FormatContent(IScenarioEventData data)
        {
            var scenarioEvent = new ScenarioEvent { formatContent = formatContent };
            return scenarioEvent.FormatContent(data);
        }
    }




    public class ScenerioEventManager : Singleton<ScenerioEventManager>
    {
        public ScenerioEventManager() { }

        /// <summary>
        /// 原设计的事件登记面(键=事件 Id)。表驱动的触发面用 eventGroups(见 Init);
        /// 本表保留给后续接入 nextEvent 链的剧情事件。
        /// </summary>
        public Dictionary<int, ScenarioEvent> eventMap = new Dictionary<int, ScenarioEvent>();

        /// <summary>
        /// 表驱动事件组(键=表文件名前缀的组 id:1=回合开始军师慰问、10=搜索失败、
        /// 11=搜索到人才);组内条目按文件序,即一次触发的演出步骤序列。
        /// </summary>
        public Dictionary<int, List<ScenarioEventTableEntry>> eventGroups =
            new Dictionary<int, List<ScenarioEventTableEntry>>();

        bool _loaded;

        /// <summary>
        /// 加载 Data/ScenarioEvent/*.json(M3.e 激活;进程内一次,表是静态内容)。
        /// 时机:启动线(SangoKernelBoot.PrepareKernel)在 VFS 网关安装后调用。
        /// </summary>
        public void Init()
        {
            if (_loaded)
            {
                return;
            }

            var groups = new List<(int GroupId, string File)>();
            Sango.Directory.EnumFiles(Path.ContentRootPath + "/Data/ScenarioEvent", "*.json",
                System.IO.SearchOption.TopDirectoryOnly, file =>
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                    int separator = fileName.IndexOf('_');
                    if (separator <= 0 || !int.TryParse(fileName.Substring(0, separator), out int groupId))
                    {
                        throw new InvalidOperationException(
                            $"ScenarioEvent table '{fileName}' lacks the '<groupId>_<name>' file contract.");
                    }

                    groups.Add((groupId, file));
                });

            // 组序确定性:按组 id 升序装载(文件系统枚举序不做确定性假设)。
            groups.Sort((a, b) => a.GroupId.CompareTo(b.GroupId));
            foreach ((int groupId, string file) in groups)
            {
                List<ScenarioEventTableEntry> entries =
                    TKNewtonsoft.Json.JsonConvert.DeserializeObject<List<ScenarioEventTableEntry>>(File.ReadAllText(file))
                    ?? throw new InvalidOperationException(
                        $"ScenarioEvent table '{file}' is not a JSON array of event entries.");
                foreach (ScenarioEventTableEntry entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.eventType) || string.IsNullOrEmpty(entry.formatContent))
                    {
                        throw new InvalidOperationException(
                            $"ScenarioEvent table '{file}' has an entry missing eventType/formatContent.");
                    }
                }

                eventGroups[groupId] = entries;
            }

            _loaded = true;
        }

        public void Clear()
        {
            eventMap.Clear();
            eventGroups.Clear();
            _loaded = false;
        }

        /// <summary>取事件组(缺组即装配缺陷:内容 mod 必须随表)。</summary>
        public List<ScenarioEventTableEntry> GetGroup(int groupId)
        {
            return eventGroups.TryGetValue(groupId, out List<ScenarioEventTableEntry> entries)
                ? entries
                : throw new InvalidOperationException(
                    $"ScenarioEvent group {groupId} is not loaded; the content mod must ship Data/ScenarioEvent/{groupId}_*.json.");
        }
    }
}
