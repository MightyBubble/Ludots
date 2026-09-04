// D-3' 部队域存档捕获面:技能冷却的原生承载(SangoTroopSkillCooldowns 组件随
// world.bin 持久化)+ sango 存档侧捕获/回放(troopDomain 节)。
// 语义源(M3.a 在案问题,原捕获面 = SangoCityPersonOrder.TroopSkillCd):原版技能表
// 不入档(Troop.cs 的 [JsonProperty] 整段注释),回灌按兵种/武将重建后 CD 归零;活
// 世界的冷却进度决定技能可用性(CanBeSpell),CD 错位让战斗 AI 决策分叉。按
// 技能表 id→CD 捕获,回灌后回放(重建技能集缺捕获 id 即回灌分叉,fail-fast)。
// D-3' 组件化退役:捕获面从城序文件(SangoCityPersonOrder)迁出,由本部队域面持有;
// 组件(SangoTroopSkillCooldowns)是 world.bin 原生载体与运行时读面,本捕获面只承担
// "内核回灌链尚未退场"期间的 CD 注入(重建的 SkillInstance.CDCount),随 D-5'/D-6'
// 存档收敛波退役。

using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Persistence;
using Sango.Core;

namespace Sango.Runtime
{
    /// <summary>部队域捕获面(sango.sim 域 troopDomain 节)。</summary>
    public sealed class SangoTroopDomainCapture
    {
        /// <summary>技能冷却(troopId → (skillId → CDCount),按 land/water/Strategy 枚举序)。</summary>
        public Dictionary<int, Dictionary<int, int>> SkillCd { get; init; } = new();

        /// <summary>从存档 JSON 节点解析(SangoSaveParticipant 写出的同形结构)。</summary>
        public static SangoTroopDomainCapture? FromJson(JsonNode? node)
        {
            if (node is not JsonObject root)
            {
                return null;
            }

            var capture = new SangoTroopDomainCapture();
            if (root["skillCd"] is not JsonObject troops)
            {
                return capture;
            }

            foreach (KeyValuePair<string, JsonNode?> entry in troops)
            {
                if (!int.TryParse(entry.Key, out int troopId) || entry.Value is not JsonObject skills)
                {
                    continue;
                }

                var cds = new Dictionary<int, int>();
                foreach (KeyValuePair<string, JsonNode?> skill in skills)
                {
                    if (int.TryParse(skill.Key, out int skillId) && skill.Value != null)
                    {
                        cds[skillId] = (int)skill.Value;
                    }
                }

                capture.SkillCd[troopId] = cds;
            }

            return capture;
        }

        /// <summary>活世界捕获(与原 SangoCityPersonOrder.TroopSkillCd 同位语义,键改技能表 id)。</summary>
        public static JsonObject Capture(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            var troopSkillCd = new JsonObject();
            scenario.troopsSet.ForEach(troop =>
            {
                if (troop == null || !troop.IsAlive)
                {
                    return;
                }

                var skills = new JsonObject();
                foreach (SkillInstance? skill in EnumerateSkills(troop))
                {
                    if (skill?.skill != null)
                    {
                        skills[skill.skill.Id.ToString()] = skill.CDCount;
                    }
                }

                troopSkillCd[troop.Id.ToString()] = skills;
            });

            return new JsonObject { ["skillCd"] = troopSkillCd };
        }

        /// <summary>回灌后按捕获回放(技能集缺捕获 id 即回灌分叉,fail-fast)。</summary>
        public void Apply(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            foreach (KeyValuePair<int, Dictionary<int, int>> entry in SkillCd)
            {
                Troop? troop = scenario.troopsSet.Get(entry.Key);
                if (troop == null)
                {
                    throw new SaveOrderException(
                        $"captured troopDomain.skillCd references troop {entry.Key} missing from the restored troopsSet.");
                }

                var rebuilt = new HashSet<int>();
                foreach (SkillInstance skill in EnumerateSkills(troop))
                {
                    if (skill.skill != null)
                    {
                        rebuilt.Add(skill.skill.Id);
                    }
                }

                foreach (int skillId in entry.Value.Keys)
                {
                    if (!rebuilt.Contains(skillId))
                    {
                        throw new SaveOrderException(
                            $"troop {entry.Key} rebuilt without captured skill id {skillId}; the restore diverged from the capture.");
                    }
                }

                foreach (SkillInstance skill in EnumerateSkills(troop))
                {
                    if (skill.skill != null && entry.Value.TryGetValue(skill.skill.Id, out int cd))
                    {
                        skill.CDCount = cd;
                    }
                }
            }
        }

        static IEnumerable<SkillInstance> EnumerateSkills(Troop troop)
        {
            if (troop.landSkills != null)
            {
                foreach (SkillInstance? skill in troop.landSkills)
                {
                    if (skill != null)
                    {
                        yield return skill;
                    }
                }
            }

            if (troop.waterSkills != null)
            {
                foreach (SkillInstance? skill in troop.waterSkills)
                {
                    if (skill != null)
                    {
                        yield return skill;
                    }
                }
            }

            if (troop.StrategySkills != null)
            {
                foreach (SkillInstance? skill in troop.StrategySkills)
                {
                    if (skill != null)
                    {
                        yield return skill;
                    }
                }
            }
        }
    }
}
